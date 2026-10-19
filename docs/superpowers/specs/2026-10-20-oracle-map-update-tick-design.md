# Oracle: the 600-tick map_update as a game-time event

After F12, artemple-idle first diverges at trace line 5809:

```
fo2ce:    t=303000 PROC artemple.int map_update_p_proc self=obj#16
hexwaste: t=303000 PROC obj_dude.int critter_p_proc self=obj#18000
```

Both engines run map_update at t=302720, 303000 and 303020. At 302720 and 303020 (the 30 s bk-time
round) both run it as line 10 of the tick, after nine critter procs. At 303000 fo2ce runs it as line 1 and
Hexwaste as line 10.

## fo2ce behavior (reference: alexbatalov e97087b)

- `EVENT_TYPE_MAP_UPDATE_EVENT` (type 12) is a game-time queue event.
  - Map load schedules it at now + 600: `map.cc:1022` `gameTimeScheduleUpdateEvent()` ->
    `queueAddEvent(600, ..., EVENT_TYPE_MAP_UPDATE_EVENT)` (`scripts.cc:396`).
  - `mapUpdateEventProcess` (`scripts.cc:507`): run the map_update round, `_queue_clear_type(12)`, and
    unless no map is loaded, queue the next at now + 600. A late event fires once; it does not catch up.
- The queue is processed in `_doBkProcesses` right after the clock tick, before that frame's critter proc
  (`scripts.cc:774-781`). So the event lands in the first frame of its tick: t=303000 is a multiple of 600.
- `queueLoad` replaces the whole queue on a save load; the saved type-12 event keeps its time.
- `opGameTimeAdvance` runs `queueProcessEvents` inline.
- The 30 s bk-time round (`scripts.cc:758-760`) is a separate source and already matches (F7).

## Hexwaste today

- `PumpMapUpdate` (`ViewerGame.CombatHost.cs:588`) runs both sources from elapsed milliseconds: the bk-time
  round (`_bkMapUpdateMs`, 30 000 ms) and the 600-tick round (`_mapUpdateClockMs`, 60 000 ms,
  `MapUpdateIntervalMs`). The 600-tick round is skipped in combat and while a loot container is open; both
  return early in dialog, the companion hub, the worldmap and game-over.
- Map load sets `_mapUpdateClockMs = 0` (`ViewerGame.cs:1915`); the vanilla import's `case 12` sets it from
  the saved event time (`ViewerGame.VanillaImport.cs:229-232`).
- The ms count runs 90 ms (nine 10 ms frames) out of phase with the game clock, so the 600-tick round fires
  in the last frame of tick 303000 instead of the first.
- `UpdateClock` (`ViewerGame.cs:~6906`) advances the clock, then runs `PumpTimers` and `ProcessAmbientSfx`
  (F13). Every frame pump (`Update`, `OracleFrame`, `PumpOneFrame`) calls `UpdateClock`.
- The `game_time_advance` handler (`ViewerGame.cs:~1419`) runs `ProcessAmbientSfx()` inline (F13).
- The heartbeat probe (`StartupAction.MapUpdateHeartbeatProbe`, `ViewerGame.Harness.cs:~528`) feeds
  `PumpMapUpdate` milliseconds and is pinned by `tests/golden-encounter/map-update-heartbeat.txt`
  (`beats=3 halfIntervalFires=0 totalFires=3`).

## Design

1. **State** (`ViewerGame.cs`): replace `_mapUpdateClockMs` and `MapUpdateIntervalMs` with
   `long _mapUpdateNextTick = -1` (no event queued) and `const int MapUpdateIntervalTicks = 600`.
