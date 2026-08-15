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
  already ported (`WantsToStopFighting`) — the piece still deferred is `PruneEscapedHostiles`,
  which needs the golden-moving `_ai_danger_source`.
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
  partial, `FriendlyOnFireLine`, `CombatEngine.cs:2382-2390`); `_ai_danger_source` + perception-based
  `PruneEscapedHostiles`. Rating-gated retaliation left this tier when its branch merged
  (2026-08-15) — it was the one item here that did move a fixture, and `brawl-watch` was
  deliberately re-recorded for it.
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
(the only fixture that moved). NOT fully closed — see F21.** *Was Effort M · re-record tier.*
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

**Not fully closed.** The re-recorded fixture still contains one phantom flee — `flee: Healthy
Slave@10270 -> 8870`, logged in rounds 3 and 4 with the critter at tile 10270 both times, lines
byte-identical before and after this fix (8870 is not blocked, so the new destination check
correctly leaves that pair alone; a different bug is responsible). See F21.

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

**F20 — The other `Pathfinder.FindPath` call sites are unaudited against their reference `a5`
counterparts.** *Effort S per site (audit) · re-record tier for any that flip.* F18 taught Hexwaste's
`FindPath` only `a5 = 0` until now; the reference passes `a5 = 1` at other call sites too —
`_ai_move_away` (`combat_ai.cc:1238-1239`, `_make_path(a1, a1->tile, destination, nullptr, 1)`) is
the known next case, feeding `_combat_ai`'s tail (see F19) and reachable independently of it. The
other Hexwaste call sites have never been checked against their reference counterparts, each still
passing the `a5 = 0` default: `CombatEngine.cs:3022` (enemy approach) and `:3266` (ally move);
`DudeController.cs:62`, `:83`, `:161` (dude walk/repath); `ViewerGame.cs:5236` (worldmap start-point
reachability probe). Auditing means finding and citing each site's reference counterpart and its `a5`
value in `animation.cc`/`combat_ai.cc`, not assuming `0` is correct by default. Changing any site
found to need `a5 = 1` is re-record tier — it moves movement transcripts, per the F18 precedent.

**F21 — LIVE BUG, most consequential finding of the F1/F18 sub-project: a stale `_npcWalkers` entry
freezes a critter while its `flee:` log keeps firing — and the golden fixtures have been recording a
harness artefact as game behaviour.** *Effort M · re-record tier once fixed.* Surfaced reviewing F18:
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
- **Not purely a harness artefact.** The prune sits *after* `if (DisableAmbientLife || _worldmapOpen)
  return;` (`ViewerGame.cs:3259-3260`) inside `UpdateAmbientLife` itself, so `--no-ambient` and an open
  worldmap defeat the same prune in the real interactive game — walker lifecycle management is nested
  inside an unrelated cosmetic feature's early return, not solely a test-loop omission.
- The brawl-watch autoplay loop (`ViewerGame.Harness.cs:203-209`) shares the identical
  `walker.Update(...)`-without-prune shape and should be checked for the same defect before this is
  called fixed.
- **Consequence to record plainly:** any golden fixture recorded through the `--fight`/brawl-watch
  autoplay path, wherever an NPC walker finishes mid-fixture, may contain frozen-critter artefacts
  like this one baked in as if they were engine behaviour. Resolve the underlying membership-vs-`Moving`
  bug (and confirm/fix the brawl-watch loop) before those transcripts can be trusted; expect any fix to
  move fixtures, hence re-record tier.

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

The four below were carried as prose (in a ledger row's Notes, or in the harvest's working notes)
rather than as entries, and were promoted here on 2026-08-14 so they survive the merge. All four sit
in the crit-failure / accidental-hit neighbourhood of `CombatEngine`; none is a fork port.

**F11 — `DAM_HIT_SELF` and `DAM_RANDOM_HIT` crit-failure damage is HALF vanilla.** *Effort S ·
**re-record tier**.* `CritFailDamage` (`CombatEngine.cs:1233`) passes `critMultiplier: 1` into
`CombatMath.RollWeaponDamage` / `RollDamage`, whose body is `raw * critMultiplier / 2` — so the
rolled figure is halved before DT/DR. The reference calls `attackComputeDamage(attack, n, 2)`
(`combat.cc:4230` at `e97087b`), and that routine multiplies by `bonusDamageMultiplier` (2) at
`:4586` and divides by 2 at `:4601`, i.e. the pair is **x1**: vanilla applies the *full* rolled
damage. A 5-12 weapon self-hit that should cost 12 costs 6 today. Pre-existing since `f77e37f`,
unrelated to the 2026-08 fork harvest, which only pinned it in a test. **Do not fold this into a
docs pass:** changing the multiplier moves recorded damage numbers, so it needs its own phase with a
diff-reviewed re-record on the P120 precedent. `CombatEngineTests.HitSelfFumbleStillRollsWeaponDamage`
currently asserts `30 - 6` and its comment states the deviation; that assertion is what changes.

**F12 — A missed shot's collateral victim runs a `damage_p_proc` the reference suppresses.**
*Effort S · **re-record tier**.* `ApplyAccidentalHit` (`CombatEngine.cs:729`) calls
`RunDamageProc(acc.Victim, attacker, …)` unconditionally for any scripted non-dude bystander. That
victim is precisely the `defender != oops` case: `_damage_object` consumes the flag as
`if (!flag) run damage_p_proc`, so at `e97087b` — and after the fork's PR #493 inversion, which does
not change this branch's outcome — the collateral victim runs **no** damage proc. Surfaced while
classifying PR #493 (ledger row `#493`) and explicitly excluded from that port. Running an extra
script proc can move fixtures, so treat as a re-record-tier change, not a one-line edit.

**F13 — `DAM_EXPLODE` crit-failure self-damage still runs no `damage_p_proc` — PR #493 is only
PARTIALLY applied.** *Effort S.* The `#493` port wired the self-damage proc into
`ApplyCritFailDamage`, which covers the `DAM_HIT_SELF` branch. The sibling `DAM_EXPLODE` branch
(`CombatEngine.cs:1193`) routes to `Explode(…)` instead, and that path never reaches
`ApplyCritFailDamage`, so a fumbling critter blown up by its own weapon runs no damage proc — where
the reference's `attackComputeDamage(attack, 1, 2)` self-damage feeds the same `_apply_damage` path
that `#493` corrects. Nothing tracked said so before this entry; the ledger's `#493` row describes
the port as covering "the attacker's self-damage call", which is true of the branch it touched and
silently not of this one. Closing it means giving the explode branch the same party-gated
`RunDamageProc(self, self, …)` tail.

**F14 — Our `CRIP_RANDOM` limb draw precedes the self-damage draws; the reference orders self-damage
first.** *Effort S · documentation of a divergence, not a live bug.* `attackComputeCriticalFailure`
resolves `DAM_HIT_SELF` / `DAM_EXPLODE` (`combat.cc:4228-4232` at `e97087b`) **before** the
`_do_random_cripple` call at `:4249`; `HandleCriticalFailure` draws the random limb first
(`CombatEngine.cs:1175`) and the self-damage after. Confirmed **inert on the shipped `_cf_table`**:
`DAM_CRIP_RANDOM` appears exactly once, row 0 column 4 (`combat.cc:1876`), paired with nothing — so
no fumble can ever take both draws and the RNG stream cannot diverge. Recorded so a future edit to
either branch does not re-derive this, and so anyone who *adds* a table entry knows the order is
wrong before they trip over a moved fixture.

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

