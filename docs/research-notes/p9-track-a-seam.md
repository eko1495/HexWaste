# Phase-9 Track A: The M0 CombatEngine seam

## Q0 — What we are actually extracting

The turn machine today is ~700 lines spread across `ViewerGame.cs`
(`CombatPhase` :100-114, `TryAttack` :2148, `RollAttack` :2239,
`ProcessCombatAnimations` :2437, `ResolveAttack` :2457, `KillCritter` :2501,
`FinishCorpse` :2567, `BeginCombat`/`AddJoiners`/`EndPlayerTurn`/
`BuildEnemyQueue`/`OnScriptAttack`/`EndCombat`/`AwardXp` :2586-2749,
`UpdateCombat`/`StepEnemyTurn`/`TryEnemyAction`/`TryAllyAction`/`EnemyAttack`/
`ResetCombatState` :2849-3116). The math it calls is already in
`Hexwaste.Formats.Combat` (CombatMath, RangedMath, LineOfFire, CritterState,
CombatRules). What is NOT extracted is the **orchestration**: phase state, the
queues, AP budgets, the roll-then-resolve-on-anim-completion choreography, and
the draw-list/`_blockedTiles` mutations. M0 lifts all of that into
`Hexwaste.Formats.Combat.CombatEngine`, leaving the viewer holding only
rendering, input, the animator, and a thin `ICombatHost` adapter.

