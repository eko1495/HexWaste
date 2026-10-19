# Oracle F13: ambient sfx as a game-time event

Finding F13 in `docs/research-notes/oracle-m1-findings-2026-10.md`. After F17, artemple-idle first diverges
at trace line 1297: fo2ce draws `RNG 0..100 site=worldmap.cc:wmSfxRollNextIdx` where Hexwaste runs the
next critter proc. Hexwaste never draws the ambient-sfx numbers, so the shared stream falls behind.

## fo2ce behavior (reference: alexbatalov e97087b)

- Ambient sfx is a game-time queue event, `EVENT_TYPE_GSOUND_SFX_EVENT` (type 13), handled by
  `ambientSoundEffectEventProcess` (`game_sound.cc:2102`):
  1. `_queue_clear_type(13)`: drop any other ambient event.
  2. Index: the event's stored index; with no data (`data == nullptr`), `wmSfxRollNextIdx()` if
     `wmSfxMaxCount() > 0`, else -1.
  3. `if (gMapHeader.name[0] == '\0') return 0;` (no map: stop).
  4. `delay = 10 * randomBetween(15, 20)` (game ticks).
  5. If `wmSfxMaxCount() > 0`: roll the next index (`wmSfxRollNextIdx`) and queue the next event at
     now + delay carrying it. Otherwise nothing is queued and the chain stops.
  6. `if (isInCombat()) index = -1;` then play the index's sound if it is not -1 (with a 5000 ms
     bk-time gate between plays).
  All draws happen with sound off.
- `wmSfxRollNextIdx` (`worldmap.cc:6083`) = `randomBetween(0, totalChances)` and a weighted pick;
  Hexwaste's `AmbientSfx.RollIndex` already ports it.
- Map load starts the chain: `map.cc:1024` `_gsound_sfx_q_start()` = the event with no data.
- Save load: `queueLoad` (`queue.cc:84`) replaces the whole queue with the saved one. Type 13 has no
  readProc, so the saved event fires with no data and draws three numbers (index, delay, next index).
- The queue runs from `_doBkProcesses` right after the clock tick (`scripts.cc:776-781`), before that
  tick's critter procs.
- Observed (artemple-idle): saved event at t=302550 draws `0..100`, `15..20 -> 15`, `0..100`; the next
  fires at 302700 (+150) and draws `15..20 -> 18`, `0..100`; the next at 302880 (+180).
- All three fixture saves hold one type-13 event: artemple t=302550, klamath t=302600, arcaves t=302560.
  The importer currently skips it (`vanilla-import: skipped queue event type 13`).

## Hexwaste today

- `TickAmbientSfx` (`ViewerGame.cs:1982`) runs on a 17 000 ms wall-time countdown
  (`AmbientIntervalMs`, `_ambientTimerMs`), returns early without `_audio`, with an empty list, or in
  combat. Called from `Update` (`ViewerGame.cs:2993`) and `OracleFrame` (`ViewerGame.Oracle.cs:202`).
- Map load sets `_mapAmbient` and resets the countdown (`ViewerGame.cs:1945-1946`).
- `_ambientRng` is the shared oracle stream under `--rng fo2ce` (`ViewerGame.Oracle.cs:94`).
- Game-time events run in `UpdateClock` (`ViewerGame.cs:6880`) right after `_clock.AdvanceRealTime`
  (poison, drugs, withdrawal, rads, `PumpTimers`, charges, trickle items).

## Design

1. **State** (`ViewerGame`): `long _ambientNextTick = -1` (no event queued) and `int? _ambientPendingIdx`
   (null = no data, roll when it fires). Remove `_ambientTimerMs` and `AmbientIntervalMs`.
