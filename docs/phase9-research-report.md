# Phase 9 Research Report — Combat Depth II (Extract First)

*Researched 2026-06-13 in-repo: five parallel tracks — the CombatEngine
extraction seam (every `ViewerGame.cs` combat mutation site enumerated and
mapped to a host callback), AI packets (`data\ai.txt` parsed, the real
`combat_ai.cc` flee/approach semantics traced, the slice critters' packets
dumped via a temp tool), criticals + aimed shots (the crit-table rows counted
empirically, the trigger disassembled), combat physics (knockback geometry,
throwing animator rung, explosives, burst — each with a hard content census via
the new `tools/ContentAudit`), and the content reality check + cross-cutting ops
that gates the whole phase. Full track notes:
`docs/research-notes/p9-track-{a,b,c,d,e}-*.md`. Every engine claim carries a
`reference/fallout2-ce/src/<file>.cc:line` cite; every data number is quoted from
the real protos/maps/ai.txt, not from memory. Four adversarial verification
passes confirmed the load-bearing claims; one correction folded in (min_hp; see
§AI). Unverified items flagged at the end.*

## TL;DR

- **Recommended path: Combat Depth II, extract-first — exactly the standing
  decision, confirmed by content.** M0 lifts the turn machine out of the viewer
  into a testable `Hexwaste.Formats.Combat.CombatEngine` as a *no-behavior-change*
  refactor (the regression net), then four content-ordered depth milestones layer
  on top: AI packets → aimed shots + criticals → explosives + persisting
  knockdown → throwing. Burst is **DEFERRED** with hard evidence (zero
  burst-capable weapons in the shippable slice).
- **The seam reduces cleanly.** `ICombatRng` already shipped (one method,
  `Next(min,max)`, seedable wrapper — confirmed at `Combat/ICombatRng.cs`; **no
  `CombatEngine` type exists yet**). The "animation-busy doubles as the turn
  clock" coupling reduces to one `IsAnimationBusy`/`IsAnyWalkerMoving` host
  signal — our `UpdateCombat` early-returns are already the inverse of the
  engine's `while (_combat_turn_running > 0)` loop (combat.cc:3123). The
  determinism gate that M0 unblocks (seeded RNG → byte-identical `--fight`
  transcript) is the deferred phase-7 test, now buildable.
- **Order the depth features BY CONTENT, not by glamour.** The data ranks payoff:
  (1) **AI packets** — every Den/Klamath fighter carries a non-trivial packet
  that the engine ignores today; (2) **aimed shots + crits** — land on every
  human/dog/gecko enemy (7 of 19 kill-type blocks reachable); (3) **explosives +
  knockdown** — the AoE + temple-door beat; (4) **throwing** — spears on every
  Den guard, the highest new-attack-mode content density. **DEFER burst** (1
  weapon, 1 deep cave), **misc-dynamite/plastic** (1 shelf), **flamer** (absent).
- **Two adversarial corrections folded in.** (a) The min_hp flee check is
  *always* a RAW comparison `CURRENT_HP < ai->min_hp` (combat_ai.cc:3077,
  confirmed this session) — the `run_away_mode` % table only *pre-computes* that
  RAW value via `aiSetRunAwayMode` (combat_ai.cc:833), it never gates the combat
  flee; the slice packets carry literal `min_hp`, so "use RAW" is correct.
  (b) Track A's behavior-preservation checklist is correct on its three headline
  couplings but **incomplete**: it must also route the **NPC-walker `TileChanged`
  mutations** (draw-list re-sort + `_blockedTiles` per-step) and the
  **script-external side-effects** (`damage_p_proc`/`destroy_p_proc`/aggro)
  through the host, or the extracted engine corrupts draw order / blocking. Both
  added to the M0 interface below.
- **Save format stays additive-V2 — no V3 bump for the whole of Combat Depth
  II.** Knockdown/crit-status/aimed-targeting are *transient combat state that
  never crosses a save boundary* (the engine can't save in combat; load force-ends
  it). The one alignment fix: gate F5/F9/Z on `_combatPhase == Idle` to match the
  engine, closing the only path by which transient state could leak.
- **Bench is green** — denbus2 `--bench 400` this session: avg 3.89 / p95 7.37 /
  max 10.46 ms, ~12 ms under threshold. Crit-table lookups (a 3-D array index)
  and AI tile-walks fire on turn events, not per frame. No perf work expected.

## Comparison / sizing table

| Feature | Effort | Felt-depth payoff | Content in slice | Risk | Verdict |
|---|---|---|---|---|---|
| **M0 — extract CombatEngine** (seam + determinism gate) | **M** (~600-900 LoC moved + ~150 new; 2-3 days) | None directly — it's the *net* | n/a | Med (mechanical refactor; dropped AP reset / re-sort) | **BUILD FIRST — non-negotiable** |
| **AI packets** (min_to_hit walk/flee + min_hp flee + distance) | **M** | **Very high** — every fighter | YES — packets on every Den/Klamath NPC | Low | **M1** |
| **Aimed shots + criticals** (full table gen + trigger + 8-loc menu) | **M-L** (table is the long pole) | **High** — every human/dog/gecko | YES — 7/19 kill-types reachable | Low (table is checksum-verified) | **M2** |
| **Explosives + persisting knockdown + metarule(49)** | **M** | High (AoE, temple-door beat) | Partial — grenade/molotov kladwtwn-only; plastic-ex arcaves | Med (door beat needs carried explosive) | **M3** |
| **Throwing** (animator rung + throw wiring) | **M** | High — spears fly, recoverable | YES — 7 throw-class weapons, on Den guards | Low | **M4** |
| **Bounce-only knockback** (no persist flag) | S (~70 LoC) | Med — every melee/blast shoves | YES — all melee maps | Low | folded into M3 (or M2 if cheap) |
| **Burst** (`_compute_spray` 3-wall cone) | M (~140 LoC) | Med | **ZERO** burst weapons in slice | — | **DEFER** |
| **misc dynamite/plastic timer-arm** | S (~50 LoC) | Med (fuse beat) | 1 shelf (klaratcv) / 1 container (arcaves) | — | **DEFER** (fold into M3 only if free) |

Effort legend: S ≈ ≤½ day, M ≈ 1-2 days, L ≈ 3+ days. "Felt-depth payoff" weighed
against *content the player actually meets in the opening hour*, per the Track E
audit — not against engine completeness.

