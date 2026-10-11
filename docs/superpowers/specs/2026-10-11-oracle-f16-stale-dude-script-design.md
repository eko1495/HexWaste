# Oracle F16: the stale obj_dude script runs with its own state

Finding F16 in `docs/research-notes/oracle-m1-findings-2026-10.md`. After F6, artemple-idle diverges at
trace line 3 and klamath-hour at line 66: fo2ce writes `LVAR sid=0x04004650 ...` where Hexwaste runs the
dude's own obj_dude.int and writes nothing under that sid.

## fo2ce behavior (reference: alexbatalov e97087b, oracle traces)

- A vanilla `.SAV` keeps the dude's previous-session script record (sid `0x04004650`, script index 0 =
  obj_dude, 5 local vars) in its critter section. After a load it is a separate `Script` with its own LVAR
  block and its own program instance. It runs obj_dude.int with `self=obj#18000` (the dude).
- Post-load critter list (F6, verified): `[map critters..., stale 0x04004650, gDude]`.
- Everything that walks the script lists runs it:
  - the critter heartbeat (`_script_chk_critters`);
  - `scriptsExecMapUpdateScripts` (`scripts.cc:2601`): the map script, then every script in type order
    (system, spatial, timed, item, critter), each list in list order, filtered by `procs[proc] > 0`.
- Procs called through an object (`object->sid`: talk, combat, damage, timers via `opAddTimerEvent`,
  `interpreter_extra.cc:2520`) reach the dude's own script, never the stale one.
- Observed map_update round (artemple, every 30 s / 600 ticks):
  ```
  fo2ce:    artemple, animfrvr#14, animfrvr#18, obj_dude (stale), obj_dude (gDude)
  hexwaste: artemple, obj_dude, animfrvr#14, animfrvr#18
  ```
  So fo2ce runs obj_dude's map_update twice per round (the open F7 note), and last, not first.

## Hexwaste today

- `ViewerGame.VanillaImport.cs:82` finds the stale sid; `ScriptHost.RebuildCritterScripts(map, extraSid,
  extraOwner)` keeps it in `CritterScripts` with the dude as owner.
- `PumpCritterProcs` (`ViewerGame.CombatHost.cs:548`) takes a sid from the list, resolves its owner, and calls
  `RunObjectProc(owner, ...)`, which runs `owner.Sid`. For the stale slot that is `DudeSid`: wrong script
  state, wrong LVARs.
- Script state is already keyed by sid: LVAR slices (`GetLocalVarSlice(map, sid, record)`, seeded from the
  record's `.SAV` offset), the VM cache (`(map, sid)`), and the trace's LVAR line (`ScriptContext._sid`).
- `RunMapUpdate` (`ScriptHost.cs:1175`) runs the map script, the spatials, then the caller's object list,
  which `WithDudeScript` prepends the dude to.

## Design

### 1. `ScriptHost.RunScriptProc`

```csharp
public ScriptRunResult? RunScriptProc(int sid, MapObject self, MapFile map, MapObject? dude,
    int fixedParam, int actionBeingUsed, params string[] procedureNames)
```

Looks up `map.ScriptsBySid[sid]` (null when absent or `sid == -1`) and calls `RunProc` with that sid and
`self`. `RunObjectProc(obj, ...)` becomes `RunScriptProc(obj.Sid, obj, ...)`, so there is one path.

### 2. Critter heartbeat runs the slot's sid

`PumpCritterProcs` calls `RunScriptProc(sid, owner, ...)` for the sid `Next()` returned. Map critters and
the dude are unchanged (their sid is their owner's sid); the stale slot now runs its own script instance.

### 3. map_update in fo2ce list order

`RunMapUpdate` runs, in order:
1. the map script (unchanged);
2. the spatials (unchanged);
3. scripted objects that are not on the critter list, in Hexwaste's object order (shortcut comment:
   fo2ce uses the item list's file order; upgrade if the oracle shows a difference there);
4. then `CritterScripts` in list order, by sid, through `RunScriptProc(sid, owner, ...)`.

The `procs[proc] > 0` filter (`DefinesProc`) stays on both object groups. Step 4 runs a list entry only
when the caller passed its owner, so the harness probes (which exclude the dude) keep their object sets;
the viewer keeps `WithDudeScript`, and the dude's sid, being on the list, moves from step 3 to step 4.

obj_dude.int also defines `map_enter_p_proc`, and fo2ce's map_enter is the same
`scriptsExecMapUpdateScripts` walk, so `RunMapEnter`'s object loop uses the same ordered helper.

Effect: the dude's map_enter and map_update now run last, and twice after a vanilla load (stale, then gDude), as in fo2ce.
This also changes live play (the dude's map_update moves from first to last).

### 4. Accepted deviations (code comments)

- fo2ce writes the stale script into the map's `.SAV` (it is not a flagged script), so it comes back on a
  revisit. Hexwaste drops it on a map change and after a Hexwaste save/reload. No oracle fixture revisits a
  map or reloads a Hexwaste save.
- The stale script's first run in fo2ce also runs its start code; that is F8 and applies to every script.

## Testing

- **Unit (hermetic, `ScriptHost`, hand-assembled `.int` + 12-byte empty DAT2 as in P115):** two sids whose
  records name the same script, run with the same `self`, keep separate LVARs; `RunScriptProc` writes to the
  sid it was given and seeds from that record's `LocalVarsOffset`.
- **Unit:** `RunMapUpdate` runs critter-list scripts after the other object scripts, in list order.
- **Oracle:** rerun all three fixtures. Success:
  - artemple and klamath move past lines 3 / 66;
  - the map_update round matches fo2ce (obj_dude twice, last);
  - the new first divergences are recorded in the findings doc.
- **Goldens:** run all suites. They never load a vanilla save, so only the map_update order can move them.
  Any moved fixture: run twice for determinism, review the diff, re-record, list it in the commit message.
- **CI steps** (ascii-lint, unit tests, Release build) pass.

## Docs

Findings doc: F16 FIXED; close F7's "obj_dude map_update twice" note; F15's "two obj_dude scripts" points
at the fix; new first divergences and next steps.

## Out of scope

- F8 (missing proc runs procedure 1, start code on first run).
- map_exit: Hexwaste's map_exit path is not touched (obj_dude.int does not define `map_exit_p_proc`).
- Item-list file order for map_update (see 3.3).
