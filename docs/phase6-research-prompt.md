# Research Prompt — Hexwaste Phase 6: From Engine Demo to Game Loop

> Paste everything below this line into Claude Desktop (enable web search).
> Phases 2–5 were researched with the empirical method (disassembling real
> game scripts, parsing real protos/maps byte-by-byte, measuring instead of
> guessing) and then built exactly as recommended. Same bar here. If a
> question can be answered by reading fallout2-ce source, counting opcodes in
> real scripts, or walking real game files, demand that method over citation
> from memory.

---

I have a C#/MonoGame re-implementation of a Fallout 2 engine slice (project name **Hexwaste**, renamed from FalloutPoc and ship-ready) where you can walk a persistent world, talk, loot, lockpick, and fight turn-based unarmed combat that the world answers with same-team joiners and a game-over loop. Phase 6 needs a direction: **barter** (the designated phase-5 spillover, externals half-done), **combat depth** (weapons/armor/XP), a **playability vertical slice** (what actually breaks in a real Arroyo→Den playthrough?), or something we're not seeing. Research the options, compare honestly, recommend one path with milestones.

## Current state (all done — 97 xUnit tests, committed; phase-5 report fully implemented)

- **C# / .NET 10 + MonoGame DesktopGL**, Linux + Windows. `Hexwaste.Formats` (parsers, hex/A*, lighting, micro INT VM, script host, combat math, save model — engine-free, tested headless) + `Hexwaste.Viewer`. Released-shape artifacts exist (`scripts/release.sh`, SUL v1.0 + NOTICE, game-dir probing).
- **World:** DAT2 VFS, FRM/PAL/MAP/PRO/AAF/MSG/ACM parsers, full renderer (hex z-sorting, palette cycling, outlines, roof fade, egg-style transparency), static lighting + day/night clock, sound (sfx/music/footsteps), worldmap travel, ambient NPC life incl. script-timer-driven behaviors (brahmin wander/moo/dung).
- **Scripts:** micro INT VM (~55 opcodes, 181 externals arity-mapped, ~50 real) + ScriptHost: map_enter on every load (locks, container stocking), gsay dialog trees, lockpick, examine overrides, **script timers** (sorted tick queue, dialog-gated, cleared on map exit — engine-accurate), real caps externals (`item_caps_total/adjust` — pay-caps dialog branches actually pay).
- **Persistent world (phase-5 M1):** per-visited-map deltas over pristine reloads — doors, taken objects (load-order ordinals; MAP object Ids collide), created objects, container snapshots, MVARs; LVAR slices keyed by map name, imported before map_enter on revisit (firstRun=0). Saves carry VisitedMaps + all LVARs. The ~590 KB/transition ScriptHost leak is fixed.
- **Combat (phase-5 M2–M4):** critter proto stat block + the 11 MAP combat ints parsed (team/results/per-instance HP); CritterState (base+bonus); unarmed to-hit/damage (skill−AC clamp 95; rand(1,2+melee)−DT ×(1−DR/100)); roll-before-animate, damage-on-completion (engine-literal); death falls → corpse = anim+28 FID, NO_BLOCK+flat, lootable via the loot panel; AP-budgeted AI turns (punch adjacent, 1 AP/hex approach), same-team joiners within 20 hexes, `_combat_should_end`, game over → F9. Deterministic `--fight <hex> --rng-seed N` transcripts.
- **Research assets:** `tools/int_analyze.py` (byte-exact INT disassembler), stubbed-external logging, full fallout2-ce tree in `reference/`, headless harness (`--screenshot`, `--use-hex`, `--attack`, `--fight`, `--goto-map`, `--save-now/--load-now`, `--examine-critter`, `--talk-hex --choose`, `--rng-seed` …).
- **Known gaps with reasons on record:**
  - Barter: `gdialog_barter`/`gdialog_set_barter_mod` still stubbed (deferred-node flow understood, phase-5 research §3); proto `cost` field skipped in ProtoDatabase.
  - Combat is unarmed-only: no equipped weapons/armor (weapon FRM codes exist in FIDs; item protos carry weapon/ammo data we skip), no criticals/aimed shots, no kill XP. The dude's stats come from a generic critter proto (hmwarr, 30 HP) — no character sheet.
  - **Corpse states don't persist**: kills mutate Fid/Flags/CombatResults which the map delta doesn't capture — killed critters resurrect on revisit (the delta only captures doors/taken/created/containers/MVARs).
  - critter_p_proc (unprovoked on-sight aggro), destroy_p_proc (scripted death reactions, XP), damage_p_proc: not wired.
  - Per-vertex floor lighting (BasicEffect quads — deferred twice), faithful TRANS_* blend LUTs, egg.frm mask, elevators (hardcoded exe tables).