---

## M0 — the CombatEngine seam (the load-bearing milestone)

### What is being extracted

The turn machine today is ~700 lines in `ViewerGame.cs`: `CombatPhase` enum
(~:100), `TryAttack` (~:2148), `RollAttack` (~:2239), `ProcessCombatAnimations`
(~:2437), `ResolveAttack` (~:2457), `KillCritter` (~:2501), `FinishCorpse`
(~:2567), the begin/queue/award cluster (~:2586-2749), and
`UpdateCombat`/`StepEnemyTurn`/`TryEnemyAction`/`TryAllyAction`/`EnemyAttack`
(~:2849-3116). The *math* it calls already lives in `Hexwaste.Formats.Combat`
(`CombatMath`, `LineOfFire`, `CritterState`, `Progression`, `SkillSet`). What is
**not** extracted is the **orchestration**: phase state, the queues, AP budgets,
the roll-then-resolve-on-anim-completion choreography, and the draw-list /
`_blockedTiles` mutations.

The engine's real loop is `_combat` → `_combat_turn` (combat.cc:3225) →
`_combat_turn_run` (combat.cc:3121), a blocking
`while (_combat_turn_running > 0) { _process_bk(); renderPresent(); }`
(combat.cc:3123). Our `CombatPhase` state machine *already is* that loop turned
inside-out (stepped from `Update`). The extraction moves the owner, not the
shape.

### ICombatRng — already shipped

`Combat/ICombatRng.cs` defines `int Next(int minInclusive, int maxExclusive)`
(mirroring `System.Random.Next` exactly) plus `SystemCombatRng` (a seedable
wrapper). It is the engine's **only** randomness source — verified used in
`RollHit`/`RollDamage`/`RollWeaponDamage`. M0 adds *nothing* here; this is what
makes the determinism gate (same seed twice → identical stdout) buildable. *Note:
this already landed in the p9-m0 prep; the report counts it as done, not as M0
work.*

### ICombatHost — the callback surface (C#-ish pseudocode)

Everything the turn machine touches that is *not* pure data on `MapObject`
becomes a host callback. Grouped by the engine concept each satisfies. **The two
italic groups are the additions the adversarial audit surfaced — without them the
extracted engine corrupts draw order / blocking.**

```csharp
namespace Hexwaste.Formats.Combat;

public interface ICombatHost
{
    // --- The turn clock (engine: _combat_turn_running via _combat_anim_begin/
    //     _combat_anim_finished, combat.cc:5322/5334; damage at zero :5363). ---
    // True while ANY attack/death/walk animation owns the turn. CombatEngine.Step()
    // is a no-op while this returns true — the inverse of while(turn_running > 0).
    bool IsAnimationBusy(MapObject critter);    // per-actor: pending attack / fall
    bool IsAnyWalkerMoving();                    // _npcWalkers.Values.Any(Moving)

    // --- Critter data resolution (engine: critterGetStat / item.cc weapon). ---
    CritterState? GetCritterState(MapObject critter);                  // :3801
    (ProtoInfo? Proto, MapObject? Item) EquippedWeapon(MapObject c);   // :2287
    int WeaponAmmo(ProtoInfo proto, MapObject item);                  // :2308 (-1 hydrate)
    AmmoProtoStats? LoadedAmmo(ProtoInfo proto, MapObject item);      // :2315
    bool TryReload(MapObject holder, ProtoInfo proto, MapObject item);// :2345

    // --- Attack choreography (engine: _action_attack → reg_anim → animator
    //     decrements _combat_turn_running on finish; damage applied at zero). ---
    void OnAttackStarted(MapObject attacker, MapObject target, ProtoInfo? weapon); // muzzle/punch FRM
    void PlayWeaponSfx(ProtoInfo? weapon);                            // :3101
    void OnAttackResolved(MapObject attacker, MapObject target,
        bool hit, int damage);                                       // hit-react FRM (anim 14)

    // --- Death + corpse-conversion draw-list move (engine: critterKill →
    //     death anim → corpse-flat move in critter.cc). ---
    void OnCritterDying(MapObject critter, int deathAnim);            // PlayFall + scream
    void OnCorpseConverted(MapObject critter, int deathAnim);         // FinishCorpse body
    bool DeathAnimExists(MapObject critter, out int deathAnim);       // PickDeathAnim probe

    // --- World mutation that MUST stay single-sourced (draw lists + blocking).---
    void RebuildBlocking(MapObject? dudeExclude);                     // :1441 RebuildBlockedTiles
    bool StartWalk(MapObject critter, int targetTile);               // :1598 StartNpcWalk
    void StopWalk(MapObject critter);                                // walker.Stop + remove
    void FaceToward(MapObject critter, int targetTile);             // Rotation = RotationTo

    // --- *AUDIT ADD #1: NPC-walker per-step mutations during animation.*
    //     StartNpcWalk's TileChanged closure mutates _blockedTiles (Remove(old)/
    //     Add(new), :1619-1620) AND re-sorts _solidObjects (:1623-1625) every step,
    //     INSIDE the animation loop before Step() runs again. The closure stays
    //     viewer-side (inside StartWalk), but the engine must be notified so its
    //     view of blocking/sort stays consistent. ---
    void OnWalkerTileChanged(MapObject critter, int fromTile, int toTile);

    // --- Scripts (engine: scriptExecProc damage_p_proc / destroy_p_proc).
    //     *AUDIT ADD #2: these can mutate critter lists, inventory, props, and
    //     NPC positions — they must be wired, not assumed no-op. RunDestroyProc
    //     may override the default death (Overridden flag). RemovePartyMember
    //     fires when a destroy proc calls object_remove on a follower.* ---
    IReadOnlyList<string> RunDamageProc(MapObject target, MapObject? source, int damage); // :2478
    (IReadOnlyList<string> Lines, bool Overridden) RunDestroyProc(
        MapObject critter, MapObject? killer);                       // :2509
    void RemovePartyMember(MapObject critter);                       // :2523 + log

    // --- Progression / end (engine: _combat_give_exps, pcAddExperience). ---
    void AwardXp(int amount);                                        // :2722
    void GameOver();                                                 // :2838

    // --- Output. Transcript = EXACT Console.WriteLine (see checklist §F);
    //     Log = the in-game monitor line (player-visible, NOT in transcript diff). ---
    void Log(string line);
    void Transcript(string line);
}
```

