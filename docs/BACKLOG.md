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

## Tier E — test-harness maintenance

**Golden-suite parallel harness — SHIPPED 2026-08-29 (`feat/golden-parallel-harness`), whole-run
byte-identical, 0 fixtures touched.** *Was Effort M.* All six golden suites (`combat`, `quest`,
`endgame`, `opening`, `encounter`, `census`) ran serially through `dotnet run`, one scenario at a
time. Measured baseline (`.superpowers/sdd/golden-baseline.log`, captured before any change):

| suite | before | fixtures | after (this run) |
|---|---|---|---|
| encounter | 960.03 s | 188 | 59.96 s |
| quest | 221.29 s | 39 | 18.23 s |
| combat | 112.73 s | 18 | 18.80 s |
| opening | 58.96 s | 13 | 9.84 s |
| endgame | 36.73 s | 5 | 5.34 s |
| census | 25.75 s | 16 | 4.03 s |
| **total** | **1415.5 s = 23.6 min** | **279** | **116.20 s = 1.94 min** |

All six runners were migrated onto a shared driver, `scripts/golden-lib.sh`, one suite per
commit, each verified byte-identical against the baseline before the next suite moved. This
entry is the whole-set closeout: all six suites report `ALL PASS`, all 279 fixtures report `ok`,
and `diff -u` of the full run's filtered output against the baseline is empty
(`WHOLE-SUITE BYTE-IDENTICAL`) — every scenario, every recorded line, byte-for-byte the same as
before the harness changed.

Three costs came out, none of them assertion strength:
- **`dotnet run` overhead**, measured directly at 1.16 s of pure startup cost per invocation
  (`git show b488061`) — replaced by invoking the already-built binary.
- **Serial execution** on a machine that reports 16 cores (`nproc`) — `golden-lib.sh` now owns a
  job pool (`GOLDEN_JOBS="${GOLDEN_JOBS:-$(nproc)}"`, `scripts/golden-lib.sh:31`) and runs
  scenarios concurrently instead of one at a time.
- **The determinism double run**, kept for every suite that had it (all but `census`, which never
  ran one): the unit of work is a *(fixture, pass)* pair, so both passes of a fixture can run
  concurrently — the double-run now costs a core, not wall time (`scripts/golden-lib.sh:8-9`).

**No assertion was weakened and no fixture under `tests/golden-*/` was touched.** The only change
to any scenario's arguments is the four sanctioned `@SCRATCH@` substitutions
(`scripts/encounter-golden.sh:153,188,189,212`), which give each concurrent pass of a
disk-writing scenario (`automap-persist`, `save-slot-roundtrip`, `save-slots-probe`,
`vic-save-roundtrip`) its own private directory so two passes of the same fixture running at once
don't race each other's save files on disk — a harness-only accommodation for concurrency, not a
change to what is being asserted.

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
`CritterSetFleeState` (`ScriptHost.cs:1834`), the script-attack ENGAGING mark (`:2146`), and
`TerminateCombat`'s DISENGAGING mark (`:2315`). The gap was narrower: the **engine's own AI never
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
- `StartNpcWalk` (`ViewerGame.cs:3389`, guard at `:3391`) refuses a new walk whenever
  `_npcWalkers.ContainsKey(npc)` — keyed on dictionary **presence**, not on `walker.Moving`.
- A finished walker is pruned only inside `UpdateAmbientLife` (`ViewerGame.cs:3262-3272`).
- The `--fight` autoplay harness that `combat-golden.sh` drives never calls `UpdateAmbientLife` — it
  pumps `walker.Update(10)` directly on every entry in `_npcWalkers.Values`
  (`ViewerGame.Harness.cs:2037-2038`). So once Healthy Slave's round-2 flee finishes, the now-idle
  walker is never removed, and every later `TryFlee` call for that critter hits the stale guard:
  `StartWalk` fails silently while the `flee:` transcript line and the AP-zeroing (`CombatEngine.cs`,
  same shape as the failure mode F18 fixed, but a different cause) have already fired.
- **Not purely a harness artefact — but only via `--no-ambient`, not an open worldmap.** The prune
  sits *after* `if (DisableAmbientLife || _worldmapOpen) return;` (`ViewerGame.cs:3334-3335`) inside
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
  `PruneFinishedWalkers(double)` (`ViewerGame.cs:3310`), called from `Update` independently of the
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

**F24 — RESOLVED 2026-08-22 as NOT A DEFECT. `BeginScriptAggro` is faithful; no code change.** *Was
"Effort unknown · needs its own investigation before scoping".* The entry recorded that
`BeginScriptAggro` joins a critter to combat without the `WithinPerception` gate `WantToJoin` applies,
and explicitly said to check `e97087b` before assuming that was a bug. Checked — it is not.

The reference has **two distinct entry paths**, and only one of them is perception-gated:

- **Script-designated combatants are seeded directly.** A script's attack external calls
  `scriptsRequestCombat` (`scripts.cc:1100`), which queues a `CombatStartData`; when serviced
  (`scripts.cc:900-912`) it calls `_combat(&gScriptsCSD)`. Inside `_combat`, the CSD's attacker and
  defender are handed straight to `_combat_sequence_init(attacker, defender)` (`combat.cc:3405-3415`),
  which places them in `_combat_list` and stamps their `whoHitMe`. **There is no perception check on
  that path at all.**
- **Bystanders are promoted through the gate.** `_combatai_want_to_join` — which carries the
  perception clause — is called only from `_combat_add_noncoms` (`combat.cc:2905`) and the
  end-of-combat check (`:3104`), i.e. for critters the script never named.

So a script saying "this critter attacks the dude" is not vetoed by whether the critter can see him,
which is exactly the semantics a scripted ambush needs. Hexwaste's `BeginScriptAggro` matches: its own
doc comment already cites `scriptsRequestCombat` as the counterpart, and its lack of a
`WithinPerception` gate mirrors `_combat_sequence_init`. `WantToJoin`'s gate correspondingly mirrors
`_combatai_want_to_join`. **The asymmetry the entry noticed is the reference's own asymmetry.**

On the observation that raised it: the blind-Perception-5 interaction seen during `feat/ai-danger-source`
was in the *prune* path (`WantsToStopFighting`), not `BeginScriptAggro`, and the blind malus of −5
zeroing effective perception exactly is a knife-edge fixture artefact. Adjusting that test to
Perception 8 was appropriate and is unaffected by this resolution.

**F25 — BLOCKED behind F33 (2026-08-21).** *Was Effort S.* The worldmap start-point reachability
probe (`ViewerGame.cs`, its `Reachable(from, to)` local) passes `IsBlocked`
(`_blockedTiles.Contains`, movement semantics) where the reference passes `_obj_shoot_blocking_at`
(`worldmap.cc:4088`). Both use `a5 = 0`, so this is orthogonal to F18/F20.

**Note the divergence runs in BOTH directions**, which this entry originally understated as "a critter
on the route wrongly rejects a start point". Comparing the two reference predicates: `_obj_blocking_at`
(`object.cc:2401`) blocks on `!HIDDEN && !NO_BLOCK` and **counts dead critters**, while
`_obj_shoot_blocking_at` (`:2451`) explicitly **excludes dead critters** (an SFALL corpse fix) and uses
a looser flag test. So switching would both *stop* corpses rejecting start points and *start* letting
some flagged objects reject them.

Blocked because F33 establishes that Hexwaste's `ShootBlockerAt` does not currently agree with
`_obj_shoot_blocking_at`, and that the naive correction breaks ordinary ranged combat. Adopting an
unresolved predicate here would propagate that uncertainty into random encounter placement. Resolve
F33 first.

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

