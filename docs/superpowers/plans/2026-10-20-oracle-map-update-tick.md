# Oracle 600-tick map_update game-time event Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The 600-tick map_update round fires on the game clock right after the clock tick, like fo2ce's `EVENT_TYPE_MAP_UPDATE_EVENT`, so artemple-idle moves past trace line 5809.

**Architecture:** Replace the millisecond counter `_mapUpdateClockMs` with a scheduled game tick `_mapUpdateNextTick`, processed by a new `ProcessMapUpdateEvent()` in `UpdateClock` (and inline in `game_time_advance`), scheduled at map load and by the vanilla import. `PumpMapUpdate` keeps only the 30 s bk-time round.

**Tech Stack:** C# / .NET 10, MonoGame viewer, bash golden suites. Spec: `docs/superpowers/specs/2026-10-20-oracle-map-update-tick-design.md`.

## Global Constraints

- `long _mapUpdateNextTick = -1` (no event queued); `const int MapUpdateIntervalTicks = 600`.
- `ProcessMapUpdateEvent()` reschedules at `_clock.Ticks + MapUpdateIntervalTicks` BEFORE running the round; a late event fires once (no catch-up loop).
- Gates: the existing `PumpMapUpdate` early-return condition (script host, map, game-over, dialog, companion hub, worldmap) via a shared `MapUpdateAllowed()`, plus not in combat and no loot container open. A gated due event waits.
- Called in `UpdateClock` right after `ProcessAmbientSfx()`, and in the `game_time_advance` handler right after its `ProcessAmbientSfx()`.
- Map load: `_mapUpdateNextTick = _clock.Ticks + MapUpdateIntervalTicks;`. Vanilla import: `_mapUpdateNextTick = -1;` before the event loop (next to `_ambientNextTick = -1;`), `case 12`: `_mapUpdateNextTick = e.Time;`.
- Heartbeat probe output stays `map-update-heartbeat: beats=3 halfIntervalFires=0 totalFires=3`.
- Shortcut comments, verbatim:
  - `shortcut: a map-update event due on the same tick as a script timer or the ambient event runs after them; fo2ce's single queue orders them by insertion. Upgrade if the oracle shows a same-tick difference.`
  - `shortcut: fo2ce's midnight event (gameTimeEventProcess -> gameTimeScheduleUpdateEvent, scripts.cc:428) queues another map-update event at midnight + 600, and whichever fires first clears the other; Hexwaste keeps one chain. Upgrade when a fixture crosses midnight.`
- Port comments: `// ported from fallout2-ce src/x.cc f()`; alexbatalov e97087b is authoritative.
- No non-ASCII characters in C# string literals (CI runs `scripts/ascii-lint.sh`).
- No new dependencies. Never commit game assets.
- Commit trailers on every commit:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk
  ```

---

### Task 1: Map-update event on the game clock

**Files:**
- Modify: `src/Hexwaste.Viewer/ViewerGame.cs` (fields ~lines 156-157; `game_time_advance` handler ~line 1419; map load ~line 1915; `UpdateClock` ~line 6912)
- Modify: `src/Hexwaste.Viewer/ViewerGame.CombatHost.cs` (`PumpMapUpdate` ~lines 581-610)
- Modify: `src/Hexwaste.Viewer/ViewerGame.VanillaImport.cs` (reset ~line 207; `case 12` ~lines 229-232)
- Modify: `src/Hexwaste.Viewer/ViewerGame.Harness.cs` (`MapUpdateHeartbeatProbe` ~lines 528-540)

**Interfaces:**
- Produces: `private void ProcessMapUpdateEvent()`, `private bool MapUpdateAllowed()`, `long _mapUpdateNextTick`, `const int MapUpdateIntervalTicks`.

There is no unit test (viewer glue, no test project for the viewer); the pinned heartbeat probe in the encounter golden suite and the oracle (Task 2) check it.

- [ ] **Step 1: Fields** (`ViewerGame.cs` ~156-157). Replace

```csharp
    private double _mapUpdateClockMs;
    private const double MapUpdateIntervalMs = 600 * 100.0; // 600 game ticks
```

(keep any doc comment above them, updated to say "game tick") with

```csharp
    private long _mapUpdateNextTick = -1; // EVENT_TYPE_MAP_UPDATE_EVENT time; -1 = none queued
    private const int MapUpdateIntervalTicks = 600;