`CombatEngine` owns the state currently held as viewer fields: `_combatPhase`,
`_hostiles`, `_enemyQueue`/`_allyQueue`, `_actingEnemy`/`_actingAlly` (+ their AP),
`_combatRound`, `_combatXpPending`, `_pendingAttack`, `_fallingCritters`,
`_dudeAp`, `_gameOver`, and the `ICombatRng`. Public surface the viewer/harness
still drives: `engine.Phase`, `engine.Round`, `engine.DudeAp`, `engine.IsBusy`,
`engine.Hostiles`, plus `TryAttack`, `EndPlayerTurn`, `Step()`
(= `ProcessCombatAnimations` + `UpdateCombat`), `BeginScriptAggro`, `Reset`.

### The COMPLETE behavior-preservation checklist (the regression net)

**§A — All three `_dudeAp` reset-to-max points (verified in source this
session).** Five sites match `_dudeAp = …MaxActionPoints` in `ViewerGame.cs`;
three are turn resets, one is adjacent, one is NOT combat:

1. **Open-from-free-swing** — `TryAttack` case `Idle`: `_dudeAp =
   attacker.MaxActionPoints;` (ViewerGame.cs:2207), then `-= apCost`. The "first
   swing pays for a fresh budget" path.
2. **New round** — `StepEnemyTurn` after everyone acted: `_dudeAp =
   stats.MaxActionPoints;` (:2923). Mirrors engine `_combat_set_move_all`
   (combat.cc:3217).
3. **Scripted ambush opens on the ENEMY's turn** — `OnScriptAttack`: `_dudeAp =
   stats.MaxActionPoints;` (:2686), so control returns to the dude with a full
   budget.
   - *Adjacent (must also move):* `EndCombat` (:2710) — out-of-combat AP so the
     next free swing reads sane. Engine `_combat_over` (combat.cc:2811).
   - *NOT combat (stays viewer-side):* `SpawnDude` (:1395) — load path.

Missing any of #1-#3 silently changes how many swings the dude gets and breaks
every `--fight` transcript. Assert each with a unit test
(open-from-idle / round-rollover / ambush all leave `DudeAp == MaxActionPoints`).

**§B — Animator-as-turn-clock + roll-now/apply-on-completion split.** The engine
applies damage in `_combat_anim_finished` (combat.cc:5363) when
`_combat_turn_running` hits 0, **not** at roll time. Our split is identical:
`RollAttack` (:2239) computes chance/hit/damage up front into `PendingAttack`;
`ResolveAttack` (:2457) lands it only once `ProcessCombatAnimations` (:2439) sees
the attacker's animation gone (`!_animator.TryGetState`). This ordering is
load-bearing — it is why a dying critter still finishes its swing, and why the
transcript prints `attack … chance=X% hit=Y damage=Z` (roll time, :2226) BEFORE
the monitor's `You hit the X for Z damage.` (resolve time). **Do not collapse
roll+apply** or the transcript line order flips. `UpdateCombat`'s four
early-returns (:2851-2860) are the inverse of `_combat_turn_running > 0`
(idle/gameover · pending attack/fall · moving enemy walker · moving ally walker);
`Step()` must keep all four guards in order.

**§C — Every `_solidObjects` / `_flatObjects` draw-list mutation in the combat
path.**
- `FinishCorpse` (:2575-2583): sets `Flags |= 0x10` (NO_BLOCK) and `|= 0x08`
  (flat) **first** (:2575-2576) so `IsFlat` is true before the move, then
  `_solidObjects[e].Remove(critter)` → `InsertSorted(_flatObjects[e], critter)`
  for **all** elevations in a loop (:2578) — a corpse on a non-current elevation
  must still convert. → `OnCorpseConverted` + `RebuildBlocking`.
- NPC walk re-sort (`StartNpcWalk.TileChanged`, :1623-1625): `solids.Remove(npc)`
  + `InsertSorted(solids, npc)` to keep hex z-order — *the §B-audit add; route via
  `OnWalkerTileChanged`*.
- `InsertSorted` (:1429) is `FindIndex(o => o.HexTile > obj.HexTile)` — stable
  ascending-by-`HexTile`. The draw order depends on it.

**§D — Every `_blockedTiles` mutation site.**
- `RebuildBlockedTiles` (:1441): full rebuild — non-NO_BLOCK critters/scenery/
  walls block their tile; MULTIHEX (0x800) blocks 6 neighbors; open doors removed.
  Called from `FinishCorpse` (:2583) so a corpse stops blocking. → `RebuildBlocking`.
  **The corpse case is the one that bites:** skip the rebuild and the AI / dude
  path *around a body that is not there*.
- `StartNpcWalk.TileChanged` (:1619-1620): per-step `Remove(old)`/`Add(new)` —
  *the audit add (`OnWalkerTileChanged`)*.
- (Door open/close, item create/destroy, dude transit share the field but are
  out-of-combat → stay viewer-side; the engine triggers rebuilds via the host.)

**§E — `MapObject` stays the single mutable source of truth.** `MapObject` is a
`sealed class` (MapFile.cs:36) — reference identity, default `GetHashCode`.
Verified as a key in `_hostiles` (HashSet), `_fallingCritters` (Dict),
`_npcWalkers` (Dict), the `_solidObjects`/`_flatObjects` lists, plus `_homeTiles`,
`_partyScriptIndex`, `_animator` state. The engine must **mutate the same
instances** (`CurrentHp`:109, `CombatResults`:103, `Sid`:58, `Team`:105,
`Rotation`, `Fid`, `Flags`, `AmmoQuantity`) — no records, no clones, no
value-equality. The interface passes `MapObject` everywhere for this reason.

**§F — The EXACT `Console.WriteLine` lines that must survive verbatim** (diffed
by the `--attack`/`--fight` transcript tests; all verified at these line numbers
this session — interpolation + spacing byte-identical):

```
:2226  $"attack {ObjectName(target)}@{target.HexTile}" + …": chance={chance}% hit={hit} damage={damage}"
:2392  $"reload: {ObjectNameByPid(weaponProto.Pid)} -> {weaponItem.AmmoQuantity}/{weapon.AmmoCapacity}"
:2612  $"joins: {ObjectName(critter)}@{critter.HexTile} (team {critter.Team})"
:2690  $"scripted-aggro: {ObjectName(attacker)}@{attacker.HexTile} starts combat"
:2728  $"xp: +{amount} (total {_dudeXp}, level {_dudeLevel})"
:2747  $"level-up: now level {_dudeLevel}, skillPoints={_unspentSkillPoints}"
:2843  "GAME OVER"
:3058  $"ally-attack {ObjectName(ally)} -> {ObjectName(target)}@{target.HexTile}" + …": chance=…% hit=… damage=…"
:3094  $"enemy-attack {ObjectName(enemy)}@{enemy.HexTile}" + …": chance=…% hit=… damage=…"
```

Plus the two harness summary lines (these stay in the viewer's `StartupAction`
handler, but the engine state they read — `_combatRound`, `_hostiles`,
`target.CurrentHp/IsDead`, `_gameOver` — must be exposed so the strings come out
identical; both verified including :644, which the audit flagged):

```
:580   $"attack-result: hp={target.CurrentHp} dead={target.IsDead}"
:644   $"fight-result: rounds={_combatRound} dudeHp={_dude?.Dude.CurrentHp} gameOver={_gameOver} targetDead={target.IsDead} hostilesLeft={_hostiles.Count(h => !h.IsDead)}"
```

The `Log(...)` monitor lines (`"Combat begins — round 1…"`, `"You hit the X for
Z damage."`, `"The X dies."`, `"Combat ends."`, etc.) go to the in-game monitor,
NOT stdout, so they are not in the transcript diff — **but they ARE
player-visible**; route them through `host.Log` and keep the strings. Map them;
do not drop them.

