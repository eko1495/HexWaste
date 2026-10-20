# Oracle F19: combat adds 5 s of game time per round

After the combat joiners (PR #44), arcaves-ambush first diverges at trace line 41:

```
fo2ce:    t=302451 TURN obj#18000 ap=9
hexwaste: t=302401 TURN obj#18000 ap=9
```

fo2ce's round 2 starts 50 ticks later. Right after the dude's round-2 TURN and `combat_p_proc`, fo2ce fires a
dozen script timers that came due (`zclrat.int` / `zclscorp.int timed_event_p_proc`, each drawing
`RNG 3..5`). Hexwaste's clock stays at 302401 for the whole fight, so they never fire.

## fo2ce behavior (reference: alexbatalov e97087b)

- `_combat_sequence` (`combat.cc:3023-3072`) ends with `gameTimeAddSeconds(5)` (`:3071`): 50 ticks per round, after
  `_combat_add_noncoms`, the dead/KO/disengaging handling and the sort, before `_combat_should_end`. It is the
  only game-time advance in combat (no other `gameTimeAdd*` in combat*.cc / actions.cc).
- `_script_chk_timed_events` (`scripts.cc:756-790`): the clock's 100 ms real-time tick increments `gGameTime` only
  out of combat. The event queue is drained (`while next event time <= now: queueProcessEvents()`) out of combat
  every pass, and in combat on each 100 ms background tick. So in combat every due queue event fires: script
  timers, the map-update event (`mapUpdateEventProcess`, `scripts.cc:507`, has no combat check), poison, drugs,
  radiation, explosives, the ambient-sfx event.
- In the arcaves trace the due timers fire after the round-2 dude TURN and its `combat_p_proc`, inside
  `_combat_input`'s background processing.

## Hexwaste today

- `UpdateClock` (`ViewerGame.cs:6904`) calls `_clock.AdvanceRealTime(elapsedMs, inCombat)`, which freezes the clock
  in combat, then runs the game-time processors every frame: poison, drugs, withdrawals, radiation,
  `PumpTimers`, `ProcessAmbientSfx`, `ProcessMapUpdateEvent`, armed charges, trickle items. None comes due in
  combat because the clock does not move.
- `ProcessMapUpdateEvent` (`ViewerGame.CombatHost.cs:~612`) also returns early in combat; its doc comment cites
  this finding.
- `CombatEngine.StartNewRound` (`CombatEngine.cs`) adds `TicksPerRound` (50) to the engine's own `_combatTick`
  and processes the engine's combat events (knockout wakes) on it; the game clock is untouched.
- Every frame pump (`Update` `ViewerGame.cs:2994/3041`, `OracleFrame` `ViewerGame.Oracle.cs:195/208`, the harness
  `ViewerGame.Harness.cs:23/27` and `:2668/2672`) runs `_combat.Step()` before `UpdateClock`. A round transition
  inside `Step` therefore opens the first actor's turn (TURN, `combat_p_proc`) before that frame's
  `UpdateClock` fires the due events, the order the trace shows for the dude.

## Design

1. **`ICombatHost.AddGameTime(int ticks)`** (default no-op), `// ported from fallout2-ce src/combat.cc
   _combat_sequence(): gameTimeAddSeconds(5)`. `StartNewRound` calls `_host.AddGameTime(TicksPerRound)` after the
   round-end bookkeeping (`SequenceCombatList`, or `AddJoiners` on the `StartBrawl` path) and before
   `BuildTurnOrder`; the end-of-combat check runs after it, as in fo2ce. Every fight, brawls included.
2. **Viewer**: `AddGameTime(int ticks) => _clock.Ticks += ticks;`. No processor call there: the same frame's
   `UpdateClock` (after `Step`) fires whatever came due.
3. **`ProcessMapUpdateEvent`** drops its combat check (the loot-container check stays) and its doc comment
   describes the fo2ce behavior instead of the gap.
4. **Shortcut comments**:
   - On `AddGameTime`'s call: `shortcut: in combat fo2ce drains the queue only on a 100 ms background tick, so a
     due event can fire a few frames later than here, and for an NPC that opens a round fo2ce's first background
     pass may come before or after its first action; upgrade if a trace shows due events landing elsewhere in a
     round`.
   - On `_combatTick`: `shortcut: knockout wakes run on the engine's own combat tick, not as fo2ce's
     EVENT_TYPE_KNOCKOUT queue event on the game clock; both advance 50 per round, upgrade if a trace orders a wake
     against a timer`.
5. **Findings doc**: F19 FIXED; the new first divergences.

## Testing

- **Unit (`CombatEngine` + test host)**: a round transition calls `AddGameTime(50)` once per round, after the
  round-end join pass and before the next round's first TURN; the `StartBrawl` path calls it too. The fake host
  records the calls.
- **Oracle**: rerun all three fixtures. Success: arcaves moves past line 41 (the round-2 timers match);
  artemple stays IDENTICAL; klamath stays at 1664.
- **Goldens**: run all suites sequentially. Fixtures whose fights last long enough for a timer, poison or drug
  event to come due will move. Each moved fixture gets two runs, a reviewed diff, a re-record and a line in the
  commit message.
- **CI steps** (ascii-lint, unit tests, Release build) pass.

## Out of scope

- Moving knockout wakes onto the game-clock queue (shortcut above).
- The 100 ms background-tick gate on queue processing in combat (shortcut above).
- Player-visible clock effects beyond fo2ce's (the HUD clock now moves 5 s per combat round, as in fo2ce).