**F4 — SHIPPED 2026-08-29 (`6c7bc25`).** *Was Effort S.* Talking heads are now bottom-anchored inside
the 388x200 display buffer, matching the engine's `destWidth * (200 - height)`:
`y = frameY + 14 + (200 - head.Height)` (`ViewerGame.cs:6342`, inside `DrawTalkingHead`,
`ViewerGame.cs:6287`). One expression change. The reference's `a3` term also carries
`artGetRotationOffsets(...)`'s X and Y out-params, feeding both the horizontal position and a
`destOffset + width * v8 > 0` guard on the vertical one; **neither was ported**, deliberately — the
186-head probe (`art\heads\*.FRM` in `master.dat`, established rejecting PR #675 hunk 20) found both
offsets identically zero on every shipped head, so porting them would be dead code with no observable
effect. 14 of 186 heads have frames shorter than 200 px (e.g. `BOSSSNF1` at 194/193); those no longer
shift between frames now that the anchor is bottom-relative.

**F5 — SHIPPED 2026-08-29 (`6c7bc25`, fix `87c9c7f`) — implemented correctly, but DORMANT on shipped
data; this is the most consequential finding of the batch.** *Was Effort S.* The accumulated
per-frame X offset (`_totalHotx`, `game_dialog.cc:4557,4585`) is now summed and applied —
`HeadAccumulatedHotX` (`ViewerGame.cs:6392`) computes it as a prefix sum over frames 0..N rather than
accumulating in a field, because `DrawTalkingHead` runs once per render frame while the reference
runs once per animation frame, and a field would over-accumulate at high frame rates; the fix `87c9c7f`
threads the *resolved* frame (post frame-count clamp) through so the sum matches what
`HeadTexture` (`ViewerGame.cs:6354`) actually drew. **The term is provably 0 on shipped data today for
an unrelated reason**: both `HeadTexture` and `HeadAccumulatedHotX` build the head FID with
`weaponCode: 1` (the pre-F46 state; `HeadTexture` now builds it at `ViewerGame.cs:6364` from the
rolled fidget) — that nibble is the *fidget number*, and the
reference chooses it in `_gdSetupFidget` (`reference/fallout2-ce/src/game_dialog.cc`), a count-gated
weighted roll that folds in `_dialogue_seconds_since_last_input`; Hexwaste never ports fidget
selection, so the nibble is hardcoded to fidget 1. The 186-head probe's 5 heads carrying a nonzero
frame X offset — `HRLD2BF3`, `HRLD2GF2`, `HRLD2NF3`, `TNDI2GF2`, `TNDI2NF3` — are all fidgets 2 and 3,
never fidget 1, so `HeadAccumulatedHotX` sums to 0 for every FID this code path can currently build,
regardless of the arithmetic being right. **Do not read this as sway being observable in-app** — it
was not, until fidget selection was ported. The arithmetic itself is correct and future-proof.

**UN-DORMANTED 2026-08-29 by F46 (below), and now MEASURED rather than argued.** With fidget
selection ported, `HeadTexture` builds the FID with the rolled fidget instead of a constant 1, so the
2/3 variants that carry the offsets are reachable. The new `--fidget-probe <headId> <rolls>` harness
reports, per fidget, the running sum of `OffsetX` over the anim's frames:

| head | fidget 1 | fidget 2 | fidget 3 |
|---|---|---|---|
| 5 `hrld2` (Harold) | maxSway 0 (2 frames) | maxSway 0 | **maxSway 12** (8 frames) |
| 6 `tndi2` (Tandi) | maxSway 0 (2 frames) | maxSway 0 | **maxSway 1** |
| 3 `elder` | maxSway 0 | maxSway 0 | maxSway 0 |

That is the whole dormancy claim closed with numbers: **every head's fidget 1 sways 0**, which is why
hardcoding the nibble to 1 could never show the term, and Harold's fidget 3 now sways up to 12 px.
Confirmed on screen too — the same Harold dialog rendered under two RNG seeds produces visibly
different fidget art where before F46 every seed produced identical output.

**F6 — SHIPPED 2026-08-29 (`4777d8e`, fix `9315661`).** *Was Effort S.* Monitor messages now prefix
the `'\x95'` bullet knob to each message's first line and wrap against the engine's own budget
(`MonitorLayout`, `src/Hexwaste.Formats/Text/MonitorLayout.cs`; call site `ViewerGame.Hud.cs:198-217`).
The open font question is resolved: `DISPLAY_MONITOR_FONT` is 101, which routes through
`interfaceFontLoad`'s `"font%d.aaf"` naming over `id - 100` — i.e. `font1.aaf`, the interface font
Hexwaste already loads at startup (`ViewerGame.cs:1489-1490`); no new asset is needed. `_max_disp`'s
unit mismatch — the wrap budget subtracts a LINE COUNT from a PIXEL width
(`display_monitor.cc:262`) — is faithful and intentional, reproduced verbatim
(`MonitorLayout.WrapBudget`, `src/Hexwaste.Formats/Text/MonitorLayout.cs:23-30`) rather than "fixed":
the shipped engine really does this, so the PoC does too. The reference's own rect
(`DISPLAY_MONITOR_X/Y/WIDTH/HEIGHT`, `display_monitor.cc:31-34`) was adopted in place of the old
hand-tuned one. **No golden fixture and no automated test covers HUD pixels, and no screenshot was
taken as part of this work** — the visual result (knob glyph, wrap boundary, rect placement) is
unverified beyond the 5 hermetic arithmetic tests on the budget itself. The continuation-line wrap
budget — the reference re-widens the available width after the first line, this port does not — is a
real residual and is filed as its own entry below rather than left as a remark here. The reference's
`DISPLAY_MONITOR_LINE_LENGTH` (80) per-line character cap (`display_monitor.cc:267-274`) was
**deliberately not ported**: it is a fixed `char[80]` buffer bound, not a display rule, and at this
font size the 167px pixel budget is always the binding constraint first, so the cap can never fire on
text the width test already accepted. That reasoning previously lived only in this branch's plan
document (now frozen with an as-of note); recorded here so it survives.

**F7 — SHIPPED 2026-08-29 (`4c63caf`).** *Was Effort S.* The automap now has a wall-colour-priority
guard, implemented in `DrawAutomap` (`ViewerGame.Panels.cs:1040`) via a per-tile
`Dictionary<int, AutomapMark>` and `AutomapPaint.Overpaints` (`src/Hexwaste.Formats/Map/AutomapPaint.cs`):
before plotting an object's mark, it checks whether the tile already carries a mark that the new one
is not allowed to overpaint, ported from `automap.cc:572-580`'s
`if (*pixel != COLOR_GREEN || objectColor != COLOR_DARK_GREEN)`. **The original entry had two errors,
corrected here rather than merely marked shipped:**
- The guard is **narrower than "any later object mark can hide a wall"** — it refuses only a
  **scenery** mark overpainting a **wall** mark (`AutomapMark.Wall`/`AutomapMark.Scenery`/`AutomapMark.Other`
  and `Overpaints`'s truth table). The dude marker and the motion-scanner critter marks are drawn in
  their own separate passes, after and independent of this dictionary, and still overpaint anything —
  matching the reference, where those are separate draw calls outside the wall/object priority rule.
- The fix belongs to **`DrawAutomap`** (`ViewerGame.Panels.cs:1040`), the full-window automap that
  implements the reference's `AUTOMAP_IN_GAME` semantics — not the Pip-Boy mini-map's `Plot` call the
  entry originally cited (`ViewerGame.Panels.cs:1015` was the wrong-path citation and no longer
  applies to this fix; the mini-map's own `Plot` at that line has no such guard and was never in
  scope).

The dictionary-of-painted-tiles substitution for the reference's direct pixel-buffer read was
**proven exact, not approximate**: the tile→pixel projection (`ax = 449 - 2*(tile % 200)`,
`ay = 2*(tile / 200) + 8`) is injective and the 2px marks tile the grid with no gap and no overlap, so
tracking "what mark is on this tile" is equivalent to reading the actual pixel the reference inspects.

**F8 — Outlined objects are uncapped; vanilla caps at 100 per frame.** *Effort S · low priority.*
`_obj_render_pre_roof` / `_obj_render_post_roof` fill a fixed `Object* _outlinedObjects[100]` with an
`_outlineCount < 100` cap — a real static from the shipped binary (0x639C00). Our renderer draws
outlines inline per sprite with no cap (`ViewerGame.Rendering.cs:336,346`), i.e. Hexwaste currently
matches the **fork** (which removed the cap) rather than vanilla. Only observable with >100 outlined
objects on screen; recorded for completeness, since removing the cap is precisely the deviation we
rejected the fork's commit for.

### Script VM

**F9 — SHIPPED 2026-08-28 (`4bdc176`).** *Was Effort S.* `e97087b`'s `opAnim` pops a plain `int` and
handles 1000 (set rotation) and 1010 (set frame) explicitly (`interpreter_extra.cc opAnim` :3420-3428);
`ScriptHost.Anim` (`ScriptHost.cs:1638`) now dispatches both through the new
`ApplyDirectAnim` (`ScriptHost.cs:103`) before falling through to `AnimRequested`. **The change was
purely additive**: `AnimRequested`'s own callback already gated `anim < 40`
(`ViewerGame.cs:1237`, `if (anim is >= 0 and < 40 …)`), so a script calling `anim(obj, 1000, rot)` was
never crashing or misbehaving before this fix — 1000/1010 simply fell through that gate and did
nothing, a silent no-op rather than a bogus animation request reaching the renderer.
`MapObject.Frame` (`Map/MapFile.cs:50`) became settable (not init-only) to let `anim(obj, 1010,
frame)` mutate it directly, mirroring `objectSetFrame`. **One deliberate divergence, recorded in
`ApplyDirectAnim`'s own comment**: the reference guards only the upper bound
(`frame < ROTATION_COUNT`, and `objectSetRotation` likewise only rejects `direction >= ROTATION_COUNT`),
so vanilla will happily store a *negative* rotation; the port adds a lower bound
(`frame is >= 0 and < 6`) because Hexwaste feeds `Rotation` into `Fid.Build` and into per-rotation
array indexing, where a negative value throws rather than rendering garbage.

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
(`CombatEngine.cs:746`, was `:729`) called `RunDamageProc(acc.Victim, attacker, …)` unconditionally for any
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

**F15 — SHIPPED 2026-08-20 (`205da73`), 948 tests, combat-golden 17/17, 0 fixtures moved.** *Was
Effort S–M · re-record tier (moved nothing — no committed burst fixture lands its inception roll on
a fumble at its recorded seed).* Unblocked once F26 landed. Before implementing, confirmed
**reachable and non-vacuous** rather than assumed: a plausible reading of `_compute_spray`'s early
return on critical failure (`combat.cc:3718-3719`) could have concluded the rounds-spent out-param is
never actually set on the fumble path, and closed this entry as unreachable instead. It isn't —
`*roundsSpentPtr = ammoQuantity;` (`combat.cc:3713`) runs *before* the roll at `:3716`, so
`attack->ammoQuantity` (assigned at `:3888`) carries the burst's rounds-spent count into both the
`DAM_HIT_SELF` ternary (`:4229`) and the `DAM_RANDOM_HIT` ternary (`:4259`, an identical
`attackType == ATTACK_TYPE_RANGED ? attack->ammoQuantity : 1`), both feeding
`attackComputeDamage`'s per-round loop (`:4589`). Changed `CritFailDamage` (`CombatEngine.cs:1291`)
and `ApplyCritFailureEffects` (`:1197`) to take a `roundCount` parameter (default `1`, so every
non-burst call site is unaffected by construction), threaded through both the `DamHitSelf` and
`DamRandomHit` branches — the reference treats the two identically, so Hexwaste now does too.
`RollBurst`'s abort call (`:561`) passes `n`, its already-computed rounds-spent count (`:537`), as
`roundCount`. The primary test asserts the draw *count* (9 draws of the same damage roll for a
9-round burst), not just the resulting total, since a correct sum can hide a wrong count; it failed
pre-change with `Expected: 9, Actual: 1` — a genuine draw-count regression, not a scaffolding
artifact. Single-shot and melee behaviour (`roundCount` defaults to `1`) is pinned unchanged by two
non-regression tests.

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
`ProcessArmedCharges` (`ViewerGame.cs:4377`), which passes `killer: _dude?.Dude` (non-null) for a
*planted* C4 charge — not an attack-sourced blast in the reference's sense. Traced in the reference:
`actionExplode` (`actions.cc:1582`) builds a synthetic `Attack` via
`attackInit(attack, explosion, critter, HIT_MODE_PUNCH, HIT_LOCATION_TORSO)` (`actions.cc:1631`)
whose attacker is the transient misc-10 explosion-marker object, never the placer/`sourceObj`
(`queue.cc:486` passes `gDude` only as `sourceObj`, used later purely for XP/reputation at
`actions.cc:1727`). So `killer != null` cannot distinguish "genuine attack" from "planted charge with
a known placer" — the explicit `attackSourced` opt-in was added instead, verified against this
falsifying caller directly rather than trusted from the brief.
**Flagged, not fixed here (tracked as F27 and F28 below):** `ApplyBurstExtras`
(`CombatEngine.cs:996`, was `:977`) modelled the same reference predicate with a simpler `!= dude && Sid != -1`
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

**F26 — SHIPPED 2026-08-20 (`c5001ac`), 945 tests, combat-golden 17/17, 0 fixtures moved.** *Was
Effort M (predicted **re-record tier**; landed **byte-identical** instead — see correction below).*

**Correction to this entry's own original claims — it was wrong on two counts, and a wrong closed
entry misleads as much as a wrong open one, so both are recorded rather than silently dropped:**
1. It claimed `TryBurst`/`RollBurst` "have no crit-failure branch anywhere in them." **False.** The
   detection — the inception roll, its RNG draws, the day-2 `_host.CriticalsEnabled` gate, and the
   abort-with-bullets-spent — was already present in `RollBurst` (`CombatEngine.cs:528`, the abort at
   `:543-563`) and was already a correct, faithful port of `_compute_spray` (`combat.cc:3703-3720`).
   Only the *effects* half — `attackComputeCriticalFailure`'s drop/destroy/explode/cripple/self-hit
   consequences — was missing.
2. It claimed wiring this in "changes the RNG draw sequence for every fixture where a burst attack
   currently misses cleanly." **False.** The detection draw already existed and is untouched by this
   fix; a cleanly-missing burst fixture draws exactly the same random numbers it always did. Only a
   fixture whose burst *actually lands on* a critical failure at its recorded seed could move, and
   none of the three committed burst fixtures (`arcaves-burst-smg`, `arcaves-burst-shotgun`,
   `denbus2-burst-collateral`) does — hence 0/3 moved, not a coincidence of scope.

**Lesson, same shape as F11's:** a prediction about which fixtures a change will move, and about what
code does or doesn't already exist, is a factual claim that needs the same verification as everything
else in this file — both went unchecked here and were wrong.

Implementation: `TriggerCritFailure` (single-shot/melee/thrown trigger, `CombatEngine.cs:1172`) was
split into itself (still owns its own day-gated + Jinxed trigger roll) plus a new
`ApplyCritFailureEffects` (`:1197`, effects only, no re-roll), which it now calls once its trigger
lands. `RollBurst`'s abort branch (`:561`) calls `ApplyCritFailureEffects` directly — no second roll,
because the burst's own inception roll already *is* the trigger in the reference: `_compute_spray`'s
`ROLL_CRITICAL_FAILURE` return dispatches straight into the shared `case ROLL_CRITICAL_FAILURE:` arm
of `attackCompute`'s post-roll switch (`combat.cc:3933-3934`), with no independent trigger draw the
way single-shot's `attackCompute` has at `:3849`. The effects call lives inside `RollBurst`'s one
abort branch rather than at each of the three call sites (`TryBurst` `:420`, `TryAllyBurst` `:3785`,
`TryEnemyBurst` `:3818`), making it structurally impossible for a burst path to abort without
applying effects. `RollBurst`'s return tuple grew a `bool LoseTurn` member so the AP-zeroing
consequence reaches all three callers, mirroring the existing single-shot pattern at `:369-370`.
F15 (above) was the burst-fumble self-hit roll-count fix this unblocked.

**F27 — SHIPPED 2026-08-20 (`e34189c`), 953 tests, combat-golden 17/17, 0 fixtures moved (no golden
pits a party member against another party member).** *Was Effort S · tracked, not fixed.*
`_damage_object` skips `SCRIPT_PROC_DAMAGE` when **both** the victim and the damage source are party
members (`combat.cc:4849`, `if (!objectIsPartyMember(a1) || !objectIsPartyMember(a5))`).

**Correction to this entry's original scope claim:** it attributed the missing party gate to
`ApplyBurstExtras` alone. In fact **four of the six** `RunDamageProc` call sites had no party-pair
consideration at all (only F16's new `Explode` blast gate and its self-damage tail carried one, each
written separately rather than shared) — so a party member's ordinary hit, burst, or burst-extra
could run a companion's `damage_p_proc`, which the reference suppresses. Fixed by extracting one
`ShouldRunDamageProc(MapObject target, MapObject? source)` helper (`CombatEngine.cs:1629`) carrying
the shared predicate — `target.Sid == -1` precondition plus the `:4849` pair gate, with `_host.Dude`
counted as a party member — `partyMemberAdd(gDude)` at object load, `object.cc:347`, which stamps the id at `party_member.cc:398` — and routing all six sites through it
(`CombatEngine.cs:964, 996, 1329, 1560, 1792, 1829`). Site-specific conditions (`victim ==
attacker.Critter && dmg > 0`, `victim == selfDamageProcFor`, `attackSourced && victim != killer`)
deliberately stayed at their own sites rather than being folded into the shared helper — doing so
would have recreated the F12 failure mode (a boundary condition silently absorbed into a shared gate).
Regression test added proving the pre-change gap failed (a dude-fired burst extra ran a companion's
proc); F12/F16's existing boundary pins re-verified to still hold post-change.

**Test-coverage note (new, tracked as F32 below):** no fixture in the combat-golden corpus pits a
party member against another party member, so this fix is exercised only by unit tests, never
end-to-end through a real map/script — see F32.

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
(`ViewerGame.cs:4377`) passes the placer (`_dude?.Dude`) directly as `killer`, and F16 deliberately
left `attackSourced: false` at this call site and at the scripted-`explosion` site
(`ViewerGame.cs:1282`, `killer: null`) rather than approximate the reference's synthetic-attacker
shape. Closing this properly needs a marker-object concept Hexwaste doesn't currently model (the
existing `SpawnExplosionMarker` is visual-only); documented here by the F16 implementer as a real,
cited gap rather than implemented.

**F29 — RESOLVED 2026-08-20 (`e34189c`) as an inert dead-code cleanup, NOT a behavioural fix.**
*Was Effort S · re-record tier (predicted; landed byte-identical instead — see below).* Originally
filed as: every Hexwaste site modelling `_damage_object`'s proc gate carried an extra `!= dude`
exclusion with no reference counterpart, since `combat.cc:4849` gates on the party **pair** only
(`if (!objectIsPartyMember(a1) || !objectIsPartyMember(a5))`) and vanilla genuinely runs the dude's
`damage_p_proc` when an enemy-sourced blast or burst catches him.

**What Task 1's investigation (predecessor to this fix) actually found, and why the term was
inert rather than live:** the dude's `MapObject` (`ViewerGame.cs`, `SpawnDude`) never set `Sid` at
construction, so it held C#'s `int` default, `0` — not `-1`. Every `Sid != -1` guard in the codebase
(including the pre-change per-site `!= dude` gates) therefore *passed* for the dude, meaning the term
did technically evaluate against a live, non-sentinel value. It was nonetheless behaviourally inert
because sid `0` can never be a real object-bound script: in the map-format's `sid >> 24` type scheme,
type `0` is "system" (map-level, not object-bound); Task 1 loaded all 146 loadable campaign maps and
confirmed `ScriptsBySid` never contains key `0` on any of them — only types 1 (spatial), 3 (item), and
4 (critter) ever appear. So `RunObjectProc`/`RunDamageProc` resolved to a no-op for the dude on every
real map regardless of the `!= dude` term, and removing it changes nothing observable on shipped data.

**The reference does the opposite of what the removed term implied**: it actively wires the dude up
for this hook rather than suppressing it. `scriptsSetDudeScript` (`scripts.cc:1460-1489`, called from
`scriptsReset` at game start and from `_obj_load_dude` after a save load) gives the dude a real, live
`sid` bound to a genuine critter-type script, specifically so hooks like `damage_p_proc` can fire; the
gate at `:4849` is pair-only with no dude-specific carve-out anywhere in `_damage_object` or its
sibling branch. Hexwaste's divergence from this was real in principle but unobservable in practice —
the dude's Hexwaste `Sid` was never live enough to reach the difference.

**Hardening applied alongside the cleanup:** `SpawnDude` now sets `Sid = -1` explicitly
(`ViewerGame.cs:3064`), turning "inert because no shipped map happens to bind sid 0 to an object" into
"inert by construction." This was applied only after Task 2's codebase-wide survey of 44 `Sid != -1`
/ `Sid == -1` sites confirmed every one of them either already excludes the dude explicitly, never
receives the dude object at all, or wraps a `ScriptsBySid.TryGetValue` lookup that fails identically
for key `0` and key `-1` — i.e. the change was *checked*, not assumed, before landing. It is worth
recording that this was a real behavioural change in principle even though nothing flipped: the
dude's `Sid` genuinely was `0`, not `-1`, before this commit.

**Correction to this entry's original supporting argument:** the spec that filed this item cited two
`ViewerGame.cs` sites (the `map_exit_p_proc` sweep and the start/`map_enter` pass) as evidence of a
deliberate "the dude's script never runs from engine hooks" convention. On review both are map-wide
**object-sweep** filters that skip the dude because the reference iterates the map's `Object` list
nodes and the player isn't one of them — they say nothing about damage hooks specifically, and using
them as grounding for F29 was a weak inference. Recorded here so the next reader doesn't re-derive the
same inference and mistake it for evidence; the real grounding is the `combat.cc:4849` pair-gate trace
above.

Result: `ShouldRunDamageProc` (`CombatEngine.cs:1629`) carries no dude-specific term at all — only the
`Sid == -1` precondition and the pair gate, with `_host.Dude` counted as a party member per
`object.cc:347` (`partyMemberAdd(gDude)`). 953 tests, combat-golden 17/17 byte-identical, 0 fixtures moved (predicted
re-record tier; the prediction was wrong for the same reason F26's was — see F26's own correction
above for the general lesson about unverified fixture-movement predictions).

**F30 — SHIPPED 2026-08-21 (`2107a89`), 955 tests, combat-golden 17/17, 0 fixtures moved.** *Was
Effort S · found by the F26/F15 whole-branch review (2026-08-20).* An INVULNERABLE critter suffered
the full critical-failure result where the reference exempts it outright.
`attackComputeCriticalFailure` early-returns when the attacker carries `CRITTER_INVULNERABLE`
(`combat.cc:4182-4184`, flag `0x400` at `obj_types.h:99`) — **before** the dude's day-6 gate
(`:4186`) and before any `_cf_table` lookup, so such an attacker draws **no severity roll at all**.
Hexwaste had no invulnerability check anywhere in `CombatEngine`, so an invulnerable critter that
fumbled could drop or destroy its weapon, hit itself, lose its ammo, be crippled or blinded, and lose
its turn. Scripted invulnerable NPCs are a normal content device, so this was reachable in ordinary
play rather than theoretical.

Fixed with one guard at the head of `ApplyCritFailureEffects`, which the burst work (F26) had already
made the single effects entry point for both the single-shot and burst paths — so one guard covers
every route. The plumbing needed nothing new: `Proto.CritterFlags` was already parsed and
`CombatEngine` already read it for `CRITTER_NO_KNOCKBACK`.

**The placement is load-bearing and the code comment says so.** The invulnerable exemption must
precede the day-6 dude gate, because the two differ in kind: the invulnerable case draws *nothing*,
while the day<6 dude case still draws the trigger and is gated only on its *effect*. A guard placed
after `CriticalFailure.Resolve` would still consume the severity draw and silently diverge the RNG
stream while every visible effect looked correct — so the test asserts the **draw count** (2, not 3),
not merely the absence of effects. That is the same class of mistake that let F11 hide for months.

No fixture moved: a golden would have to contain an invulnerable critter that fumbles, and none does.

**F31 — REFRAMED 2026-08-22; UNBLOCKED 2026-08-22 by F34. The entry was wrong on three counts.** *Was
"Effort S-M · burst ammo-cost scaling".* Grounding `_item_w_compute_ammo_cost` (`item.cc:1947-1965`)
showed the function is nothing like what this entry described:

```c
int _item_w_compute_ammo_cost(Object* obj, int* inout_a2)
{
    if (inout_a2 == nullptr) return -1;
    if (obj == nullptr) return 0;
    pid = obj->pid;
    if (pid == PROTO_ID_SUPER_CATTLE_PROD || pid == PROTO_ID_MEGA_POWER_FIST) {
        *inout_a2 *= 2;
    }
    return 0;
}
```

1. **It is not "scaling", and not proto-driven.** It doubles the cost for exactly **two hardcoded
   PIDs** — 399 Super Cattle Prod and 407 Mega Power Fist (`proto_types.h:177-178`). Both protos ship
   (`proto\ITEMS\00000399.pro`, `00000407.pro`), so the case is real, just narrow.
2. **It is not burst-related.** Its call site (`combat.cc:3905`) runs after *both* branches — the
   ranged one that sets `ammoQuantity` from the spray, and the melee one at `:3900-3902` that sets it
   to 1 when `ammoGetCapacity(weapon) > 0`. Those two weapons are melee, so this only ever doubles the
   melee path.
3. **The "aborts the attack on `-1`" half is unreachable** from that call site: `-1` is returned only
   when the out-pointer is null, and `attackCompute` passes `&(attack->ammoQuantity)`.

**Unblocked 2026-08-22 — F34 shipped (`30a9371`, `b0063e5`, `a2bbc56`, `2b8d7ba`).** Charges are now
spent for any weapon with ammo capacity, so there is a per-attack charge for the two special PIDs to
double, and this is actionable. F34's census confirms both hardcoded PIDs — 399 Super Cattle Prod and
407 Mega Power Fist — ship (`proto\ITEMS\00000399.pro`, `00000407.pro`) and are among the five non-gun
ammo-capacity weapons in the game. F31 is now a two-PID special case on top of F34's general
charge-spend: double `attack->ammoQuantity` for those two PIDs per `item.cc:1947-1965`.

**SHIPPED 2026-08-23 (`69de7ea`, `dabd5c7`), combat-golden 18/18, quest-golden 39/39, encounter-golden
188/188 (all ALL PASS), `dotnet test` 968 passed / 91 skipped (pre-existing `FALLOUT2_DIR` gate),
`git status` clean, nothing re-recorded — re-measured at `dabd5c7` after the clamp landed, not carried
over from the earlier run at `69de7ea` and not inferred from "nothing wields either PID."**
`AmmoCost(weaponProto, quantity)` (`CombatEngine.cs`, beside `UsesCharges`) doubles `quantity` for PIDs
399 and 407 and is unchanged otherwise; wired at all four charge-spend sites — the three single-shot
sites (dude/ally/enemy, each `... - AmmoCost(weaponProto, 1)`) and the burst site
(`Math.Max(0, b.AmmoBefore - AmmoCost(b.WeaponProto, b.RoundsFired))`, inert here since neither PID is
burst-capable). Four hermetic tests in `CombatEngineTests.cs`, mutation-verified pre-change
(`Expected 18, Actual 19` on both PIDs); the Cattle Prod (160) and a gun case are inertness guards.

**Corrected 2026-08-23 — the odd-charge-count drift is NOT ported as-is; it is clamped at zero, and the
original write-up understated why.** The reference's refusal tests `ammoGetQuantity(weapon) == 0`
(`_combat_check_bad_shot`, `combat.cc:5680`) and the deduction clamps only at the *top*
(`ammoSetQuantity`, `item.cc:1423`) — there is no floor. So in vanilla, for these two weapons starting
from an **odd** charge count, spending 2 from 1 lands on −1; `−1 != 0`, so the refusal never fires and
the weapon keeps attacking, drifting −1, −3, −5…

Two things the earlier version of this passage got wrong. First, it called the Mega Power Fist's
capacity even, so this case was "reachable only from an odd starting count that is never reloaded" —
false: F34's own census gives the Mega Power Fist a capacity of **25**, which is odd, so the very first
attack from a full weapon lands on an odd count and this is reachable in ordinary play, not just from
some hand-placed map oddity. Second, it claimed Hexwaste "halts the drift after one negative step" —
also false. `AmmoQuantity == -1` is this codebase's sentinel for "unhydrated item, refill to capacity"
(`WeaponAmmo`, `ViewerGame.CombatHost.cs:183-188`), so the write that would have produced −1 never gets
read back as −1: the next attack's `WeaponAmmo` call sees the sentinel first and rewrites the weapon to
full, silently reintroducing the infinite-ammo bug F34 removed. The `<= 0` refusal never sees the −1 at
all.

The reload path also refutes "never reloaded" as a precondition even where it was true: `weaponReload`
tops a partial ammo box up to an arbitrary count, not just to capacity, so an odd count can arise from
ordinary reloading, not only from a map-placed instance.

**Given that, Hexwaste clamps the three single-shot spend sites at 0 instead of letting them go negative
— a deliberate deviation from the reference, forced by the sentinel collision, not an oversight.**
Reproducing vanilla's drift would not reproduce vanilla's behaviour here; it would produce a refill to
full, which is further from vanilla than clamping is. The doubling itself (`AmmoCost`) is still ported
faithfully; only the floor is new, and only because −1 cannot be represented as an ordinary negative
count in this engine. The root sentinel collision is filed separately (see F41 below) since closing it
is a repo-wide convention change, not a one-item fix.

**F32 — SHIPPED 2026-08-21 (`ff30069`), one new golden fixture, zero modified.** *Was Effort S ·
test-coverage gap, not a defect · found during F27/F29 closeout (2026-08-20).* F27 made all six
`RunDamageProc` sites honour `_damage_object`'s party **pair** gate (`combat.cc:4849`), but nothing in
the golden suite could catch a regression of it.

**The entry's own suggestion — author a companion-vs-companion fixture — would not have worked, and
that is the useful part to record.** Damage-proc output goes through `_host.Log(...)`, and
`ViewerGame.Log` appends to `_messageLog` and queues a floating-text entry; it **never writes to
stdout**. Only `Transcript` does (`Console.WriteLine`), and the golden scripts capture stdout. None of
the six production `RunDamageProc` call sites emits a `Transcript` line. So a party-on-party *fixture*
would have been byte-identical whether the proc ran or not — it would have looked like coverage,
provided none, and retired this entry while leaving the hole open.

Closed instead with the shape F21 established for the same problem (behaviour with no golden-visible
signal): a headless harness probe printing a **discriminating value**, pinned as a combat-golden
scenario. `--party-proc-probe` reports both halves of the pair gate on one line, because reporting
only the suppression would pass against a gate stubbed to always-false:

```
party-proc-probe: victim=20529 sid=67108868 partyToPartyRan=0 enemyToPartyRan=1
```

`partyToPartyRan=0` is F27's fix; `enemyToPartyRan=1` is the positive case the pair gate requires
(`:4849` suppresses only when *both* sides are party members). The victim carries a real critter `sid`
— a `Sid == -1` victim can never run a proc under any gate and would have made the probe vacuous.

**Proven to discriminate, not merely present:** reverting `ShouldRunDamageProc`'s pair test flips the
line to `partyToPartyRan=1 enemyToPartyRan=1`; restoring returns it. That is the same standard F21's
pinned scenario met — a probe never shown to fail is not a regression net.

Deliberately not done: adding `Transcript` output to the six production call sites. That would emit new
lines wherever procs currently run, re-recording many existing fixtures, and would bake diagnostic
output into the engine's transcript to solve what is a coverage problem. The probe reaches the gate
through a small documented `ProbePartyDamageProc` seam instead.

**F33 — `ShootBlockerAt`'s flag test is the De Morgan inverse of `_obj_shoot_blocking_at`'s — but the
naive fix breaks ordinary ranged combat, so the predicate is NOT the whole story.** *Effort M ·
investigation before implementation · measured 2026-08-21, change attempted and reverted.*

`_obj_shoot_blocking_at` (`object.cc:2451`) gates on:

```c
(flags & OBJECT_HIDDEN) == 0
    && ((flags & OBJECT_NO_BLOCK) == 0 || (flags & OBJECT_SHOOT_THRU) == 0)
```

By De Morgan that is "blocks **unless both** flags are set" — and both together is exactly
`OBJECT_OPEN_DOOR` (`obj_types.h:89` = `SHOOT_THRU | LIGHT_THRU | NO_BLOCK`), which is the case the
disjunction exists to let shots through. Hexwaste's `ShootBlockerAt`
(`ViewerGame.CombatHost.cs:~213`) writes the same test with `&&`:
`(Flags & noBlock) == 0 && (Flags & shootThru) == 0` — "blocks **only when neither** flag is set", so
anything carrying either flag alone lets shots pass.

**The two readings disagree widely.** A survey of all 155 shipped maps (temporary `ProcAnalyze`
instrumentation, reverted) over 209,413 solid-type objects (critter/scenery/wall):

| flags | count | reference | Hexwaste |
|---|---|---|---|
| neither | 91,117 | blocks | blocks |
| `NO_BLOCK` only | 5,368 | blocks | **passes** |
| `SHOOT_THRU` only | 95,463 | blocks | **passes** |
| both (open door) | 17,465 | passes | passes |

**48% of solid objects are classified differently** — so unlike F29's `!= dude` term, this is not
inert.

**But the naive fix is wrong, and that is the important half of this entry.** Swapping `&&` for `||`
was tried and measured: `dotnet test` stayed 955/0, but `denbus2-burst-collateral` lost its *entire*
scenario — the burst never fires, no collateral, no combat opening, target undamaged (`hp=47` vs
`hp=35`). An ordinary 10mm SMG burst at 8 tiles across a normal map became impossible. If
`SHOOT_THRU`-only objects truly blocked shots, ranged combat would be frequently impossible in
vanilla, which it plainly is not.

So one of these is true, and the next person should establish which **before** touching the operator:

1. The predicate is right but the **object set is wrong** — Hexwaste applies it over
   `_solidObjects[_elevation]`, while vanilla's line-of-fire trace may consult a narrower set, or
   exclude objects earlier (`_obj_shoot_blocking_at` takes an `excludeObj`, and the callers differ).
2. The expression means something other than the plain De Morgan reading — worth checking fo2ce issue
   history or disassembly notes for `_obj_shoot_blocking_at` before assuming the `&&` was a slip.

The change was **reverted**; `main` keeps the `&&`. Reach if it is ever corrected: ~11 consumers —
to-hit line-of-fire penalties, missed-shot overshoot, explosion line-of-sight, `DangerSource`
reachability, enemy/ally approach, and rendering — so treat as re-record tier with a wide blast
radius, and expect to re-record combat *and* encounter fixtures.

**F25 is blocked behind this.** F25 asks the worldmap start-point probe to use shoot-blocking instead
of movement-blocking (`worldmap.cc:4088` passes `_obj_shoot_blocking_at`); adopting a predicate whose
correctness is unresolved would propagate the same uncertainty into encounter placement.

**F34 — SHIPPED 2026-08-22 (`30a9371`, `b0063e5`, `a2bbc56`, `2b8d7ba`), combat-golden 18/18,
quest-golden 39/39, encounter-golden 188/188, `dotnet test` 963 passed / 91 skipped (pre-existing
`FALLOUT2_DIR` gate), byte-identical, nothing re-recorded (`census`, `endgame` and `opening` were
not run — they are combat-free).** *Was Effort S-M ·
re-record tier if any fixture wields one · found grounding F31 (2026-08-22).* Melee/unarmed weapons
with ammo capacity consumed no charges; the reference spends one per attack. `attackCompute`'s
non-ranged branch (`combat.cc:3819`, capacity test `:3900`) sets `attack->ammoQuantity = 1` whenever
`ammoGetCapacity(attack->weapon) > 0`, and the refusal (`_combat_check_bad_shot`, `combat.cc:5679-5683`)
gates on the same capacity test, attacker-agnostic. Hexwaste gated all of it on `isGun` instead.

**The gap was four sites, not one — the entry's original "Effort S-M" framing undersold it.** Three
spend sites (`30a9371`: dude, ally, enemy attack paths) plus the refusal, which itself turned out to
have three attacker-side call sites (dude, ally, enemy) of which only the target-selection helper,
`CheckBadShot`, was already capacity-gated correctly. `a2bbc56` fixed the dude and ally refusal paths;
`2b8d7ba` found and fixed the enemy path's structural twin during review, after the controller
initially assumed `CheckBadShot` already covered it (it doesn't — `CheckBadShot` gates target
selection only and explicitly tolerates `ShotStatus.NoAmmo` there). `b0063e5` corrected a citation
error introduced by this very entry: the reference function is `attackCompute` (`combat.cc:3819`), not
`_compute_attack`, which does not exist at pin `e97087b`; the capacity test is `:3900`, not `:3899`.

**Full proto census — five non-guns with ammo capacity, all obtainable:**

| PID | Name | Anim | Capacity |
|-----|------|------|----------|
| 116 | Ripper | SWING | 30 |
| 160 | Cattle Prod | SWING | 20 |
| 235 | Power Fist | PUNCH | 25 |
| 399 | Super Cattle Prod | SWING | 20 |
| 407 | Mega Power Fist | PUNCH | 25 |

All five draw Small Energy Cell (ammo PID 38) and were reachable in normal play with infinite charges
before this fix.

**Both halves shipped:** spending is now gated on `UsesCharges` (`(weaponProto?.Weapon?.AmmoCapacity
?? 0) > 0`, `CombatEngine.cs:235`) at all three spend sites, and the drained-weapon refusal is gated
on the same predicate on the dude, ally and enemy attack paths. None of the 18 combat, 39 quest or 188
encounter fixtures wields one of the five weapons, so nothing moved — `git status` stayed clean. **F31
sits on top of this**: once charges are spent, the two hardcoded PIDs (399 / 407) double the cost per
`item.cc:1960-1962`.

**F35 — Hexwaste auto-reloads the dude's empty weapon inside the attack path; vanilla refuses the
attack instead.** *Effort S-M · re-record tier (moves any fixture where a gun runs dry mid-fight) ·
found filing F34 (2026-08-22).* `_combat_attack_this` prints "Out of ammo.", plays the out-of-ammo sfx
and returns without reloading (`combat.cc:5738-5747`) when `_combat_check_bad_shot` reports
`COMBAT_BAD_SHOT_NO_AMMO` for the dude. Only the AI reloads mid-attack (`combat_ai.cc:2732-2740`, the
`_ai_try_attack` loop). Hexwaste's `TryAttack` does the opposite for the dude: on the same
capacity-gated empty-weapon check it attempts `_host.TryReload(...)` and consumes AP for it before
ever refusing (`CombatEngine.cs:329-346`, the block Task 2 of F34 touched to move it off `isGun` and
onto `UsesCharges`).

This predates the CombatEngine extraction (`53c1df4`) — it is not a regression introduced by F34's
work, only exposed by reading the same block closely. F34 deliberately left it in place and extended
it to cover the five newly-gated non-gun weapons rather than fix it, because fixing it here would move
fixtures for a reason unrelated to charge-spending (see F34's spec, "the auto-reload deviation itself"
section). Closing it means the dude behaves like vanilla — printing "Out of ammo." and losing nothing
but the turn's attempt — for any capacity weapon it fires empty, guns included, so **any existing or
future fixture that runs a gun dry mid-combat would move**: re-record tier.

**F36 — SHIPPED 2026-08-23 (`0c28cb4`, `2fc46df`), combat-golden 18/18, quest-golden 39/39,
encounter-golden 188/188, all byte-identical, `git status` clean, nothing re-recorded.** *Was Effort
S-M · re-record tier (damage-affecting) · found filing F34 (2026-08-22), F34's natural successor
alongside F31.* **Grounding this before touching code corrected its own framing**, the same pattern as
F37/F38: the reference reads `weaponGetAmmoDamageResistanceModifier`, `weaponGetAmmoDamageMultiplier`
and `weaponGetAmmoDamageDivisor` from `attack->weapon` with no attack-type gate (`combat.cc:4579-4587`,
plus the AC modifier read in `attackDetermineToHit`, `combat.cc:4429-4434`); Hexwaste read the loaded
ammo's mods only on the gun path, and the melee/unarmed branch called `CombatMath.RollWeaponDamage`/
`RollDamage`/`ToHitChance` with none of them. **A proto census, not just a code diff, showed the entry's
"damage-affecting" and "re-record tier" claims were wrong**: exactly five non-gun weapons carry a real
`ammoTypePid` at all (the F34 five — Ripper 116, Cattle Prod 160, Power Fist 235, Super Cattle Prod
399, Mega Power Fist 407), and all five load Small Energy Cell (38), whose modifiers are every one the
neutral value (AC 0, DR 0, multiplier 1, divisor 1). So on shipped data "computed as if unloaded" is
numerically identical to "computed as loaded" — this was always a structural fidelity gap with
**provably zero behavioural effect on shipped data**, not the damage-moving change the entry advertised;
the census is why the fixtures were expected byte-identical and why they landed that way. The value is
structural: 17 ammo protos genuinely carry non-neutral modifiers (proving the mechanism is real for
guns), and a future non-gun weapon or a corrected proto would otherwise silently diverge.

Closed by threading the loaded ammo's DR modifier, damage multiplier, damage divisor and AC modifier
into the melee/unarmed path (`CombatEngine.cs` `RollAttack`'s `else` branch, both the damage call and
the `else` branch of the to-hit computation) exactly as the gun branch already does, and giving
`CombatMath.RollDamage`/`RollWeaponDamage`/`ToHitChance` the three neutral-defaulted parameters so
every existing call site is unchanged by construction. Applied in the reference's order
(`combat.cc:4586-4600`): multiply by `critMultiplier * ammoDamageMultiplier`, divide by the divisor only
when non-zero (`combat.cc:4596-4598`, the `if (damageDivisor != 0)` guard — ported without the ranged
path's `Math.Max(_, 1)` clamp workaround, see F43 below), then `/ 2`, then the difficulty modifier, then
DT, then DR with the ammo modifier added in before the existing `[0, 100]` clamp
(`CombatMath.cs:90-93`). `2fc46df` pins the multiply-before-halve order with a mutation-verified test
(odd raw damage × an odd multiplier: `Expected: 31, Actual: 30` under the wrong order) and fixes three
citation slips introduced by `0c28cb4` (`combat.cc:4593`→`4596`, `:4554`→`4553`, `:4602`→`4603`, some
appearing in more than one file) caught by the citation-verification pass. Both melee damage helpers
still take exactly one `rng.Next` draw before
any arithmetic, so the RNG stream is unchanged — the reason the golden suites came back byte-identical
rather than needing a re-record.

**F37 — `AiSwitchWeapon` can settle on a drained non-gun capacity weapon that then fires anyway,
because Hexwaste never re-checks after the switch; the reference does.** *Effort S · found in
Task 2 review of F34 (2026-08-22), untestable through `FakeCombatHost` as it stands.*
`AiSwitchWeapon`'s candidate loop (`CombatEngine.cs:3206-3225`) rejecting a candidate for empty ammo
only `if (attackType == WeaponClass.AttackRanged && _host.WeaponAmmo(proto, item) <= 0 && ...)`
(`:3223`) is **not itself a divergence** — the reference does the same: `_ai_search_inven_weap`
(`combat_ai.cc:2035-2039`) wraps its own `ammoGetQuantity`/`aiHaveAmmo` test in
`if (weaponGetAttackTypeForHitMode(weapon, HIT_MODE_RIGHT_WEAPON_PRIMARY) == ATTACK_TYPE_RANGED)`.
Widening `:3223` to cover melee candidates, as this entry previously prescribed, would *introduce* a
deviation rather than remove one.

**The real divergence is downstream, in what happens after the switch.** `_ai_try_attack`
(`combat_ai.cc:2725-2731`) re-runs `_combat_check_bad_shot` inside a 10-attempt loop — so a
just-switched-to drained melee weapon that `_ai_search_inven_weap` let through unscreened is still
caught on the very next pass through the loop, before it can fire. Hexwaste's switch call sites check
capacity once before the switch and never again. `TryReloadSwitchedGun` (`CombatEngine.cs:3309-3323`),
which every switch call site runs immediately afterward, returns `false` and does nothing when
`!isGun` (`:3311`) — its contract only covers guns, so a switched-to drained non-gun weapon is neither
reloaded nor cleared to fists, and there is no equivalent of the reference's retry loop to catch it.
The caller then proceeds to fire it: the ally path's spend site (`:3796-3797`, `if
(UsesCharges(weaponProto) && weaponItem is not null) weaponItem.AmmoQuantity = _host.WeaponAmmo(...)
- 1;`) decrements an already-zero magazine, and the enemy path's structural twin has the same shape.

**Currently untestable through `FakeCombatHost` for this exact combination.** The fixture that would
prove it needs an ally or enemy carrying two drained non-gun capacity weapons — the currently-equipped
one plus a `CritterInventoryWeapons` backup — so `AiSwitchWeapon` lands on the second while it is still
empty. `CritterInventoryWeapons` defaults to an empty list on `FakeCombatHost` and existing tests that
populate it (e.g. the dry-gun-switches-to-dry-gun tests around line 1340/1377) only ever construct
*gun* backups; no existing scenario exercises two non-gun capacity weapons through the switch-then-fire
path, so proving this needs new test-double coverage before it can be closed. Closing it: give
`TryReloadSwitchedGun` (or a sibling) a non-gun path that clears to fists when a switched-to capacity
weapon is drained, matching what the reference's post-switch retry loop achieves — not by widening the
pre-switch screen at `:3223`, which already matches vanilla.

**F38 — SHIPPED 2026-08-22 (`5b0bc06`, `1744765`), combat-golden 18/18, quest-golden 39/39,
encounter-golden 188/188 (including `awareness-perk`, the fixture that exercises the examine gate),
endgame-golden and opening-golden pass, byte-identical, nothing re-recorded, `git status` clean.**
*Was Effort S · viewer-only, no golden coverage · found in Task 2 review of F34 (2026-08-22).*
**Grounding this before touching code corrected two of its three claims** — filed straight from a
review finding without checking the reference first, the same failure as F37 the day before. Both
gates are now re-based on the reference condition instead of `IsGun`:

- `ViewerGame.Hud.cs:146` (HUD ammo-bar counter) now gates on `AmmoCapacity > 0`
  (`ShowsAmmoReadout`, `ViewerGame.CombatHost.cs`), citing `interface.cc:1357-1359`:
  `if (p->isWeapon != 0) { int maximum = ammoGetCapacity(p->item); if (maximum > 0) { ... } }`.
- `ViewerGame.cs:5987` (the Awareness examine readout) now gates on `Caliber != 0`
  (`ShowsExamineShots`), citing `proto_instance.cc` `_obj_examine_func` (`:316-323`, the caliber test
  at `:319`) and `item.cc:1395-1412` (`ammoGetCaliber`).

**The original entry's third claim — that the HUD site is a "counter" needing only re-gating — was
also wrong**, and is not what this fix touches: vanilla's HUD site is a 70px dithered gauge, not
digits (filed separately, see the digits-vs-gauge entry below). What survived grounding is that the
two gates genuinely differ (capacity vs. caliber) rather than both being capacity, which the fix
preserves: `WeaponProtoStats.Caliber != 0` is a faithful stand-in for `ammoGetCaliber(weapon) != 0`,
audited across **all 110 weapon protos** — the proto's own caliber field equals the caliber of the
proto resolved through `AmmoTypePid` for every one, and is 0 when that pid is −1, with zero
mismatches (stronger than the spec's "checked across the weapon set").

**Probe evidence** (`--awareness-probe <hex>:<pid>`, extended in this work to force-arm an NPC with an
arbitrary weapon pid, exercising the real `ShowsAmmoReadout`/`ShowsExamineShots` predicates). The
`hudGate` column evaluates the shared `ShowsAmmoReadout` predicate against the *NPC's* forced weapon —
the same predicate the HUD draw site calls, so it is not a restatement — but it is not the HUD draw
itself: `ViewerGame.Hud.cs:146` only ever draws the *dude's* equipped weapon, and additionally requires
`weaponItem is not null && bar.Numbers is {}` before it paints anything. Read the table as "the gate
would pass," not as "the HUD would render":

| Case | pid | capacity | caliber | hudGate | examineGate | examineShotsPrinted |
|---|---|---|---|---|---|---|
| Cattle Prod (capacity melee) | 160 | 20 | 3 | 1 | 1 | 1 |
| 10mm SMG (normal gun) | 9 | 30 | 8 | 1 | 1 | 1 |
| **Solar Scorcher (caliber-0 gun)** | **390** | **6** | **0** | **1** | **0** | **0** |
| capacity-less melee weapon | 5 | 0 | 0 | 0 | 0 | 0 |

The Solar Scorcher case is load-bearing: `hudGate=1` with `examineGate=0`/`examineShotsPrinted=0`
proves the two gates stayed genuinely distinct rather than collapsing into one condition — a
collapsed "just use capacity for both" fix would have wrongly set `examineGate=1` here.

**User-visible subtraction:** re-basing the examine gate on caliber rather than capacity does not
just add rows — it *removes* the shots line from six caliber-0 guns that the old capacity-based gate
had been showing it for: PIDs 161, 162, 261, 390 (Solar Scorcher, tabulated above), 427, and 498. This
is correct — vanilla's `_obj_examine_func` picks message 546 (no shots line) for these, not 526 — but
it is a visible change to six items, not just an addition for the five F34 weapons.

Two follow-ons filed below rather than fixed in passing: the digits-vs-gauge HUD shape, and the
MISC-charges branch. A third gap — two further readout sites that also hide these weapons' charges,
absent for every weapon rather than a regression — is recorded as an addendum to F40 below.

**F39 — Hexwaste's HUD ammo readout is `NUMBERS.FRM` digits; vanilla paints a dithered gauge, not
numbers.** *Effort S-M · changes the HUD for every gun, needs its own decision + visual verification
· found grounding F38 (2026-08-22).* The paint loops at `interface.cc:1985-2007` (inside
`interfaceUpdateAmmoBar`, which itself runs `:1985-2016`, the extra lines being the
`windowRefreshRect` tail) draw a
70px vertical column, one pixel wide, at `x = 463 + gInterfaceBarContentOffset` from `y = 26`
downward: colour 14 for the empty span, then alternating 196/14 for the filled span with the ratio
forced even (`if ((ratio & 1) != 0) ratio -= 1;`). There is no numeric ammo readout anywhere in the
vanilla interface bar. Hexwaste's digits (`ViewerGame.Hud.cs`) date from the original HUD work
(`1a7d27a`, P11-M1/M2) and carry no citation — they were never grounded against this code. Closing it
means replacing the digit draw with the dithered-column paint, which is a visible change to every
weapon's HUD (not just the five F34 weapons), so it needs its own decision before starting and a
visual before/after check, not just a fixture pass — no golden covers the HUD bar's pixels.

**F40 — `_intface_update_ammo_lights`'s `else` branch shows the same gauge for a non-weapon MISC
item held in hand; Hexwaste's HUD ammo slot is weapon-only.** *Effort M · needs more than a gate ·
found grounding F38 (2026-08-22).* The reference's `else` (`interface.cc:1363-1370`, sibling to the
weapon branch F38 fixed) reads `miscItemGetMaxCharges`/`miscItemGetCharges` off the held MISC item and
draws the identical bar. Hexwaste already parses `MiscCharges` (`ProtoDatabase.cs:46`, stamped on
instances per P116), so the data exists, but the HUD slot's draw path only ever looks at the equipped
weapon — there is no branch for "a chargeable MISC item is in the active hand." Closing it needs a
second data path into the HUD slot, not a condition change on the existing one, and depends on F39's
outcome if the gauge shape changes underneath it.

**F40 addendum — two further readouts also hide these weapons' charges; Hexwaste implements neither.**
*Found in F38 pre-merge review (2026-08-23).* Not a five-weapon regression — both are absent for
*every* weapon, so this does not reopen F38 — but they are additional sites a player would expect
charges to show:
- `inventory.cc:3127-3153` — `inventoryRenderSummary`'s `"Ammo: %d/%d %s"` line, gated purely on
  `ammoGetCapacity(item) > 0` (no caliber check, unlike the examine branch F38 fixed). Hexwaste's
  inventory window (`ViewerGame.Panels.cs`, `DrawInventoryWindow` / `DrawItemList`) renders name,
  count, and price and has no summary panel at all — there is nowhere for this line to go yet.
- `proto_instance.cc:487-505` — `_obj_examine_func`'s `ITEM`/`ITEM_TYPE_WEAPON` branch, message 526
  (`"%d/%d %s"`), gated on `ammoGetCaliber(target) != 0`. Reachable when examining an item from
  inventory (`inventory.cc:3687`, `_obj_examine_func(_stack[0], item, ...)`) or from the barter screen
  (`inventory.cc:3962`, the `GAME_MOUSE_ACTION_MENU_ITEM_LOOK` case). Hexwaste's `Examine`
  (`ViewerGame.cs:5961`) has a critter branch only — there is no item-examine path at all, in or out
  of combat.

Closing either needs new UI surface (an inventory summary panel; an item-examine entry point), not a
gate change, so both are scoped as their own future work rather than folded into F38/F40.

**F41 — `AmmoQuantity == -1` is overloaded as an "unhydrated item, refill to capacity" sentinel across
the codebase, which makes any genuinely negative ammo count unrepresentable.** *Effort M · repo-wide
convention change · found closing F31 (2026-08-23).* Six sites treat `-1` this way: `WeaponAmmo`
(`ViewerGame.CombatHost.cs:185` and `:276`), `Map/InventoryWeight.cs:44`, `Map/ItemCost.cs:19` and
`:27`, and `SaveState.cs:170`. F31's two special-cost PIDs exposed the collision directly: vanilla's
floorless ammo subtraction drifts to `-1` from an odd charge count (`item.cc:1423`'s clamp is top-only),
but in Hexwaste that write is read back as the refill sentinel by the very next `WeaponAmmo` call, so
the weapon silently tops back up to full instead of running dry — reintroducing the infinite-charge bug
F34 removed. F31 worked around this locally by clamping the three single-shot spend sites at 0, which
is correct for that call site but does not close the underlying ambiguity: any other current or future
code path that legitimately drives `AmmoQuantity` negative will collide with the same sentinel the same
way. Closing it means giving "unhydrated" its own representation distinct from any legal quantity — for
example, hydrating proto-default ammo counts once at load time instead of lazily on first read, so `-1`
never needs to mean anything special afterward. That touches every site listed above and is a
repo-wide convention change, not a one-item fix, hence its own entry rather than folding into F31.

**F42 — SHIPPED 2026-08-28 (`f0b4fcd`, `57d9fe7`); combat-golden 18/18, endgame-golden 5/5,
opening-golden 13/13, quest-golden 39/39, census-sweep 16/16, encounter-golden 188/188 — 279 fixtures,
ALL PASS, byte-identical, 0 re-recorded.** *Was Effort M · re-record tier (damage-affecting) · found
reviewing F36 (2026-08-23).* `ReduceByArmor`'s post-DT reduction now matches the reference's
subtract-form, `afterThreshold - afterThreshold * resistance / 100` (`CombatMath.cs:100`, ported from
`combat.cc:4606-4610`), replacing the old `afterThreshold * (100 - resistance) / 100`. The two forms
are algebraically equal over the reals and diverge under integer truncation by exactly `+1` (the
subtract-form always the larger) **iff `d*r % 100 != 0`**, where `r` is the clamped *effective*
resistance — `Math.Clamp(dr + ammoDrModifier, 0, 100)` (`CombatMath.cs:93`), so Finesse's +30 and F36's
ammo DR modifier fold in before the rule applies, not the defender's raw DR stat. `f0b4fcd` is the
fix plus five hermetic point tests, and it also **changed one pre-existing assertion** —
`DamageRespectsThresholdAndResistance`'s `dr: 100` case went `Assert.Equal(0, …)` → `Assert.Equal(1,
…)`. That is the branch's one concrete non-hermetic behaviour statement: `dr: 100` clamps to
`CritterState`'s DR cap of 90 (`CritterState.cs:47`), and at `r = 90` every unarmed hit with
`d ∈ 1..10` now deals 1 rather than 0 — a critter that could not be hurt at all can now be killed.
`57d9fe7` is the exhaustive-domain test
(`TheSubtractFormBeatsTheMultiplyFormByOneIffDamageTimesResistanceIsNotAMultipleOf100`,
`CombatMathTests.cs`) proving the `+1` rule over the entire reachable domain, `d ∈ [0,999] × r ∈
[0,100]`, and mutation-verified: reverting `CombatMath.cs:100` to the old multiply-form fails it first
at `d=1 r=1: expected 1 (multiply-form 0), got 0`.

**The closeout spec predicted the fix would move golden fixtures and planned a re-record; measurement
refuted that prediction.** A throwaway stderr probe on `ReduceByArmor`'s changed expression, run across
all six golden suites (combat, endgame, opening, quest, census, encounter — 279 fixtures total),
recorded **123** melee post-armor damage computations. **Zero moved. Every one of the 123 had
`r = 0`** — a single-bucket distribution, even though `d` itself varied (1..16), so the melee damage
path is exercised by the combat and encounter suites (the other four — quest, endgame, opening and
census — never reach it) — its damage-resistance term specifically is not. **The
count of fixtures re-recorded is 0**; there is no re-record commit, and `differs.txt` from the
measurement pass was empty. The derivation above is unaffected by this — Task 1 proved it exhaustively
and independent of any fixture — the fixture set simply never satisfies the rule's `r ≠ 0`
precondition. Confirmed as a real property of the fixtures, not an instrumentation artifact: the
defenders behind two of the probed hits were dumped directly and are genuinely unarmored (arcaves hex
20529 Radscorpion `dt=0 dr=0`; denbus2 hex 11670 Healthy Slave `dt=0 dr=0`).

Because no fixture value moved, there is no fixture-based traced example — only the **hermetic** one:
`d = 7`, `r = 33` gives `4` under the old multiply-form and `5` under the reference's subtract-form
(`CombatMathTests.cs:236`, `MeleeDamageResistanceUsesTheSubtractForm`). Provenance: found by a reviewer
reading the melee and ranged paths side by side during F36 (2026-08-23) — not by a failing fixture,
because both forms were self-consistently wrong in the baseline when the goldens were recorded.

**F42a — The golden suites exercise the melee damage-resistance term zero times, which is the
mechanical reason F42's bug survived into the baseline.** *Effort — measurement, not yet scoped ·
found closing F42 (2026-08-28).* F42's measurement pass instrumented `ReduceByArmor` and counted 123
melee post-armor damage computations across all 279 fixtures in the six golden suites — every single
one at `r = 0`. `d` ranged 1..16 and was varied, so the melee damage path itself is genuinely driven
by the suites; the resistance term specifically never is. That is why re-running the suites at any
point since the bug was introduced could not have caught it, and why re-running them now cannot
confirm the fix beyond "did not regress the `r = 0` case." **This is established for the melee path
only** — `RangedMath.RollDamage`'s equivalent block (`CombatMath.cs:168-186`) was not instrumented by
this measurement, so this entry makes no claim about whether the suites exercise nonzero DR on the
ranged path. Closing it means adding at least one fixture (combat or encounter) where a melee/unarmed
attacker faces a defender with nonzero effective DR, so the term F42 just fixed has golden coverage
going forward.

**F43 — SHIPPED 2026-08-29 (`bfad700`); combat 18/18, encounter 188/188, quest 39/39, endgame 5/5,
opening 13/13, census-sweep 16/16 — 279 fixtures, ALL PASS, byte-identical, 0 re-recorded.** *Was
Effort S · found reviewing F36 (2026-08-23).* `RangedMath.RollDamage` used to compute
`raw * critMultiplier * Math.Max(ammoDamageMultiplier, 1)`; the `Math.Max(_, 1)` clamp is gone
(`CombatMath.cs:160`, `int damage = raw * critMultiplier * ammoDamageMultiplier;`), matching the
reference's unconditional multiply (`combat.cc:4586-4587`) and Hexwaste's own melee path
(`CombatMath.cs:44`, `:59`), which never clamped it. The divisor guard is unchanged and stays correct
as a guard, not a clamp (`damage /= Math.Max(ammoDamageDivisor, 1);`, `CombatMath.cs:161`), mirroring
the reference's `if (damageDivisor != 0)` (`combat.cc:4594-4598`).

**The entry's "unverified whether any shipped ammo proto carries a multiplier of 0" is now measured,
replacing the earlier unverified claim.** `AmmoProtoCensusTests.NoShippedAmmoProtoHasADamageMultiplierOfZero`
(`tests/Hexwaste.Formats.Tests/AmmoProtoCensusTests.cs`) walks `items.lst` via `ProtoDatabase` on real
game data and found **25 ammo protos, zero with a damage multiplier of 0, zero with a divisor of
0** (`AMMO CENSUS: 25 ammo protos; multiplier==0: 0; divisor==0: 0`). The fix was therefore inert on
shipped data, as predicted, and this test is the standing guard against a future ammo addition (or a
`.pro` edit) reintroducing a live multiplier-0 case: it fails loudly (`Assert.True(zeroMultiplier.Count
== 0, …)`) rather than silently reverting to the old full-damage behaviour. All six golden suites —
combat, encounter, quest, endgame, opening, census-sweep, 279 fixtures total — passed with zero
fixtures differing and nothing re-recorded, consistent with the census result: the change can only
matter for ammo that does not exist on shipped data.

**F44 — `ReduceByArmor` and `RangedMath.RollDamage`'s DT/DR block now perform identical arithmetic in
identical shape and should be unified.** *Effort S-M · refactor, own risk · found closing F42
(2026-08-28), deliberately held out of it.* `ReduceByArmor` (`CombatMath.cs:66-100`) and the DT/DR/
resistance tail of `RangedMath.RollDamage` (`CombatMath.cs:168-186`, whole method `:147-186`) both now
read `dt`/`dr` off the target, apply the same bypass-armor 20% cut, the same Finesse `extraDr` addend,
the same Penetrate 20% DT cut, and the same clamp-then-subtract-form resistance reduction — the
divergence F42 closed was exactly this last step disagreeing between the two copies. They are two
independent implementations of the same sequence rather than one shared helper. F42 deliberately did
not merge them: the fix touched only `ReduceByArmor`'s final line, so any fixture delta stayed
attributable to that one expression rather than to a refactor's own reshuffling. Merging them is real
work with its own risk (a shared helper needs a signature covering both callers' existing parameters —
`bypassArmor`, `extraDr`, `penetrate`, `ammoDrModifier`, plus `RollDamage`'s own pre-DT difficulty-
modifier step — without silently changing either call site's behavior) and needs its own
measurement pass the way F42 got one. Filed as its own entry rather than left as a remark inside F42's
now-shipped writeup, because that is exactly how F13 was lost for a release cycle.

### Tier F small-batch follow-ups (2026-08-29)

Found while shipping F4/F5/F6/F7/F9/F43 (`feat/tier-f-small-batch`) but deliberately not folded into
those entries' writeups — see F13's history for what happens to a finding left as a remark inside a
shipped entry instead of filed on its own.

**F45 — `AutomapColor` assigns a colour to object classes the reference's `AUTOMAP_IN_GAME` branch
never draws.** *Effort S · found reviewing F7 (2026-08-29), pre-existing, not introduced by F7.*
`AutomapColor` (`ViewerGame.Panels.cs:980`) draws items (`ObjectType.Item`), non-scanner critters
(`ObjectType.Critter`), and misc objects (`ObjectType.Misc`) unconditionally, alongside walls and
scenery. The reference's in-game automap branch (`automap.cc` `AUTOMAP_IN_GAME`) assigns a colour only
to a wall, the dude, a scanner-visible critter, and one special PID — everything else falls through
to `_colorTable[0]` (black) and is never drawn. Closing it means narrowing `AutomapColor`'s switch to
that same set, which will change what appears on the automap for any map with items/misc objects on
seen tiles. **Two more special-case rules the reference's `AUTOMAP_IN_GAME` branch applies before its
own type test are also missing** (`automap.cc:534-540`): an object carrying `PROTO_ID_0x2000031` gets
`_colorTable[32328]` unconditionally, ahead of the wall-type check, so a non-wall object with that PID
would still get the wall-adjacent colour; and scenery carrying `PROTO_ID_0x2000158` is excluded from
the scenery colour (`objectType == OBJ_TYPE_SCENERY && pid != PROTO_ID_0x2000158`) and falls through
to `_colorTable[0]` (skipped) instead. `AutomapColor` (`ViewerGame.Panels.cs:980`) is purely
type-based and has no PID rules at all, so both objects currently render by their raw type rather than
their special case. F45 as shipped covers the item/critter/misc half of this divergence; these two
special PIDs are the other half and remain open.

**F46 — SHIPPED 2026-08-29. Fidget selection ported; F5's sway is now reachable.** *Was Effort M ·
found shipping F5 (2026-08-29).* `_gdSetupFidget` (`reference/fallout2-ce/src/game_dialog.cc`) is a
count-gated weighted roll — folding in `_dialogue_seconds_since_last_input` — that picks which of a
head's 1/2/3 fidget variants plays. Hexwaste hardcodes the fidget nibble to 1 everywhere a head FID is
built (`HeadTexture` and `HeadAccumulatedHotX`, both `weaponCode: 1` — line numbers as of `8bcd551`,
before this entry shipped). Porting the roll so the fidget nibble stops being a constant is what makes F5's
already-correct sway arithmetic observable: all 5 heads with a nonzero frame X offset
(`HRLD2BF3`, `HRLD2GF2`, `HRLD2NF3`, `TNDI2GF2`, `TNDI2NF3`) are fidgets 2/3, never fidget 1.

**Shipped, in four parts:**
- `HeadFidget` (`src/Hexwaste.Formats/Art/HeadFidget.cs`) — the roll itself
  (`_gdSetupFidget`, `game_dialog.cc:2505-2529`): 1 variant → 1; 2 → split at 68; 3 → split at 52/77;
  `chance = randomBetween(1,100) + secondsSinceLastInput / 2`. Two quirks reproduced deliberately
  rather than regularised: the idle term is integer-halved (so it only starts biasing after two
  seconds), and the reset of the idle accumulator lives inside the **3-variant case alone**
  (`:2520`) — a 1- or 2-variant head keeps accumulating across rolls.
- `ArtIndex.HeadFidgetCount` — `artGetFidgetCount` (`art.cc:365-388`) over the `heads.lst` counts.
  **The reference's parse is itself defective and is reproduced as such:** after `*sep1 = '\0'` the
  next `strchr(sep1, ',')` starts *on* that NUL, so `sep2`/`sep3` collapse back onto `sep1` and all
  three emotion counts parse the same first number (`art.cc:286-316`). Provably unobservable — all
  12 comma-bearing `heads.lst` lines carry equal triples (ten `3,3,3`, two `2,2,2`), pinned by
  `HeadFidgetCountsMatchTheShippedList`. Head 0 (`reser`) has no comma and yields 0, exactly as the
  reference's `atoi("eser")` does.
- The idle-fidget loop in `ViewerGame.TickHeadFidget` — a fidget plays out, parks the head on frame 0,
  waits `_tocksWaiting` (`1000 * (randomBetween(0,3) + 4)`, i.e. 4-7 s), then a NEW fidget is rolled
  (`gameDialogTicker`, `:2861-2875`). A finished voiced line seeds the accumulator at **3**, not 0
  (`:2853-2857`). Before this, our head played its fidget once and froze — there was no re-roll at all.
- `HeadAccumulatedHotX` now takes the FID `HeadTexture` resolved instead of rebuilding its own, which
  closes the review finding that the two would desynchronise the moment the nibble became a variable.

**One deliberate divergence:** the reference rolls from the shared global RNG (`randomBetween`);
Hexwaste gives the head its own `_fidgetRng`, following the per-subsystem pattern already used by
`_sneakRng`/`_stealRng`/`_tauntRng`. A purely cosmetic dialog animation must not perturb the gameplay
RNG stream that 279 golden fixtures pin.

**Verification, all three re-runnable via the new `--fidget-probe <headId> <rolls>` harness:**
- *The roll:* 1000 rolls on a 3-variant head give 499/260/241 against the thresholds' expected
  51/25/24.
- *The loop:* driven over 60 s of simulated time, the head changes fidget **6 times** at 7.3 s /
  5.0 s / 13.5 s / 8.0 s / 11.5 s intervals, cycling through variants 1, 2 and 3 — consistent with an
  8-frame fidget at ~8 fps plus the 4-7 s pause. Before F46 the head played one fidget and froze.
- *On screen:* the same Harold dialog under two RNG seeds now renders visibly different fidget art;
  before F46 every seed produced byte-identical output.

Note the probe needs its own driver (`StepHeadFidgetForProbe`): the ticker lives in `Update`, and
`--pump-ms` pumps subsystems directly without ever calling it, so the existing pump cannot reach the
fidget loop.

**A shipped-data gap this newly reaches, found by auditing the art before trusting the feature.**
Loading fidget 2/3 art is new — before F46 only fidget 1 was ever built, and fidget 1 exists for
every head. Enumerating every declared variant against `master.dat` turns up exactly **one hole**:
head 11 `bosss` declares 2 fidgets and ships `bosssnf2`/`bosssbf2` but **not `bosssgf2`** — the GOOD
family's second fidget has no art. Vanilla hits the same hole (its own `artGetFidgetCount` returns 2
for the good family too) and renders *nothing*: `artLock` fails and `_gdSetupFidget` only
`debugPrint`s. Hexwaste degrades better — `DrawTalkingHead`'s missing-family fallback drops to the
neutral family at the same fidget, `bosssnf2`, so the head still renders, in the wrong emotion.
Recorded rather than "fixed": matching vanilla here would mean rendering no head at all.

**F47 — The monitor's continuation-line wrap budget is narrower than vanilla for every line after the
first.** *Effort S · found shipping F6 (2026-08-29).* The reference re-widens the available width
after a message's first line — the knob's width is subtracted from the budget only for the line that
actually carries the knob glyph (`display_monitor.cc:266-272`, the `knobWidth = 0` arm on later
lines). `AafFontRenderer.WrapText` (`src/Hexwaste.Viewer/AafFontRenderer.cs`) takes one width for the
whole string, so the call site (`ViewerGame.Hud.cs:213-214`,
`MonitorLayout.WrapBudget(_fontRenderer.LineHeight, knobWidth)`) passes the knob-reduced budget once
and it applies to every wrapped line, not just the first. Bounded: the difference is exactly one
character's width (the knob glyph), so at most one extra wrap point per message, in the worst case.
Closing it means a `WrapText` overload taking a distinct first-line width, then re-measuring
continuation lines against the full budget. **A second, pre-existing divergence in the same
comparison, not introduced by this branch:** `WrapText` breaks only when `MeasureWidth(candidate) >
maxWidth` (`AafFontRenderer.cs:70`), i.e. a candidate whose width exactly equals the budget is
accepted onto the current line; the reference's own accumulation loop requires strictly `<`
(`display_monitor.cc:262`, `while (fontGetStringWidth(str) < DISPLAY_MONITOR_WIDTH - _max_disp -
knobWidth)`), so an exact-width string stops the reference's loop but not `WrapText`'s. At most a
one-pixel effect at one wrap boundary per message. The fork-fix ledger's PR #675 hunk 17 row
(`docs/research-notes/fork-fix-ledger-2026-08.md`) verdicts this comparison `not-a-gap`, reasoning that
`WrapText` is "already the fork's post-fix semantics, with no `<` to flip" — true against the fork's
*fixed* `<=`, but that is not the standard: `e97087b`'s own (unfixed) loop uses strict `<`, and
`WrapText`'s `>` accepts the exact-width case `e97087b` would reject, so the row's own evidence shows a
real, if tiny, divergence from the authoritative tree it was checked against. Corrected there rather
than left overstated; recorded here since F47 already owns the wrap-budget follow-ups.

**F48 — `anim(obj, 1010, frame)` stores an out-of-range frame the reference refuses.** *Effort S ·
found shipping F9 (2026-08-28), harmless today.* The reference's `objectSetFrame` rejects a frame at
or beyond the FRM's frame count and leaves the object's stored frame unchanged. `ApplyDirectAnim`
(`ScriptHost.cs:103`) stores any value into `obj.Frame` unconditionally and relies on the renderer
clamping it at draw time (`ViewerGame.Rendering.cs:275`). Harmless with today's single reader — the
renderer always clamps before indexing — but `MapObject.Frame` can now hold a value vanilla would
never have let it hold, a trap for any future second reader (a save-state dump, a debug overlay, a
different renderer) that reads `Frame` without doing its own clamp. **`Frame` is also not persisted
across save/load, unlike `Rotation` — the other half of the same `anim` opcode's new mutability.**
`CaptureMapDelta` snapshots a moved object's tile, elevation and `Rotation` into
`SaveState.MovedObject` (`ViewerGame.SaveLoad.cs:86-89`; the record itself,
`src/Hexwaste.Formats/SaveState.cs:187`, has no `Frame` field), and `ApplyDeltaBeforeScripts` restores
only those three onto the live object (`ViewerGame.SaveLoad.cs:149-150`). Before F9, `Frame` was
init-only, so its absence from the save format was not a gap — nothing could change it. Now a script
calling `anim(obj, 1010, N)` to park scenery on a specific frame has that state silently reverted by a
save/load round-trip or a map revisit, while vanilla's `objectSetFrame` write is durable. The two
halves of one opcode (F9) now behave asymmetrically: `Rotation` survives a round-trip, `Frame` does
not.

**Minor findings, raised in review and judged Minor — not fixed, recorded so they are not
rediscovered:**
- `AmmoProtoCensusTests`'s loop bound of 1000 (`AmmoProtoCensusTests.cs`, `for (int index = 1; index
  <= 1000; index++)`) has no comment tying it to the real `items.lst` size (531 lines, measured via
  `wc -l`); a future items list past 1000 entries would truncate the census silently instead of
  failing loudly.
- `DrawAutomap` (`ViewerGame.Panels.cs:1040`) allocates a new `Dictionary<int, AutomapMark>` every
  frame while the automap window is open, rather than reusing one across frames.
- The hermetic test named `TheDudeMarkOverpaintsAWall` actually exercises the `AutomapMark.Other`
  rule (an unclassified object mark over a wall), not the dude — the real dude marker is drawn in its
  own separate pass and never goes through `AutomapPaint.Overpaints` at all.
- ~~`HeadAccumulatedHotX` re-derives the head FID with its own hardcoded `weaponCode: 1` instead of
  receiving the FID `HeadTexture` already resolved for the same frame — when F46 ports fidget
  selection, changing the nibble in only one of the two call sites would silently desynchronise the
  sway from the drawn head.~~ **CLOSED 2026-08-29 with F46**, which is exactly the change this
  predicted: `HeadTexture` now reports its resolved FID through an `out` parameter and
  `HeadAccumulatedHotX` consumes it, so the two cannot diverge.
- `HeadTexture` (`ViewerGame.cs:6354`) still throws and catches an exception per render frame for
  genuinely missing head art — the per-frame exception cost that was deliberately removed from its
  sibling helper (`HeadAccumulatedHotX`'s `TryGetFrm` path) was not also removed here.

**F49 — SHIPPED 2026-08-29. Non-ASCII characters were truncated into arbitrary glyphs.**
*Was undiscovered · found by the visual HUD verification F6 never had.* A screenshot of the message
monitor showed `Active hand: right [box] 10mm SMG.` — an em-dash rendering as a meaningless glyph.
AAF fonts are **byte-indexed** (256 glyph records) and the engine's own text is single-byte, so the
reference never meets a character above U+00FF and there is nothing here to port. C# strings are
UTF-16, though, and both the draw path (`AafFontRenderer.DrawGlyphs`) and the measure path
(`AafFont.CharWidth`) indexed with a plain `(byte)` cast, which **truncates**: U+2014 becomes `0x14`,
a control slot holding whatever the font has there. Because both paths truncated *identically*,
wrapping stayed self-consistent and only the glyph shown was wrong — which is precisely why no test
and no golden could have caught it, and why it took a screenshot. Fixed in two deliberately separate
layers: the seven monitor-bound messages that used a non-ASCII dash or arrow now use ASCII (the
actual visible fix — the game's own font has no such glyph and never could), and `AafFont.GlyphIndex`
maps anything above U+00FF to `'?'` with both paths routed through it, marked in its doc comment as a
**Hexwaste-side robustness guard with no reference counterpart**. Re-screenshotting the same scene
confirms the dash now renders and the line re-wraps accordingly. Five hermetic tests pin the mapping.
**Worth generalising:** no golden fixture contains a single non-ASCII byte, so this class of defect is
invisible to the entire suite by construction.

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