**Acceptance gate:** with a fixed `--rng-seed`, two `--fight HEX` runs must
produce byte-identical stdout (the deferred phase-7 determinism test, now
buildable because `ICombatRng` is the only randomness source). M0 is done when
(1) that test passes and (2) the existing `--attack`/`--fight` transcript fixtures
diff clean against pre-refactor captures. **Capture golden transcripts BEFORE
touching the code** (Track A flagged: the harness flags exist at `Program.cs`,
but no checked-in transcript-diff test was located — step 1 creates the fixtures).

### M0 extraction order (each independently buildable; commit after each)

1. **Golden capture (½ day, first).** Run `--attack`/`--fight` with a fixed seed
   vs artemple/Den critters; save stdout as fixtures; add the determinism test as
   a *currently-passing* baseline. No behavior change yet — this is the net.
2. **Define `ICombatHost` + empty `CombatEngine` skeleton in Formats (½ day).**
   `ViewerGame` implements `ICombatHost` by delegating to its existing private
   methods. No call-sites moved. Compiles; tests green.
3. **Move the pure-decision methods (½ day):** `RollAttack`, `BeginCombat`,
   `AddJoiners`, `BuildEnemyQueue`, `EndPlayerTurn`, `CombatShouldEnd`,
   `EndCombat`, `AwardXp`. Fewest viewer deps. Re-run transcript + determinism.
4. **Move the choreography (1 day):** `TryAttack`, `ProcessCombatAnimations`,
   `ResolveAttack`, `KillCritter` (→ `OnCritterDying`), `OnScriptAttack`,
   `UpdateCombat`/`Step`, `StepEnemyTurn`, `TryEnemyAction`, `TryAllyAction`,
   `EnemyAttack`, `ResetCombatState`. This is where §A AP resets, §C/§D draw-list/
   blocking callbacks, and the §B-audit walker/script callbacks cross the
   boundary — watch them. The viewer's `Update` becomes `_combat.Step()`.
5. **Wire the determinism test + a `[GameDataFact]` per assertion (½ day):** the
   three AP-reset asserts, corpse-still-blocks-nothing, transcript line-order.
   This becomes the M1-M5 regression net.

**Pivot/risk:** the soft spot is `EquippedWeapon`/`WeaponAmmo`/`TryReload`/
`LoadedAmmo` (:2287-2397) — they read `_protos`/`_dudeInventory`/the proto cache,
all viewer-owned. Keep them on the host side (callbacks); do NOT pull the proto
loader into Formats (it drags file I/O into the engine-free library). If the host
surface for them feels heavy, that's correct — they are genuinely viewer concerns.

---

## M1 — AI packets (the headline depth feature)

**Confirmed:** `MapObject.AiPacket` is parsed (MapFile.cs:432, exposed :104) and
**read nowhere for AI decisions** (grep-confirmed across `src/`). Current enemy
logic fires at *any* computed hit chance and never flees — "flat min_to_hit 30"
is a characterization (the only clamp is `CombatMath.ToHit` 0..95). `data\ai.txt`
is **8267 lines / 158509 bytes / 187 `[Section]`s** (all three numbers verified
this session), parsed by `aiInit()` (combat_ai.cc:370-470) into the `AiPacket`
struct; the lookup key is the integer `packet_num`.

**Ship these fields, in priority order:**

1. **`min_to_hit` (the headline, ~80% of the felt change).** The walk-closer-or-
   flee loop in `_ai_try_attack` (combat_ai.cc:2692-2900, `minToHit` read at
   :2705): if OUT_OF_RANGE and even point-blank `toHitNoRange < minToHit` →
   **flee** (`_ai_run_away`, :2812); else move closer (:2818). If in range but
   `accuracy < minToHit` (:2845): **walk tile-by-tile toward the target, stopping
   at the first tile where projected to-hit clears the floor** (:2853-2879,
   `if (toHit >= minToHit) break`); if no reachable tile qualifies → flee. The
   engine never fires below `min_to_hit` when it could close the gap. Reuse our
   existing A* + `CombatMath.ToHit` (recompute distance/LoF from each candidate
   tile — we lack `_determine_to_hit_from_tile` but `ToHit` is position-
   independent except for distance falloff + LoF crowd). Slice packets affected:
   12 (min 20), 13 (40), 14 (34), 17/24/30 (30), 22 (15), 39 (25), 50/57/77/134
   (10), 1 (30).

