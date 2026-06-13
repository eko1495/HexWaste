# Phase 8 Research Report — The Character Comes Alive

*Researched 2026-06-13 in-repo: four parallel tracks — random encounters (worldmap.txt decoded end-to-end, `saved=No` verified against the engine save path + 3 encounter maps), Vic's rescue + companion management (every gating script disassembled operand-by-operand via a fan-out workflow over pre-built `.dis` listings), combat depth II (ai.txt parsed, crit table rows counted, fresh bench), and progression/ops (skill-point + creation formulas read from the editor, two shipped-build bugs found and **independently confirmed in this session**). Full track notes: `docs/research-notes/p8-track-{a,b,c,d}-*.md`. All engine claims carry fallout2-ce file:line; unverified items flagged.*

## TL;DR

- **Recommended path: "The Character Comes Alive" — fix the two shipped progression bugs, close the missing skill-growth loop, add character creation + rest-to-heal + ammo restock.** This is the only candidate direction that fixes *live defects* in v0.7.0 **and** closes the single loop that turns our mechanics into a game. Every milestone is small-to-medium and headless-testable; bench has ~13 ms of headroom (denbus2 2.90 ms avg).
- **Two real bugs in the shipped build, both confirmed here, not just reported:** (1) `CritterState` adds `proto.Skills[n]` with **no tag bonus** — tagged skills lose the engine's +20 and double-rate (skill.cc:251-256), understating guns/lockpick/traps/barter for every premade; (2) `SpawnDude` hardcodes `hmjmps` (male art) and `HumanDeath` always passes `female:false`, so the **female DIPLOMAT premade (Chitsa, gender byte=1) renders and screams male**. Both are corrected in M0.
- **The missing game loop:** skills now gate six systems but can never change. The engine grants `5 + 2×IN` skill points per level (character_editor.cc:5686) spent past a cost ramp (skill.cc:355-371). Wiring that — with a level-up allocator and a character sheet to make it legible — is what converts combat/lockpick/barter from a demo into a game.
- **A debunked bug saved us a wrong fix:** Track B's first pass claimed a dialog-end bug (choosing any option ends the conversation). The verification agent proved it false — `SessionEnded` is set only by `end_dialogue` (0x80DF), never by `gsay_end` (0x811D), and multi-round real-game dialog tests pass. **Do not touch `ResetDialogRound`/`Choose`.** Recorded so it isn't "fixed" later.
- **The other three directions are strong but deferred:** encounters (Track A) is fully designed and phase-sized but adds *surface* to a hollow core; combat depth II (Track C) is the deepest engineering and rightly wants the CombatEngine extraction done *first* — a phase-9 opener; Vic's rescue (Track B) is charming and zero-new-externals on the cash path but narrow. Their cheap, broadly-useful pieces (the `OnStubbedExternal` audit, `metarule` rule 16) are noted as low-cost wins.
- **Save format stays additive-V2** across every track: the custom dude sheet + spent skill points are new optional JSON props; old saves fall back to the named premade. No V3 needed.

## Key findings by track

### A. Random encounters (full design, deferred to phase 9)
- `saved=No` (57 of ~150 maps) is verified engine behavior: skip-and-delete the .SAV (map.cc:1456/1074), never mark visited (worldmap.cc:2866). For us: **skip the VisitedMaps delta entirely**, but still run `map_enter` (encounter maps carry map scripts; LVARs are vacuously 0).
- **Ambush hostility is free:** the parsed AMBUSH/FIGHTING keyword is *never read* by the engine — encounter hostility comes from the spawned critters' own `critter_p_proc` (obj_can_see_obj → attack), which our heartbeat already runs. Only group-vs-group FIGHTING has real auto-combat code (skippable v1).
- Composition grammar fully decoded (weighted entries, conditions over Global/PlayerLevel/Rand/time_of_day, `Script:N` = scripts.lst N−1, formation keywords, `ratio:` absent ⇒ exactly-one leaders). Exit grids with `Map=-2` ⇒ return to worldmap at preserved position.
- Plan: WorldmapFile parser (S) → traveling-dot worldmap (M) → roll+pick (M) → transient LoadMap + spawn via our `AllocateSid` path (M) → return/resume (S) → polish (S). Additive-V2 (worldmap pos + one-shot counters). **A clean, self-contained phase — just not the one that fixes the core.**

