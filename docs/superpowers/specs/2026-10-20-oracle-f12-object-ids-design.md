# Oracle F12: object ids from fo2ce's counter

Finding F12 in `docs/research-notes/oracle-m1-findings-2026-10.md`. After F13, artemple-idle first diverges
at trace line 3011:

```
fo2ce:    t=302720 PROC artemple.int map_update_p_proc self=obj#16
hexwaste: t=302720 PROC artemple.int map_update_p_proc self=obj#-2
```

Hexwaste gives synthetic and runtime objects constant ids (`-2` map-script owner, `-3` script/encounter
spawns, `-4` stack splits and rebuilt items, `-5` spatial stand-in and money, `-7` explosion marker).
fo2ce takes them from one process-wide counter.

## fo2ce behavior (reference: alexbatalov e97087b)

- `scriptsNewObjectId` (`scripts.cc:526`):
  `do { _cur_id++; } while (an object on a map tile has id == _cur_id); _cur_id++; return _cur_id;`.
  Only the first increment is checked. `_cur_id` starts at 4 (`scripts.cc:189`) and is never reset.
- The in-use set is what `objectFindFirst`/`objectFindNext` walk (`object.cc:2141`): every object in the
  per-tile lists, all elevations. Inventory items and objects off the map are not in it.
- Every `objectCreateWithFidPid` (`object.cc:913`) and `_obj_copy` (`object.cc:1024`) takes one id. The
  oracle's SPAWN line is printed right there, so SPAWN shows that first id.
- Paths that take a second id, which becomes the object's final id:
  - the map-script object: `objectCreateWithFidPid`, then `map.cc:970`;
  - `create_object_sid` with a script (`interpreter_extra.cc:938`);
  - a scripted encounter critter: `objectCreateWithPid`, then `_obj_new_sid_inst` (`worldmap.cc:3848`,
    `proto_instance.cc:158`); its inventory items follow, one id each.
- A spatial script has no owner until it calls `self_obj` (`scriptGetSelf`, `scripts.cc:571`), so its
  procs trace `self=obj#-1`.
- Observed: the map-script object is obj#16 in artemple-idle and klamath-hour (counter 12 before the map
  load, on the oracle's launch path: main menu, load a slot). klamath's scripted spawns print SPAWN
  obj#18, #22, #26, #30, #34 (two ids each).

## Hexwaste today

- `ScriptHost` builds the map-script owner with `Id = -2` in four places: `SynthMapScriptOwner()` (start,
  map_exit, combat-over), and inline in `RunMapEnter` and `RunMapUpdate`.
- `ViewerGame.LoadMap` (`ViewerGame.cs:1895-1911`) runs `RunStartProcs`, `RunMapEnter`, `RunMapUpdate` on
  every map load (vanilla import included).
- Only trace calls read `MapObject.Id` (`OracleTrace.Proc/Turn/Attack/Move/Spawn`); scripts compare
  objects by reference. The harness reads `Id == -3` to find spawned critters
  (`ViewerGame.Harness.cs:186,193,219,370,398`).
- SPAWN sites: `ScriptContext.CreateObject` (`ScriptHost.cs:2650`), script money (`ScriptHost.cs:791`),
  encounter critter and its items (`ViewerGame.cs:5575-5616`), stack split (`ViewerGame.cs:4952`),
  ejected ammo box (`ViewerGame.CombatHost.cs:390`), explosion marker (`ViewerGame.CombatHost.cs:1050`).
- `GetSpatialSelf` (`ScriptHost.cs:600`) builds the spatial stand-in with `Id = -5`.

## Design

1. **`ObjectIds`** (`src/Hexwaste.Formats/Map/ObjectIds.cs`), a port of `scriptsNewObjectId`:
   ```csharp
   public sealed class ObjectIds
   {
       public int Current { get; private set; } = 12;
       public int Next(Func<int, bool> inUse) { do Current++; while (inUse(Current)); return ++Current; }
   }
   ```
   Comment on the 12: `_cur_id` starts at 4 and is never reset; 12 is the value observed before the map
   load on the oracle's launch path (both fixtures with a map script). Shortcut comment:
   `shortcut: the start value is observed, not derived; upgrade if a fixture's first map-script id is not 16`.
2. **`ScriptHost`**:
   - `public ObjectIds ObjectIds { get; } = new();`
   - `public Func<int, bool> ObjectIdInUse { get; set; } = _ => false;` (installed by the viewer).
   - `public int NewObjectId() => ObjectIds.Next(ObjectIdInUse);`