2. **`min_hp` flee — RAW value (adversarially confirmed).** At turn start
   `_combat_ai` (combat_ai.cc:3075-3081): flee if `STAT_CURRENT_HIT_POINTS <
   ai->min_hp` (RAW) OR a `hurt_too_much` bit is set OR FLEEING is latched.
   **The comparison is ALWAYS RAW.** The `run_away_mode` → `_hp_run_away_value`
   {0,25,40,60,75,100} % table is NOT used by the combat flee — its only consumers
   are `aiSetRunAwayMode` (combat_ai.cc:833, which *pre-converts* a percentage to
   a RAW `min_hp` for the party-UI setter) and a `debugPrint`. The slice's packets
   carry literal `min_hp` (4/10/12/1/8/5…), so **port `min_hp` as an absolute HP
   threshold; ignore `run_away_mode`** (party-UI/debug). Minimal version: when
   `CurrentHp < packet.min_hp`, set a transient combat-scoped `Fleeing` state; on
   its turn the critter walks away from the nearest hostile instead of toward.
   Affects Thugs (10), Peasants/Karl/Anna (12), Store Owner (8), Guards (4) — the
   single biggest "this feels like Fallout" beat after #1.

3. **`distance` prefs (cheap; only 4 slice packets use it).**
   `_cai_perform_distance_prefs` (combat_ai.cc:2970-3033): charge / snipe /
   stay_close / on_your_own / stay. In the slice only **charge** (pkt 25 "Loser"
   crowd — already rush) and **on_your_own** (pkts 1/22/32 — default approach)
   appear on real fighters; both ≈ current behavior. `stay_close` (Cyberdog) and
   `snipe` appear on no slice hostile. **Wire the field, add only the STAY (4)
   "shoot-or-flee" early-out (and STAY is on zero slice critters, so it's a
   documented no-op for our content).**

**Defer:** `best_weapon` (slice NPCs carry one weapon — nothing to rank),
`disposition` (branches *only for party members* in `_ai_danger_source`,
combat_ai.cc:1541; companions are 100% script-side per phase-7 M4),
`area_attack_mode`/`chem_use`, `hurt_too_much` cripple-flee (free to add once M2's
crippling lands — the flee path already exists from #2), and `aggression` (parsed
at combat_ai.cc:409 but **read by no combat function** — grep-confirmed; legacy/
flavor, UNVERIFIED whether sfall reads it, safe to ignore).

**The slice packets** (dumped via temp tool, names from `pro_crit.msg`,
cross-referenced to `/tmp/ai.txt`):

| pkt | who | min_to_hit | min_hp | distance |
|---|---|---|---|---|
| 12 Generic Guards | denbus1/2 slaver guards | 20 | 4 | (default) |
| 13 Thugs | denbus1 thugs | 40 | 10 | — |
| 14 Peasants | Den/kladwtwn homesteaders, Anna/Karl | 34 | 12 | — |
| 22 Tough Guard | denbus1/2 | 15 | 1 | on_your_own |
| 25 Wimpy Peasant | "Loser" crowd | 0 | 0 | charge |
| 1 Arroyo Warrior | artemple villager (1 placed) | 30 | 5 | on_your_own |
| 7/8/26 Rat/Scorpion/Gecko | Klamath packs | 0 | 0 | — |

The animal packs (rats/geckos/scorpions, min_to_hit 0 / min_hp 0) already behave
correctly under the current rush-and-bite AI — **M1 changes nothing for the most
numerous critters** (no regression risk). The humanoid fights (Den slavers/thugs/
guards) are exactly the ones with non-zero packets.

**Sizing:** an `AiPacketTable` INI parser in `Formats.Combat` (reuse the MSG/
config reading style, keyed by `packet_num`, parse all 187 cheaply) + consume
`MapObject.AiPacket` + the walk/flee loop = **M**. distance/attack_who add-on =
**S**. **No save impact** — `min_to_hit`/`min_hp` derive from the already-saved
`AiPacket` + live `CurrentHp`; `Fleeing` is combat-scoped (cleared on combat end).

**DEFER-if-content gate:** none — every slice fighter carries a packet. Build it.

**Demo / headless:** extend `--fight` with an out-of-range start → assert a
min_to_hit-20 slaver closes the gap before firing; a thug at HP < 10 emits a
flee/disengage transcript line rather than attacking. Both deterministic under
`--rng-seed`. Re-run `--bench` after M1 (cheap; AI tile-walks bounded by AP).

---

## M2 — aimed shots + criticals (the table reality check)

### Crit-table dimensions (verified empirically this session)

Two tables, both `CriticalHitDescription` arrays:

- **`gCriticalHitTables[SFALL_KILL_TYPE_COUNT][HIT_LOCATION_COUNT]
  [CRTICIAL_EFFECT_COUNT]`** (combat.cc:189). `SFALL_KILL_TYPE_COUNT =
  KILL_TYPE_COUNT × 2 = 38` (proto_types.h:131; KILL_TYPE_COUNT=19), but only the
  first **19** kill-type blocks are literally initialized (rows 19-37 are sfall
  zero-fill, never reached by base content). `HIT_LOCATION_COUNT = 9`,
  `CRTICIAL_EFFECT_COUNT = 6` (combat_defs.h:85/8). **Literal rows: 19×9×6 =
  1026**, confirmed by `grep -cE "^\s*\{ [0-9-]"` over combat.cc:189-1786 = 1026,
  and `grep -c "// KILL_TYPE_"` = 19.
- **`gPlayerCriticalHitTable[HIT_LOCATION_COUNT][CRTICIAL_EFFECT_COUNT]`**
  (combat.cc:1791) — used when the *defender is the dude*. 9×6 = **54** rows
  (confirmed `grep -cE` over 1791-1864 = 54).

**Total literal rows = 1080**, each row = 7 ints
`{ damageMultiplier, flags, massiveCriticalStat, massiveCriticalStatModifier,
massiveCriticalFlags, messageId, massiveCriticalMessageId }`. Message-id range
1000..7106 (combat.msg).

### The crit trigger (disassembled)

A crit is the *same* to-hit roll upgrading SUCCESS → CRITICAL_SUCCESS — no
separate "did I crit" dice. Single-shot path (combat.cc:3852-3853):
`roll = randomRoll(accuracy, criticalChance − hit_location_penalty[loc], …)`.
Because `hit_location_penalty` values are **negative**, subtracting ADDS to the
crit modifier — an aimed shot is harder to hit AND more likely to crit when it
lands. `randomRoll` (random.cc): `delta = accuracy − d100`; if `delta ≥ 0` the
hit lands, then a **second** `d100 ≤ delta/10 + (critChance − locPenalty)`
upgrades to a crit (criticals enabled from in-game day 2). Our `RollHit`
(`d100 ≤ accuracy`) is exactly the `delta ≥ 0` test — capture `delta` and add the
second roll.