### B. Vic's rescue + companions (deferred; cheap pieces noted)
- **Cash rescue path needs ZERO new externals.** Metzger Node024/025 (`item_caps_adjust(-1000)`, set GVAR457=2/GVAR100=2, `GVAR445 |= 0x8000000`) → dcVic reads that bit → Node994 `party_add` + team-0; follow is 100% script-side `critter_p_proc` (matches our p7-m4). Every gate runs on machinery shipped in p5-p7.
- The only genuinely-needed engine addition is **`metarule` rule 16 PARTY_COUNT** (refuse-when-full; default-0 still offers the join) — an S, and broadly useful. The radio flavor leg wants 3 inventory-query externals (`obj_carrying_pid_obj`/`obj_is_carrying_obj`/`rm_obj_from_inven`, S-M), optional to the rescue.
- Companion management: dismiss/rejoin reuse existing externals (S); trade is engine-side flat-1:1 (`partyMemberControlWindow`, **not** our priced barter box) → a follower-inventory swap panel (M).
- **The dialog-end "bug" is not real** (verified). The real Q2a follow-up is an `OnStubbedExternal` audit, not dialog-round surgery.

### C. Combat depth II (deferred to phase 9; extract first)
- Bench green: denbus2 400 frames → avg 2.90 / p95 5.32 / max 9.47 ms. No frame-budget concern.
- **Extract the turn machine to Formats FIRST** (behind an `ICombatPresenter`, ~5-method surface, M) — every depth feature is roll-heavy orchestration that needs seeded deterministic tests; building features in the viewer first makes extraction strictly harder.
- Then **ai.txt packets** (M, highest felt-depth/line): honor `min_to_hit` (walk-until-hittable, flee if unhittable), `min_hp` flee threshold, distance prefs. Our 4 maps use only 12 of 187 packets. Then knockdown (S; real get-up anims 36/37 exist), a **minimal crit cut** (the full table is 1080 rows — honor only damage-mult/KNOCKED_DOWN/DEAD/BYPASS), throwing (S/M; one straight-line projectile animator mode unlocks the abundant rocks/spears) and dynamite/metarule-49 for the temple-door beat (M).
- **Defer burst** — zero burst-capable weapons in our entire map slice's raw data (10mm SMG is pid 9, absent; it'd be a player-bought toy only).

### D. Progression / ops (RECOMMENDED — the spine of this phase)
- **Skill points:** `5 + 2×IN` per level, banked cap 99 (character_editor.cc:5686); spend cost ramps 1/2/3/4/5/6 at ≤100/101/126/151/176/201, hard cap 300 (skill.cc:355-371). Tag = +20 once **and** double-rate, applied in skillGetValue — so cost is computed on the *effective (doubled)* value.
- **Creation:** 5 free points over base-5 SPECIAL (1-10), 3 tag skills, derived recompute (HP=ST+2EN+15, AP=AG/2+5, AC=AG, MeleeDmg=max(ST−5,1), Seq=2PE, Crit=LK, HealRate=max(EN/3,1) — stat.cc:554-579). We never write .gcd (all in-memory) — keep that invariant.
- **Hour-2-3 pain, ranked:** (1) no rest-to-heal → attrition death-spiral (S; engine heals HEALING_RATE per 3 game-hours, our GameClock already advances hours); (2) empty level-ups + no character sheet (M = the allocator); (3) ammo economy dead-end — merchants never restock since the p5 snapshot model (S-M).
- **Ops** is genuinely tiny and non-blocking: a ~15-line CI workflow (the test split already self-skips data tests), issue templates, a public SCOPE.md. Perf canary stays local (CI runners have no GPU and no game data). Ecosystem quiet (MonoGame 3.8.5 still preview, .NET 10.0.9, SUL unchanged).

## Comparison table

| Direction | Effort | Payoff | Risk | Fun | Verdict |
|---|---|---|---|---|---|
| **Progression: bug fixes + skill growth + creation + rest + ammo** (D) | M (phase) | **Very high** — fixes 2 shipped bugs, closes the core loop | Low | High | **RECOMMENDED — phase 8** |
| Combat depth II: extract → ai.txt → knockdown → minimal crits → throwing (C) | L | High (felt combat) | Med (extraction) | High | **Phase 9** (extract-first) |
| Random encounters (A) | M (phase) | High (content surface) | Low | High | Phase 9/10 |
| Vic's rescue + companion mgmt (B) | M (phase) | Med (one quest + companion lifecycle) | Low | High | Phase 10 / fold pieces in |
| Ops only (D Q4) | S | Low-med (hygiene) | None | Low | **Folded into M0** |

## Recommended roadmap — "The Character Comes Alive"