```

- [ ] **Step 2: `PumpMapUpdate` keeps only the bk-time round; add `MapUpdateAllowed` and `ProcessMapUpdateEvent`** (`ViewerGame.CombatHost.cs`). Replace the whole `PumpMapUpdate` method and its doc comment with:

```csharp
    /// <summary>Script host, map and normal map play (not game-over, dialog, the companion hub or the
    /// worldmap): both map_update sources need it.</summary>
    private bool MapUpdateAllowed() =>
        _scriptHost is not null && _map is not null && !_combat.IsGameOver && _dialog is null
        && _companionHub is null && !_worldmapOpen;

    /// <summary>The 30 s bk-time map_update round (fallout2-ce scripts.cc:758-760, _script_chk_timed_events):
    /// runs on elapsed ms and keeps running in combat. The 600-tick round is the game-time event in
    /// <see cref="ProcessMapUpdateEvent"/>.</summary>
    private void PumpMapUpdate(double elapsedMs)
    {
        if (!MapUpdateAllowed())
            return;

        _bkMapUpdateMs += elapsedMs;
        if (_bkMapUpdateMs >= BkMapUpdateIntervalMs)
        {
            _bkMapUpdateMs = 0; // _last_light_time = now: the excess is dropped
            RunMapUpdateRound();
        }
    }

    /// <summary>P1-M2 / oracle: the recurring 600-tick map_update heartbeat as fo2ce's game-time queue event
    /// (EVENT_TYPE_MAP_UPDATE_EVENT): processed right after the clock tick, before that frame's critter proc
    /// (scripts.cc:774-781). Lets quests that POLL state in map_update_p_proc (klagraz KCTorr's brahmin
    /// defense, the Den gang-war escalation) re-evaluate over time. Not in combat (the clock is frozen there)
    /// or with a loot container open; a gated due event waits.</summary>
    // shortcut: a map-update event due on the same tick as a script timer or the ambient event runs after them; fo2ce's single queue orders them by insertion. Upgrade if the oracle shows a same-tick difference.
    // shortcut: fo2ce's midnight event (gameTimeEventProcess -> gameTimeScheduleUpdateEvent, scripts.cc:428) queues another map-update event at midnight + 600, and whichever fires first clears the other; Hexwaste keeps one chain. Upgrade when a fixture crosses midnight.
    // ported from fallout2-ce src/scripts.cc mapUpdateEventProcess(): run the round, reschedule at now + 600
    private void ProcessMapUpdateEvent()
    {
        if (_mapUpdateNextTick < 0 || _clock.Ticks < _mapUpdateNextTick || !MapUpdateAllowed()
            || _combat.Phase != Formats.Combat.CombatPhase.Idle || _lootContainer is not null)
            return;
        _mapUpdateNextTick = _clock.Ticks + MapUpdateIntervalTicks; // before the round: a round that moves the clock does not refire it
        _mapUpdateFires++;
        RunMapUpdateRound();
    }
```

Keep `RunMapUpdateRound` and `_mapUpdateFires` as they are.

- [ ] **Step 3: Call sites** (`ViewerGame.cs`)
- `UpdateClock` (~line 6912): after `ProcessAmbientSfx(); // oracle F13: ...` add
  ```csharp
          ProcessMapUpdateEvent(); // the 600-tick map_update event fires on game time
  ```
- `GameTimeAdvanceRequested` lambda (~line 1419): after `ProcessAmbientSfx(); // oracle F13: queueProcessEvents runs inline, before the script continues` add
  ```csharp
                      ProcessMapUpdateEvent(); // same inline queueProcessEvents
  ```
- Line ~3013 in `Update`: the comment `// P1-M2: recurring 600-tick map_update` on the `PumpMapUpdate(...)` call becomes `// 30 s bk-time map_update round`.

- [ ] **Step 4: Map load** (`ViewerGame.cs` ~line 1915): replace `_mapUpdateClockMs = 0;` with

```csharp
            _mapUpdateNextTick = _clock.Ticks + MapUpdateIntervalTicks; // map.cc:1022 gameTimeScheduleUpdateEvent
```

and in the comment block above it change "the clock resets here so the first heartbeat lands 600 ticks after load" to "the first heartbeat is scheduled 600 game ticks after load".

- [ ] **Step 5: Vanilla import** (`ViewerGame.VanillaImport.cs`)
- Next to `_ambientNextTick = -1;` (~line 207) add
  ```csharp
          _mapUpdateNextTick = -1; // queueLoad replaces the queue: drop the event the import's map load scheduled
  ```
- `case 12` (~lines 229-232) becomes
  ```csharp
                  case 12: // EVENT_TYPE_MAP_UPDATE_EVENT: the next map_update heartbeat lands on the saved time
                      _mapUpdateNextTick = e.Time;
                      break;
  ```

- [ ] **Step 6: Heartbeat probe** (`ViewerGame.Harness.cs` ~528-540): replace the two `PumpMapUpdate(...)` calls so the probe drives game ticks:

