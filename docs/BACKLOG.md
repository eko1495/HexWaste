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
- **Deferred mid-batch to the re-record tier:** rating-gated retaliation
  (`_combatai_check_retaliation`). It was implemented, then found to move the `brawl-watch`
  encounter fixture (rounds 11→9, survivors 1→2, winning team 2→1); the project owner ruled it be
  deferred rather than re-record the fixture. `RegisterHit`/whoHitMe remains last-hitter-wins.
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
  `PruneEscapedHostiles`; and the rating-gated retaliation above.
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