## Constraints

- Hobby project, AI pair programming; milestones demoable + headless-testable.
- Legal: SUL v1.0 conditions on record (free/non-commercial, no assets, attribution comments stay). The rename is DONE — don't reopen it.
- C#/MonoGame stays; fallout2-ce in `reference/` is the spec; **port — don't invent**. No shaders (mgfxc needs Wine; CPU paths benchmarked fine).

## Directions to research

1. **Barter (the designated spillover)** — implementation-grade this time:
   a. The exact deferred-node flow (`game_dialog.cc` gdialog_barter → `barter.cc`): confirm our planned model (session flag set by the opcode, trade window opens after the proc returns, queued node presents on close) against the source, line-cited.
   b. Price math: `barterComputeValue` exactly — what does a flat-modifier version (fixed barter skill) get wrong, and is it visible in Den/Klamath shop inventories? Empirically price 5 real items both ways.
   c. The proto `cost` field offset per object type (we skip it today) and any items with cost 0 quirks (money pid 41).
   d. UI: reuse the two-pane loot panel for offer/counter-offer — what's the minimal trade-loop state machine? What do shopkeeper scripts (Tubby, Flick — disassemble them) check after barter ends; can a failed/cancelled trade break their dialog state?
2. **Combat depth — weapons, armor, XP:**
   a. Equipped items: where MAP/critter data stores equipped weapon/armor (inventory flags? `OBJECT_IN_RIGHT_HAND` etc. in obj flags), how the engine resolves attack FIDs from weapon animation codes (we already compose FID weapon codes), and the minimal "dude equips a spear/pistol from inventory" model.
   b. Ranged: hit chance vs distance (`determineToHit` — range/perception terms), ammo consumption + reload, projectile/laser animation — or is throwing/melee the better second weapon class for effort/payoff?
   c. Armor: AC/DT/DR from equipped armor protos vs our current proto-stat-only model.
   d. Kill XP (`killType` exp values we already parse) + a minimal level-up (HP only?) — does progression make the combat loop *fun* without a full character sheet, or is a character sheet (SPECIAL allocation at start) the real unlock? Where does the engine keep the dude's stats (gcd files — parse or synthesize?).
   e. **Corpse persistence**: cleanest delta-model extension for dead critters (capture CombatResults/HP/Fid by ordinal? a `DeadOrdinals` list replaying KillCritter's corpse conversion on revisit?) — this is a bug-fix-grade item; size it.
3. **Critter scripts in combat** — wire critter_p_proc (on-sight aggro: empirically check geckos/temple ants/Den thugs — who actually aggros via script vs team), destroy_p_proc (scripted death: quest flags, XP via `give_exp_points` — which we'd need real), damage_p_proc. Survey 5+ real critter scripts with the disassembler: what breaks if these stay stubs, what lights up if they run?
4. **Playability vertical slice** — the alternative framing: instead of a system, pick the *Arroyo → Temple of Trials → Klamath/Den* opening and empirically audit what a real playthrough hits that we lack (Temple ants combat? the Vic quest chain? key items/doors? spatial scripts? use_obj_on for the Temple door explosive?). Produce a ranked break-list with per-item effort. Is "make the opening hour actually playable" a better phase 6 than any single system?
5. **Renderer/audio fidelity backlog** — per-vertex floor lighting (BasicEffect quads), TRANS_* LUTs, egg.frm, combat sfx (hit/miss/death sounds — SfxName already does doors/footsteps). Worth a milestone, or slack-fill items?
6. **Anything we're not seeing** — random encounters on the worldmap? party member (Sulik/Vic minimal follow+fight)? spatial/timed map scripts? push back if a higher-leverage phase 6 exists.

## Cross-cutting

- Save-format growth: VisitedMaps + LVARs + (new) corpse/equipment state — any versioning we should add NOW before saves circulate publicly?
- Test-coverage gaps phase 6 should close regardless of direction (combat controller is currently viewer-side and untested headless — extractable?).
- Post-release reality: anything in the SUL/upstream situation changed since June 2026 (issue #428/#476 movement, fallout2-ce activity)? Quick check, don't dwell.
- MonoGame/.NET ecosystem delta (quick check).

## Deliverable

Comparison table (effort / payoff / risk / fun), ONE recommended path with rationale (combinations allowed — e.g. barter + corpse persistence + combat sfx as one "the Den is alive" phase), M0..M5 milestones (each demoable + headless-testable, with specific fallout2-ce files and line numbers), pivot thresholds, explicit unverified flags. Where a direction depends on empirical script behavior, name the exact scripts to disassemble and what to look for.