**M0 — Clear the decks (S).** Fix the two confirmed bugs: make `CritterState` tag-aware (the dude's gcd `TaggedSkills` add +20 and double the spent points in skill values — skill.cc:251-256), and pick `hfjmps` art + female death scream when the dude's gcd gender byte (baseStats[34]) is 1 (also pass NPC gender from the art-name char to `HumanDeath`). Ship ops: `.github/workflows/ci.yml` (build + the self-skipping test split), issue templates, and a public `SCOPE.md` distilled from CLAUDE.md. *Headless: a tagged premade's Small Guns skill now matches the engine; the DIPLOMAT premade renders female; `dotnet test` (no FALLOUT2_DIR) stays green in CI.*

**M1 — Skills grow (M).** Per-level skill-point award (`5 + 2×IN`, banked cap 99); a level-up text allocator over the ~6-8 skills we actually gate (cost ramp on effective value, cap 300); spent-point deltas persist in the JSON save (additive V2). The allocator reuses the menu list-widget. *Headless: `--level-up`-style transcript shows points awarded, a spend raising Small Guns past a gate, and the cost ramp at 101; save/load round-trips the spend.*

**M2 — The character sheet (S).** A read-only panel (C key) showing SPECIAL, derived stats, the gated skills with tag marks, level/XP/next-level. Reuses the loot/examine panel rendering; makes M1 (and M3) legible. *Demo: open the sheet on a leveled character — the numbers the systems actually read are finally visible.*

**M3 — Rest to heal (S).** A rest key: refuse if hostiles are within sight, else advance the GameClock by `ceil(hpNeeded / HEALING_RATE) × 3` hours and restore HP (HEALING_RATE = max(EN/3,1)). Kills the attrition death-spiral. *Headless: wounded dude rests, clock advances, HP returns to max; resting is refused next to a hostile.*

**M4 — Character creation (M).** A SPECIAL point-buy screen (5 free points over base-5, clamp 1-10, live derived-stat readout), a tag-3 picker over the gated skills, and a gender toggle — building an in-memory `GcdFile` (no .gcd write). New Game routes "Create character" alongside the premade picker; the custom sheet serializes into the save. Reuses the M2 stat/skill row widget. *Demo: roll a custom high-AG gunslinger, start in artemple, the to-hit reflects the build; save/load preserves the custom sheet.* **Pivot:** if creation balloons past M, ship gender-pick-on-the-premade-picker only (still fixes the bug) and defer point-buy to phase 9.

**M5 — Ammo economy (S-M).** Per-merchant restock-after-N-game-days: on map enter, if the stored container snapshot is older than the threshold, refresh that container from pristine map data. Closes the small-guns dead-end without reopening the restock-on-every-revisit quirk. *Headless: drain Tubby's stock, wait N days, return — restocked; return before N days — unchanged.*

## Pivot thresholds
- **M1:** if the allocator UI fights the menu system after a session, ship **auto-spend on tagged skills** (Track D option a) as the floor — numbers still grow; agency returns later. Still fixes the "leveling does nothing" perception with HP+skill growth.
- **M4:** gender-pick-only fallback above; SPECIAL point-buy is the cuttable part, the gender toggle is the bug fix and stays.
- **M5:** independent and last — cut it if the phase runs long; the ammo dead-end is the least acute of the three hour-2 pains.
- **M0 ops** is non-blocking — if CI setup snags on runner specifics, ship the bug fixes and land the YAML separately.

## Cross-cutting
- **Save format: additive V2.** New optional props — `int[] SkillPointsSpent` (or a per-skill dict), `int UnspentSkillPoints`, and a serialized custom dude stat block (SPECIAL + tags + gender) used only when the character was created rather than picked. Old saves lack them → fall back to the named premade and zero spent points. **Verify the in-memory `GcdFile` round-trips through JSON** (it's currently load-only).
- **Skill cost is computed on the EFFECTIVE (tag-doubled) value** — get the order right (apply tag double-rate, then ramp), or tagged skills cost too little.
- **Bench is green** — no perf work needed; the per-vertex floor pass and heartbeat leave ~13 ms of headroom.
- **The dialog-end bug is NOT real** — leave `ResetDialogRound`/`Choose` alone; any dialog misbehavior is a missing external/metarule surfaced via `OnStubbedExternal`.
- **Phase-9 setup:** when combat depth II (Track C) becomes the chosen phase, do the **CombatEngine extraction first** — it's the regression net for crits/ai/burst and gets strictly harder with each viewer-side combat feature added meanwhile.

## Caveats / unverified
- The custom-dude save round-trip is new ground (`GcdFile` has only ever been deserialized from game data) — prove it with a round-trip test in M4.
- Derived-stat formulas (HP/AP/AC/MeleeDmg/Seq/Crit/HealRate) are cited from stat.cc:554-579 but not yet unit-tested against in-game premade values — add a fact test in M1/M4 comparing a parsed premade's derived stats to the formula.
- The ammo-restock threshold (N days) and which containers count as "merchant" vs "world loot" is a design choice, not an engine port — the engine's per-proto restock timers are the model but we deliberately cleared that timer class; M5 is an honest approximation, document it like the GameClock day/night curve.
- Tag-bonus magnitude: a gcd's `proto.Skills[]` already include the editor-allocated points; M0 adds the +20 and the *second* count of those points — verify against a known premade (e.g. "combat"/Narg's tagged Small Guns) so we don't double-apply points the gcd already baked in.