Severity via `attackComputeCriticalHit` (combat.cc:4089-4159): `chance =
rand(1,100) + STAT_BETTER_CRITICALS` (stat 16, = 0 for a base character),
bucketed `≤20→0, ≤45→1, ≤70→2, ≤90→3, ≤100→4, else→5` (effect 5 unreachable
without the Better Criticals perk). Lookup keyed by `critterGetKillType`
(critter.cc:745 — dude = MAN/WOMAN by gender; else proto killType; both already
parsed in Hexwaste). Massive-crit upgrade fires when the defender **fails** a
`statRoll` (e.g. EN−3) — a tough target resists.

### Aimed shots (verified)

The 8-location penalty array `hit_location_penalty_default` (combat.cc:172-182,
verified verbatim): **HEAD -40, L_ARM -30, R_ARM -30, TORSO 0, R_LEG -20, L_LEG
-20, EYES -60, GROIN -30, UNCALLED 0.** Applied FULL for ranged, **HALVED for
melee** (combat.cc:4437-4441). Aimed shot costs **+1 AP** (item.cc:1706,
`if (aiming) actionPoints += 1`). The +40-to-hit-vs-prone modifier (combat.cc:4474,
verified) makes crits feed M3's knockdown for free. BYPASS reduces DT/DR to 20%
(combat.cc:4530).

### The minimal honest cut + pivot

**Honor only `{damageMultiplier, KNOCKED_DOWN (0x02), DEAD (0x80), BYPASS
(0x800)}`; mask everything else** at apply-time
(`defenderFlags &= DAM_DEAD|DAM_KNOCKED_DOWN|DAM_BYPASS|DAM_CRITICAL`).
`damageMultiplier` + BYPASS are pure number math that drop straight into
`RollDamage`'s existing ×2/÷2 wrapper (the table's `2` is identity, so the crit
multiplier slots where the hardcoded 2 lives); KNOCKED_DOWN feeds M3; DEAD routes
to `KillCritter`. **Mask** KNOCKED_OUT/BLIND/LOSE_TURN/CRIP_*/ON_FIRE — each
needs a status-tick system we don't have. Document the mask like the LoF
deviation.

**TRANSCRIBE THE FULL TABLE ANYWAY** but **do NOT hand-type 1080 rows.** Write a
one-off generator (`tools/` python or `dotnet run` snippet) that reads
combat.cc:189-1864, resolves `DAM_*`/`STAT_*` symbols to integers, and emits a C#
`static readonly int[]` + index helper + a checksum. Keep the generated file +
generator + a checksum unit test checked in.

**PIVOT THRESHOLD (fires immediately):** the table is 1080 rows × 7 ints = 7560
integers — far past hand-transcription. *If the generator itself is deemed too
much for one milestone*, ship **multiplier-only with the 7 reachable kill-type
blocks** — the slice's critters map to only **7 of 19** kill types: MAN, WOMAN,
CHILD, BRAHMIN, DOG, RAT, GECKO = 7×9×6 = **378 rows** + the 54-row player table
(fall back to MAN for any other killtype). **VERIFIED via `tools/ContentAudit`
over artemple/denbus1/denbus2/kladwtwn:** the slice's critters map to exactly
these 7 kill types — MAN ×110, WOMAN ×68, CHILD ×14, BRAHMIN ×9, DOG ×2, RAT ×1,
GECKO ×1 — so MAN/WOMAN (178 of 205) overwhelmingly dominate felt crits and the
378-row 7-block cut loses essentially nothing. The full-1080 generator is still
preferred if cheap; the 7-block subset is the proven-safe fallback.

**Sizing:** table generator + checksum test = **S-M** (mechanical, machine-
checked); crit trigger logic = **S** (~40-60 LoC; all inputs on `CritterState`
already); 8-location menu + AP = **S-M** (reuse the loot/examine panel render).
Better Criticals stat = free (index 16, default 0). **Total M-L, table is the
long pole.**

**DEFER-if-content gate:** none — crits/aimed land on every human/dog/gecko enemy.
Build it.

**Demo / headless:** a `[Fact]` re-runs the generator checksum vs source; a
seeded `--fight --force-crit` flag asserts a (killtype, loc, effect) row yields
the expected multiplier + flags; `--attack --aim eyes` asserts to-hit drops by 60
(ranged) / 30 (melee) and AP rises by 1. Re-run `--bench` (3-D array index is
free).

---

## M3 — explosives + persisting knockdown + metarule(49)

This is the damage-results cluster — explosions, the crit-fed persisting
knockdown, and the temple-door beat all share the `combat.results` plumbing; do
them together.