The engine's real loop is `_combat` → `_combat_turn` (combat.cc:3225) →
`_combat_turn_run` (combat.cc:3121), a blocking `while (_combat_turn_running > 0)
{ _process_bk(); renderPresent(); }`. Our `CombatPhase` state machine (:98-114
comment: "the engine's blocking `_combat_turn` loop flattened into a state
machine stepped from Update") already *is* that loop turned inside-out. The
extraction does not change the shape; it moves the owner.

---

## Part 1 — The `ICombatHost` interface (C#-ish pseudocode)

`ICombatRng` already exists (`Formats/Combat/ICombatRng.cs`, one method
`int Next(int minInclusive, int maxExclusive)` with `SystemCombatRng`). The
engine keeps rolling through it; M0 adds nothing there. Everything else the
turn machine touches that is *not* pure data on `MapObject` becomes a host
callback. Grouping by the engine concept each one satisfies:

```csharp
namespace Hexwaste.Formats.Combat;

public interface ICombatHost
{
    // --- The turn clock (engine: _combat_turn_running via _combat_anim_begin/
    //     _combat_anim_finished, combat.cc:5322/5334). ---
    // True while ANY attack/death/walk animation owns the turn. The engine
    // gates _combat_turn_run on this; the headless harness fakes it by pumping
    // the animator. CombatEngine.Step() is a no-op while this returns true.
    bool IsAnimationBusy(MapObject critter);   // per-actor (pending attack/fall)
    bool IsAnyWalkerMoving();                   // _npcWalkers.Values.Any(Moving)

    // --- Critter data resolution (engine: critterGetStat / item.cc weapon). ---
    CritterState? GetCritterState(MapObject critter);            // :3801
    (ProtoInfo? Proto, MapObject? Item) EquippedWeapon(MapObject critter); // :2287
    int WeaponAmmo(ProtoInfo proto, MapObject item);            // :2308 (-1 hydrate)
    AmmoProtoStats? LoadedAmmo(ProtoInfo proto, MapObject item);// :2315
    bool TryReload(MapObject holder, ProtoInfo proto, MapObject item); // :2345

    // --- Attack choreography (engine: _action_attack → reg_anim → the
    //     animator decrements _combat_turn_running on finish). ---
    void OnAttackStarted(MapObject attacker, MapObject target,  // muzzle/punch FRM
        ProtoInfo? weapon);                                     // = StartAttackAnimation
    void PlayWeaponSfx(ProtoInfo? weapon);                      // :3101
    // damage applied; play the hit-react FRM if target survives (anim 14).
    void OnAttackResolved(MapObject attacker, MapObject target,
        bool hit, int damage);                                 // hit-react part of :2457

    // --- Death (engine: _apply_damage → critterKill → death anim, then the
    //     corpse-conversion draw-list move in critter.cc). ---
    void OnCritterDying(MapObject critter, int deathAnim);      // PlayFall + scream
    void OnCorpseConverted(MapObject critter, int deathAnim);   // FinishCorpse body
    bool DeathAnimExists(MapObject critter, out int deathAnim); // PickDeathAnim probe

    // --- World mutation that MUST stay single-sourced (draw lists + blocking).---
    void RebuildBlocking(MapObject? dudeExclude);               // :1441
    bool StartWalk(MapObject critter, int targetTile);          // :1598 StartNpcWalk
    void StopWalk(MapObject critter);                           // walker.Stop + remove
    void FaceToward(MapObject critter, int targetTile);         // Rotation = RotationTo

    // --- Scripts (engine: scriptExecProc damage_p_proc / destroy_p_proc). ---
    IReadOnlyList<string> RunDamageProc(MapObject target,
        MapObject? source, int damage);                        // :2478
    (IReadOnlyList<string> Lines, bool Overridden) RunDestroyProc(
        MapObject critter, MapObject? killer);                 // :2509
    void RemovePartyMember(MapObject critter);                  // :2523 + log

    // --- Progression / end (engine: _combat_give_exps, pcAddExperience). ---
    void AwardXp(int amount);                                   // :2722
    void GameOver();                                            // :2838

    // --- Output (engine: displayMonitorAddMessage + debugPrint). ---
    void Log(string line);                  // the in-game monitor line
    void Transcript(string line);           // EXACT Console.WriteLine (see Part 2)
}
```

The `CombatEngine` owns the state currently held as viewer fields: `_combatPhase`,
`_hostiles`, `_enemyQueue`/`_allyQueue`, `_actingEnemy`/`_actingAlly`,
`_actingEnemyAp`/`_actingAllyAp`, `_combatRound`, `_combatXpPending`,
`_pendingAttack`, `_fallingCritters`, `_dudeAp`, `_gameOver`, and the
`ICombatRng`. The viewer keeps only references it also uses outside combat
(`_dude`, the draw lists, `_blockedTiles`, `_npcWalkers`, `_animator`) and
exposes the slices the engine needs through the host. Public surface the
viewer still drives directly (the harness reads these):
`engine.Phase`, `engine.Round`, `engine.DudeAp`, `engine.IsBusy`,
`engine.Hostiles`, plus `TryAttack`, `EndPlayerTurn`, `Step()`
(= `ProcessCombatAnimations` + `UpdateCombat`), `BeginScriptAggro`
(= `OnScriptAttack`), `Reset`.

**Confirming the prompt's four load-bearing claims:**

(a) **"animation-busy = turn clock" reduces to `IsAnimationBusy`.** YES, and it
is already this clean. The engine clock is the integer `_combat_turn_running`
(combat.cc:152), bumped by `_combat_anim_begin` (:5322) and dropped by
`_combat_anim_finished` (:5334); when it hits 0 the finish callback runs
`_apply_damage` (:5363) and `_combat_standup`. Our `UpdateCombat` already
encodes the inverse: it *returns early* while `_pendingAttack != null ||
_fallingCritters.Count > 0 || walker.Moving` (:2853-2860). So the busy signal is
the union of three host facts. The harness (`--fight`, :601-602) computes
exactly that union by hand — proof the boundary is real. The engine's
"apply damage when the counter hits 0" maps 1:1 to `ProcessCombatAnimations`
(:2439-2443) firing `ResolveAttack` once `!_animator.TryGetState(attacker)`.

(b) **Every draw-list / `_blockedTiles` mutation routes through a host
callback.** There are exactly these mutation sites and each gets a callback:
- corpse conversion `FinishCorpse` (:2578-2583): removes from
  `_solidObjects[e]`, `InsertSorted` into `_flatObjects[e]`, then
  `RebuildBlockedTiles` → `OnCorpseConverted` + `RebuildBlocking`.
- NPC walk `StartNpcWalk.TileChanged` (:1617-1626): `_blockedTiles.Remove(old)`,
  `.Add(new)`, `RunSpatialsAt`, re-`InsertSorted` in `_solidObjects` →
  inside `StartWalk` (the closure stays viewer-side; the engine only calls
  `StartWalk`).
- `KillCritter` (:2531-2532): `_npcWalkers.Remove`, `_homeTiles.Remove` —
  these stay viewer concerns triggered by `OnCritterDying`.
Routing them through the host is what keeps corpses from going invisible or
mis-sorted: the engine never touches `_solidObjects`/`_flatObjects` directly.

(c) **All three `_dudeAp` reset points reproduced** — found and enumerated in
Part 2 §A.

(d) **`MapObject` stays the single mutable source of truth.** `MapObject` is a
`sealed class` (MapFile.cs:36) — reference identity, default `GetHashCode`. It
is a key in `_hostiles`(HashSet), `_fallingCritters`(Dict), `_npcWalkers`(Dict),
`_homeTiles`, `_partyScriptIndex`, `_animator` state, and the `_solidObjects`/
`_flatObjects` lists. The engine must **mutate the same instances** (CurrentHp,
CombatResults, Sid, Rotation, Fid, Flags, AmmoQuantity, WhoHitMeCid) and never
copy/clone them. `PendingAttack` already holds `MapObject` references, not ids
(:91). The interface passes `MapObject` everywhere for the same reason.

---

## Part 2 — Behavior-preservation checklist (the regression net)

### §A — ALL THREE `_dudeAp` reset-to-max points (the prompt's explicit ask)

The engine resets the dude's AP to `STAT_MAXIMUM_ACTION_POINTS` at turn start
(`_combat_set_move_all` combat.cc:3217 for the list; the dude's own turn) and at
combat end (`_combat_over` combat.cc:2811). Our flattened machine has it in
**three** places — all must move into `CombatEngine` byte-for-byte:

1. **Combat opens from a free swing** — `TryAttack`, case `Idle`:
   `_dudeAp = attacker.MaxActionPoints;` (ViewerGame.cs:2207), then `-= apCost`
   (:2210). This is the "first swing pays for a fresh budget" path.
2. **Each new round** — `StepEnemyTurn`, after everyone acted:
   `_dudeAp = stats.MaxActionPoints;` (:2923) before flipping back to
   `PlayerTurn`. Mirrors `_combat_set_move_all`.
3. **Scripted ambush opens combat on the ENEMY's turn** — `OnScriptAttack`:
   `_dudeAp = stats.MaxActionPoints;` (:2686) so when control returns to the
   dude after the ambush round he has a full budget.

A fourth, *adjacent* reset is in `EndCombat` (:2710) — out-of-combat AP so the
next free swing reads a sane value. Not a turn reset but lives in the same code
and must move too. Plus the load-path reset (`SpawnDude` :1395) which is NOT
combat and stays in the viewer. **Missing any of #1-#3 silently changes how many
swings the dude gets and breaks every `--fight` transcript.** A unit test should
assert each: open-from-idle, round-rollover, and ambush all leave `DudeAp ==
MaxActionPoints` at the right instant.

### §B — Animator-as-turn-clock coupling

The engine applies damage in `_combat_anim_finished` (:5363), NOT at roll time —
the roll is computed up front (`RollAttack` :2239, stored in `PendingAttack`),
the animation plays, and `ResolveAttack` (:2457) lands the damage only once
`ProcessCombatAnimations` (:2439) sees the attacker's animation gone. This
"roll-now, apply-on-completion" ordering is load-bearing: it is why a critter
that would die still finishes its swing, and why the transcript prints `attack
... chance=X% hit=Y damage=Z` (roll time, :2226) BEFORE `You hit the X for Z
damage.` (resolve time, :2470). The engine equivalent: `attackCompute` runs in
`_combat_attack` (:3501), damage applies in the finish callback. **Preserve the
two-phase split exactly** — do not collapse roll+apply, or the transcript line
order flips.

`UpdateCombat`'s four early-returns (:2851-2860) are the inverse of
`_combat_turn_running > 0`: idle/gameover, pending attack/fall, moving enemy
walker, moving ally walker. The `Step()` method must keep all four guards in
order; reordering them can let a turn advance mid-animation.

### §C — Every `_solidObjects` / `_flatObjects` draw-list mutation in the combat path

- `FinishCorpse` (:2580-2581): `_solidObjects[e].Remove(critter)` then
  `InsertSorted(_flatObjects[e], critter)` — only if not already in flats.
  Done for **all** elevations in a loop (:2578) — keep the loop; a corpse on a
  non-current elevation must still convert. Sets `Flags |= 0x10` (NO_BLOCK) and
  `|= 0x08` (flat) FIRST (:2575-2576) so `IsFlat` is true before the move.
- NPC walk re-sort (`StartNpcWalk.TileChanged` :1623-1625): `solids.Remove(npc)`
  + `InsertSorted(solids, npc)` to keep hex z-order. Stays inside `StartWalk`.
- `InsertSorted` (:1429) is `FindIndex(o => o.HexTile > obj.HexTile)` — stable
  ascending-by-`HexTile` order. The engine's draw order depends on it; if the
  engine ever inserts without it, sorting breaks.

### §D — Every `_blockedTiles` mutation site

- `RebuildBlockedTiles` (:1441): full rebuild from `_solidObjects[e]` —
  non-NO_BLOCK critters/scenery/walls block their tile; MULTIHEX (0x800) blocks
  6 neighbors; open doors removed. Called from `FinishCorpse` (:2583) so a
  corpse stops blocking. → `RebuildBlocking`.
- `StartNpcWalk.TileChanged` (:1619-1620): per-step `Remove(old)`/`Add(new)`.
- (Out of combat but shares the field: door open/close :3308/:3317, item
  create/destroy :3235/:3243, dude spawn/transit. These stay viewer-side; the
  engine only triggers rebuilds via the host.)
**The corpse case is the one that bites:** if `RebuildBlocking` is not called
after `OnCorpseConverted`, the dead critter's tile stays blocked and the dude /
AI path around a body that is not there.

### §E — `MapObject`-as-dict/HashSet-key ownership

Already covered in Part 1(d): `MapObject` is a `sealed class` keyed by
reference. The engine must hold and mutate the exact instances the viewer's
collections hold. No records, no clones, no value-equality. `_hostiles`
(HashSet), `_enemyQueue`/`_allyQueue` (Queue), `_fallingCritters`/`_npcWalkers`
(Dict) all move into the engine and keep referencing the live objects. The
viewer's `_animator`, `_homeTiles`, `_partyScriptIndex` keep their own
references to the same instances; consistency holds because everyone points at
one heap object.

### §F — The EXACT `Console.WriteLine` lines that must survive verbatim

These are produced on the combat path and are diffed by the `--attack`/`--fight`
transcript tests. Quoted from `ViewerGame.cs` — interpolation and spacing must
be byte-identical:

```
:2226  $"attack {ObjectName(target)}@{target.HexTile}" + $"{(weaponProto is null ? "" : $" [{ObjectNameByPid(weaponProto.Pid)}{(isGun ? $" {weaponItem!.AmmoQuantity}rnd d{distance}" : "")}]")}: chance={chance}% hit={hit} damage={damage}"
:2392  $"reload: {ObjectNameByPid(weaponProto.Pid)} -> {weaponItem.AmmoQuantity}/{weapon.AmmoCapacity}"
:2612  $"joins: {ObjectName(critter)}@{critter.HexTile} (team {critter.Team})"
:2690  $"scripted-aggro: {ObjectName(attacker)}@{attacker.HexTile} starts combat"
:2728  $"xp: +{amount} (total {_dudeXp}, level {_dudeLevel})"
:2747  $"level-up: now level {_dudeLevel}, skillPoints={_unspentSkillPoints}"
:2843  "GAME OVER"
:3058  $"ally-attack {ObjectName(ally)} -> {ObjectName(target)}@{target.HexTile}" + $"{(weaponProto is null ? "" : $" [{ObjectNameByPid(weaponProto.Pid)}]")}: chance={chance}% hit={hit} damage={damage}"
:3094  $"enemy-attack {ObjectName(enemy)}@{enemy.HexTile}" + $"{(weaponProto is null ? "" : $" [{ObjectNameByPid(weaponProto.Pid)}{(isGun ? $" d{distance}" : "")}]")}: chance={chance}% hit={hit} damage={damage}"
```

And the two harness summary lines printed AFTER the loop (these stay in the
viewer's `StartupAction` handler, but the engine state they read
— `_combatRound`, `_hostiles`, `target.CurrentHp/IsDead`, `_gameOver` — must be
exposed so the strings come out identical):

```
:580   $"attack-result: hp={target.CurrentHp} dead={target.IsDead}"
:644   $"fight-result: rounds={_combatRound} dudeHp={_dude?.Dude.CurrentHp} gameOver={_gameOver} targetDead={target.IsDead} hostilesLeft={_hostiles.Count(h => !h.IsDead)}"
```

The `Log(...)` monitor lines (`"Combat begins — round 1, your turn (AP {_dudeAp})."`
:2595, `"Round {_combatRound} — your turn (AP {_dudeAp})."` :2925, `"You hit the
{target} for {damage} damage."` :2471, `"The {attacker} hits you for {damage}
damage."` :2472, `"You missed..."`/`"...misses you."` :2465, `"The {critter}
dies."` :2533, `"Combat ends."` :2711, `"You earn {amount} experience points."`
:2727) go to the in-game monitor, not stdout, so they are not in the transcript
diff — BUT they ARE player-visible behavior; route them through `host.Log` and
keep the strings to avoid a felt regression. Map them; do not drop them.

**Acceptance gate (from the prompt's cross-cutting section):** with a fixed
`--rng-seed`, two `--fight HEX` runs must produce byte-identical stdout. M0 is
done when (1) that determinism test passes (it is the deferred phase-7 test,
now buildable because `ICombatRng` is the only randomness source the engine
touches), and (2) the existing `--attack`/`--fight` transcript fixtures diff
clean against pre-refactor captures. Capture golden transcripts BEFORE touching
the code.

---

## Part 3 — M0 sizing + extraction order

**Size: M (the largest single milestone of the phase, ~600-900 LoC moved +
~150 new interface/adapter/test LoC, est. 2-3 focused days).** It is a *move*,
not new logic, so risk is mechanical-refactor risk (a dropped AP reset, a
re-sorted draw list) — exactly what the transcript + determinism gates catch.

**Order of steps (each independently buildable; commit after each):**

1. **Golden capture (½ day, do first).** Run the existing `--attack`/`--fight`
   probes with a fixed `--rng-seed` against artemple/Den critters; save stdout
   as fixtures. Add the determinism test (same seed twice → identical) as a
   *currently-passing* baseline. Nothing changes behavior yet — this is the net.
2. **Define `ICombatHost` + `CombatEngine` skeleton in Formats (½ day).** Empty
   engine owning the state fields; `ViewerGame` implements `ICombatHost` by
   delegating to its existing private methods (`StartAttackAnimation`,
   `FinishCorpse`, `RebuildBlockedTiles`, etc.). No call-sites moved yet.
   Compiles, tests still green.
3. **Move the pure-decision methods first (½ day):** `RollAttack` (already calls
   only Formats math + the host for ammo/proto), `BeginCombat`, `AddJoiners`,
   `BuildEnemyQueue`, `EndPlayerTurn`, `CombatShouldEnd`, `EndCombat`, `AwardXp`.
   These have the fewest viewer dependencies. Re-run transcript + determinism.
4. **Move the choreography (1 day):** `TryAttack`, `ProcessCombatAnimations`,
   `ResolveAttack`, `KillCritter` (→ host `OnCritterDying`), `OnScriptAttack`,
   `UpdateCombat`/`Step`, `StepEnemyTurn`, `TryEnemyAction`, `TryAllyAction`,
   `EnemyAttack`, `ResetCombatState`. This is where the AP resets (§A) and the
   draw-list/blocking callbacks (§C/§D) cross the boundary — watch them. The
   viewer's `Update` (:1242-1243) becomes `_combat.Step()`; the harness loop
   (:599-642) drives `_combat` through its public surface.
5. **Wire the determinism test + a `[GameDataFact]` per assertion (½ day):**
   the three AP-reset asserts (§A), corpse-still-blocks-nothing, transcript
   line-order. This becomes the regression net for M1-M5.

**Pivot/risk note:** the only soft spot is `EquippedWeapon`/`WeaponAmmo`/
`TryReload`/`LoadedAmmo` (:2287-2397) — they read `_protos`, `_dudeInventory`,
and the proto cache, all viewer-owned. Keep them on the host side of the seam
(callbacks), do NOT pull the proto loader into Formats; that would drag
MonoGame-adjacent file I/O into the engine-free library and is out of M0 scope.
If the host surface for them feels heavy, that is correct — they are genuinely
viewer concerns and the interface honestly reflects it.

---

## UNVERIFIED

- The `--attack`/`--fight` transcript *tests* themselves: the harness flags
  exist (`Program.cs:113-117`) and the exact stdout lines are quoted above, but
  I did not locate a checked-in test that captures+diffs them (the
  `Formats.Tests` project is engine-free and contains none; the prompt asserts
  they exist as a transcript-diff). Treat "fixtures exist" as an assumption;
  step 1 above creates them if absent. UNVERIFIED: location of any existing
  transcript-diff harness outside `Formats.Tests`.
- Engine `_combat_should_end` (combat.cc:3339) uses team + whoHitMe over the
  combat list; our `CombatShouldEnd` (:2701) is the simpler "any hostile still
  standing". This is a pre-existing divergence, not introduced by M0 — flagged
  so the parity claim is honest.