2. **`ProcessMapUpdateEvent()`** (next to `PumpMapUpdate`):
   ```csharp
   // ported from fallout2-ce src/scripts.cc mapUpdateEventProcess(): run the round, reschedule at now + 600
   private void ProcessMapUpdateEvent()
   {
       if (_mapUpdateNextTick < 0 || _clock.Ticks < _mapUpdateNextTick || !MapUpdateAllowed()
           || _combat.Phase != Formats.Combat.CombatPhase.Idle || _lootContainer is not null)
           return;
       _mapUpdateNextTick = _clock.Ticks + MapUpdateIntervalTicks;
       _mapUpdateFires++;
       RunMapUpdateRound();
   }
   ```
   `MapUpdateAllowed()` holds `PumpMapUpdate`'s existing early-return condition (script host, map, game-over,
   dialog, companion hub, worldmap), shared by both. A due event that is gated waits until it is allowed.
   Reschedule before running the round, so a round that moves the clock does not refire it.
3. **Call sites**: in `UpdateClock` right after `ProcessAmbientSfx()`; in the `game_time_advance` handler
   right after its `ProcessAmbientSfx()` (fo2ce runs the queue inline there).
4. **`PumpMapUpdate`** keeps only the bk-time round; the 600-tick block goes away. Update its doc comment.
5. **Scheduling**:
   - Map load (`ViewerGame.cs:1915`): `_mapUpdateNextTick = _clock.Ticks + MapUpdateIntervalTicks;`
     (`gameTimeScheduleUpdateEvent`).
   - Vanilla import (`ImportVanillaQueue`): next to the existing `_ambientNextTick = -1;` reset before the
     event loop (`ViewerGame.VanillaImport.cs:~207`), add `_mapUpdateNextTick = -1;` (`queueLoad` replaces
     the queue, dropping the event the import's map load scheduled); `case 12`:
     `_mapUpdateNextTick = e.Time;` (the saved time).
6. **Heartbeat probe**: advance the game clock instead of feeding ms:
   `_clock.Ticks += MapUpdateIntervalTicks / 2; ProcessMapUpdateEvent();` for the half interval, then
   `_clock.Ticks += MapUpdateIntervalTicks; ProcessMapUpdateEvent();` per beat. Output unchanged.
7. **Shortcut comments** on `ProcessMapUpdateEvent`:
   - `shortcut: a map-update event due on the same tick as a script timer or the ambient event runs after them; fo2ce's single queue orders them by insertion. Upgrade if the oracle shows a same-tick difference.`
   - `shortcut: fo2ce's midnight event (gameTimeEventProcess -> gameTimeScheduleUpdateEvent, scripts.cc:428) queues another map-update event at midnight + 600, and whichever fires first clears the other; Hexwaste keeps one chain. Upgrade when a fixture crosses midnight.`
8. **Re-entrancy** (checked, no change): the inline `game_time_advance` call runs the round from inside a
   running script, as fo2ce does. `PumpTimers` already does this, and a same-sid re-entrant proc gets a
   throwaway VM (`IntVm.cs:608`, `ScriptHost.cs:895`).

Live play: the round still fires every 60 s of real time while the clock runs and never in combat. After a
clock jump (rest, `game_time_advance`) it fires once, as in fo2ce; the ms count kept waiting.

## Testing

- **Oracle**: rerun all three fixtures. Success: artemple moves past line 5809; klamath (180 map_update
  rounds per hour) stays at 1664; arcaves stays at 9.
- **Goldens**: run all suites sequentially. The heartbeat probe's output is pinned. A moved fixture gets two
  runs, a reviewed diff, a re-record and a line in the commit message.
- **CI steps** (ascii-lint, unit tests, Release build) pass.
- No new unit test: the change is viewer glue; the pinned heartbeat probe and the oracle check it.

## Docs

Findings doc: under F7, the 600-tick source now reads the game clock (it was the known ms-count limit); under
F18, fo2ce's map-update event also keeps running during worldmap travel (the map stays loaded), which
Hexwaste's worldmap gate skips; the new first divergences.

## Out of scope

- A unified fo2ce-style event queue.
- Persisting the pending map-update event in Hexwaste saves (the round restarts at map load + 600, as today).
