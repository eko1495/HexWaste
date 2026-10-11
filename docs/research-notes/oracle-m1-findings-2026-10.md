# Trace oracle M1 — findings (2026-10)

Source: `scripts/oracle-run.sh <fixture>` for the three M1 fixtures (`artemple-idle`, `klamath-hour`,
`arcaves-ambush`), fo2ce = trace-patched `alexbatalov e97087b`, Hexwaste = `feat/trace-oracle` @ `3297b99`
(no engine changes in this task). Traces live in `scratch/oracle-runs/<fixture>/{fo2ce,hexwaste}.trace`.
Spec/plan: `docs/superpowers/specs/2026-10-10-fo2ce-trace-oracle-design.md`,
`docs/superpowers/plans/2026-10-10-fo2ce-trace-oracle.md`.

## Run results

| fixture | scenario | fo2ce lines | Hexwaste lines | first divergence |
| --- | --- | --- | --- | --- |
| artemple-idle | seed, wait 60 s | 6 026 | 605 | line 2 |
| arcaves-ambush | seed, wait 5 s, end-turn ×5 | 510 | 12 366 | line 2 |
| klamath-hour | seed, wait 1 h | 589 340 | 301 940 | line 2 |

First divergent pairs (TraceDiff, `-` fo2ce / `+` Hexwaste):

```
artemple-idle   - t=302420 PROC obj_dude.int critter_p_proc self=obj#18000
                + t=302474 PROC acklint.int critter_p_proc self=obj#24
arcaves-ambush  - t=302400 PROC zclscorp.int critter_p_proc self=obj#590
                + t=302412 PROC zclrat.int timed_event_p_proc self=obj#126
klamath-hour    - t=302420 PROC kcdunton.int critter_p_proc self=obj#26
                + t=302462 PROC zcbrahmn.int timed_event_p_proc self=obj#30
```

All three first divergences are PROC lines (no `site=` field): the two engines disagree about *which script
runs next* before any RNG draw is compared. That is findings F2 (game clock), F3 (obj_dude script) and F6
(critter cadence/order) — **lockstep first-divergence is currently masked by those earlier engine
differences**, so the oracle cannot yet localise a later bug (e.g. a to-hit drift) as *the* first
fo2ce-vs-Hexwaste divergence. See "Planted-bug check" for how detection was demonstrated anyway.

All engines exited 0 on all fixtures (sound off; `--no-audio`).

## Planted-bug check

Planted `+1` on the accuracy in `CombatEngine.RollAttack` (`ComputeToHit(...) + locPenalty + 1`), rebuilt,
ran `arcaves-ambush` in Hexwaste only, reverted with `git checkout -- src/Hexwaste.Formats/Combat/CombatEngine.cs`
(`git status` clean of it afterwards; rebuilt clean).

**(a) Hexwaste clean vs Hexwaste planted.** First established that two clean Hexwaste runs are
byte-identical (`IDENTICAL (12365 lines)`). Clean vs planted:

```
DIVERGED at line 8469
  ... t=306372 PROC zclscorp.int combat_p_proc self=obj#98
      t=306372 RNG 1..10000 -> 6943 site=AiCalledShot:Pick
      t=306372 RNG 1..100 -> 85 site=CombatEngine:RollAttack
- clean:   t=306372 ATTACK obj#98->obj#18000 chance=58 hit=0 dmg=0 dflags=0x0
+ planted: t=306372 ATTACK obj#98->obj#18000 chance=59 hit=0 dmg=0 dflags=0x0
```

The oracle catches a ±1 hit-chance drift: **yes — exactly at the first ATTACK line (line 8469), `chance`
58 vs 59**. No RNG line precedes it in the divergence: the accuracy is computed *after* the called-shot pick
and does not change how many draws `RollAttack` makes (the d100 roll is drawn regardless; the crit roll only
on a hit, and roll 85 misses at either chance), so the two streams stay aligned up to the ATTACK line.

**(b) ATTACK-only projection, fo2ce vs clean Hexwaste** (`grep ' ATTACK '` of both traces, for context):