**Persisting knockdown + get-up + +40** (the prone combo, rides on M2's crit
flag — the flag's only non-explosion source is the crit table). `KnockedDown`
bool on `CritterState`; at turn start `_combat_standup` (combat.cc:5391) costs
**3 AP** (1 with Quick Recovery — dude perk, skip), plays ANIM_PRONE_TO_STANDING
(36) / ANIM_BACK_TO_STANDING (37), clears the flag (animation.cc:3195); +40
to-hit while the target carries KNOCKED_DOWN (combat.cc:4474, verified). **M ~120
LoC + additive-V2 save delta** (1 bool/critter ordinal — but transient, see
cross-cutting; no Version bump).

**Bounce-only knockback** (the standalone S, ~70 LoC — can land here or earlier
in M2 if cheap). Gate (combat.cc:4633-4637): target not multihex AND
`(damageType==EXPLOSION || weapon==null || attackType==MELEE)` AND critter AND
not NO_KNOCKBACK — **ranged guns never shove.** Distance = `damage / 10`
(combat.cc:4651, verified). Geometry (`actionKnockdown`, actions.cc:102-154):
rotation = `tileGetRotationTo(attacker, defender)`, walk straight 1..maxDist,
**stop (distance--) on a blocked tile (`_obj_blocking_at`) or exit grid** —
**yes, blocked by occupied tiles**; `MAX_KNOCKDOWN_DISTANCE = 20`. Our
`HexGrid.TileInDirection`/`RotationTo` are 1:1 ports — drops straight on; the only
new host call is "is tile occupied?" (the viewer's `_blockedTiles`). **The subtle
split:** a pure shove with no crit flag bounces the critter back up *in the same
sequence, no AP cost* (actions.cc:416-423); only a crit-set KNOCKED_DOWN flag
*persists* (actions.cc:400-409).

**ActionExplode core** (actions.cc:1582, verified). Spawn a misc-10 explosion
object (`fid = buildFid(OBJ_TYPE_MISC, 10, 0, 0, 0)`) — **the explosion object is
the attacker** (this is what makes metarule(49) work). AoE via
`_compute_explosion_on_extras` (combat.cc:3987): ring spiral, default rocket
radius **3** (plain actionExplode uses isGrenade=0), LoS-gated extras (cap 6),
self-hit gets DAM_BACKWASH. Per-victim `_compute_explosion_damage`
(actions.cc:1811): `roll − DT_explosion`, then `− DR_explosion×dmg/100`, knockback
= dmg/10. Our `LineOfFire.Trace` is the LoS check; the ring is `TileInDirection`
walks. **M ~150 LoC.**

**`_scr_explode_scenery` broadcast** (scripts.cc:2879-2950): radius-3 ITEM/SPATIAL
scripts get `damage_p_proc` with `fixed_param = 20` and `target = the explosion
object`. Hexwaste already dispatches `damage_p_proc` + runs spatial scripts
(`RunSpatialsAt`, phase-7 M3) — a radius-gated broadcast over existing plumbing.
**S ~40 LoC.**

**metarule(49)** = `METARULE_WEAPON_DAMAGE_TYPE` (interpreter_extra.cc:78, handler
:3297 — both verified). On the misc-10 explosion object → returns
DAMAGE_TYPE_EXPLOSION (6); on a weapon item → that weapon's proto damage type.
The temple door's `damage_p_proc` calls `metarule(49, target)`, gets EXPLOSION,
opens. One case in the IntVm metarule switch. **S ~25 LoC.**

**Dynamite/plastic timer arm** (folds in only if free): the engine's `delay =
10 × seconds` is 1/10-sec ticks — identical granularity to Hexwaste's phase-5
timer queue (1 tick = 100 ms). Arm = `AddTimer(map, explosive, 10×seconds,
EXPLOSION)`; TRAPS skill roll decides premature/half/normal (our `ICombatRng` +
skills cover it); detonate runs `ActionExplode`. **S ~50 LoC.**

**CONTENT GATE — DEFER honestly where the slice is thin:**
- Grenade (Frag) `0x19` (dmg 20-35) + Molotov `0x9F` (dmg 8-20) exist **only on
  kladwtwn**, on a Child + a "Loser" + bookcases — **loot/pickpocket, not combat
  drops.** Build the AoE for completeness; the felt-payoff is M4's spear-throw,
  not the grenade.
- Plastic Explosives `0x55` on **arcaves[e1, container]** is the provable timed-
  explosive demo. **No dynamite (PID 51) is placed in any slice map** (Track E
  found 1 dynamite shelf in klaratcv rat caves; Track D says plastic-ex on
  arcaves — both are deep/optional). **DEFER the misc-explosive timer-arm path
  unless free; it buys one container in an optional dungeon.**
- **The "blow the temple door" fantasy is a trap-demo:** artemple places NO thrown
  explosive — a player can only blast the door by *carrying* a grenade from
  Klamath. Wire metarule(49) (cheap, correct) but **do NOT advertise it as a
  shippable opening-hour demo — the lockpick bypass stays the real path.** Flag
  honestly.

**Sizing:** persisting-knockdown M + bounce-only S + ActionExplode M + scenery
broadcast S + metarule(49) S = **M (phase milestone)**. **Save: additive-V2**
(KnockedDown bit; transient — see cross-cutting).

**Demo / headless:** a grenade thrown into a cluster damages multiple ring
victims; a melee hit ≥10 dmg shoves the target along the hex line, stopping at a
wall; a crit sets persisting prone (−3 AP next turn, +40 to-hit vs it);
metarule(49) on the explosion marker returns 6.

---

## M4 — throwing

**The attack reuses the ranged to-hit math + ONE new animator mode.** To-hit =
`attackDetermineToHit` exactly as for guns (our `CombatMath.ToHitChance`), but the
skill is **Throwing** (already in `SkillSet.cs`) and the range is
`weaponGetRange = min(maxRange1, 3 × effectiveStrength)` (item.cc:1611-1627) —
a ST-6 dude throws a spear (maxRange1=8) at min(8,18)=8, a rock (15) at min(15,18)=15.

**The new animator rung** is `animationRegisterMoveToTileStraight`
(`_action_ranged`, actions.cc:692/753-806): the thrown weapon IS the projectile —
removed from hand (`itemRemove`), art swapped to in-flight, placed on the map,
then tweened along the straight hex line at walk speed to the target tile with a
completion callback. Hexwaste's `ObjectAnimator` already plays per-frame FRM
offsets; what's new is tweening an object between two hexes' *screen positions*
over N frames. **S/M ~60-80 LoC** (consistent with the phase-7/8 animator
sizings). **Recoverable:** a non-explosive thrown weapon (rock/spear/throwing-
knife) restores its FID on landing and is `_obj_connect`-ed onto the ground →
picks back up; explosive throws are destroyed after the blast (by design).

**Throw wiring: S ~50 LoC** — range calc, Throwing skill into the existing
to-hit, item-remove-from-hand + land-on-ground (recoverable), auto-rewield.
Grenade/molotov = "throw + AoE at landing tile" (reuses M3's explosion core).

**CONTENT GATE — this is the highest new-attack-mode content density.** 7
throw-class weapons across the slice: Spear `0x07` (artemple/denbus1/denbus2/
kladwtwn/klamall — **carried by the actual Agile Guard / Agile Thug / Weak Gun
Guard**, secondary THROW range 8), Rock `0x13`, Throwing Knife `0x2D` (denbus2/
kladwtwn/klamall), Sharpened Spear `0x118` (klamall), Flare `0x4F`, plus the two
explosives. **Build it — spears are on every Den fighter.**

**Sizing: M (phase milestone).** No save impact (a thrown weapon landing on the
ground reuses the existing taken/created delta machinery).

**Demo / headless:** `--throw HEX` flies a spear along the hex line, lands it
recoverable at the target tile; a thrown grenade detonates at landing with AoE.

---

## DEFER — burst (with evidence)

`_compute_spray` (combat.cc:3703, verified): a cone via three LoF walls
(center + rotation±1, each `ammoQuantity/3` rounds), per-round accounting,
extras cap 6. **M ~140 LoC.** **DEFER.** **Content census (Track D + E, 8 maps):
ZERO burst-capable weapons in the shippable slice.** The only burst gun is the
Bozar `0x15E` (rounds 15) on klatoxcv[e2] (toxic caves — deep optional, on a
robot). No 10mm SMG (PID 9, the canonical burst weapon — absent everywhere), no
minigun, no burst pistol on any Den/Klamath surface map. Building a 3-wall cone
for one weapon nobody fires in the opening hour is the textbook "engine with no
payoff." **Revisit only if a later phase adds the SMG to merchant restock (a
player-bought toy).** One-line rationale for the changelog: *"burst = 1 weapon, 1
deep cave → deferred until a burst weapon reaches the player's hands in normal
play."*

---

## Cross-cutting

**Save format: additive-V2 SUFFICES — NO V3 bump for any of Combat Depth II.**
`SaveState.cs` is JSON, `CurrentVersion=2`, refuse-on-mismatch. Knockdown / aimed-
targeting / crit-status are **transient combat state that never crosses a save
boundary** — the engine *cannot save in combat* (pipboy blocked, game.cc:652-666),
and load force-ends combat (`_combat_over_from_load`, loadsave.cc:1703). So these
are by-design ephemeral; new fields, if any, are additive nullable/defaulted.
**One alignment fix (S):** Hexwaste's F5 save is NOT gated by combat
(ViewerGame.cs:1102, no `_combatPhase` check) unlike the engine — gate
**F5/F9/Z on `_combatPhase == Idle`** (matches game.cc), which also removes the
only path by which transient knockdown could leak into a save. Correctness
alignment, not a format change.

**Determinism** is M0's acceptance gate: seeded `--rng-seed` → byte-identical
`--fight` stdout, the deferred phase-7 test, now buildable (ICombatRng is the only
randomness source). Every M1-M5 feature extends the `--fight` harness with a
seedable flag (force-crit, force-flee, aim-location, throw) so each is provable
headless.

**Bench** is green (denbus2 `--bench 400` this session: avg 3.89 / p95 7.37 / max
10.46 ms, ~12 ms under threshold). Depth math is per-attack CPU (3-D array index,
AP-bounded A* tile-walks), not per-frame. Re-run `--bench` after M1 and M3 to
confirm — cheap, no concern expected.

**Headless coverage:** every milestone ships a `[GameDataFact]` or transcript
probe (enumerated per-milestone above). The crit-table generator's checksum test
is a `[Fact]` (no game data needed).

## Pivot thresholds

- **M0:** if a transcript or determinism diff fails after a move-step, the
  failing step is isolated (one method group per commit) — revert that step, not
  the milestone. The golden fixtures (step 1) make every regression a one-commit
  bisect.
- **M1:** if the walk-until-hittable loop is expensive or flaky, ship
  **min_hp-flee + a static min_to_hit gate per packet** first (still beats flat
  30) and add the tile-by-tile walk second; both come from the same packet table.
- **M2 (the big one):** *if even the table generator is too much for one
  milestone*, ship **multiplier-only with MAN/WOMAN/CHILD/BRAHMIN/DOG/RAT/GECKO
  (378 rows) + the player table (54)**, MAN-fallback for any other killtype —
  after parsing the raw critter protos' killType to confirm the 7-block coverage.
  *If the 8-location menu UI fights the panel system*, ship the to-hit/AP math
  with a fixed-location debug flag and defer the interactive picker.
- **M3:** the persisting-knockdown + crit cluster is the integration risk — if it
  destabilizes, ship **bounce-only knockback (S) + ActionExplode + metarule(49)**
  and defer persisting-prone + the +40 to a follow-up (the crit flag is its only
  source, so it's independently cuttable). DEFER the misc-dynamite timer-arm
  unless free (1 optional-cave container).
- **M4:** independent and last — cut it if the phase runs long; the spear-throw is
  the felt payoff, the grenade AoE is completeness (kladwtwn-only loot).
- **Burst:** stays deferred; do not build the cone until a burst weapon ships to
  the player.

## Caveats / unverified

- **Killtype distribution of slice critters — RESOLVED** (was unverified; parsed
  this session via `tools/ContentAudit` over artemple/denbus1/denbus2/kladwtwn):
  MAN ×110, WOMAN ×68, CHILD ×14, BRAHMIN ×9, DOG ×2, RAT ×1, GECKO ×1 — exactly
  the 7 kill-type blocks Track E predicted, MAN/WOMAN 178/205. The 378-row 7-block
  M2 cut is confirmed safe.
- **`aggression` is read by no combat function** (combat_ai.cc never references it
  after the :409 parse — grep-confirmed). UNVERIFIED whether sfall or another TU
  reads it. Safe to ignore for M1.
- **No checked-in transcript-diff test was located** — the `--attack`/`--fight`
  harness flags exist (`Program.cs`), and the exact stdout lines are quoted in §F,
  but `Formats.Tests` is engine-free and contains none. Treat "fixtures exist" as
  an assumption; M0 step 1 creates them if absent. UNVERIFIED: location of any
  existing transcript-diff harness outside `Formats.Tests`.
- **`_combat_should_end` divergence (pre-existing).** Engine
  `_combat_should_end` (combat.cc:3339) uses team + whoHitMe over the combat list;
  our `CombatShouldEnd` is the simpler "any hostile still standing." Not
  introduced by M0 — flagged so the parity claim is honest.
- **The temple-door-blast beat is engine-correct but NOT a shippable opening-hour
  demo** — artemple places no thrown explosive; the player must carry one from
  Klamath. Wire metarule(49); keep lockpick as the advertised path.
- **No dynamite/plastic explosive is placed where the timed-explosive beat would
  shine** (plastic-ex: arcaves container; dynamite: klaratcv shelf — both deep/
  optional). The timed-arm port is engine-real but content-thin; defer unless free.
- **`_inven_set_timer` exact default seconds is UNVERIFIED** — set interactively
  in the engine's timer dialog, not a constant cited from source. A fixed
  countdown is fine for our scope; the `delay = 10×seconds` math + TRAPS roll is
  the load-bearing part and is cited.
- **Knockback bounce-vs-persist split** (actions.cc:400-409 vs 416-423) was traced
  from Track D's read but not re-disassembled this session beyond the cited line
  ranges; the gate (combat.cc:4633), distance (4651), and +40 (4474) were
  re-verified directly.