3. **Viewer in-use check** (set once where `_scriptHost` is created): true when any object in
   `_map.Elevations[*].Objects` with `HexTile >= 0` has that id, or the dude has it.
4. **Map-script owner**: one owner per map load.
   - `RunStartProcs` builds it when `map.Header.ScriptIndex > 0`: `NewObjectId()` (fo2ce's
     `objectCreateWithFidPid`), then `Id = NewObjectId()` (`map.cc:970`); stored in `_mapScriptOwner`.
   - `RunMapEnter`, `RunMapUpdate`, `RunMapExit`, `RunMapCombatOver` and the start pass all use it.
     `SynthMapScriptOwner()` and the two inline builds go away. If none was built yet for this map (a
     caller that skips `RunStartProcs`, e.g. tests), build it on first use the same way.
   - A map load always builds a new one (fo2ce creates a new map-script object per load).
5. **Spawn sites**. Each takes `id1 = NewObjectId()` and traces SPAWN with it; the object keeps the last id:

   | site | fo2ce | ids |
   |---|---|---|
   | `ScriptContext.CreateObject` | `opCreateObject` | 1, +1 when `scriptIndex != -1` |
   | script money (`ScriptHost.cs:791`) | `item.cc:3236` | 1 |
   | encounter critter (`ViewerGame.cs:5575`) | `wmSetupCritterObjs` | 1, +1 when scripted; then each item 1 |
   | stack split (`ViewerGame.cs:4952`) | `item.cc:435` `_obj_copy` | 1 |
   | ammo box (`ViewerGame.CombatHost.cs:390`) | `item.cc:1893` | 1 |
   | explosion marker (`ViewerGame.CombatHost.cs:1050`) | `actions.cc:1595` | 1 |

   The second id is taken where fo2ce takes it: after the SPAWN line, before the critter's MOVE line.
   Viewer sites reach the counter through `_scriptHost.NewObjectId()`; with no script host they keep their
   current constant.
6. **`MapObject.Spawned`** (`bool`, not saved): set at the two sites that used `-3` (`CreateObject`,
   encounter critter); the five harness checks read `o.Spawned` instead of `o.Id == -3`. The `-3` marker
   was already lost on a Hexwaste save load (rebuilt objects get `-4`), so behavior is unchanged.
7. **Spatial stand-in**: `GetSpatialSelf` uses `Id = -1` (fo2ce prints the missing owner as -1).
8. **Shortcut comments** (in `ObjectIds.cs` unless noted):
   - `shortcut: fo2ce creation paths Hexwaste does not model do not advance the counter (projectiles
     actions.cc:780, explosion ring actions.cc:867/1608, hidden barter/loot boxes inventory.cc:4123/5060,
     the spatial owner scriptGetSelf creates on self_obj); upgrade when a fixture shows a SPAWN gap`.
   - At `RebuildObject` (`ViewerGame.SaveLoad.cs:249`): `shortcut: objects rebuilt from a Hexwaste save keep
     -4, fo2ce restores their saved ids; upgrade if save round trips need stable ids`.

## Testing

- **Unit (hermetic, `ObjectIds`)**: from 12 with nothing in use, `Next` returns 14 then 16; with 13 in use,
  `Next` from 12 returns 15 (the second, unchecked increment flips the parity, as in fo2ce).
- **Unit (`ScriptHost`)**: the map-script owner from a fresh host is obj#16 and the same object across
  start, map_enter and map_update (trace lines `self=obj#16`); a scripted `create_object_sid` after it traces
  `SPAWN obj#18` and leaves the object with `Id == 20`; an unscripted one traces and keeps 18.
- **Oracle**: rerun all three fixtures. Success: artemple moves past line 3011; klamath stays at 1664 (its
  spawns come later) and arcaves at 9.
- **Goldens**: run all suites sequentially. Scripts never read ids, so none should move; a moved fixture
  gets two runs, a reviewed diff, a re-record and a line in the commit message.
- **CI steps** (ascii-lint, unit tests, Release build) pass.

## Docs

Findings doc: F12 FIXED with the counter rule, the observed start value, the site table and the
unmodeled creation paths; the new first divergences.

## Out of scope

- Porting the unmodeled fo2ce creation paths (projectiles, explosion ring, hidden boxes, spatial owner).
- Saving object ids in Hexwaste saves.
- The new-game dude id (`ViewerGame.cs:3219`, `-1`; fo2ce uses 18000). The oracle only loads saves.
