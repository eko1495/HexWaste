# Research Prompt — Hexwaste Phase 7: Deeper, Wider, or Out the Door?

> Paste everything below this line into Claude Desktop (enable web search).
> Phases 2–6 were researched with the empirical method (disassembling real
> game scripts, parsing real protos/maps byte-by-byte, running headless
> playthrough probes, measuring instead of guessing) and then built exactly
> as recommended. Same bar here. If a question can be answered by reading
> fallout2-ce source, disassembling real .int scripts, or attempting the
> interaction headless, demand that method over citation from memory.

---

I have a C#/MonoGame re-implementation of a Fallout 2 engine slice (**Hexwaste**, SUL-licensed, release-shaped) where the opening hour now plays: real dude stats gate dialog correctly, temple critters aggro on sight via their own scripts, fights are winnable with spear/armor/stimpaks, kills pay XP and level you up, corpses and loot persist across the world, and Tubby runs a real shop at the engine's price formula. Phase 7 needs a direction: **deeper combat** (ranged + ammo + smarter AI), **wider world** (traps, quest chains, party members, random encounters), **fidelity/polish**, or **ship a public release first**. Research the options, compare honestly, recommend one path with milestones.

## Current state (all done — 114 xUnit tests, committed; phase-6 report fully implemented)

- **C# / .NET 10 + MonoGame DesktopGL**, Linux + Windows. `Hexwaste.Formats` (all parsers, hex/A*, lighting, micro INT VM + script host, combat/barter/progression math, save model — engine-free, tested headless) + `Hexwaste.Viewer`. Release artifacts via `scripts/release.sh` (SUL v1.0 + NOTICE, game-dir probing).
- **World/scripts**: DAT2 VFS, FRM/PAL/MAP/PRO/AAF/MSG/ACM, full renderer + lighting + sound, worldmap travel, persistent per-map deltas (doors/taken/created/containers/MVARs/**corpses** — ordinal-keyed, LVARs by map name, versioned saves that refuse mismatches), script timers, **critter_p_proc heartbeat** (10 Hz round-robin, engine-gated), map_enter for ALL scripted objects including hidden stock boxes, **cross-script external variables** (export.cc semantics, session-scoped), ~60 real externals (stat/trait/check family, attack, critter_add_trait, give_exp_points, move_obj_inven_to_obj, caps, timers, geometry...), per-map stub histograms on stderr.
- **The dude**: `premade\player.gcd` stat sheet (SPECIAL/skills/traits), level-ups (engine XP table, EN/2+2 HP), equipment (hand/worn item flags straight from MAP format), HP/AP/level/XP HUD.
- **Combat**: turn-based, roll-before-animate/damage-on-completion, melee weapons (damage/AP/reach/attack-anim from protos; enemies use their MAP-equipped weapons), armor as bonus-stat mutation, drugs (stimpak heal rolls, 2 AP in combat), death→corpse→loot, AI turns (AP-budgeted approach), same-team joiners, script-driven on-sight aggro, destroy/damage procs, game over → F9.
- **Barter**: gdialog_barter flow (flag-only opcode, queued node on close, modifier-overwrite semantics), trade vs the shopkeeper's live stock box, `_barter_compute_value` pricing, CRITTER_BARTER gating.
- **Research assets**: `tools/int_analyze.py` (byte-exact disassembler), stub histograms, fallout2-ce tree in `reference/`, rich headless harness (`--fight/--attack/--give/--use-item/--buy/--sell/--talk-hex --choose/--goto-map/--save-now`... all transcripted, `--rng-seed` deterministic).
- **Known gaps, on record with reasons**:
  - Ranged weapons: proto fields parsed but unused beyond melee; no ammo/reload (MAP ammo fields still skipped), no distance to-hit terms, no LoF, no projectile anims; the dude's hmwarr art has no gun animations (hmjmps has all).
  - Spatial scripts: parsed but discarded — the arcaves spear-trap corridors (18 records, elev 2) are inert; trigger model documented (edge-triggered on tile change, scripts.cc:2516).
  - use_obj_on_p_proc not wired (temple-door explosives have a lockpick bypass); steal/first-aid/repair skill-use likewise.
  - Party members: `party_member_obj` stubs to 0; Vic's quest chain and Sulik recruitment blocked (L-effort, deferred twice).
  - AI is approach-and-punch only: no fleeing (critter_is_fleeing stubs 0), no AI-packet behavior (aiPacket parsed but unused), no enemy healing/ranged use, no reaction system (reaction_influence stub).
  - Renderer/audio fidelity backlog: per-vertex floor lighting (deferred three times), TRANS_* blend LUTs, egg.frm mask, combat hit/miss/death sfx (SfxName covers doors/footsteps only).
  - override_map_start stubbed (transition spawn positions occasionally wrong); play_gmovie no-op (Elder cutscene); NPC positions don't persist (only doors/taken/created/corpses/containers do).
  - Combat controller still lives in the viewer (turn/AP/joiner math extracted to Formats; the state machine itself is M to extract).

## Constraints

- Hobby project, AI pair programming; milestones demoable + headless-testable.
- Legal: SUL conditions on record; no assets ever; rename DONE — don't reopen.
- C#/MonoGame stays; fallout2-ce in `reference/` is the spec; **port — don't invent**. No shaders (mgfxc/Wine; CPU paths benchmarked fine).

## Directions to research

1. **Ranged combat + ammo (the natural "deeper")** — implementation-grade:
   a. The full determineToHit distance/perception/range terms (combat.cc:4331-4402, cite exactly), LoF blockers (`_combat_is_shot_blocked`), and what a minimal-but-honest port keeps vs drops (aimed shots? burst? — push back on scope).
   b. Ammo: MAP/save fields we skip (ammoQuantity/ammoTypePid in ReadObjectData), reload flow (item.cc:1437/1553), ammo DR/DAM modifiers (ammo proto fields parsed already?) — and the save-format migration cost (Version bump 2?).
   c. Art: the dude must swap to a FID index with gun anims (hmjmps?) or stay stat-only; projectile/laser animations — what does the engine actually draw for a 10mm shot (animation.cc fire sequences) and what's the cheapest honest visual?
   d. Enemy ranged use: which opening-area critters carry guns (parse real maps — Den thugs? Metzger's slavers?), and what AI changes ranged enemies force (kiting? stay-at-range — combat_ai.cc distance preferences, cite).
   e. Effort/risk table for: throwing only / single-shot pistols / full burst+aimed.
2. **The wasteland's tricks (the "wider" basket)** — size each:
   a. Spatial scripts: wire the documented trigger model; disassemble 2 arcaves traps end-to-end (notice check, damage, disarm XP) — S plumbing + M behavior was the phase-6 estimate, refine it.
   b. use_obj_on_p_proc + the temple door explosive; steal (critter pockets exist) and first-aid/doctor skill-use on critters — which opening-hour scripts actually consume these (survey)?
   c. Party member MINIMUM: what does Sulik/Vic recruitment actually require script-wise (disassemble dcVic/kcSulik: party_member_obj, add to party externals, follow behavior)? Is a "follower walks with you and fights on your team, no inventory/dialog management" cut coherent, or does the party system's tendrils (party.cc protos swap on level, save format) make any cut L?
   d. Random encounters: worldmap.txt encounter tables — parse one region's table empirically; minimal model (chance roll per travel tick → load encounter map with spawned critters). What do encounter maps need that town maps don't?
   e. NPC position persistence + override_map_start (both small, both correctness).
3. **Fidelity/polish basket** — per-vertex floor lighting (BasicEffect quads — it's been deferred three times; either size it honestly for THIS phase or argue for killing it), TRANS_* LUTs, egg.frm, combat sfx (hit/miss/death sound names — sfx name composition for weapons, snd_lookup tables), play_gmovie as a static-slide+caption. Which of these move the "feels like Fallout" needle most per day of work?
4. **Ship-first argument** — the repo is release-shaped but unpublished. Make the honest case for/against cutting v0.6 public NOW (fresh-history publish per docs/RELEASING.md) before phase 7 lands: what's the smallest pre-publication checklist remaining (README screenshots? a short demo gif? the publish dry-run), what feedback could a public release realistically generate for prioritizing phase 7, and any SUL/upstream changes since June 2026 (quick web check — fallout2-ce activity, issues 428/476)?
5. **Anything we're not seeing** — character creation screen? skill points + tag skills on level-up? perks? push back if a higher-leverage phase 7 exists that the gap list doesn't name.

## Cross-cutting

- The critter_p_proc heartbeat + per-frame UpdateCombat now run every tick — any measurable frame-cost creep on heavy maps (newr1.map was the phase-2 benchmark)? Re-run `--bench` and compare.
- Test gaps: the combat state machine is still viewer-side; barter transactions are untested headless beyond transcripts. What's worth locking down before more combat features pile on?
- Save format: Version is 1; ranged ammo and NPC positions both touch it — plan ONE bump, not two.

## Deliverable

Comparison table (effort / payoff / risk / fun), ONE recommended path with rationale (combinations allowed — e.g. ship v0.6 + ranged combat + traps as "the dangerous wasteland"), M0..M5 milestones (each demoable + headless-testable, with specific fallout2-ce files and line numbers), pivot thresholds, explicit unverified flags. Where a direction depends on script behavior, name the exact scripts to disassemble and what to look for.
