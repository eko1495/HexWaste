# Hexwaste — current backlog (2026-07-16)

Supersedes the stale `docs/CAMPAIGN-PORT-REVIEW.md` (dated 2026-06-30) and the P1–P114
`docs/PHASE-HISTORY.md`. Synthesized from a four-axis code+docs sweep at P137.

## Headline

**There is no missing gameplay subsystem for the vanilla campaign.** The engine is a
near-complete FO2 port carried through ~137 phases: all 155 maps load/walk, the *entire*
vanilla external opcode set (0x80A1–0x8155) is wired (221 `case` handlers in `IntVm.cs`;
0/155 maps reference an unwired external), and every large system is built and golden-tested —
lighting, reg_anim, combat AI, perks/traits, skill checks, inventory/encumbrance, worldmap
encounters, party/companions, doors/traps, sound + `.lip` lip-sync, **MVE movies**
(`Movie/MveVideo.cs`), **endings slideshow** (`Endgame/`), chargen + character screen, barter.
The old review's "stubbed externals" list (dialogue_system_enter, load_map, explosion,
endgame_slideshow/movie, …) is all wired now (P100/P102 "Zero Stubs").

So the backlog is (A) a short list of genuinely-unbuilt/partial *engine* items, (B) the
quest-driver + per-quest QA frontier (the project's own dominant remaining work), (C) cosmetic
rendering-fidelity polish, and (D) doc housekeeping. Plus the explicitly out-of-scope layer.

---

## Tier A — genuinely unbuilt / partial engine gaps (small, high-value)

**A1 — Karma / reputation EFFECTS. ~~THE one behavioral hole.~~ VERIFIED FAITHFUL — NOT A GAP
(2026-07-16).** The initial research flagged this as the top engine hole; grounding it against
`reference/fallout2-ce` proved the opposite. `_reaction_influence_()` (`reaction.cc:39`) is
*itself a no-op that returns 0* in the real engine — so Hexwaste's arity-stub of the 0x80B3 opcode
is faithful, not a defect. The engine never auto-awards karma (nothing in `combat.cc`/`critter.cc`;
the only `GVAR_PLAYER_REPUTATION` write-hook in `game.cc:1003` is an sfall-only "You gained N
karma" *message*, not a mechanic). `PC_STAT_KARMA` (stat 4) and `GVAR_PLAYER_REPUTATION` (global 0)
are separate read-only stores (`stat.cc:605`) — exactly how Hexwaste holds them (`_dudeKarma` +
`Gv(0)`, `ViewerGame.cs:1441/530`). Karma/reputation *effects* in FO2 are entirely SCRIPT-driven:
scripts read the values (`get_pc_stat`/`get_global_var`, all exposed) and branch or set the
per-critter reaction var; the engine's only roles — expose the stats and show the dialogue
reaction meter/head-mood — are both wired (P31/P122). So karma already works wherever content uses
it; the "cosmetic" appearance is a CONTENT-slice consequence, not an engine gap. Building "karma
effects" would mean inventing behavior fo2ce lacks (violates "port, don't guess"). **Closed.**

**A2 — Combat-AI fidelity residuals. RECONCILED after the fo2ce-combat-ai-fidelity batch
(2026-08-12).** *Effort M (golden re-records) · medium-high impact.* Most of the batch landed;
what's left is smaller and more precisely scoped than the 2026-07-16 pass below.

- **Ported this batch:** `_combatai_rating` (`AiRating.cs`) feeding the companion Strongest/Weakest
  comparators (vanilla's inverted-comparator quirk deliberately preserved); the `_ai_best_weapon`
  weapon-perk ×2 damage factor; `aiHaveAmmo` inventory-caliber search; NPC combat-drug (Jet/Psycho)
  timed wear-off on the game clock (replacing the old "cleared at combat end" behavior); AI
  ground-weapon pickup (`_ai_search_environ` + `_ai_retrieve_object`, BIPED-only, with a
  stale/claimed-item guard); and the two crippled/out-of-range `_ai_switch_weapons` triggers — a
  crippled arm making the wielded weapon unusable (`combat_ai.cc:2800`) and already-unarmed-and-
  out-of-range (`combat_ai.cc:2823`), both wired in `CombatEngine.cs` `TryEnemyAction`. The
  reference's crippled-arm branch condition is actually `reason == NOT_ENOUGH_AP || ARM_CRIPPLED ||
  BOTH_ARMS_CRIPPLED` (`combat_ai.cc:2799-2804`) — the not-enough-AP site of that OR is neither
  wired nor exercised here; only the two crippled sites landed this batch.
- **Shipped: rating-gated retaliation (`_combatai_check_retaliation`).** Originally deferred
  mid-batch because it moved the `brawl-watch` encounter fixture; the project owner reconsidered
  and authorized a deliberate re-record (see
  `docs/superpowers/specs/2026-08-12-retaliation-rerecord-design.md` and the
  `feat: rating-gated retaliation` commit). `RegisterHit` now replaces `whoHitMe` only when the new
  attacker's `_combatai_rating` is strictly greater than the incumbent's (equal-rated attackers do
  not steal aggro) — `CombatEngine.cs` `RegisterHit`/`Rating`. The blast radius was measured at
  exactly one fixture (`brawl-watch`: rounds 11→9, survivors 1→2, winTeam [2]→[1], dudeHp unchanged
  at 30) before recording; `record` touched only that file.
- **Cut (needs prior work):** `_ai_search_inven_armor` (companion armor auto-equip,
  `combat_ai.cc:2051`) — Hexwaste has no per-NPC worn-armor model (`ApplyArmorBonus`,
  `ViewerGame.cs:4318`, folds armor into the dude's sheet only; `CritterState` has no worn-armor
  slot for any other critter). Needs a `WornArmorProto`/equip pair (stat bonuses, DR/DT in the
  damage path, sprite fid) as its own task before this item can land.
- **Grounding corrections recorded this batch:** `attack_who` is party-member-only
  (`combat_ai.cc:1544`), so Hexwaste's companion-only application of it is faithful, not a gap;
  `_combatai_rating` also keys `_compare_strength`/`_compare_weakness` (the previous HP-based
  companion ranking was an undocumented divergence, now fixed); perception-based disengage was
  already ported (`WantsToStopFighting`).
- **Shipped: `_ai_danger_source` port, and the deletion of `PruneEscapedHostiles`
  (2026-08-20, `feat/ai-danger-source`, commits `f4392bb`, `effabf1`, `d76eb14`, `ce2ee04`,
  `775fd8d`, `83864fe`, `9ae21b7`, `84af67b`).** This closes the item this tier had been carrying
  since the batch above as "the piece still deferred." Full account:
  - `f4392bb` ports `aiFindAttackers` (`combat_ai.cc:1457-1525`) and `_ai_find_nearest_team`
    (`:1397-1423`) as pure, inert helpers — no caller yet, so this commit cannot move a fixture by
    construction.
  - `effabf1` ports `_ai_danger_source` itself (`combat_ai.cc:1529-1705`) as
    `CombatEngine.DangerSource(MapObject)`, replacing `TryEnemyAction`'s hand-rolled
    target-selection prologue (`FriendAttacker` deleted as subsumed). Four scope expansions rode
    in with it, all shipped: `CheckBadShot`/`ShotStatus`, a port of `_combat_check_bad_shot`
    (`combat.cc:5643-5694` — Hexwaste had no unified version of this before); `CombatRoster`, the
    `_curr_crit_list` equivalent; `StartBrawl`'s whoHitMe seeding, a port of `_caiTeamCombatInit`
    (`combat_ai.cc:1725-1755`) generalized from two teams to N (every reference call site passes
    `flags=1`/same-team; `StartBrawl` supports arbitrary team counts, so it widens the flag
    argument rather than hardcoding it); and `MapObject.LastAttackTarget`, backing
    `aiInfoSetLastTarget` (defined `combat.cc:2469`, the call this ports is at `:3558`).
  - `d76eb14` and `ce2ee04` are review fixes: routing `BuildTurnOrder`'s combat-open whoHitMe
    stamp through a single `SetWhoHitMe` helper carrying `_critter_set_who_hit_me`'s team gate
    (`critter.cc:1285-1301`) instead of a raw assignment, plus `CheckBadShot` fidelity and clearing
    `LastAttackTarget` at combat end.
  - **Two marker decisions, recorded so neither is re-litigated:** the `// CE:` "Whomever is
    attacking me" targeting improvement at `combat_ai.cc:1565` was **excluded** — CLAUDE.md puts
    fork/CE quality-of-life out of scope, and because the block is purely additive (an `if (1)`
    wrapping extra logic ahead of the vanilla fallback, not a replacement of it), omitting it
    leaves the vanilla `e97087b` behavior intact underneath. The `// SFALL: Add `continue`...`
    one-slot-per-candidate fix inside `aiFindAttackers` (`combat_ai.cc:1481-1482`) was **ported** —
    precedent already set by `EventQueue.cs` (SFALL multi-event dedup, cites `combat.cc:4802`) and
    `AiBestWeapon.cs` (SFALL avg-damage fix), both of which already treat a cited SFALL correction
    as part of the baseline to port, not fork-only QoL.
  - **The plan's original design was wrong, and the correction is the useful part.** The plan
    (`ee7f7c5`/`ca1dedc`, the spec/plan docs commits preceding this sub-project) told the
    implementer to put the danger-source test inside a mutating `PruneEscapedHostiles`. `775fd8d`
    did exactly that — and re-recorded
    `denbus2-fight-flee` with `hostilesLeft` 16→6, a large, unreviewed-feeling swing. `83864fe`
    caught the error: `_combatai_want_to_stop` (`combat_ai.cc:3211`) was **already** correctly
    ported, as `WantsToStopFighting`/`TryEndCombat`, non-mutating, tested, and wired to the
    reference's actual and only call site (`combatAttemptEnd`, `combat.cc:3087`, which only
    *queries* whether combat may end — the reference never evicts anyone from a fight). Following
    the plan produced a **second** implementation of one reference function, and the duplicate had
    a bootstrap gap: `AddJoiners` adds a critter to `_hostiles` without stamping `WhoHitMe`, so a
    fresh joiner had no danger source, was evicted before its first turn by the new mutating prune,
    and `WantToJoin` re-added it next round — measured live as `Villager@9274` evicted every round
    with `enemy=null`. `83864fe` deleted `PruneEscapedHostiles` entirely and routed
    `WantsToStopFighting` into `CombatShouldEnd` instead: no mutation, no vacancy for `AddJoiners`
    to refill, so the oscillation became structurally impossible rather than tuned away.
  - That correction exposed a second, unrelated live bug (`9ae21b7`): `StepTurnOrder`'s
    `while (true)` loop calls `StartNewRound()` with no return, so once one team is fully
    eliminated and every remaining actor's turn returns `false`, it free-runs through rounds
    inside a single `StepTurnOrder()` call and never reaches the caller's `CombatShouldEnd()`. The
    only backstop was `MaxSpectatorBrawlRounds` — a P73 cap built for two teams that can't reach
    each other, not for an already-decided fight. Proven with an unfiltered trace in which all 34
    `enemy-attack`/`knockback` lines were byte-identical to a pre-fix baseline and only the round
    counter differed. Fixed by porting `combat.cc:3446`'s own
    `} while (!_combat_should_end());`, checked once per round (the increment precedes the check,
    `combat.cc:3445-3446` — `84af67b` re-recorded `brawl-watch`'s round count 7→8 to match that
    increment-then-check order; reproducing 7 was explicitly declined as bending the port to
    preserve a fixture).
  - **Blast radius, measured: exactly two fixture lines, not the broad move this tier's own
    framing above led readers to expect.** `denbus2-fight-flee`: `joins: Vic@17070` 5→1 and
    `fight-result hostilesLeft` 16→17 — Vic was oscillating under the OLD flat-distance
    `PruneEscapedHostiles` before this sub-project ever started; removing the mutation fixed
    pre-existing churn as a side effect, it did not introduce new churn. `brawl-watch`: `rounds`
    7→8 in `84af67b`, but **6→8 net versus the merge base** — the 6→7 step came earlier in the
    branch, in `effabf1` (the `_ai_danger_source` port itself); everything else (teamsAtStart, ended, survivors, winTeam, dudeHp) unchanged. Why so small:
    the entire `tests/golden-combat/` suite is dude-initiated 1v1/1v-few combat, where old
    hand-rolled target selection and the new `_ai_danger_source` converge on the same target almost
    everywhere — only the multi-team, dude-absent `tests/golden-encounter/` fixtures had any real
    chance of showing a difference, and only two of those did. Final verification:
    `dotnet test` 936 passed / 0 failed / 91 skipped; `combat-golden.sh check` 17/17;
    `encounter-golden.sh check` 187 ok plus the one justified re-record.
  - **Open follow-up, not fixed here, tracked as F24 below:** `BeginScriptAggro` joins a critter
    to combat without the `WithinPerception` gate that `WantToJoin` applies.
- **Shipped BYTE-IDENTICAL, not as a re-record (2026-08-13):** the ring-spiral explosion victim
  walk (`ExplosionSpiral.Tiles`, ported from `_compute_explosion_on_extras`, `combat.cc:4022-4045`),
  `Explode`'s ordering by that spiral with the centre critter kept primary, and the `_ai_best_weapon`
  explosive ×(extras+1) damage-score factor (`AiBestWeapon.AvgDamage`'s new `explosionExtras`
  parameter, wired via a damage-free `ExplosionExtrasAt` counting mode in `CombatEngine.cs`, extras
  applied BEFORE the weapon-perk ×2, `combat_ai.cc:1857-1870`). This item entered the backlog
  expecting to move a fixture (hence its earlier re-record-tier placement); it didn't — all 16
  `combat-golden.sh` fixtures stayed byte-identical, so it shipped without a re-record. The factor's
  liveness (not just wired) is proven by `AiPrefersABlastWeaponWhenExtraVictimsPushItsScoreAhead`
  (`CombatEngineTests.cs`), which fails when `ExplosionExtrasAt` is stubbed to return 0. Five
  documented divergences from the reference remain (see the `Explode`/`WeaponDamageRadius`/
  `ExplosionExtrasAt` doc comments in `CombatEngine.cs` for the exact citations): attacker backwash
  is not ported in the `Explode` DAMAGE path (`combat.cc:4056-4060` — `arcaves-throw-grenade`
  exercises this: the dude takes ordinary blast damage from his own grenade where the reference
  computes backwash separately). `ExplosionExtrasAt` — the AI's damage-free COUNTING helper for the
  ×(extras+1) weapon-switch score — is a distinct site that hits the same `combat.cc:4056-4060`
  special-case: it now excludes the attacker from its `occupied` tile set (fixed 2026-08-13; see its
  doc comment), so the two attacker-vs-explosion sites (damage in `Explode`, counting in
  `ExplosionExtrasAt`) diverge from the reference in different, independently-tracked ways — `Explode`
  still applies ordinary (non-backwash) damage to a self-caught attacker; `ExplosionExtrasAt` now
  correctly never counts the attacker as an "extra". The centre critter is hit first inside `Explode`'s
  own loop rather than through the reference's main attack path; victim discovery is by tile-occupancy
  map, not per-tile `_obj_blocking_at` (differs for multihex critters spanning several tiles); the
  grenade (2) / rocket (3) radius split is applied in `WeaponDamageRadius` for the AI's count, but
  `Explode` itself still takes one caller-supplied radius; and damage computation stays Hexwaste's
  pre-existing simplified formula, not `attackComputeDamage`.
- **Still in the re-record tier** (unchanged by this batch — see the individual re-record-tier
  bullets below for detail): `_combat_safety_invalidate_weapon` +
  `_cai_retargetTileFromFriendlyFire` (ally-in-LoF weapon-switch invalidation + snipe-back is
  partial, `FriendlyOnFireLine`, `CombatEngine.cs:2382-2390`). Rating-gated retaliation left this
  tier when its branch merged (2026-08-15) — it was the one item here that did move a fixture, and
  `brawl-watch` was deliberately re-recorded for it. `_ai_danger_source` + `PruneEscapedHostiles`
  left this tier when the `feat/ai-danger-source` branch merged (2026-08-20) — see the shipped
  bullet above; despite the tier's name, the actual blast radius was two fixture lines, not a broad
  move, because the golden suite is almost entirely dude-initiated 1v1 combat where old and new
  target selection agree.
- **Final-review follow-ups (not implemented — documentation only):**
  - The out-of-range switch trigger (`CombatEngine.cs` `TryEnemyAction`, `:2732-2753`) is ordered
    AHEAD of the reference's flee check: the engine's `COMBAT_BAD_SHOT_OUT_OF_RANGE` branch
    (`combat_ai.cc:2807-2815`) evaluates `_determine_to_hit_no_range` with the PRE-switch weapon and
    flees BEFORE switching if it can never land a hit; Hexwaste's min-to-hit flee check (`:2761-2763`)
    runs AFTER the switch, on the POST-switch weapon shape — a residual ordering divergence,
    unexercised by any golden.
  - The AI's ground-pickup walk (`_ai_search_environ` → `TryRetrieveItem`/`StartWalk`) deducts no AP
    from `_actingEnemyAp`, unlike the reference's move-then-pickup cost.
  - `_npcDrugBonus`/`_pendingDrugEvents` are never cleared on map change: a living NPC that chemmed
    up mid-fight and was left behind on the old map (not killed, not carried to the new map) retains
    a strong `MapObject` reference plus a pending timed event indefinitely.
  - `ApplyNpcDrugEffect` has no `_combatKillCritterOutsideCombat` analogue — intentional, not a gap:
    no vanilla combat drug applies a negative stat-35 (`current_hp`-adjacent) kick outside combat, so
    there is nothing for such a hook to catch. (Also noted as a code comment at the call site.)
  - **Genuine fidelity gap, not implemented (2026-08-13 review):** the explosive ×(extras+1) factor
    is wired only for the ENEMY weapon-switch path — `TryEnemyAction` → `AiSwitchWeapon(..., defender:
    defenderObj)` and its harness `ProbeAiWeaponSwitch` both supply a defender, so `ExplosionExtrasAt`
    can count victims. `TryAllyAction` (`CombatEngine.cs:3068`) and `ProbeAllyWeaponSwitch` call the
    same `AiSwitchWeapon` overload with NO defender argument, so companions' weapon-switch scoring
    never gets the boost. Review traced the reference call graph — `combat_ai.cc:3060-3150` →
    `_ai_try_attack` → `_ai_switch_weapons` → `_ai_best_weapon` — and confirmed it runs identically for
    any AI-controlled combatant, including party members; the reference makes no enemy/ally
    distinction here. So a companion carrying both a rifle and a grenade, with several hostiles
    clustered around its target, will never prefer the grenade the way an enemy would. Follow-up:
    thread a defender (the ally's current target) through `TryAllyAction`'s `AiSwitchWeapon` calls and
    `ProbeAllyWeaponSwitch`, matching the enemy path. Not implemented in this fix (scope: enemy-path
    porting-error corrections only) — recorded here as the documentation gap review flagged.

**A3 — Per-ally whoHitMe tracker (party tactics). GROUNDED + FIXED (2026-07-16).** The claim was
half-stale: the per-critter `WhoHitMe` tracker *does* exist (added P101, `CombatEngine.cs:1616`),
but the P50 companion candidate builder still hardcoded `HitMe=false`, so `WhoeverAttackingMe`
always degraded to Closest. One-line wire-up (`ReferenceEquals(ally.WhoHitMe, h)`) — companions in
Defensive/Custom now target the hostile that last hit them (combat_ai.cc `_ai_find_target`). Default
disposition is Closest so goldens unaffected. **Closed.**

**A4 — Poison / radiation counters. MOSTLY ALREADY BUILT — two small gaps fixed (2026-07-16).**
The research over-reported this too: poison (P35) + radiation (P101/P113) are fully modeled —
counters on the dude, `AdjustPoison`/`AdjustRadiation` with resistance, the `10*(505-5·p)` poison
tick + the radiation band/endurance-roll/7-day-heal/rad-death model (`RadiationTables`), the
`poison`/`radiation_inc`/`radiation_dec`/`get_poison` externals wired, `get_critter_stat` 36/37,
save/load, HUD + Pip-Boy "Poisoned". Two genuine remainders, now **fixed**: (1) the drug pipeline
excluded stats 36/37, so RadAway/antidote/healing-powder were inert for their signature effect —
now routed through `ApplyPoison`/`ApplyRadiation` per fo2ce `critterSetBonusStat`'s non-SAVEABLE
switch (`stat.cc:530`); (2) Pip-Boy "Radiated" was hardcoded false — now `Radiation != 0`
(`character_editor.cc:2675`). Golden `drug-radaway` locks RadAway 300→275. **Closed.**

**A5 — Small correctness residuals.** *Effort S each.*
- Ghost perk's light-gated Sneak bonus is unwired — GROUNDED: a documented cut (`PerkRules.cs:48`)
  needing a per-object `objectGetLightIntensity`; the tile light grid exists, the per-object query
  doesn't. Real but deferred (small-medium).
- Cross-script **imported-procedure calls threw** (`IntVm.cs:1105`) — **GROUNDED + FIXED**: fo2ce's
  own `opCall` no-ops the imported branch (`interpreter.cc:2044`, `// TODO: Incomplete`), so vanilla
  never emits a direct call to an imported proc; Hexwaste's throw was strictly less robust than the
  reference. Now a faithful no-op (falls through). **Closed.**
- Unwired externals silently return 0 (`IntVm.cs:2094`) — safe for vanilla (0/155 maps), but the
  fallback masks any future gap; a `--strict` mode that logs would be a cheap guardrail.
- Area-explosion VICTIM ORDERING now follows the engine ring-spiral (`ExplosionSpiral.Tiles`,
  shipped 2026-08-13 — see A2's shipped bullet); damage computation itself stays Hexwaste's
  simplified formula, not `attackComputeDamage` (`CombatEngine.cs` `Explode`).
- **Final-review correction (2026-08-14): the near-grid-edge divergence is a SET change, not just an
  ordering change.** `ExplosionSpiral.Tiles` stops its walk the first time it detects a
  `tileGetTileInDirection`-style clamp at a grid edge, whereas the reference's caller keeps walking
  and re-examines that same clamped tile as if it were new ground on every subsequent step of the
  ring (an unguarded loop, not a designed feature — `tile.cc:893-906` / `combat.cc:4022-4045`). So
  near an edge, this port's spiral enumerates strictly fewer distinct tiles than the reference would
  visit, and the victim SET can shrink, not merely reorder. The reference is no better here (its
  repeated-tile behaviour would just spin), so stopping early remains the right engineering call —
  NOT changed by this fix, only documented (see `ExplosionSpiral.cs`'s doc comment).
- **Final-review addition (2026-08-14): `Explode`'s `maxTargets = 6` cap semantics.** The reference's
  `explosionGetMaxTargets()` (6) bounds only the `extras` array — the primary defender at the blast
  tile is hit outside that cap, so one reference blast can damage up to 7 critters. This port's
  `maxTargets` counts the centre critter toward the same cap of 6, so it can damage at most 6.
  Documented divergence, not changed.
- **Final-review addition (2026-08-14): shared-tile collapse in `Explode`.** `byTile.TryAdd` means
  when two critters occupy the same tile, only the first one enumerated becomes that tile's possible
  victim — a second critter on the same tile now takes zero blast damage where a resolution that
  picked the other critter would have dealt it full damage. Judged MORE faithful, not less: the
  reference's own `_obj_blocking_at` also yields a single object per tile, and can itself resolve to
  a wall that masks a critter standing on the same tile. Not changed — but gameplay-visible, so
  documented here and in `Explode`'s doc comment.

**A6 — Party members land STACKED on one hex after a map transition. ~~LIVE BUG~~ FIXED 2026-08-14
(found and fixed the same day, independently reviewer-confirmed).** *Effort S · user-visible.*
Fixed by `Placement.FreeTilesAround` (`src/Hexwaste.Formats/Map/Placement.cs`), which does one
placement pass for the whole party and claims each tile as it is handed out; `InjectPartyMembers`
now calls it once instead of hand-rolling a per-member scan. Covered by `PlacementTests`
(3 cases: distinctness, blocked-neighbour skip, centre fallback once the ring is exhausted). All
golden suites stayed byte-identical — the first member keeps the tile it always got, only the
subsequent ones move. Original diagnosis, for the record: `InjectPartyMembers`
(`src/Hexwaste.Viewer/ViewerGame.Party.cs:77-107`) picks each member's spawn tile by scanning
`_blockedTiles` for the first free neighbour of the dude (`:84-92`), but `RebuildBlockedTiles` is
called only **after** the loop (`:106`) and the just-placed member's tile is never added to the set
inside the loop. So every member in `_scriptHost.PartyMembers` resolves to the *same* first free
neighbour: with 2+ companions they all land on one hex on arrival at a new map. Fix: add the chosen
`spawn` to `_blockedTiles` immediately after `member.HexTile = spawn` (`:94`), or call
`RebuildBlockedTiles` per member (cheaper to do the former). Not a fidelity nicety — this is a
straightforward defect in our own placement loop, unrelated to the reference. Surfaced while closing
PR #675 hunk 60 in the fork survey (`docs/research-notes/fork-fix-ledger-2026-08.md`); see also F3,
which is the separate *fidelity* half of the same routine.

## Tier B — quest-driver + campaign-QA frontier (the dominant remaining WORK)

Per the project's own analysis, the bulk of "finishing the game" is per-quest playtest/QA, not
engine code. 25 quest goldens locked; census baseline: 56 quest-bearing maps, batch stuck=183.

**B1 — Deep sub-menu routing. INVESTIGATED — the premise was wrong; almost no real targets
(2026-07-16).** The hypothesis was that New Reno's ~76 stuck runs are single-NPC deep menus the
greedy can't descend. Grounding the actual failures disproved it. The newr1 stuck tail is:
**7 of 10 combat-gated** (`damage_p_proc` completions — the crime-family war; ncSalMen/ncBisMen/
ncMorMen/ncWriTee/ncCasBou — these need `--kill`, i.e. B3, not routing), and the dialog ones
(286 `WRIGHT_MYSTERY`, 343 `SAD`, 547 `WESTIN_SNUFF`) are **multi-NPC investigation chains**, not
deep menus: a QDTRACE of the driver on 286 shows `onGraph=[]` every round — the completing NPC's
intro options don't touch the quest gvar *at all* (the quest is advanced by *other* NPCs/events;
this NPC only closes it once investigation state is accumulated elsewhere). So there is no
single-NPC dialogue path to route, deep or otherwise. A reverse-BFS distance-field router (score
every proc that can reach a gvar-write, vs one shortest path) was tried and **reverted**: it
regressed 497 (the single-path ORDERING matters — a flat distance picks a nearer-but-wrong branch)
and didn't help the targets (`onGraph=[]` means reverse-BFS finds nothing reachable either).

**Re-scoped real levers for the NR/SF stuck tail:** (1) **B3 — driver-invoked `--kill`** for the
`damage_p_proc` family-war quests (the majority of the NR tail). (2) **A multi-NPC investigation
driver** (a much larger cross-map state-accumulation engine — talk NPC-A → advance a stage-gvar →
talk NPC-B → … → completer) — this is the genuine hard problem, materially bigger than B2's
bit-prereq. Neither is "deep sub-menu routing." B1 as originally framed is **closed as a non-lever**.

**B2 — Cross-map bit-prerequisite driver.** *Effort M.* Combine the existing cross-map hop (§9 #3)
with the P137 bit-level prereq tracking (§12) so activate-at-A / bit-set-at-B / complete-at-C
chains drive end-to-end (VC 321/89 cross-town, New Reno multi-NPC) without hand-tracing.

**B3 — Driver-invoked escort / combat / clock verbs.** *Effort M.* Teach the driver to auto-emit
`--teleport`+`--escort-pump` (693 Jonny, 616 Woody), `--kill` for combat completions (454 Lara
gang war), and `--set-hour` for time-gated NPCs — instead of an operator hand-assembling them.

**B4 — Campaign-state fixture track.** *Effort M.* A sanctioned way to seed *legitimate* prior-
story prerequisites (without `--set-global`-faking the quest gvar itself) to unlock the non-bit
prereq tier: 488 (Goris/V13 deathclaw storyline), 481 (NCR brahmin reputation gate), 85 (jet/drug
context). Explicitly deferred as its own track in plan §11a.

**B5 — Continue the per-town QA sweep.** *Effort XL, ongoing.* The long tail: Den 101/454, Gecko
powerplant (82/397), New Reno families/prizefight/Jet, NCR, SF/Chinatown. Now semi-automated by
the harvest pipeline + the driver; B1-B4 each shrink what's left manual.

## Tier C — rendering-fidelity polish (cosmetic; weigh against the no-shader decision)

Consistent with the deliberate P4 no-shader / float-damage-numbers choices — optional authenticity:
per-pixel light/dark palette blend (currently uniform per-object tint, `Rendering.cs:312`),
per-pixel translucency + egg-mask alpha (uniform now), palette-lerp screen fades (GPU black-quad
now), and the monitor-log-vs-floating-damage-text divergence. All cosmetic; none affect mechanics.

## Tier D — doc housekeeping

`docs/PHASE-HISTORY.md` stops at P114 (code carries P137 markers); `CAMPAIGN-PORT-REVIEW.md`
(2026-06-30) is obsolete — it lists now-wired externals as stubbed and predates endings/MVE/
lip-sync/harvest. Several docs say "16 goldens" (actual: 25). Reconcile so future work isn't
misled by stale strategy docs. *Effort S.*

## Tier F — fidelity gaps surfaced by the maintained-fork survey (2026-08-14)

These were found while classifying fork candidates in
`docs/research-notes/fork-fix-ledger-2026-08.md`: each sat inside a row whose status is
`not-a-gap` / `not-applicable` / `rejected-non-vanilla` — meaning *nothing to do about the fork
commit* — while the reading that closed the row exposed a real **Hexwaste-side** divergence from
`e97087b`. They are unrelated to the fork commits that surfaced them, and would have been lost as
ledger prose. Each is independently actionable. Summary + context:
`docs/research-notes/fork-survey-2026-08.md` §5.3.

### Combat AI

**F1 — SHIPPED 2026-08-15 (`57e6ce6`), byte-identical, no fixture re-recorded.** *Was Effort M ·
re-record tier (prediction falsified — see below).* Ported `e97087b`'s `_ai_run_away`
(`combat_ai.cc:1173-1217`) into `CombatEngine.TryFlee`: inside `max_dist` the critter is marked
`CRITTER_MANUEVER_FLEEING` (`:1184`) and runs as before; at or beyond it, the `else` sets
`CRITTER_MANEUVER_DISENGAGING` (`:1216`) and the critter takes no movement, no AP and no attack.
The comparison is `<`, matching `e97087b` (`:1183`) — the fork's PR #675 flips it to `<=` and that
hunk was rejected as ungrounded. A null AI packet keeps the pre-gate behaviour (always flee) rather
than inventing a default `max_dist`. Six hermetic tests, four mutation-verified; four pre-existing
tests repaired from the placeholder `MaxDist: 0` to a realistic `10`.

**This entry originally predicted `denbus2-fight-flee` would move and be re-recorded on the P120
precedent. That prediction was wrong, and the wrongness is the useful fact for a future reader:**
every fleeing critter in that fixture stays inside its packet's `max_dist` (recorded distance ≤ 8
against `max_dist` 10), so the gate never fires there and the fixture came out byte-identical.
`dotnet test`: 910 passed / 0 failed / 91 skipped. `combat-golden.sh check`: 16/16. Don't assume a
"live golden fixture" claim in a backlog entry means the fixture *will* move — verify against the
actual recorded values before touching `record`.

**Corrected framing (the original entry's diagnosis was too broad).** The entry originally claimed
Hexwaste "has no distance predicate at all." The sharper, verified finding: the maneuver flags, all
of their consumers, and the script-side setters already existed and were correct —
`WantToJoin`'s ENGAGING/DISENGAGING|FLEEING checks (`CombatEngine.cs:1995,1997`), the turn-order
filter that drops disengaging critters (`:2085`), `WantsToStopFighting`'s
DISENGAGING|FLEEING short-circuit (`:2213-2217`, the real predicate behind `TryEndCombat`), the
enemy and ally flee-continuation checks (`:2840`, `:3147`), and the script-side setters
`CritterSetFleeState` (`ScriptHost.cs:1805`), the script-attack ENGAGING mark (`:2113`), and
`TerminateCombat`'s DISENGAGING mark (`:2282`). The gap was narrower: the **engine's own AI never
set the flags on an engine-initiated flight** — the only engine write to `Maneuver` before this fix
was the `= 0` reset once a critter joined combat (`CombatEngine.cs:2016`) — so a critter that fled
because it got hurt or ran low on HP could never be marked FLEEING, and could never reach
DISENGAGING to let a fight actually end.

**F18 — SHIPPED 2026-08-15 (`64500e8`, `ec736ad`), `denbus2-fight-flee` deliberately re-recorded
(the only fixture that moved). Fully closed as of F21 (SHIPPED 2026-08-17) — the residual phantom
flee noted below was F21's cause, now fixed.** *Was Effort M · re-record tier.*
`64500e8` ported `Pathfinder.FindPath`'s `requireFreeDestination` parameter, the reference's `a5`
argument (`animation.cc:1716-1722`): with it set, a blocked destination yields no path before any
search runs. Defaults to `false` (`a5 = 0`, the unconditional goal exemption Hexwaste always had),
so every other call site stayed inert; the class doc, which had claimed the unconditional exemption
"matched the original" (true only of `a5 = 0`), was corrected. `ec736ad` made `CombatEngine.TryFlee`'s
retreat search opt in (`CombatEngine.cs:3096-3097`), matching `_make_path(a1, a1->tile, destination,
nullptr, 1)` (`combat_ai.cc:1192`, inside `_ai_run_away`). `tests/golden-combat/denbus2-fight-flee.txt`
was re-recorded: `Cute Slave@11272 -> 10480` logged four times with no movement becomes
`11272 -> 9672` once and actually moves; `Handsome Slave@12670 -> 14270` likewise becomes
`12670 -> 14070`. Six phantom flee lines removed. Combat outcome is byte-identical (rounds=5,
dudeHp=0, gameOver=True).

**Rejected alternative, on purpose:** moving the `flee:` transcript line to after a successful
`StartWalk` was considered and deliberately not done. It treats the symptom (the log line appearing
regardless of outcome) rather than the cause (the destination itself being illegal), and doing both
would have made the fixture delta impossible to attribute to either change individually.

**Not fully closed by this fix alone.** The re-recorded fixture still contained one phantom flee —
`flee: Healthy Slave@10270 -> 8870`, logged in rounds 3 and 4 with the critter at tile 10270 both
times, lines byte-identical before and after this fix (8870 is not blocked, so the new destination
check correctly leaves that pair alone; a different bug was responsible). Explained and fixed by F21
(SHIPPED 2026-08-17): the line is gone from the fixture as re-recorded there.

**F19 — Out of scope for now: the reference's second `DISENGAGING` setter, at the tail of
`_combat_ai`, is unported.** *Effort M–L · **re-record tier** once attempted.* Beyond `_ai_run_away`
(F1, shipped), `e97087b` sets `CRITTER_MANEUVER_DISENGAGING` a second time, at `_combat_ai`'s tail
(`combat_ai.cc:3098-3112`): when the target is alive, the critter has AP left, and
`distance > max_dist`, it first tries to back away from a friendly corpse
(`aiInfoGetFriendlyDead` + `_ai_move_away`, `:3102-3105`) and, failing that, tries
`_ai_find_friend(a1, perception * 2, 5)` (`:3108`), setting DISENGAGING only if no friend is found
(`:3109`). **Neither `aiInfoGetFriendlyDead`/`aiInfoSetFriendlyDead` nor `_ai_find_friend` exists
anywhere in this repo** (repo-wide search, confirmed while writing this entry) — porting this needs
friendly-corpse tracking and a friend search built first. Note the effect is the opposite of what it
sounds like: porting it makes disengagement *harder*, not easier — a critter with a nearby friend
keeps fighting instead of disengaging — so it will move fixtures. Re-record tier, not a docs-only
follow-up.

**F20 — AUDITED CLEAN 2026-08-20: every `Pathfinder.FindPath` call site already matches its
reference `a5`; no code change was needed.** *Was Effort S per site · re-record tier for any that
flipped. None flipped.* F18 established that Hexwaste's `FindPath` modelled only `a5 = 0` and added
`requireFreeDestination` for `a5 = 1`. This entry asked whether the other sites were wrong by
default. They are not — each was traced to its reference counterpart and its `a5` read there rather
than assumed:

| Hexwaste site | Reference counterpart | ref `a5` | verdict |
|---|---|---|---|
| `DudeController.cs:62`, `:83`, `:161` (dude walk / repath) | `_anim_move` → `_make_path(obj, obj->tile, tile, sad->rotations, a5)` (`animation.cc:2407`); **both** callers pass `0` (`:2145`, `:2361`) | 0 | already correct |
| `CombatEngine.cs:2225` (danger-source WHOMEVER fallback) | `combat_ai.cc:1609` | 0 | already correct |
| `CombatEngine.cs:2286` (danger-source main loop) | `combat_ai.cc:1696` | 0 | already correct |
| `CombatEngine.cs:3399` (enemy approach) | `_ai_try_attack`'s move-closer, `combat_ai.cc:2854` | 0 | already correct |
| `CombatEngine.cs:3473` (`TryFlee`) | `_ai_run_away`, `combat_ai.cc:1192` | **1** | ported in F18 |
| `CombatEngine.cs:3643` (ally move) | `_ai_move_steps_closer`, `combat_ai.cc:2396`/`:2398` | 0 | already correct |
| `ViewerGame.cs:5267` (worldmap start-point probe) | `worldmap.cc:4088` | 0 | correct on `a5` — but see F25 |

Two things this audit did *not* close, both tracked elsewhere. `_ai_move_away` (`combat_ai.cc:1239`,
`:1244`, `:1249`, all `a5 = 1`) has **no Hexwaste counterpart at all**, so there is no site to audit —
it belongs to F19. And the worldmap probe diverges from its counterpart on a different axis than
`a5`; that is F25.

The useful negative result: `a5 = 0` was the right default everywhere it was used, so F18's fix was
correctly scoped to the one site that needed `1` rather than being a symptom of a systematic gap.

**F21 — SHIPPED 2026-08-17 (`ad4b79f`, `633a617`, `f64b5d4`), two fixtures deliberately re-recorded.**
*Was Effort M · re-record tier.* Most consequential finding of the F1/F18 sub-project: a stale
`_npcWalkers` entry froze a critter while its `flee:` log kept firing — and the golden fixtures had
been recording a harness artefact as game behaviour. Surfaced reviewing F18:
`denbus2-fight-flee` still logs `flee: Healthy Slave@10270 -> 8870` in rounds 3 and 4, byte-identical,
with the critter's origin tile frozen at 10270 both times — 8870 is not blocked, so F18's new
destination check correctly leaves this pair alone; the cause is different and F18 could not have
touched it. Mechanism, traced through the actual code:
- `StartNpcWalk` (`ViewerGame.cs:3326`, guard at `:3328`) refuses a new walk whenever
  `_npcWalkers.ContainsKey(npc)` — keyed on dictionary **presence**, not on `walker.Moving`.
- A finished walker is pruned only inside `UpdateAmbientLife` (`ViewerGame.cs:3262-3272`).
- The `--fight` autoplay harness that `combat-golden.sh` drives never calls `UpdateAmbientLife` — it
  pumps `walker.Update(10)` directly on every entry in `_npcWalkers.Values`
  (`ViewerGame.Harness.cs:2037-2038`). So once Healthy Slave's round-2 flee finishes, the now-idle
  walker is never removed, and every later `TryFlee` call for that critter hits the stale guard:
  `StartWalk` fails silently while the `flee:` transcript line and the AP-zeroing (`CombatEngine.cs`,
  same shape as the failure mode F18 fixed, but a different cause) have already fired.
- **Not purely a harness artefact — but only via `--no-ambient`, not an open worldmap.** The prune
  sits *after* `if (DisableAmbientLife || _worldmapOpen) return;` (`ViewerGame.cs:3259-3260`) inside
  `UpdateAmbientLife` itself, so `--no-ambient` defeats the same prune in the real interactive game —
  walker lifecycle management is nested inside an unrelated cosmetic feature's early return, not
  solely a test-loop omission. **The open-worldmap half of that claim does not hold**, corrected during
  final review: `Update` itself returns early whenever `_worldmapOpen` is true, before `UpdateAmbientLife`
  is ever called, so no walker advances while the worldmap is open and none can go stale from that path
  — the worldmap being open just means nothing moves, not that pruning is skipped for movement that did
  happen. The real non-harness arm is `--no-ambient` alone.
- The brawl-watch autoplay loop (`ViewerGame.Harness.cs:203-209`) shares the identical
  `walker.Update(...)`-without-prune shape and should be checked for the same defect before this is
  called fixed.
- **Consequence to record plainly:** any golden fixture recorded through the `--fight`/brawl-watch
  autoplay path, wherever an NPC walker finishes mid-fixture, may contain frozen-critter artefacts
  like this one baked in as if they were engine behaviour. Resolve the underlying membership-vs-`Moving`
  bug (and confirm/fix the brawl-watch loop) before those transcripts can be trusted; expect any fix to
  move fixtures, hence re-record tier.

**Shipped — the fix, in three steps.**
- `ad4b79f` replaced `StartNpcWalk`'s guard (`ViewerGame.cs:3356`) with
  `_npcWalkers.TryGetValue(npc, out var active) && active.Moving`, testing walker **liveness** instead
  of dictionary **membership** — ported from `animationIsBusy` (`animation.cc:581`), whose busy test
  walks only sequences actually in use. Because `StartNpcWalk` is a private `ViewerGame` member and
  `tests/` holds only `Hexwaste.Formats.Tests` (no viewer test project), the proof is a live harness
  probe, `--walker-restart-probe <hex> <t1> <t2>` (`Program.cs`, `ViewerGame.Harness.cs:872-895`): start
  a walk, pump it to completion, then start a second walk for the same critter. The dictionary state is
  identical in both runs; only the guard's interpretation of it changed:
  ```
  before: walker-restart-probe: from 14716 t1=14718 started1=1 movingAfterPump=0 inDict=1 t2=14716 started2=0 tile=14718
  after:  walker-restart-probe: from 14716 t1=14718 started1=1 movingAfterPump=0 inDict=1 t2=14716 started2=1 tile=14718
  ```
- `633a617` re-recorded the two fixtures this freed. **Measured, not assumed**: the spec warned the
  blast radius could not be predicted and might be large; the complete failing set across every suite
  was exactly two fixtures, because the suite exercises the frozen-walker path in only two places.
  - `tests/golden-combat/denbus2-fight-flee.txt` — **directly observed**: `Villager@11872` was frozen
    and now moves to `11670`, attacking from there (to-hit shifts 42% → 41% with the new position). The
    duplicate `flee: Healthy Slave@10270 -> 8870` line is gone — that is exactly the residual phantom
    flee F18 could not fix and which this entry predicted had a different cause. The prediction is
    confirmed: this entry was filed while fixing F18 precisely because that pair of lines could not be
    explained by F18, and this fix is that explanation.
  - `tests/golden-encounter/brawl-watch.txt` — `rounds=9 → 6`, `winTeam=[1] → [2]`, survivors=2 and
    dudeHp=30 unchanged. **Originally recorded here as "inferred from mechanism, not observed" — that
    claim was wrong, caught in final review, and corrected below with the actual observation.** The
    "no movement lines to point at" excuse does not hold: the movement lines exist in the fixture's own
    unfiltered stdout, `scripts/encounter-golden.sh`'s `FILTER` regex just strips them out of the
    committed `.txt`. Running the fixture's own command
    (`--brawl-watch desert1.map ARRO_War_Party 2 ARRO_Cannibals 2 --rng-seed 3`) unfiltered against a
    pre-fix build (worktree at `9e90d84`) and the committed post-fix build:
    - both runs reproduce their fixture summaries exactly (`rounds=9 winTeam=[1]` pre, `rounds=6
      winTeam=[2]` post) — deterministic, so the delta is attributable to this change;
    - the pre-fix output contains the F21 signature verbatim: `flee: Cannibal@19901 -> 19109` logged
      **twice**, origin frozen at 19901 both times — the same phantom flee as `denbus2-fight-flee`.
      Post-fix, both lines are gone;
    - post-fix a `Cannibal` attacks from hex `20099` late in the fight, with no `knockback:` line
      getting it there; pre-fix no Cannibal ever occupies that hex (Cannibal positions pre-fix change
      only via `knockback:`/`flee:`).

    So the winner flip is movement-driven and of the same class as the combat fixture, observed rather
    than inferred. Frozen critters cannot close, so the brawl dragged to 9 rounds and resolved by
    attrition; unfrozen ones close and settle it in 6.
- `f64b5d4` hoisted finished-walker pruning out of `UpdateAmbientLife` into its own
  `PruneFinishedWalkers(double)` (`ViewerGame.cs:3265`), called from `Update` independently of the
  `DisableAmbientLife || _worldmapOpen` early return that used to gate it, and used by both autoplay
  loops (`ViewerGame.Harness.cs:207`, dt `100000` for brawl-watch; `:2066`, dt `10` for `--fight`; each
  kept its original dt). Behaviour-neutral by construction after the guard fix: no fixture moved here.
  `dotnet test`: 914 passed; combat-golden: 16/16.

**The brawl-watch open question, answered.** This entry's own text above asked for the brawl-watch loop
to be "checked for the same defect before this is called fixed." It was: the loop shared the identical
no-prune shape, it now calls `PruneFinishedWalkers`, and brawl-watch was one of the two fixtures that
moved as a direct result.

**Suite-credibility consequence, now measured instead of warned.** This entry originally warned that
fixtures recorded through the autoplay paths might contain frozen-critter artefacts baked in as engine
behaviour, with an unknown blast radius. The answer: exactly two fixtures were affected, and both are
now corrected. The concern was real but narrow.

**Rejected alternative, on purpose (same reasoning as F18):** reordering `CombatEngine.TryFlee`'s
`flee:` transcript line to after a successful `StartWalk` was considered and not done. It would treat
the symptom — the log line appearing regardless of outcome — rather than the cause (the stale guard
refusing the walk), and the correct fix makes the line truthful instead of merely quieter.

**Do not "tidy" the probe's pump loop.** `ViewerGame.Harness.cs:889` deliberately calls
`walker.Update(...)` directly on every entry in `_npcWalkers.Values` rather than routing through
`PruneFinishedWalkers` — draining the dictionary there would remove the stale entry the probe exists to
test, and the proof would silently become vacuous.

**F22 — SHIPPED 2026-08-20 (`83864fe`), CORRECTED 2026-08-20: `PruneEscapedHostiles` decided
combat participation by a flat sight-distance radius — an invented *gate*, not an invented
mechanism.** *Was carried inside A2's re-record tier as "the piece still deferred."* Hexwaste had a
`PruneEscapedHostiles` method, called every `Step()`, that removed a hostile from `_hostiles` once
it was more than ~20 hexes away. **Nothing in `e97087b` decides participation by a fixed hex
radius** — that gate is the defect, and deleting it was right.

**The justification originally recorded for the deletion was false, and is corrected here.** This
entry (and the `HISTORY` comment in `CombatEngine.cs`) previously claimed the reference "never
evicts anyone from the fight." It evicts every round. `_combat_sequence()` (`combat.cc:3023`,
called once per round from `_combat()`'s loop at `:3443`) removes dead critters from the combatant
list (`:3030-3042`) and moves knocked-out or `CRITTER_MANEUVER_DISENGAGING` critters to the
non-combatant list (`:3044-3060`); `_combat_add_noncoms()` (`:2899`) re-admits them later via
`_combatai_want_to_join`. `_combat_should_end()` (`:3339-3376`) then reads the *post*-eviction
`_list_com`. And `DISENGAGING` is set by `_ai_run_away` (`combat_ai.cc:1216`) precisely when a
fleeing critter is at or past its packet's `ai->max_dist` (`:1183`) — the reference's own "a hostile
that escaped leaves the fight" mechanism, which the flat prune was crudely approximating. So
**evict-and-re-add is the reference's architecture**, and the `Villager@9274` / `Vic@17070`
evict/rejoin oscillation traced on `denbus2-fight-flee` was evidence that the *gate* was wrong, not
that mutation was wrong.

The follow-on error compounded it: the deletion's replacement folded `WantsToStopFighting` (the
port of `_combatai_want_to_stop`) into the *automatic* end check. `_combat_should_end` never calls
that function — its sole caller is `combatAttemptEnd` (`combat.cc:3087`), the player's manual
"leave combat" gate — so folding it in added two terms vanilla never applies automatically
(`ManeuverFleeing`, and the perception term), and, because `WantsToStopFighting` hardcoded the
danger source as dude+party instead of calling `_ai_danger_source` (`combat_ai.cc:3227`), it also
made combat able to end *mid-fight* when two hostile teams brawled outside the dude's perception.
**Corrected shape (this fix):** `CombatShouldEnd` applies `_combat_sequence`'s own eviction
predicate — dead / KO / `DISENGAGING`, the same one `BuildTurnOrder` already applies to `_order` —
and `WantsToStopFighting` stays where the reference puts it, `TryEndCombat` alone, now deriving its
danger source from the ported `DangerSource` per `:3227-3228`. Hexwaste still keeps a KO hostile
blocking automatic end (P14-M2, a deliberate departure from `:3044-3060`), and still never mutates
`_hostiles` for termination — it does not need to, because `_order` is where the eviction predicate
lives.

**The lesson that still holds** (the one the original entry got right): the `feat/ai-danger-source`
plan (docs commits `ee7f7c5`/`ca1dedc`) told the implementer to port the danger-source test into
the existing mutating method, producing a *second* implementation of `_combatai_want_to_stop`
(`775fd8d`) alongside the already-correct, already-tested, already-wired
`WantsToStopFighting`/`TryEndCombat`. Nobody grepped for an existing port of the reference function
before specifying a new home for its logic. A deferral note that says "needs a port of X" should be
treated as a prompt to search for an existing port of X before scoping new work, not just a
statement that X is missing. **And the second lesson, added here:** the note also asserted a fact
about the reference ("it never evicts") that nobody re-derived from `e97087b` before acting on it.
An architectural claim about the reference is a claim to verify, not a premise to build on.

**F23 — SHIPPED 2026-08-20 (`9ae21b7`), `StepTurnOrder`'s round loop free-ran through every
remaining round once one team was eliminated, silently skipping the automatic end-of-combat
check.** *Live bug, fixed as a direct consequence of F22.* `StepTurnOrder`'s `while (true)` loop
calls `StartNewRound()` with no `return`, so once one team is fully eliminated and every remaining
actor's `TryEnemyAction`/`TryAllyAction` returns `false` (nothing left to fight), the loop falls
through every actor, back to the top, and into another `StartNewRound()` — repeatedly, all inside a
single `StepTurnOrder()` call, never returning to the caller where `CombatShouldEnd()` (F22's fix)
could run. The only backstop was `MaxSpectatorBrawlRounds`, a P73 stalemate cap built for two teams
that structurally cannot reach each other, not for a fight that has already been decided. **Proof,
not inference:** an unfiltered, deterministic double-run (`--brawl-watch desert1.map ARRO_War_Party
2 ARRO_Cannibals 2 --rng-seed 3`) against this build and a pre-F22 baseline worktree showed all 34
`enemy-attack`/`knockback` transcript lines byte-identical between the two — same actions, same
hits, same misses, same damage, same order — with only the final round count differing (7 vs 100),
confirming a pure control-flow spin rather than an AI or RNG change. Fixed by porting the
reference's own round-loop shape, `combat.cc:3446`'s `} while (!_combat_should_end());`, checked
once per round immediately after `StartNewRound()` — not before each actor's turn, so an actor's
own turn (flee, KO forfeit, a script-preset maneuver) still runs before any want-to-stop judgment.
Because the reference increments its round counter and *then* evaluates the end condition
(`combat.cc:3445-3446`: `_combatNumTurns += 1;` then the `while`), the faithful port also corrected
`brawl-watch`'s round count from a pre-increment 7 to a post-increment 8 (`84af67b`) — reproducing
7 was explicitly declined as bending the port to preserve a fixture. **Correction for future
archaeology:** the net move versus the merge base (`5eb2bd5`) is **6→8**, not 7→8. The 6→7 step was
recorded earlier in the branch, by `effabf1` (the `_ai_danger_source` port itself), and only the
7→8 step belongs to this item.

**F24 — OPEN, not fixed: `BeginScriptAggro` joins a critter to combat without the
`WithinPerception` gate that `WantToJoin` applies.** *Effort unknown · needs its own investigation
before scoping.* `WantToJoin` (`CombatEngine.cs:2284-2299`) requires
`WithinPerception(c, dude)` before a critter joins an in-progress fight; `BeginScriptAggro`
(`CombatEngine.cs:2432`) — the script-driven aggro entry point — has no equivalent gate. This
surfaced during the `feat/ai-danger-source` work via a test where a blind enemy at Perception 5
(the blind malus of −5 zeroing effective perception exactly) behaved differently than expected
under the new `WithinPerception`-based prune; the test was adjusted to Perception 8 to sidestep
the zero-perception edge case, and the underlying `BeginScriptAggro`/`WantToJoin` asymmetry was
left as-is (see the Task-3 report's "Concerns" section for the specific interaction). Not yet
grounded against the reference's own script-aggro join site — record here so it is tracked instead
of re-discovered, not so it is assumed to be a bug without checking `e97087b` first.

**F25 — The worldmap start-point reachability probe uses movement-blocking where the reference uses
SHOOT-blocking.** *Effort S · found during the F20 audit.* `ViewerGame.cs:5267`'s
`Reachable(from, to)` passes `IsBlocked` (`_blockedTiles.Contains`, i.e. anything that blocks
movement, critters included). Its reference counterpart, `worldmap.cc:4088`, passes
`_obj_shoot_blocking_at` — the line-of-*fire* predicate, under which a critter standing in the way
does not necessarily block. Both use `a5 = 0`, so this is orthogonal to F18/F20. Consequence: a
start point the reference would accept can be rejected here whenever a critter happens to stand on
the route, making random encounter placement fussier than vanilla. Not yet measured against a
fixture, so the blast radius is unknown; treat as re-record tier until measured.
### Dialog and party

**F2 — `start_gdialog` head mood should come from the critter's REACTION value, not the script's
argument.** *Effort S–M.* For a head-ful dialog **both** `e97087b` and the fork derive the fidget
family from `reactionTranslateValue(reactionGetValue(obj))` and *overwrite* the script's
`reactionLevel`; our P122 seeding always honours the script's argument
(`ScriptHost.DialogSessionStart`, reached from `IntVm.cs:1438`, opcode 0x80DE). Honouring the
argument is exactly what the fork's opt-in, off-by-default `start_gdialog_fix` knob does — i.e. we
ship the modded behaviour as our default. The ingredient already exists: `ScriptHost.ReactionValue`
(`reaction.cc` LVAR[0]), wired for barter in P114. The work is plumbing the `obj` handle the opcode
currently pops and discards (`IntVm.cs:1444`) through to `DialogSessionStart` and using the target
object's reaction. Use the fork's corrected object (`obj`), not `gGameDialogSpeaker` — that
staleness is a decompilation artefact.

**F3 — `_partyMemberSyncPosition`'s fan-out has no analogue: companions do not spread by the dude's
facing.** *Effort M.* `party_member.cc:796` at `e97087b` places each member alternately at
`(dude.rotation + 2) % 6` and `(dude.rotation + 4) % 6`, at an increasing `distance/2`, via
`_objPMAttemptPlacement` (`proto_instance.cc:2244`) — a **chained** walk that steps one tile per
iteration in an incrementing rotation from `gDude->tile`, gated by `wmEvalTileNumForPlacement` and a
`tileDistanceBetween > 8` bail. `InjectPartyMembers` (`ViewerGame.Party.cs:77-107`) is an unchained
ring scan of directions 0..5 at distance 1, with no seed variable and no `wmEvalTileNumForPlacement`.
Note for whoever ports it: `e97087b`'s seed is `int v7 = 0;` with `v7++` **before** first use, i.e.
the first direction tried is 1 (`ROTATION_E`); PR #675 reseeds it to `ROTATION_NW` with no
disassembly, so follow `e97087b`. **A6 is done** (2026-08-14) — it was the distinct, simpler bug in
the same routine, and this port was not required for it. Whoever takes F3 replaces
`Placement.FreeTilesAround`'s ring scan with the chained walk; the per-member tile claiming A6 added
is still wanted, since `_objPMAttemptPlacement` also refuses an occupied tile.

### Rendering / UI fidelity

**F4 — Talking heads are top-anchored instead of bottom-anchored; 14 of 186 heads sit up to 7 px
high.** *Effort S.* The engine bottom-anchors the head inside the 388x200 display buffer —
`destWidth * (200 - height)` — while we pin `y = frameY + 14` (`ViewerGame.cs:6143`). A probe over
all 186 `art\heads\*.FRM` in `master.dat` found 14 with frames shorter than 200 px (e.g. `BOSSSNF1`
at 194/193), so those heads sit high and **shift between frames** as the frame height changes. Note
this is *not* PR #675 hunk 20, whose disputed `rotationOffsetY` term is provably 0 on all 186 heads;
this is our own anchoring choice.

**F5 — `_totalHotx` is unapplied: Harold's and Tandi's fidgets lack their horizontal sway.**
*Effort S.* The accumulated per-frame X offset is not applied in `DrawTalkingHead`
(`ViewerGame.cs:6106`). The same 186-head probe found 5 heads that use it, all X-only: `HRLD2BF3`,
`HRLD2GF2`, `HRLD2NF3`, `TNDI2GF2`, `TNDI2NF3`, offsets within ±5 px. Small, self-contained, and
pairs naturally with F4.

**F6 — Monitor messages render no `'\x95'` bullet knob and wrap to the wrong width.** *Effort S.*
Vanilla prefixes the bullet knob `'\x95'` to the first line of every monitor message and wraps to
`167 - _max_disp - knobWidth` (`display_monitor.cc:262`); our HUD wraps to a flat `mw = 162` with no
knob (`ViewerGame.Hud.cs:194-202`). Cosmetic but visible on every message line. (The *wrap-boundary*
half of PR #675 hunk 17 is already correct here — `AafFontRenderer.WrapText` breaks on
`> maxWidth`, the post-fix semantics — so only the knob and the budget are outstanding.)

**F7 — The automap has no wall-colour-priority guard: any later object mark can hide a wall.**
*Effort S.* `automap.cc:572-580` at `e97087b` refuses to repaint a bright-green **wall** pixel with a
dark-green object colour — `if (*pixel != COLOR_GREEN || objectColor != COLOR_DARK_GREEN)`.
`DrawAutomap` (`ViewerGame.Panels.cs:1015`, `Plot(obj.HexTile, col, 2)`) overpaints every plotted
object unconditionally, in `_flatObjects`-then-`_solidObjects` order, so the priority model is absent
entirely rather than merely incomplete. (PR #675 hunk 5 extends that guard to the mark's second
pixel — irrelevant until the guard exists.)

**F8 — Outlined objects are uncapped; vanilla caps at 100 per frame.** *Effort S · low priority.*
`_obj_render_pre_roof` / `_obj_render_post_roof` fill a fixed `Object* _outlinedObjects[100]` with an
`_outlineCount < 100` cap — a real static from the shipped binary (0x639C00). Our renderer draws
outlines inline per sprite with no cap (`ViewerGame.Rendering.cs:336,346`), i.e. Hexwaste currently
matches the **fork** (which removed the cap) rather than vanilla. Only observable with >100 outlined
objects on screen; recorded for completeness, since removing the cap is precisely the deviation we
rejected the fork's commit for.

### Script VM

**F9 — The `Anim` external silently drops script anim values 1000 and 1010.** *Effort S.*
`e97087b`'s `opAnim` pops a plain `int` and handles 1000 (set rotation) and 1010 (set frame)
explicitly; `ScriptHost.cs:1610` forwards `anim` straight to `AnimRequested` with no 1000/1010
branch, so scripts using them get no effect at all. Unrelated to the fork commit that surfaced it
(`d9c24e1cc`, which is the fork repairing its own enum refactor). Cheap to close, and a script
calling `anim(obj, 1000, rot)` to face a critter is a plausible vanilla content pattern.

### Crit-failure self-damage and `damage_p_proc` reach

F11-F14 below were carried as prose (in a ledger row's Notes, or in the harvest's working notes)
rather than as entries, and were promoted here on 2026-08-14 so they survive the merge. F11-F13
shipped 2026-08-15 (see each entry); F15 was added the same day, once F11's fix exposed the
divergence it deliberately left open. All five sit in the crit-failure / accidental-hit
neighbourhood of `CombatEngine`; none is a fork port.

**F11 — `DAM_HIT_SELF` and `DAM_RANDOM_HIT` crit-failure damage was HALF vanilla. SHIPPED —
commit `c0ab7f8` (2026-08-15), byte-identical, no fixture re-recorded.** *Effort S.*
`CritFailDamage` (`CombatEngine.cs:1233`) passed `critMultiplier: 1` into
`CombatMath.RollWeaponDamage` / `RollDamage`, whose body is `raw * critMultiplier / 2` — so the
rolled figure was halved before DT/DR. The reference calls `attackComputeDamage(attack, n, 2)`
(`combat.cc:4230` for `DAM_HIT_SELF`, `:4260` for `DAM_RANDOM_HIT`, both at `e97087b`); that
`bonusDamageMultiplier` (2) feeds `damageMultiplier` at `:4586` and is applied to `damage` at
`:4594`, then undone by the flat `damage /= 2;` at `:4601` — net **×1**: vanilla applies the *full*
rolled figure. A 5-12 weapon self-hit that should cost 12 was costing 6. Changed `critMultiplier`
to 2. Pre-existing since `f77e37f`, unrelated to the 2026-08 fork harvest, which only pinned it in
a test.
**Correction to this entry's own prediction:** the version of this entry written before the fix
predicted it would move `tests/golden-combat/arcaves-crit-fail-day6.txt`. It did not. That
fixture's fumble is `flags=0x8000` (`DAM_LOSE_TURN` only), and no file under `tests/golden-combat/`
or `tests/golden-encounter/` contains a `crit-fail-self` or `crit-fail-random-hit` line — no
committed fixture reaches `CritFailDamage` at all. Zero fixtures were re-recorded on this branch;
the combat/encounter golden nets stayed untouched. Because there was no golden coverage of this
path, the fix's proof rests entirely on two mutation-verified unit tests
(`CombatEngineTests.HitSelfFumbleStillRollsWeaponDamage` and its `DAM_RANDOM_HIT` sibling), not a
recorded transcript. Lesson for future entries: "this will move fixture X" is itself a factual claim
about which code paths a fixture exercises, and it needs the same verification as everything else in
this file — it went unchecked here and was wrong.

**F12 — A missed shot's collateral victim ran a `damage_p_proc` the reference suppresses. SHIPPED —
commit `75c6dfb` (2026-08-15), byte-identical, no fixture moved.** *Effort S.*
`_check_ranged_miss` reassigns `attack->defender` to the bystander (`combat.cc:3620`) while
`attack->oops` keeps the originally-intended target, set once at attack-init time and never
reassigned (`:3485`). So the defender damage call at `:4723` passes `defender != oops` = true, and
`_damage_object`'s gate `if (!a4)` at `:4847` skips `SCRIPT_PROC_DAMAGE`. `ApplyAccidentalHit`
(`CombatEngine.cs:729`) called `RunDamageProc(acc.Victim, attacker, …)` unconditionally for any
scripted non-dude bystander; the call was removed to match. HP loss, on-hit path and kill path
unchanged.

**F13 — `DAM_EXPLODE` crit-failure self-damage ran no `damage_p_proc` — PR #493 was only PARTIALLY
applied. SHIPPED — commit `4f77897` (2026-08-15), byte-identical, no fixture moved.** *Effort S.*
The `#493` port wired the party-gated self-damage proc into `ApplyCritFailDamage`, which only the
`DAM_HIT_SELF` branch reaches. The sibling `DAM_EXPLODE` branch (reference `combat.cc:4231-4232`)
routes to `Explode(…)` instead, which never reached `ApplyCritFailDamage`, so a fumbling critter
blown up by its own weapon ran no damage proc — where the reference's proc gate (`:4847`) precedes
its `DAM_DEAD` destroy block (`:4855`), i.e. both `DAM_HIT_SELF` and `DAM_EXPLODE` self-damage feed
the same proc-then-maybe-destroy path in the reference. `Explode` gained an optional
`selfDamageProcFor` parameter (default `null`, so the other four call sites stay inert) that fires
the proc inside the victim loop, before the kill check.

**F14 — Our `CRIP_RANDOM` limb draw precedes the self-damage draws; the reference orders self-damage
first.** *Effort S · documentation of a divergence, not a live bug.* `attackComputeCriticalFailure`
resolves `DAM_HIT_SELF` / `DAM_EXPLODE` (`combat.cc:4228-4232` at `e97087b`) **before** the
`_do_random_cripple` call at `:4249`; `HandleCriticalFailure` draws the random limb first
(`CombatEngine.cs:1175`) and the self-damage after. Confirmed **inert on the shipped `_cf_table`**:
`DAM_CRIP_RANDOM` appears exactly once, row 0 column 4 (`combat.cc:1876`), paired with nothing — so
no fumble can ever take both draws and the RNG stream cannot diverge. Recorded so a future edit to
either branch does not re-derive this, and so anyone who *adds* a table entry knows the order is
wrong before they trip over a moved fixture.

**F15 — BLOCKED, not merely open: reference ranged self-hits roll `attack->ammoQuantity` times per
fumble; Hexwaste rolls once — but `ammoQuantity` is rounds *fired*, not magazine contents, so this
is unreachable until F26 (below) ships.** *Effort S–M · re-record tier · **blocked on F26**.*
`attackComputeDamage` initializes `int v26 = 1;` (`combat.cc:3845`) and only raises it via
`_compute_spray(attack, accuracy, &ammoQuantity, &v26, anim)` (`:3850`), called exclusively when
`anim == ANIM_FIRE_BURST || anim == ANIM_FIRE_CONTINUOUS`; the result is assigned at
`attack->ammoQuantity = v26;` (`:3888`). For every **single-shot** ranged attack `v26` stays `1`, so
`attackComputeDamage`'s `for (int index = 0; index < ammoQuantity; index++)` loop
(`:4589`) already rolls exactly once — which is exactly what Hexwaste does today. The divergence
this entry originally described only exists for a **burst** fumble (`ammoQuantity` = rounds fired,
matching the F14/F15 sibling note at `:4229-4230`/`:4259-4260`), and Hexwaste's burst path cannot
reach a crit-failure roll at all (F26) — `TriggerCritFailure` is never called from `RollBurst` or
`TryBurst`. F15 therefore has no live code path to fix yet: closing F26 first is a prerequisite, not
just a nearby entry. Once F26 lands, F15 becomes the burst-fumble self-hit roll-count fix described
below and remains **re-record tier** for the same reason as before — rolling N times instead of once
changes the RNG draw *count*, not just the resulting figure.

**F16 — SHIPPED 2026-08-20 (`2f2c483`), 941 tests, combat-golden 17/17, 0 fixtures moved.**
*Was Effort S–M · re-record tier (moved nothing).* `Explode`'s OTHER blast victims now run their
`damage_p_proc`, closing the asymmetry F13 left: the fumbler got its self-damage proc, the bystanders
caught in his exploding gun did not. `Explode` gained an `attackSourced` opt-in (default `false`,
mirroring `selfDamageProcFor`'s shape) wired `true` only at the two callers that resolve a genuine
reference `Attack` — the grenade-throw `Explode` call in `ResolveThrow`
(`CombatEngine.cs:883`, citing `combat.cc:3973-3976`) and the crit-fail-explode call in
`ApplyCritFailDamage`'s `DamExplode` branch (`CombatEngine.cs:1204`, citing `combat.cc:3976`,
`isFromAttacker=1`). For every victim other than the one named by `selfDamageProcFor`, when
`attackSourced` is set, the proc now runs gated by `Sid != -1` and the same party-both-members check
`_damage_object` applies (`combat.cc:4849`, `!objectIsPartyMember(a1) || !objectIsPartyMember(a5)`).
**Read this polarity note against `e97087b`, not against Hexwaste's own behaviour, or it looks like a
bug:** at the bare pinned reference the extras loop's `_damage_object` call passes
`attack->defender == attack->oops` as its proc-suppression flag (`combat.cc:4751`), and for a
`DAM_EXPLODE` crit-failure that expression is **true** (`Explode` never diverges a defender from its
intended target), so `_damage_object`'s `if (!a4)` gate (`:4847`) would suppress the proc — at bare
`alexbatalov e97087b` this proc would **not** run. Hexwaste, however, already carries community fix
`#493` at these three `_apply_damage` call sites (F13 ported the attacker-side half): #493 replaces
all three site-specific `oops`/`defender` expressions with one collapsed
`hitUnintendedTarget = attack->defender != attack->intendedTarget`, which is **false** for this event
— so under **the polarity Hexwaste has already adopted**, the proc should run. F16 is a divergence
from that adopted polarity, not from `e97087b`'s literal text; a future reader who checks only
`e97087b` and sees the proc firing here needs this paragraph, not a revert.
**Scope question, resolved against the drafted recommendation:** the recommendation was to gate the
new proc on `killer is not null` ("this blast had an attacker"). That heuristic is falsified by
`ProcessArmedCharges` (`ViewerGame.cs:4348`), which passes `killer: _dude?.Dude` (non-null) for a
*planted* C4 charge — not an attack-sourced blast in the reference's sense. Traced in the reference:
`actionExplode` (`actions.cc:1582`) builds a synthetic `Attack` via
`attackInit(attack, explosion, critter, HIT_MODE_PUNCH, HIT_LOCATION_TORSO)` (`actions.cc:1631`)
whose attacker is the transient misc-10 explosion-marker object, never the placer/`sourceObj`
(`queue.cc:486` passes `gDude` only as `sourceObj`, used later purely for XP/reputation at
`actions.cc:1727`). So `killer != null` cannot distinguish "genuine attack" from "planted charge with
a known placer" — the explicit `attackSourced` opt-in was added instead, verified against this
falsifying caller directly rather than trusted from the brief.
**Flagged, not fixed here (tracked as F27 and F28 below):** `ApplyBurstExtras`
(`CombatEngine.cs:977`) models the same reference predicate with a simpler `!= dude && Sid != -1`
gate, no party check — a second site now modelling one reference behaviour two different ways; and
the C4/scripted-`explosion` paths still don't build the reference's synthetic-attacker shape at all.

**F17 — SHIPPED 2026-08-20 (`f1c9aa7`), 938 tests, combat-golden 17/17, 0 fixtures moved (no golden
exercises a ≥10-damage self-blast).** *Was Effort S · re-record tier (moved nothing).* A fumbling
attacker is no longer knocked back by its own `DAM_EXPLODE` blast; vanilla computes no knockback for
self-damage at all. `attackComputeCriticalFailure` clears `DAM_HIT` as its first statement
(`combat.cc:4180`), so the `attackComputeDamage` call it then makes for `DAM_HIT_SELF` /
`DAM_EXPLODE` takes the attacker-damage branch, which sets `knockbackDistancePtr = nullptr`
unconditionally (`:4513-4517`) — the reference computes **zero** knockback for the fumbler's own
self-damage. Hexwaste's generic `Explode` was shoving the attacker standing on the blast tile
(`HexGrid.RotationTo(centerTile, centerTile)` degenerate self-to-self case). Fixed by gating the
per-victim `Shove` call on `victim != selfDamageProcFor`; other victims of the same blast are still
shoved normally (boundary-pinned by a dedicated test). Surfaced during the F13 final-fix round
(2026-08-15): the shipped `Explode` comment already recorded the reference behaviour in prose, but
the divergence itself was untracked, which is the same failure mode F16 exists to prevent.

**F26 — Hexwaste's burst attacks never trigger critical failure at all.** *Effort M ·
**re-record tier** · blocks F15.* `TriggerCritFailure` (`CombatEngine.cs:1153`) has exactly three
callers, and all three are single-attack paths: `TryAttack` (`CombatEngine.cs:369`), the ally
single-attack path (`:3671`), and the enemy single-attack path (`:3802`). `TryBurst`
(`CombatEngine.cs:420-515`) and its `RollBurst` engine (`:524` on) have no crit-failure branch
anywhere in them — nor do the parallel `TryAllyBurst` (`:3734`) / `TryEnemyBurst` (`:3764`) paths. In
the reference, every attack shape reaches `attackComputeCriticalFailure` through the same shared
`case ROLL_CRITICAL_FAILURE:` arm of the post-roll switch (`combat.cc:3933-3934`), which a burst's
inception roll (`_compute_spray`, `:3850`) can land on exactly like a single shot's roll can — bursts
are not exempt in vanilla. **Consequence, stated plainly: no burst attack can ever drop its weapon,
hit itself, lose ammo, or cripple the shooter — in a game where burst-capable weapons (SMGs,
shotguns, miniguns) are common and heavily used.** Mark **re-record tier**: wiring a crit-failure
branch into the burst inception roll changes the RNG draw sequence for every fixture where a burst
attack currently misses cleanly (the crit-failure check consumes/branches on the same roll that
currently just resolves to a plain miss), so this is a deliberate, diff-reviewed re-record, not a
byte-identical port. F15 is blocked behind this: F15's actual content (roll `ammoQuantity` times on a
burst self-hit) has nothing to fix until a burst can reach `DAM_HIT_SELF` in the first place.

**F27 — `ApplyBurstExtras` lacks the party gate the new F16 `Explode` code carries.** *Effort S ·
tracked, not fixed.* `_damage_object` skips `SCRIPT_PROC_DAMAGE` when **both** the victim and the
damage source are party members (`combat.cc:4849`,
`if (!objectIsPartyMember(a1) || !objectIsPartyMember(a5))`), which F16's new `Explode` block now
implements verbatim. Hexwaste's other extras site, `ApplyBurstExtras` (`CombatEngine.cs:977`), gates
the same proc call on the simpler `ex.Victim != dude && ex.Victim.Sid != -1` — no party check at all.
Two sites now model one reference behaviour with two different shapes; if `ApplyBurstExtras` is ever
exercised with a party-member attacker and a party-member bystander it will run the proc where the
reference (and F16's own code) would not. This is exactly the shape that produced a Critical finding
earlier in this crit-failure work (F13 going unnoticed until F16's review), so it is recorded as its
own entry rather than left as a stray comment. Flagged by the F16 implementer during that task, not
fixed there — out of that task's stated scope.

**F28 — The C4/planted-charge and scripted-`explosion` detonation paths don't match `actionExplode`'s
attacker shape.** *Effort M–L · documented, not implemented.* In the reference, both the
planted-charge detonation (`queue.cc:486`) and the scripted `explosion` opcode (`scripts.cc:1004`)
route through `actionExplode(tile, elevation, minDamage, maxDamage, sourceObj, animate)`
(`actions.cc:1582`), which builds a synthetic `Attack` via `attackInit(attack, explosion, critter,
HIT_MODE_PUNCH, HIT_LOCATION_TORSO)` (`:1631`) — the attacker is the transient misc-10 explosion
marker object, never `sourceObj`/the placer. `sourceObj` (`gDude` for the C4 path) is used only later,
in `_report_explosion` (`:1727`), for XP/reputation bookkeeping. Because the marker is a non-critter
object, `_damage_object`'s party gate (`!objectIsPartyMember(a5)`) is trivially true for it, so a
faithful port would run the victim `damage_p_proc` for blasts on these paths too, sourced from the
marker rather than the placer. Hexwaste has no marker-object concept — `ProcessArmedCharges`
(`ViewerGame.cs:4348`) passes the placer (`_dude?.Dude`) directly as `killer`, and F16 deliberately
left `attackSourced: false` at this call site and at the scripted-`explosion` site
(`ViewerGame.cs:1282`, `killer: null`) rather than approximate the reference's synthetic-attacker
shape. Closing this properly needs a marker-object concept Hexwaste doesn't currently model (the
existing `SpawnExplosionMarker` is visual-only); documented here by the F16 implementer as a real,
cited gap rather than implemented.

**F29 — The blast/burst `damage_p_proc` gates carry a `!= dude` term the reference does not have.**
*Effort S · re-record tier · found by the F16/F17 whole-branch review (2026-08-20).* `_damage_object`
gates the proc as a **pair** test only — `if (!objectIsPartyMember(a1) || !objectIsPartyMember(a5))`
(`combat.cc:4849`) — so vanilla **does** run the dude's `damage_p_proc` when an enemy-sourced blast
or burst catches him. Every Hexwaste site that models this carries an additional `victim != dude`
exclusion with no reference counterpart: the new F16 blast gate (`CombatEngine.cs:~1751`),
`ApplyBurstExtras` (`:977`), and F13's self-damage tail. The F16 comment originally claimed its gate
"mirrors :4849 exactly"; that claim was corrected in place rather than the behaviour, because
removing the term is a real behaviour change that would reach nearly every fixture (the dude is in
almost all of them) and deserves its own measured item. Related to F27, which tracks the *other*
inconsistency between those same two sites — the party gate that `ApplyBurstExtras` lacks entirely.
Closing F27 and F29 together would leave one coherent model of `_damage_object`'s proc gate instead
of three near-misses.

### Pointer

**F10 — Surveyed-but-unbuilt QoL catalogue.** *Not scheduled work.*
`docs/research-notes/fork-survey-2026-08.md` §6 catalogues the quality-of-life features carried by
`fallout2-ce/fallout2-ce` and `cambragol/fission-ce` (party looting/bartering, expanded
inventory/barter screens, ctrl-click item moves, music continuity, auto-open doors, highlighting,
44.1 kHz audio; and fission-ce's `[enhancements]` toggle set). It exists so nothing needs
re-discovering — **it is not a task list.** The catalogue is sourced from fork READMEs and was **not
individually verified against our code**; several items may already exist in Hexwaste. Each needs its
own confirm pass before it becomes work, and any adoption must be **vanilla by default, opt-in
toggle**, because the golden suites encode vanilla behaviour.

## Out of scope by design (NOT backlog — listed for completeness)

- **sfall / mod-extender opcode range (0x8156+)** — 0/1443 vanilla scripts use it; only matters if
  scope shifts to Restoration Project / RPU / Et Tu / total conversions. `IntVm.cs:2096` hard-throws.
- Full Highwayman **drive-travel** (trunk + worldmap car-monitor ARE built, P116/P122; the map-
  spawned drivable + fuel-gated travel is content-gated).
- BLOODY_MESS "big hole" death, Tag!/4th-tag-skill UI, ~80 perks' timed/content effects
  (data-present, faithfully inert), Scrounger (correctly inert — no impl exists in fo2ce either).

---

## Recommendation

Nearly every proposed item was investigated and either closed-as-faithful (A1 karma), found
already-built (A4 poison/rad, ~95%), fixed as a one-liner (A3 whoHitMe, A5a imported-proc), or
disproven as a lever (B1 deep-menu). The engine + single-NPC quest-driver are essentially complete.
What remains, honestly, is:

- **B3 — driver-invoked combat (`--kill`). DONE for `destroy_p_proc` (2026-07-17).** The driver now
  has a KILL pass: if dialogue doesn't finish a quest and the completer's write is in
  `destroy_p_proc`, it fires that critter's death path (`CombatEngine.Kill → destroy_p_proc →
  set_global_var`) and keeps the ones that advance the gvar, emitting `--kill <tile>`. Also fixed
  `--kill` to find the completer on ANY elevation (the Rat God is on klaratcv elev 2), so kill
  recipes replay without threading elevation. Auto-completes 390 (Rat God) and 100 (kill Metzger →
  Vic device); golden `quest-kill-metzger` locks it. **Caveat:** *unconditional*-kill quests are few
  — many `destroy_p_proc` writes are gated on prior activation (474 Kill Darion, the 454 gang war),
  needing activate-then-kill the driver only does when both are on one map. `damage_p_proc` quests
  (the crime-family war) are NOT covered — `--kill` fires `destroy_p_proc`, not `damage_p_proc`;
  those need a hurt-to-threshold verb, separate. Run the harvest to enumerate the clean kill-wins.
- **A multi-NPC investigation-chain driver** — GROUNDED (plan §13): the archetype 286 Wright-mystery
  is P137-style bit-prereqs but needs FOUR subsystems together — full-subtree multi-bit gathering,
  cross-map prereq resolution, deep-activation navigation, and quest-specific accusation — with NO
  incremental payoff (all required before any investigation quest completes). A large multi-session
  build with convergence risk, for a handful of quests (several also combat-completable). Deferred.
- **A2 / A5b / A5c** — real but deliberately-deferred combat/rendering fidelity polish (golden-heavy).

If the goal is more quest coverage, **B3** is the tractable next step. If it's engine fidelity,
**A2** (combat feel). Otherwise the project is at a natural banking point — the substantive gaps
are closed.