2. **`AmbientSfx.RunEvent`** (`src/Hexwaste.Formats/Map/AmbientSfx.cs`, next to `RollIndex`): the
   draw logic of `ambientSoundEffectEventProcess`, pure and testable:
   ```csharp
   public static (int PlayIdx, int? NextIdx, int DelayTicks) RunEvent(
       IReadOnlyList<(string Name, int Chance)> entries, int? storedIdx, bool inCombat, Func<int, int, int> between)
   ```
   `between(min, max)` is fo2ce's inclusive `randomBetween`. In fo2ce's order:
   - `idx = storedIdx ?? (entries.Count > 0 ? RollIndex(entries, t => between(0, t)) : -1)`;
   - `delay = 10 * between(15, 20)` (always drawn);
   - if `entries.Count > 0`: `next = RollIndex(...)`, `DelayTicks = delay`; else `next = null`,
     `DelayTicks = -1` (not rescheduled);
   - `if (inCombat) idx = -1`.
3. **`RunAmbientSfxEvent(int? storedIdx)`** (`ViewerGame`):
   - `_ambientRng ??= new Random(RngSeed ?? Environment.TickCount)` (unchanged initialization);
   - call `RunEvent(_mapAmbient, storedIdx, _combat.Phase != CombatPhase.Idle, (a, b) => _ambientRng.Next(a, b + 1))`;
   - `_ambientNextTick = DelayTicks >= 0 ? _clock.Ticks + DelayTicks : -1`, `_ambientPendingIdx = NextIdx`;
   - if `PlayIdx != -1` and `_audio` is not null: play `RemapBirdForNight(_mapAmbient[PlayIdx].Name, _clock.Hour)`.
   fo2ce's no-map early return (`gMapHeader.name[0] == '\0'`) has no Hexwaste case: the event only runs
   with a map loaded.
4. **`ProcessAmbientSfx()`**, called in `UpdateClock` right after `_scriptHost?.PumpTimers(...)`:
   `while (_ambientNextTick >= 0 && _ambientNextTick <= _clock.Ticks) RunAmbientSfxEvent(_ambientPendingIdx);`
   (the loop catches up after clock jumps like the other game-time events).
5. **Map load** (`ViewerGame.cs:1945`): after setting `_mapAmbient`, `RunAmbientSfxEvent(null)`
   (`_gsound_sfx_q_start`).
6. **Vanilla import** (`ImportVanillaQueue`):
   - Before the event loop: `_ambientNextTick = -1; _ambientPendingIdx = null;` (`queueLoad` replaces the
     queue, dropping the event map load queued).
   - `case 13:` `_ambientNextTick = e.Time; _ambientPendingIdx = null;` (no readProc: no data).
   - The draws made by the import's map load land before the oracle installs its stream and opens the
     trace, as in fo2ce's runner.
7. **Remove** `TickAmbientSfx` and both calls.
8. **Comments**:
   - `shortcut: an ambient event and a script timer due on the same tick run timers first; fo2ce's single
     queue orders them by insertion. Upgrade if the oracle shows a same-tick difference.`
   - `shortcut: a Hexwaste save does not store the pending ambient event; the chain restarts at map load.
     Upgrade if save/load round trips need the exact ambient schedule.`
   - fo2ce's 5000 ms bk-time play gate is not ported (audio only, no draws).

Live play: ambient sounds follow game time (every 15-20 game seconds, paused wherever the game clock
pauses), silent in combat as before.

## Testing

- **Unit (hermetic, `AmbientSfx.RunEvent` with a scripted `between`)**, two-entry list:
  - no data: three draws in order (index `0..total`, delay `15..20`, next index `0..total`);
    `DelayTicks = 10 * delay roll`;
  - stored index: two draws (delay, next index), `PlayIdx` = the stored index;
  - combat: same draws, `PlayIdx = -1`;
  - empty list: one draw (delay), `DelayTicks = -1`, `NextIdx = null`, `PlayIdx = -1`.
- **Oracle:** rerun all three fixtures. Success: artemple moves past line 1297; klamath (1664, before its
  t=302600 event) and arcaves (9) stay or move later.
- **Goldens:** run all suites sequentially. Their `_ambientRng` is a private `Random`, so only its own
  draws change; no fixture should move. Any moved fixture: run twice, review the diff, re-record, list it
  in the commit message.
- **CI steps** (ascii-lint, unit tests, Release build) pass.

## Docs

Findings doc: F13 FIXED, the new first divergences.

## Out of scope

- A unified fo2ce-style event queue for all event types.
- Persisting the ambient event in Hexwaste saves.
- The 5000 ms bk-time play gate.
