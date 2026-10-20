# Oracle F4/F5: combat opening, joiners, one combat_p_proc per turn, taunt draws

After the map_update tick, artemple-idle is identical and arcaves-ambush first diverges at trace line 9:

```
fo2ce:    t=302401 LVAR sid=0x04000008 0 0->-3
hexwaste: t=302401 PROC zclscorp.int combat_p_proc self=obj#102
```

`zclscorp.int critter_p_proc` (obj#70) calls `attack` on the dude. The traces then differ in five ways, all on
the combat opening and joiner path:

1. fo2ce writes the attacker's reaction LVAR before the first turn; Hexwaste does not.
2. Hexwaste asks every other critter whether it joins at combat start (the nine `combat_p_proc` lines before
   obj#70's turn) and lets joiners act in round 1. fo2ce's round 1 is the attacker, the defender and the dude.
3. fo2ce checks the other critters at round end in its combat-list order, and a joiner takes its turn at once
   with full AP (obj#98 acts right after the 118/118/206/98 checks). Hexwaste only adds joiners to the next
   round's sorted order.
4. Hexwaste runs an AI critter's per-turn `combat_p_proc` again after it walks (obj#70, obj#98).
5. fo2ce draws `RNG 1..100 site=combat_ai.cc:_combatai_msg` for taunts (17 in the trace, including the move
   taunt before each approach walk); Hexwaste draws none.

The findings doc's F4 text ("obj#70 never fights", combat starts at 302700) predates F6 and is stale.

## fo2ce behavior (reference: alexbatalov e97087b)

- **Combat list.** `_combat_begin` (`combat.cc:2563`) builds `_combat_list = objectListCreate(-1, elevation,
  CRITTER)`: every non-hidden critter on the elevation, the dude included, in hex-tile order (tile 0..39999;
  `object.cc:2652`). `_list_com = 0`, `_list_noncom = _list_total`. Each critter's `ap = 0`,
  `damageLastTurn = 0`, `whoHitMe = null`, `maneuver &= ENGAGING`.
- **Round 1** (`_combat_sequence_init`, `combat.cc:2963`): swap the attacker to index 0, the defender to the
  next index, then the dude if he is neither; `_list_com` = the number placed (2 or 3),
  `_list_noncom -= _list_com`. Then `_critter_set_who_hit_me(attacker, defender)` and
  `_critter_set_who_hit_me(defender, attacker)`. No join check runs before round 1.
- **`_critter_set_who_hit_me(a1, a2)`** (`critter.cc:1285`): if `a2 == null`, or the teams differ, or
  (`statRoll(a1, INT, -1) < 2` and not both party members): `a1.whoHitMe = a2`, and if `a2 == gDude`,
  `reactionSetValue(a1, -3)` = `scriptSetLocalVar(a1->sid, 0, -3)` (`reaction.cc:8`). The INT roll runs only
  when the teams match (the `||` short-circuits).
- **Round loop** (`_combat`, `combat.cc:3378`): `_combat_set_move_all()`, each `_combat_list[0.._list_com)`
  takes `_combat_turn`, then `_combat_sequence()`, then `_combat_should_end()`.
- **`_combat_sequence`** (`combat.cc:3023`): `_combat_add_noncoms()`, drop the dead, move knocked-out and
  disengaging non-dude critters to the non-combatant part, `qsort` the combatants by Sequence then Luck
  (descending), `gameTimeAddSeconds(5)`.
- **`_combat_add_noncoms`** (`combat.cc:2899`): `_combatai_notify_friends(gDude)` (`combat_ai.cc:3632`: every
  critter on the dude's team that is not ENGAGING and perceives the dude becomes ENGAGING). Then for
  `index = _list_com; index < _list_com + _list_noncom; index++`: if `_combatai_want_to_join(obj)`:
  `maneuver = NONE`, swap `_combat_list[index]` with `_combat_list[_list_com]`, `_list_com++`,
  `_list_noncom--`, `ap = maxAP` (0 for the dude) `+ _gcsd->actionPointsBonus` when `_gcsd` is set, and
  `_combat_turn(obj, false)` immediately. (The loop bound is unchanged by a join, so every non-combatant is
  visited once, in list order.)
- **`_combatai_want_to_join`** (`combat_ai.cc:3165`): `_process_bk()` first, then the hidden / elevation /
  dead-or-KO / `damageLastTurn > 0` / `combat_p_proc` fp=5 / maneuver / `_ai_danger_source` tests.
  `_process_bk` runs no critter procs in combat (`_script_chk_critters` is gated on `!isInCombat()`,
  `scripts.cc:705`); it can only fire due queue events.
- **`_combat_turn`** (`combat.cc:3225`): runs the per-turn `combat_p_proc` (fp=4) once, then the AI or the
  dude's input; the turn's walk and attack animations run inside `_combat_turn_run` with no further
  `combat_p_proc`.
- **`_combatai_msg`** (`combat_ai.cc:3302`): returns early (no draw) for a non-critter, taunts disabled, the
  dude, or a dead/KO critter; otherwise always draws `randomBetween(1, 100)` and returns if `> ai->chance`;
  then picks the type's range (`end < start` returns) and draws `randomBetween(start, end)`. Types and ranges
  (`ai.txt`, `combat_ai.cc:421-443`): RUN `run_*`, MOVE `move_*`, ATTACK `attack_*`, MISS `miss_*`, HIT
  `hit_<location>_*` indexed by `attack->defenderHitLocation`.
- **Taunt call sites:**
  - MOVE: `_ai_move_steps_closer` after its early returns (already adjacent, AP checks), when `taunt` is true
    (`combat_ai.cc:2382`). `_ai_attack` passes `taunt = true` on its first approach and `false` after
    (`combat_ai.cc:2697-2887`); `_cai_perform_distance_prefs` charge passes 1 (`:2996`); the other callers
    pass false.
  - ATTACK: the attacker, in `_action_melee` (`actions.cc:630`) and `_action_ranged` (`:734`).
  - Melee outcome, the attacker: HIT if hit and the defender is not dead, else MISS on a miss
    (`actions.cc:674-680`).
  - Ranged outcome, the defender: MISS on a miss, HIT if hit and not dead (`actions.cc:917-923`).
  - RUN: a fleeing critter (`combat_ai.cc:1209`).

## Hexwaste today

- `CombatEngine.BeginCombat` (dude opens, `CombatEngine.cs:2323`) and `BeginScriptAggro` (`:2808`) clear
  `_hostiles`, add the target or attacker, and call `AddJoiners()`. `BuildTurnOrder(firstRound: true, ...)`
  (`:2725`) places attacker, defender, dude, then every other hostile and party member.
- `StartNewRound` (`:3230`) runs `AddJoiners()` and then `BuildTurnOrder(firstRound: false)`, which sorts
  dude + hostiles + party by Sequence then Luck. Joiners never get an immediate turn.
- `AddJoiners` (`:2682`) walks `_host.CombatCritters` (`_solidObjects[elevation]` order, dude excluded) and
  calls `WantToJoin` (`:2662`); `WantToJoin` has no `notify_friends` step and uses a perception heuristic in
  place of `_ai_danger_source`.
- `SetWhoHitMe(target, attacker)` (`:2073`) stores the attacker when the teams differ; no reaction write.
- `StepTurnOrder` (`:3105`) continues a multi-frame AI turn by calling `TryEnemyAction(ae)` /
  `TryAllyAction(aa)` again; both run `RunCombatProcOverridesTurn` (`:3564`, `:3893`) on every call.
- `CombatTaunt.Pick` (`CombatTaunt.cs`) has only ATTACK and RUN, and skips the draw when `chance <= 0`. The
  viewer's `TryTaunt` (`ViewerGame.CombatHost.cs:970`) draws from `_tauntRng`, which is a separate stream
  outside oracle mode and the shared `OracleRng` in oracle mode (`ViewerGame.Oracle.cs:89`). Call sites:
  ATTACK in `OnAttackStarted` (`:1216`), RUN (`:1366`).
- `AiPacket` (`AiPackets.cs`) reads `attack_*` and `run_*` only.
- `ScriptHost.SetObjectLocalVar(map, obj, index, value)` (`ScriptHost.cs:1487`) stores an object's LVAR.

## Design

1. **Reaction write.** `SetWhoHitMe` becomes an instance method. When it stores `attacker` and `attacker` is
   the dude, it calls `_host.SetReaction(target, -3)`. New `ICombatHost.SetReaction(MapObject critter, int
   value)` (default no-op); the viewer implements it with `_scriptHost.SetObjectLocalVar(_map, critter, 0,
   value)` and that store traces an LVAR line (sid, index 0, old, new) like a script store. A critter with no
   script writes nothing (fo2ce's `scriptSetLocalVar` fails on sid -1). Shortcut comment:
   `shortcut: fo2ce also stamps a same-team attacker when statRoll(INT, -1) < 2; that roll (a shared-stream
   draw) is not modeled, upgrade when a same-team hit fixture exists`.
2. **Combat list.** `CombatEngine` keeps `List<MapObject> _combatList` and `int _listCom`:
   - Built at combat start (`BeginCombat`, `BeginScriptAggro`) from the existing host members
     (`CombatCritters`, `PartyMembers`, `Dude`): every non-hidden critter on the dude's elevation, the dude
     included, ordered by `HexTile` (stable for equal tiles). Each entry's `Maneuver &= ManeuverEngaging`,
     `DamageLastTurn = 0`.
   - Round 1: fo2ce's three swaps (attacker, defender, dude if neither) and `_listCom` = the number placed.
     `BeginCombat` and `BeginScriptAggro` no longer call `AddJoiners()`. `_hostiles` keeps today's meaning
     (every non-dude, non-party critter that has entered the fight; never removed by a round transition), so
     targeting and end-of-combat code is unchanged. The dude is always placed in round 1, so he is never a
     non-combatant.
   - A critter no longer on the map (not in `CombatCritters`/`PartyMembers`) is skipped by the join pass;
     dead combatants leave the live part at the round transition. A critter created mid-combat is not added. Shortcut comment:
     `shortcut: critters created during combat never enter the fo2ce list, upgrade if a fixture spawns one
     mid-fight`.
   - `BuildTurnOrder(firstRound: true)` takes `_combatList[0.._listCom)` as the order; party members not
     placed are non-combatants until they join.
   - `StartBrawl` keeps its own entry path and does not build the list (out of scope).
3. **Round end** (`StartNewRound`), in fo2ce's order:
   1. **Notify friends**: every critter on the dude's team in the list that is not ENGAGING and perceives the
      dude (`WithinPerception`) becomes ENGAGING.
   2. **Join pass** (fo2ce `_combat_add_noncoms`): visit `index = _listCom .. _listCom + noncomCount - 1` in list order. A critter that
      `WantToJoin`s: `Maneuver = 0`, swap with `_combatList[_listCom]`, `_listCom++`, add to `_hostiles` (or
      leave it to the party set if it is a party member), and take its turn now through the existing
      enemy/ally turn path (which sets the AP to its max and traces `TURN`).
   - State: `int _joinIndex` (-1 = no pass running). A joiner's turn that spans frames sets
      `_actingEnemy`/`_actingAlly` as today; when `StepTurnOrder` finishes such a turn while `_joinIndex >= 0`,
      it resumes the pass at `_joinIndex + 1` instead of advancing `_orderIndex`. When the pass ends, step 3
      runs and `_joinIndex = -1`.
   - fo2ce ignores `_combat_turn`'s result here, so a joiner that kills the dude does not stop the pass; Hexwaste
      stops it when `_gameOver` is set (the existing game-over path owns the rest).
   3. Then the existing round transition: tick `_combatTick`, process combat events, drop the dead, the
      knocked-out and disengaging critters leave the combatant part, sort the combatants by Sequence then
      Luck, and rebuild `_order` from `_combatList[0.._listCom)`.
   `AddJoiners()` goes away; its per-joiner side effects (`WhoHitMeCid = -1`, the log and transcript lines)
   move to the join pass. Shortcut comment on the pass:
   `shortcut: fo2ce runs one background frame (_process_bk) before each join check, which can fire due queue
   events; not modeled, upgrade if a trace shows a timer firing between join checks`.
4. **One combat_p_proc per turn.** A `_turnProcRan` flag, cleared when an actor's turn opens (the `TraceTurn`
   sites and the join pass) and set by the first `RunCombatProcOverridesTurn` of that turn. `TryEnemyAction`
   and `TryAllyAction` skip the hook when it is set, so a continued turn does not re-run it.
5. **Taunt draws.**
   - `CombatTaunt.Type` gains `Move`, `Miss`, `Hit`; `Pick(pkt, type, rng, hitLocation = 3)` always draws
     `rng.Next(1, 101)` first, then the range draw as today. `Hit` uses `hit_<location>` ranges indexed by fo2ce
     `HIT_LOCATION_*` (head 0, left arm 1, right arm 2, torso 3, right leg 4, left leg 5, eyes 6, groin 7); an
     uncalled shot uses torso.
   - `AiPacket` reads `move_*`, `miss_*` and the eight `hit_<location>_*` pairs (`AiPackets.cs`).
   - New `ICombatHost.Taunt(MapObject critter, CombatTaunt.Type type, int hitLocation = 3)` (default no-op);
     the viewer routes it to `TryTaunt`. `TryTaunt` gains the dude early return (today only its
     `OnAttackStarted` caller checks it) and keeps its dead / KO / no-packet returns.
   - Wire the sites in the engine, in fo2ce's order relative to the attack's own draws. The arcaves trace
     pins it: `_combat_attack` (to-hit) and `weaponGetDamage` draws, the `ATTACK` line, then the ATTACK taunt
     draw, then the outcome taunt draw (`fo2ce.trace` lines 138-142, 241-246).
     - MOVE: the first approach of an AI attack turn (fo2ce `_ai_attack`'s `taunt` flag: a per-turn bool set
       when the turn opens and cleared after the first approach) and the distance-preference charge, after
       the "already adjacent" and AP early returns, before the first MOVE line (trace lines 13, 30).
     - ATTACK: `OnAttackStarted`, moved after the attack's to-hit and damage draws if an AI path calls it
       earlier.
     - Melee: the attacker's HIT (hit, defender not dead) or MISS. Ranged: the defender's MISS or HIT (hit, not
       dead).
   - Outside oracle mode `_tauntRng` stays a separate stream, so the new draws do not shift combat rolls.
6. **Findings doc** F4/F5: FIXED with the above; rewrite F4's stale evidence; the new first divergences.

## Testing

- **Unit (hermetic, `CombatEngine` with the test host):**
  - an attack by the dude on a cross-team critter calls `SetReaction(critter, -3)`; a non-dude attacker does not;
  - script aggro: round 1's order is attacker, dude; a third hostile critter acts only after the dude, at round
    end, with its max AP, and before the round-2 sorted order;
  - a non-joining critter is asked once per round end, in tile order;
  - a multi-frame AI turn runs `combat_p_proc` (fp=4) once;
- **Unit (`CombatTaunt`):** chance 0 draws once and returns -1; MOVE/MISS/HIT pick from their ranges; HIT uses
  the hit location's range.
- **Oracle:** rerun all three fixtures. Success: arcaves moves past line 9 and through the opening round
  (the next gap is expected at t=302451, F19's 5 s round clock); artemple stays identical; klamath stays at
  1664.
- **Goldens:** run all suites sequentially. Combat fixtures will move (round-1 composition, joiner timing, one
  combat_p_proc per turn). Each moved fixture gets two runs, a reviewed diff, a re-record and a line in the
  commit message.
- **CI steps** (ascii-lint, unit tests, Release build) pass.

## Out of scope

- F19: the 5 s game-time advance per round and game-time events during combat.
- The background frame before each join check (shortcut above).
- The same-team INT-roll stamp in `_critter_set_who_hit_me` (shortcut above).
- `StartBrawl` (NPC-vs-NPC fights) building the fo2ce list.
- Taunt float text for the new types beyond what `TryTaunt` already shows.