```csharp
                    int start = _mapUpdateFires;
                    _clock.Ticks += MapUpdateIntervalTicks / 2;
                    ProcessMapUpdateEvent();
                    int halfIntervalFires = _mapUpdateFires - start;
                    for (int i = 0; i < beats; i++)
                    {
                        _clock.Ticks += MapUpdateIntervalTicks;
                        ProcessMapUpdateEvent();
                    }
                    int totalFires = _mapUpdateFires - start;
```

Update its comment: "P1-M2: prove the recurring 600-tick map_update heartbeat's cadence on the game clock."

- [ ] **Step 7: Check no old names remain**

Run: `grep -rn "_mapUpdateClockMs\|MapUpdateIntervalMs" src/`
Expected: no output.

- [ ] **Step 8: Build, test, goldens**

```bash
FALLOUT2_DIR=/home/eko/dev/FPOC/game-data dotnet test tests/Hexwaste.Formats.Tests -v q --nologo
scripts/ascii-lint.sh
dotnet build src/Hexwaste.Viewer -c Release -v q -nologo
```

Expected: all tests pass (report counts), lint clean, 0 errors. Then run the golden suites SEQUENTIALLY (never two at once, never alongside an oracle run), each in check mode (each can take minutes):

```bash
for s in combat quest endgame opening encounter; do scripts/$s-golden.sh > scratch/mu-golden-$s.out 2>&1; echo "$s rc=$?"; done
```

Expected: all rc=0, and `grep map-update-heartbeat scratch/mu-golden-encounter.out` shows the fixture passing. The encounter suite has a known PRE-EXISTING native crash flake (SIGSEGV/SIGABRT, empty fixture output); if only encounter fails that way, rerun it once and report both runs. If any fixture's output changed (not a crash), STOP and report the fixture names and the diff; do not re-record.

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Viewer/ViewerGame.cs src/Hexwaste.Viewer/ViewerGame.CombatHost.cs src/Hexwaste.Viewer/ViewerGame.VanillaImport.cs src/Hexwaste.Viewer/ViewerGame.Harness.cs
git commit -m "fix(scripts): the 600-tick map_update runs as fo2ce's game-time event, right after the clock tick

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk"
```

---

### Task 2: Oracle rerun + findings doc

**Files:**
- Modify: `docs/research-notes/oracle-m1-findings-2026-10.md` (F7 section ~line 203, F18 section, "Run results" ~line 9, "Recommended next steps")

**Interfaces:**
- Consumes: Task 1 (no code changes in this task).

- [ ] **Step 1: Run the oracle, sequentially** (each a few minutes; timeout 600000 ms)

```bash
scripts/oracle-run.sh artemple-idle  > scratch/oracle-runs/artemple-idle-run.out 2>&1
scripts/oracle-run.sh klamath-hour   > scratch/oracle-runs/klamath-hour-run.out 2>&1
scripts/oracle-run.sh arcaves-ambush > scratch/oracle-runs/arcaves-ambush-run.out 2>&1
pgrep -f build-oracle/fallout2-ce && pkill -9 -f build-oracle/fallout2-ce
```

Take each first divergence from the TraceDiff output in the `*-run.out` file (a plain diff of the trace files gives different line numbers). Expected: artemple past line 5809; klamath still 1664 (`MOVE obj#26 17500->17701` vs `kcitizen critter_p_proc obj#156`); arcaves still 9. If any fixture's first divergence moves EARLIER, or artemple still differs on a `map_update_p_proc` line, STOP and report the two lines (do not change code). Also compare the map_update ticks and their position within the tick in both klamath traces:

```bash
for e in fo2ce hexwaste; do grep "kladwtwn.int map_update_p_proc" scratch/oracle-runs/klamath-hour/$e.trace | cut -d' ' -f1 | sort | uniq -c | wc -l; done
```

(the counts of distinct map_update ticks should be equal).

- [ ] **Step 2: Update the findings doc**

- F7 section: append a line: the 600-tick source now reads the game clock (`_mapUpdateNextTick`, processed right after the clock tick like fo2ce's queue), replacing the ms count that ran 90 ms out of phase (artemple t=303000: fo2ce line 1 of the tick, Hexwaste line 10). Remove or update the sentence "The 600-tick source still counts ms rather than reading the game clock".
- F18 section: add that fo2ce's map-update event also keeps running during worldmap travel (the map stays loaded), which Hexwaste's worldmap gate skips.
- Run results / Recommended next steps: the new first divergences from Step 1, with the two differing lines for any fixture that moved.

- [ ] **Step 3: Commit**

```bash
git add docs/research-notes/oracle-m1-findings-2026-10.md
git commit -m "docs(oracle): 600-tick map_update on the game clock; new first divergences

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NgVzN5ZE2i9K4evL7wHGbk"
```
