# Oracle F6: critter_p_proc cadence and order

Finding F6 in `docs/research-notes/oracle-m1-findings-2026-10.md`. After F1-F3 and F7, every oracle
fixture still diverges at trace line 2 because Hexwaste runs critter scripts at a different rate and in a
different order from fallout2-ce. This change ports fo2ce's critter heartbeat model.

## fo2ce behavior (reference: alexbatalov e97087b)

`_script_chk_critters` (`src/scripts.cc:705-742`) runs from the `_doBkProcesses` ticker, once per pass of
the input/main loop:

- Skipped while a dialog is active or combat is running.
- `_count_` (a file static, initial 0, never reset) is incremented, wrapped to 0 when it reaches the list
  length, then the script at slot `_count_` of `gScriptLists[SCRIPT_TYPE_CRITTER]` runs `critter_p_proc`.
  So the first run after start uses slot 1.
- The list covers every elevation and has no eligibility filter.
- List order:
  - Map and save loads read the script section in file order.
  - `scriptAdd` appends.
  - `scriptRemove` (called by `critterKill`, `critter.cc:898`, and on object destruction) moves the tail
    entry into the removed slot.
- The rate is frame-bound: fo2ce caps at 60 fps (`FpsLimiter`, default 60); the oracle's virtual clock gives
  100 passes per second.
- Observed in the oracle traces: after a save load the dude's script is in the critter list twice (artemple:
  `dude, dude, acklint` per round; klamath: both dude entries at the end of a 41-entry round). This is also
  why fo2ce runs `obj_dude.int map_update_p_proc` twice per round (F7 leftover).

## Hexwaste today

- `PumpCritterProcs` (`src/Hexwaste.Viewer/ViewerGame.CombatHost.cs:544`) runs one critter every 100 ms
  (10 Hz).
- It walks `_solidObjects[_elevation]` filtered by `IsHeartbeatEligible`: critter, not dead, has a sid,
  not a waiting companion.
- `ScriptHost.AllocateSid` gives every runtime-created object a type-3 (item) sid, critters included.
- The live loop runs at MonoGame's fixed 60 Hz `Update`. The headless harness (`PumpOneFrame`) and oracle
  mode pump 10 ms frames.

## Design

### 1. `CritterScriptList` (new, `src/Hexwaste.Formats/Int/CritterScriptList.cs`)

A small ordered list of critter sids with fo2ce's semantics, independent of MonoGame:

- `Add(int sid)` appends.
- `Remove(int sid)`: when the sid is not the last entry, move the last entry into its slot; then shrink by
  one. A sid that is not present is a no-op.
- `Next()` (the round-robin): increment the counter, wrap to 0 when it is `>= Count`, return the sid at that
  slot, or `null` when the list is empty.
- `Clear()`, and `Count`.
- The counter is NOT reset by `Clear()`: fo2ce's `_count_` survives map loads.
- The same sid may appear twice (the dude case); `Remove` takes out the first match, like `scriptRemove`.

Ported with `// ported from fallout2-ce src/scripts.cc _script_chk_critters() / scriptAdd() / scriptRemove()`.

### 2. Building the list

- `MapFile.ReadScripts` records the critter-type (type 4) sids in file order in a new
  `MapFile.CritterSids` list, alongside `ScriptsBySid`.
- On map load the viewer clears the list and adds `MapFile.CritterSids` in order. It then appends the dude's
  sid when `BindDudeScript` runs, as fo2ce's `scriptsSetDudeScript` appends after the map scripts.
- Runtime spawns: `ScriptHost.AllocateSid` takes the script type. A critter gets a type-4 sid (`0x04800000`
  range, clear of map sids and of `DudeSid`) and is appended. Items keep type 3. The callers in the
  create-object paths pass the type from the object's fid type.
