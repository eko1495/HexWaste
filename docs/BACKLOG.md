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

**A2 — Combat-AI fidelity residuals (the only cluster that still shifts combat outcomes).**
*Effort M (golden re-records) · medium-high impact.* Core AI is faithful; documented residuals:
- `_combat_safety` (`combat.cc:2249`) wires only gun-hold-when-teammate-in-LoF; ally-in-LoF
  weapon-switch invalidation + snipe-back is partial (`CombatEngine.cs:2375-2380`).
- `_combatai_rating` not ported → whoHitMe retaliation is "last-hitter-wins" vs rating-based.
- NPC combat-drug (Jet/Psycho) use has **no timed wear-off** — bonus cleared at combat end
  (`Combat/AiCombatDrug.cs`).
- Perception-based **disengage** (`_combatai_want_to_stop`, combat_ai.cc site 3) deferred (P113 4.3).
- `_ai_best_weapon` score omits the weapon-perk ×2 and explosive ×(extras+1) factors.

**A3 — Per-ally whoHitMe tracker (party tactics).** *Effort S-M · low-medium.* Companion
"Attack whoever's attacking me" degrades to Closest (`Combat/CompanionAi.cs`) — no per-ally
danger-source tracker. The 7 party-control *settings* + ally AI are otherwise real (P50-P52).

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
- Ghost perk's light-gated Sneak bonus is unwired (needs a per-object `objectGetLightIntensity`;
  the tile light grid exists, the per-object query doesn't — `Perks/PerkRules.cs`).
- Cross-script **imported-procedure calls throw** (`IntVm.cs:1105`) — a latent crash if any bound
  script calls an imported proc; worth confirming 0 vanilla scripts hit it (likely, but unverified).
- Unwired externals silently return 0 (`IntVm.cs:2094`) — safe for vanilla (0/155 maps), but the
  fallback masks any future gap; a `--strict` mode that logs would be a cheap guardrail.
- Area-explosion damage is radius+LoS, not the engine ring-spiral (`CombatEngine.cs:1543`).

## Tier B — quest-driver + campaign-QA frontier (the dominant remaining WORK)

Per the project's own analysis, the bulk of "finishing the game" is per-quest playtest/QA, not
engine code. 25 quest goldens locked; census baseline: 56 quest-bearing maps, batch stuck=183.

**B1 — Deep sub-menu routing. Biggest single lever.** *Effort L.* The greedy dialogue router
only picks the nearest on-route option; it can't descend multi-level menus
(building→center→NPC→negotiate→tier, e.g. Barkus 459). This is the dominant New Reno failure mode
(~76 of the 183 stuck runs) + SF/NCR negotiation menus. Converts newr2 (11/11 stuck), sfchina
(5/5) from stuck to driveable.

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

A1 was investigated first and **closed as faithful-not-a-gap** (see above) — a useful reminder
that "karma is cosmetic" is a content-slice symptom, not an engine defect. With A1 out, the two
coherent directions are:

- **"Make the engine's own systems complete"** → **A4 (poison/radiation counters)** is now the
  strongest genuinely-unbuilt, self-contained engine gap with a clear fo2ce reference
  (`queue.cc` poison/radiation events + `critter.cc` rad thresholds). Then **A2 (combat-AI
  residuals)** for combat feel (golden-heavy). A5's imported-proc-call throw is worth a quick
  audit (latent crash if any vanilla script hits it).
- **"Finish the campaign / land more quests"** → **B1 (deep sub-menu routing)** unlocks the most
  quests (~76 New Reno + SF/NCR), then B2 (cross-map bit-prereq) generalizes the P137 win.

Both A4 and B1 are real and independent; A4 is self-contained (one subsystem, clear reference),
B1 is more open-ended (router search redesign) but higher user-facing value.