```
DIVERGED at line 2
- fo2ce:    t=302451 ATTACK obj#98->obj#18000 chance=58 hit=0 dmg=0 dflags=0x0
+ hexwaste: t=306372 ATTACK obj#98->obj#18000 chance=58 hit=0 dmg=0 dflags=0x0
```

With timestamps stripped the first attack is identical; fo2ce then has 7 attacks (obj#98 ×4, obj#70 ×3),
Hexwaste 4 (obj#98 only). Every obj#98 attack has `chance=58` in both engines — the to-hit formula agrees
for this attacker/defender pair. The planted run would show 59 vs 58 on that same first attack, but the
timestamp and the attack sequence already differ (F2, F5), so in a fo2ce comparison the drift is visible
only by projection, not as the lockstep first divergence.

## Findings (ranked by likely fidelity impact — fix top-down)

Status of every entry: **open**. "Evidence" is a trace pair/statistic where the M1 traces show it, or a code
reading where the fixtures do not exercise it.

### F1 — `create_object_sid` binds the wrong script (off by one) — FIXED

- **Fixed:** `IntVm` 0x80B7 now maps sid → `sid - 1` (-1 stays unscripted). klamath-hour re-run:
  `scwesgrd.int` 0 calls (was 52 558); the dung now runs `zibrapoo.int`. Its call count is still
  inflated by F2. No golden fixture moved (combat/opening/endgame/quest pass; encounter only shows
  the pre-existing nondeterminism).

- Fixture: klamath-hour.
- Evidence: Hexwaste runs `scwesgrd.int map_update_p_proc self=obj#-3` 52 558 times; fo2ce never runs
  `scwesgrd.int`. The brahmin script spawns its dung with `create_object_sid(pid, …, sid)`; scripts.lst
  index (sid − 1) is `zibrapoo.int`, index sid is `scwesgrd.int` (the next line).
- Hypothesis: fo2ce `opCreateObject` stores `script->index = sid - 1` (`interpreter_extra.cc:931`); Hexwaste
  `IntVm` 0x80B7 passes the popped value straight through as the scripts.lst index (`IntVm.cs:1585`
  → `ScriptHost.CreateObject` → `AllocateSid`). Every runtime-created scripted object in the game gets the
  following script in scripts.lst. One-line fix, but it changes behavior everywhere scripts spawn scripted
  objects → expect golden re-records.

### F2 — Game clock: 60× rate and keeps running in combat — FIXED

- **Fixed:** `GameClock.AdvanceRealTime` ports `_script_chk_timed_events`: +1 tick once ≥100 ms have passed
  (the window restarts, the first check fires at once since `_last_time__` starts at 0), frozen in combat.
  Re-run: artemple-idle and klamath-hour now span exactly the same ticks as fo2ce (303020 / 338420), and the
  line-2 divergence happens at the same timestamp in both engines. timed_event_p_proc is still 1 620 vs 2 731
  in klamath (now down to F3/F6). No golden fixture moved.

- Fixtures: all. artemple-idle 60 s: fo2ce advances 600 ticks (302420→303020), Hexwaste 36 000
  (302474→338414). klamath-hour 1 h: fo2ce 36 000 ticks, Hexwaste 2 160 000. arcaves-ambush: fo2ce game time
  is frozen in combat (timed events fire only at the +50-tick round boundaries, 42 per round), Hexwaste fires
  5 571 `timed_event_p_proc` while in combat and a round spans ~480 ticks instead of 50.
- Knock-on: klamath brahmin dung spawns 1 704 (Hexwaste) vs 36 (fo2ce); timed_event_p_proc 52 557 vs 2 731.
- Hypothesis: fo2ce `scriptsHandleRequests`/bk loop adds 1 tick per ≥100 ms and only when `!isInCombat()`
  (`scripts.cc:766-771`); Hexwaste `GameClock.IdleRate = 60` (`GameClock.cs:26`, 1 game-minute per real
  second) and advances during combat. The single biggest source of divergence; changes all time-driven
  behavior (timers, healing, spawns, day/night).

### F3 — `obj_dude.int` never runs in Hexwaste — FIXED

- **Fixed:** `ScriptHost.BindDudeScript` binds obj_dude.int (scripts.lst 0) to the dude on every map load
  under a fixed synthetic sid (`DudeSid`); its LVARs are not keyed by map (SCRIPT_FLAG_0x10). The dude now
  joins the start/map_enter/map_update/map_exit passes, the critter_p_proc heartbeat, his per-turn
  combat_p_proc (combat.cc:3243; an override skips his turn), the on-hit combat_p_proc, and
  `is_dropping_p_proc` before drop_p_proc. Re-run: artemple line 2 is now the same proc in both engines
  (`obj_dude.int critter_p_proc self=obj#18000`, one tick apart: F6 cadence); arcaves dude combat_p_proc
  6 = 6, TURN 16 = 16. Call counts still differ by the F6 cadence (artemple critter_p_proc 4 000 vs 300).
  map_update cadence: see F7 (fo2ce 3 rounds/min, Hexwaste 1; an earlier "every ~10 s" reading here was wrong).
  Golden moved: companion-lifecycle (heartbeatEligible +1 = the dude).

- Fixtures: all. artemple-idle: 4 000 of 6 014 fo2ce PROC lines are `obj_dude.int critter_p_proc`, plus
  GVAR 104 writes (589 in klamath-hour) and obj_dude `map_update_p_proc`/`combat_p_proc`/`damage_p_proc`;
  Hexwaste has zero `obj_dude` lines anywhere.
- Hypothesis: fo2ce attaches obj_dude.int to the dude (`scriptsSetDudeScript`, `scripts.cc:1460-1488`, proto
  sid 0x4000000) so it is in the critter list and the map-update/combat passes; Hexwaste deliberately gives
  the dude no script (`ViewerGame.cs:4055-4057`) and filters him out of map_update (`ViewerGame.CombatHost.cs:598`).

### F4 — arcaves combat starts later, with a different initiator, and obj#70 never fights

- Fixture: arcaves-ambush. fo2ce: obj#70's `critter_p_proc` starts combat at t=302401 (`TURN obj#70` first),
  rounds obj#70 / dude / obj#98, 7 attacks. Hexwaste: combat starts at t=302700 after obj#98's
  `critter_p_proc` (t=302694), only obj#98 takes turns, 4 attacks; the dude's first two TURNs are 2 700
  ticks apart with no enemy TURN in between.
- Hypothesis: the initiator follows from F6 (critter order/cadence) and F2; the missing obj#70 points at
  joiner handling — see F5.

### F5 — Combat joiners get no AP and no immediate turn

- Fixture: arcaves-ambush (contributes to F4).
- Evidence (code): fo2ce `_combat_add_noncoms` sets the joiner's AP to its max (+ `_gcsd` bonus) and calls
  `_combat_turn(obj, false)` at once (`combat.cc:2905-2930`); Hexwaste `CombatEngine.AddJoiners`
  (`CombatEngine.cs:2681`) only adds the critter to `_hostiles`.

### F6 — critter_p_proc cadence and order — FIXED

**FIXED.** `ScriptHost.CritterScripts` (`CritterScriptList`) holds the critter sids in fo2ce's order
(file order on load, append on add, tail-into-slot on remove, the stale .SAV obj_dude slot plus the dude after a vanilla save load) and
`PumpCritterProcs` runs one per frame. After a vanilla save load, the .SAV critter sid that no map object
holds and whose script record is obj_dude (scripts.lst index 0; 0x04004650 = 0x04000000 | dude id 18000 in
all three fixtures) is kept at its section position, owned by the dude, and `BindDudeScript` then appends
the dude's own sid, as fo2ce does. No round-robin counter adjustment was needed: Hexwaste's first `Next()`
already lands on slot 1, as fo2ce's does. The critter_p_proc order (first 100, script + `self=`) now matches
fo2ce in artemple-idle, klamath-hour and arcaves-ambush. New first divergences (all were line 2):
artemple-idle line 3 and klamath-hour line 66 (fo2ce's stale obj_dude slot writes `LVAR sid=0x04004650`,
see F16); arcaves-ambush line 9 (fo2ce `LVAR sid=0x04000008 0 0->-3`, Hexwaste
`PROC zclscorp.int combat_p_proc self=obj#102`). Still unverified: the list order after a map change (no
oracle fixture changes maps). No golden fixture moved (the suites record probe/final-state lines, and
combat scenarios gate the heartbeat off); golden runtime 91.55 s -> ~96 s.

Because the heartbeat now also runs critters on other elevations, the cross-floor gates were ported:
`obj_can_see_obj` / `obj_can_hear_obj` return false and `tile_distance_objs` returns 9999 unless both
objects share an elevation (`interpreter_extra.cc:1793` / `:2631` / `:1498`).

Known deviations:
- Critter procs stop during loot, the companion hub and the worldmap. fo2ce's `_doBkProcesses` ticker keeps
  running there; only dialog, combat and movies stop it.
- Script-driven walks (`MoveRequested` / reg_anim moves) of a critter not on the dude's elevation are
  refused. fo2ce animates it on its own elevation; Hexwaste's walker only models the current one (blocked
  tiles, draw list, spatials). Float lines from such critters are also kept out of the message log.

Original finding:

- Fixtures: all. fo2ce runs one critter script per `tickersExecute` pass (100 Hz on the oracle's 10 ms
  virtual clock): 359 999 critter_p_proc in klamath-hour; Hexwaste pumps at 10 Hz: 36 000. Order also
  differs: fo2ce walks the critter *script list* (arcaves: 590, 126, 130, 146, 206, 126, 70…), Hexwaste
  walks the current elevation's solid objects (102, 206, 118, 118, 98…); fo2ce's list spans all elevations.
- Hypothesis: fo2ce `_script_chk_critters` (`scripts.cc:705-742`, `_count_` round-robin over
  `gScriptLists[SCRIPT_TYPE_CRITTER]`); Hexwaste `PumpCritterProcs` at the 10 Hz game tick
  (`ViewerGame.CombatHost.cs:544`). Note vanilla's cadence is frame-rate-bound; the oracle's 100 Hz is a model choice
  (F15), so match *order* first and make the rate a parameter.
- Checked (F6 Task 1, corrected in Task 6): after `--load-vanilla-save` (artemple-idle) fo2ce's critter
  list is three slots: `0 sid=0x04000001 flags=0x5` (acklint, obj#24, held by a map object),
  `1 sid=0x04004650 flags=0x18` (a stale obj_dude script record from the .SAV, held by no map object;
  0x4650 = 18000, the dude's object id), `2 sid=0x04000005 flags=0x18` (gDude->sid). Task 1's text guessed
  slot 0 was the stale dude entry; that was wrong (Task 6 read the .SAV: 0x04000001 is acklint, 0x04004650's
  record is scripts.lst index 0 = obj_dude.int, and SAVE.DAT's dude carries sid -1). `scriptsSetDudeScript`
  printed (`oracle-set-dude`) five times in the run, with sids 0x04000000, 0x04000001, 0x04000002,
  0x04000004 and 0x04000005, the last being gDude->sid; which call sites made the first four is unknown
  (not traced). fo2ce's `dude, dude, acklint, ...` is slots 1, 2, 0: the round-robin starts at slot 1.
  Golden baseline before F6: 91.55 s (combat 8.06, opening 7.14, endgame 3.77, quest 15.41,
  encounter 57.17).

### F7 — map_update cadence and the "has the proc" filter

**FIXED** (bk-time source, procs filter, spatials first). Hexwaste adds the 30 000 ms bk-time round
(`ViewerGame.CombatHost.cs` PumpMapUpdate, runs in combat, never reset on map load); `ScriptHost.RunMapUpdate`
skips scripts without `map_update_p_proc` and runs spatials before objects; the trace patch starts fo2ce's
window at save-load completion (`oracleResetBkMapUpdate`, from `getTicks()` — `_get_bk_time()` is stale
there). klamath-hour and artemple-idle now run map_update at the same ticks (180/h) with the same script
set per round. fo2ce's second `obj_dude.int map_update_p_proc` per round is explained by F16 (the stale slot), fixed;
the order now matches. The 600-tick source still counts
ms rather than reading the game clock (the map-update heartbeat probe feeds it ms directly).

- Fixture: klamath-hour. fo2ce runs 180 map_update rounds per hour (gaps alternate 6/294/300 ticks),
  Hexwaste 60. Hexwaste also runs `map_update_p_proc` for `ksbboard.int`, `stuff.int`, `trespass.int`
  (60 each), which fo2ce never runs.
- Hypothesis: fo2ce has two sources — the 600-tick `EVENT_TYPE_MAP_UPDATE_EVENT` (`scripts.cc:509-517`) and
  a 30 000 ms bk-time pass (`scripts.cc:758-760`) — Hexwaste only the first (`MapUpdateIntervalMs`,
  `ViewerGame.cs:160`). fo2ce `scriptsExecMapUpdateScripts` only calls scripts whose `procs[proc] > 0`
  (`scripts.cc:2642`); Hexwaste calls every scripted object (`ScriptHost.RunMapUpdate`, `ScriptHost.cs:1100`).
- Checked (after F1-F3): confirmed. klamath-hour fo2ce rounds land at t=…694/…994 (bk, every 300 ticks)
  and …000/…600 (queue, every 600 ticks): 120 + 60 = 180/h. The bk timer (`_doBkProcesses` ticker) runs
  whenever iso is enabled and no dialog/movie is up — including combat and inventory — and
  `_last_light_time` is never reset, so its phase is time since process start, not since map load.
  The extra Hexwaste lines are trace-only: `kcitizen`, `ksbboard`, `stuff`, `trespass` and the other
  extras define no `map_update_p_proc`, but `ScriptHost.TraceProc` logs before `TryRunProcedure` finds
  the proc missing (fo2ce returns before its PROC line). fo2ce also runs `obj_dude.int map_update_p_proc`
  twice per round (the stale slot, F16 - fixed), and puts spatial scripts before object scripts (script-type list order); Hexwaste runs
  the dude once and spatials last.

### F8 — Missing proc → fo2ce runs procedure 1; Hexwaste runs nothing

- Evidence (code; the PROC line matches, the side effects do not): fo2ce `scriptExecProc` maps
  `procs[proc] == 0` to procedure index 1 and also runs the start code on a script's first execution
  (`scripts.cc:1306-1338`); Hexwaste `TryRunProcedure` returns false for a missing proc and `RunStart` runs
  only the global-init prologue, never the start body (`ScriptHost.cs:946-1024`).

### F9 — `random(min, max)` with min ≥ max does not draw

- Evidence (code; no degenerate `RNG a..a` draw in any M1 trace yet): fo2ce `opRandom` → `randomBetween`
  always calls `getRandom`, which always advances the generator (`interpreter_extra.cc:782`,
  `random.cc:134-172`, and it swaps a reversed range); Hexwaste returns `min` without drawing
  (`IntVm.cs:1343`). Shifts the shared stream by one draw per occurrence.

### F10 — Saved-timer tie order after load

- Evidence (code): fo2ce `queueLoad` re-inserts pre-existing (map_enter-created) events *after* saved events
  of equal time (`queue.cc:171-184`), so saved events win same-tick ties; Hexwaste imports saved timers
  via `AddTimer` after map_enter, appending them behind (`ViewerGame.VanillaImport.cs:170-188`).

### F11 — Dude position and sheet during load-time map_enter

- Evidence (code): fo2ce loads the map (`_SlotMap2Game`, `loadsave.cc:246`) with the dude at the
  entering/centre tile (`map.cc:928`) and a post-reset sheet, runs map_enter (`map.cc:968`), and only then
  restores the saved dude (`_obj_load_dude`, `critterLoad`, `statsLoad`: `loadsave.cc:248-256`). Hexwaste
  spawns him on his saved tile/rotation first (`ViewerGame.VanillaImport.cs:78-81`). Matters for map_enter
  scripts that test the dude's tile, distance or stats.

### F12 — Object ids of synthetic/runtime objects

- Fixtures: all. Map-script owner: fo2ce `obj#16` vs Hexwaste `obj#-2` (artemple, kladwtwn); spatial owner
  `obj#-1` vs `obj#-5`; runtime spawns fresh ids (obj#18, #22, #26…) vs a constant `obj#-3`.
- Hypothesis: fo2ce gives the map-script object and every new object an id from `scriptsNewObjectId`
  (`map.cc:965`, `scripts.cc:526-549`), which increments `_cur_id` twice per call and only checks the first
  value for collisions (`scripts.cc:531,547`) — vanilla can hand out an id already in use (the arcaves save
  already holds duplicates: obj#126 carries two scripts, obj#118 two). Hexwaste uses constants
  (`ScriptHost.cs:979,1066,1106` `-2`, `:2474` `-3`). Mostly a trace-alignment issue, but scripts compare object ids.

### F13 — Ambient-sfx RNG draws missing

- Fixture: artemple-idle — the only RNG lines in fo2ce's trace are `wmSfxRollNextIdx` 0..100 and
  `ambientSoundEffectEventProcess` 15..20 (t=302550, 302700, 302880); Hexwaste draws none.
- Hypothesis: fo2ce schedules ambient sfx on the game-time queue and draws even with sound off
  (`game_sound.cc:2112-2127`, `worldmap.cc:6083`); Hexwaste's `_ambientRng` is wall-time-driven and behind
  `_audio` (`ViewerGame.cs:1974-1985`), and the importer skips the saved type-13 event. Shifts the shared
  stream by 2 draws per ambient event.

### F14 — ATTACK `dflags` pre-masked in Hexwaste

- Evidence (code; every M1 ATTACK has `dflags=0x0`): Hexwaste crit flags are masked by
  `CriticalTables.HonoredFlags` (`CriticalTables.cs:53`) before `TraceAttack`, so flags fo2ce sets but Hexwaste
  does not honour (drop, lose-ammo, random-hit, explode, on-fire…) cannot show as a dflags divergence — they
  are dropped, not compared. Latent until a crit fixture exists.

### F15 — fo2ce-side observations (not Hexwaste bugs; needed for lockstep)

- **Two obj_dude scripts after load.** fo2ce's first LVAR writes come from sid 0x04004650 and 0x04000005
  (artemple) / 0x04000028 (klamath), both `self=obj#18000`; obj_dude gets exactly 2 of every 3 critter
  slots (artemple 4 000 vs acklint 1 999). Cause: the stale .SAV obj_dude record plus the dude's own
  script; see F16 (explained and fixed with F16).
- **Render-only frames advance the virtual clock 10 ms** (`oracleRenderGuard`, see
  `tools/fo2ce-trace/README.md`). An oracle-model artefact with no Hexwaste equivalent; affects only waits
  inside render-only loops (end-turn button animation, dialog fidgets).

### F16 — Stale obj_dude script after a save load runs with its own state — FIXED

- Fixtures: artemple-idle (fo2ce line 3), klamath-hour (fo2ce line 66); the stale sid is 0x04004650 in
  all three fixtures.
- Evidence: fo2ce's run of the stale slot writes under its own sid —
  ```
  - fo2ce:    t=302420 LVAR sid=0x04004650 0 0->302420
  + hexwaste: t=302421 PROC obj_dude.int critter_p_proc self=obj#18000
  ```
  (artemple, which also writes `GVAR 104 1->2`); klamath-hour `- fo2ce: t=302424 LVAR sid=0x04004650 0
  0->302424`. The gDude run then writes `LVAR sid=0x04000005`. Hexwaste runs that slot (owner = the dude)
  with the dude's own script and LVARs, so it emits neither line.
- Hypothesis: fo2ce keeps the .SAV record as a separate `Script` with its own LVAR block (the .SAV record has 5
  locals) that runs obj_dude.int against `self=obj#18000`; Hexwaste reuses the dude's script state.
  Porting it means giving the stale slot its own script instance. Also F15's "two obj_dude scripts".
- Status: FIXED (see below). Still open: the critter list order after a map change (F6).
- **Fixed:** (1) `ScriptHost.RunScriptProc(sid, self, ...)`: the critter heartbeat runs the sid it took from the
  critter list, so a slot whose owner holds a different sid runs its own LVARs/VM. (2) map_enter/map_update
  (`ScriptHost.RunObjectScriptsInListOrder`): non-critter-list object scripts first, then the critter list in
  list order, by sid (fo2ce `scriptsExecMapUpdateScripts`); the dude's map_enter/map_update now run last.
  (3) Vanilla import models fo2ce `_obj_load_dude` (`ScriptHost.ReplaceDudeScriptAfterVanillaLoad`): during the
  load passes the .SAV's obj_dude record (sid 0x04004650) is the dude's script (its saved LVARs seed the dude's
  slice); afterwards the dude gets a fresh script appended last and the stale 0x04004650 slot stays at its
  critter-section position, fresh (no LVAR block, program not loaded). Each runs obj_dude's `start` body on its
  first execution during play (top-level code jumps into start: `if LVAR0 == 0 { LVAR0 := game_time;
  GVAR 104++ }`). Grounded by a temporary fo2ce debug dump (reverted). Result: artemple-idle first divergence
  line 3 -> 6, klamath-hour 66 -> 69; both are now only the sid label (F17). The map_update round matches
  fo2ce (artemple, animfrvr obj#14, animfrvr obj#18, obj_dude obj#18000 x2; only the map script self
  obj#16 vs obj#-2 differs = F12). Accepted deviations: the stale slot is dropped on a map change and after a
  Hexwaste save/reload (fo2ce saves it into the map .SAV and it returns on revisit); no fixture covers either.

### F17 — Dude script sid label

- Fixtures: artemple-idle (line 6), klamath-hour (line 69).
- Evidence: fo2ce `LVAR sid=0x04000005 0 0->302421` (klamath `0x04000028 ... 0->302424`) vs hexwaste
  `LVAR sid=0x04800000`.
- Cause: fo2ce allocates the dude's script sid from the critter list's id counter at `scriptsSetDudeScript`
  (0x04000005 artemple, 0x04000028 klamath after load); Hexwaste uses a fixed `DudeSid` 0x04800000. Values
  match; only the trace label differs. Scripts cannot observe a sid, so this is trace alignment (like F12).
- Status: open. Options: a label mapping in the trace, or allocate the dude sid like fo2ce.

## Recommended next steps

1. Fix F1 (one line, `sid - 1`), re-record the goldens it moves, re-run the oracle.
2. Decide the clock model (F2): an oracle-only switch to the authentic 1 tick per 100 ms, frozen in combat,
   is enough to make traces comparable; the gameplay rate is a separate design question.
3. Done: attach obj_dude.int (F3), the map_update sources/filter (F7) and critter-list order + cadence
   (F6). The first divergence is now off the critter order (artemple line 3, klamath line 66, arcaves
   line 9); F16 (stale obj_dude slot) is done. Next: F17 (trace-label alignment) and the arcaves line 9
   difference (fo2ce `LVAR sid=0x04000008` vs hexwaste `zclscorp combat_p_proc`).
4. Then the combat joiner path (F5) and re-check F4, then F8-F14. The start-body-on-first-run timing
   (obj_dude `start` executes on first run) is part of F8 in general.
5. F8–F13 are small, independent ports; F12/F14 first need trace-alignment decisions. Each fix should be
   validated by a fresh `oracle-run.sh` showing the first divergence moving later.