- Deaths: fo2ce removes the script at the moment of death (`critterKill`, `critter.cc:898`), so the list
  order after a fight depends on the order of deaths. Removing lazily when the round-robin reaches a dead
  critter would break the oracle match after any combat, so every kill site removes the sid immediately
  through one viewer helper, `RemoveCritterScript(MapObject)`. The five sites that set `DAM_DEAD` (0x80):
  - `CombatEngine.KillCritter` (`src/Hexwaste.Formats/Combat/CombatEngine.cs:2271`). It lives in
    Formats and cannot call the viewer, so it calls a new `ICombatHost.RemoveCritterScript(MapObject)`
    (default no-op, like `RecordKill`) right after `destroy_p_proc`, matching the engine's "then the
    script is removed" order (`combat.cc:4850-4876`). The viewer's host forwards it to the helper.
  - `KillCrittersByType` (`src/Hexwaste.Viewer/ViewerGame.CombatHost.cs:509`).
  - `KillCritterObject` (`ViewerGame.CombatHost.cs:529`).
  - `ViewerGame.cs:5516`.
  - Save load (`ViewerGame.SaveLoad.cs:165`) marks restored corpses dead. The list is rebuilt after it,
    so it calls nothing and must not re-add dead critters' sids.
- Destroys: fo2ce also removes the script when its object is destroyed (`object.cc:3905`). About 14 viewer
  sites take an object off the map; each one that can remove a scripted critter calls the same helper.
  The list is rebuilt from the map on load, so only in-session removals matter.
- Vanilla save import (the dude twice): the first implementation step checks whether the imported `.SAV`
  script section carries a critter record for the dude. If it does, `--load-vanilla-save` keeps that entry
  in the list, so the dude appears twice as in fo2ce. If it does not, find the real source of the second
  entry in `_obj_load_dude` / `mapLoadSaved` before porting anything. Hexwaste's own save/load is unchanged.

### 3. `PumpCritterProcs`: one script per frame

- Drop the 100 ms `_critterProcTimerMs` gate. Each call (one per frame) takes `Next()` and runs that sid's
  owner's `critter_p_proc`.
- The sneak re-check (`RollSneak`, `sneakEventProcess`) is game-time driven, so it moves to its own 100 ms
  accumulator with unchanged behavior.
- The owner lookup covers all elevations (sid to object). A sid whose owner cannot be found is skipped for
  that frame; the counter still advances, as in fo2ce.
- The gates stay as they are: no runs in combat, dialog, the companion hub, loot, the worldmap or game-over.
- Kept Hexwaste deviations, each with a code comment:
  - Waiting companions (`_waitingCompanions`) are skipped: their slot is consumed but nothing runs.
  - A script without `critter_p_proc` runs nothing (fo2ce runs procedure 1, F8, out of scope).
- `IsHeartbeatEligible` stays only where other code still uses it (the harness probes and the
  `companion-lifecycle` diagnostics). The pump itself uses the list.

## Testing

- **Unit (hermetic):** `CritterScriptListTests` covers:
  - the first `Next()` returns slot 1;
  - wrap at `Count`;
  - swap-remove moves the tail into the removed slot;
  - removing a missing sid is a no-op;
  - an empty list returns null;
  - `Clear()` keeps the counter;
  - duplicate sids are allowed.
- **Oracle:** rerun all three fixtures. This also checks the list order against fo2ce. Success means:
  - the `critter_p_proc` sequence matches fo2ce's;
  - the first divergence moves past line 2;
  - the new first divergence is recorded in the findings doc.
- **Goldens:** expect many fixtures to move (10x the critter-proc rate in the harness). Re-record suite by
  suite:
  - run each changed fixture twice and require identical output before recording;
  - review each changed fixture's diff for plausibility (more NPC movement or float lines, no crashes or
    missing sections);
  - summarize the moved fixtures in the commit message.
- **Full unit suite and CI steps** (ascii-lint, harness self-test, Release build, data-free tests) pass.

## Out of scope

- F8 (missing proc runs procedure 1).
- F4/F5 (combat joiners).
- Vanilla party-script waiting.
- Hexwaste's own save format.
