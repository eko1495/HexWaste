# Research Prompt — Hexwaste Phase 8: The Second Hour

> Paste everything below this line into Claude Desktop (enable web search).
> Phases 2–7 were researched with the empirical method (disassembling real
> game scripts operand-by-operand, parsing real protos/maps to the byte,
> running headless playthrough probes and benchmarks, citing fallout2-ce
> file:line for every engine claim) and then built exactly as recommended.
> Same bar here. If a question can be answered by reading source,
> disassembling scripts, or attempting the interaction headless, demand that
> method over citation from memory.

---

I have a C#/MonoGame re-implementation of a Fallout 2 engine slice (**Hexwaste**, tagged v0.7.0, publish-ready) where the opening hour plays armed: guns with ammo/reload/line-of-fire, recruited companions who travel and fight, spear traps that spring and disarm, a persistent world, barter, XP/levels, a main menu and a death screen. Phase 8 needs a direction: **random encounters** (the named spillover), **the second hour** (Klamath/Den quest chains — Vic's rescue end-to-end), **combat depth II** (burst/aimed/criticals/throwing/explosives), **character progression** (skill points, creation screen, gender), **companion management**, or something we're not seeing. Research the options, compare honestly, recommend one path with milestones.

## Current state (all done — 124 xUnit tests, committed, v0.6.0/v0.7.0 tagged; phase-7 report fully implemented)

- **C# / .NET 10 + MonoGame DesktopGL** (3.8.4.1 pinned — 3.8.5 still preview). `Hexwaste.Formats` (parsers, hex/A*/LoF, lighting, micro INT VM + script host, combat/ranged/barter/progression math, save model — engine-free, headless-tested) + `Hexwaste.Viewer`. Release pipeline proven (`scripts/release.sh`, SUL + NOTICE, screenshots in README, CHANGELOG, tags); the public git push is the only unexecuted step (docs/RELEASING.md).
- **World/scripts**: persistent per-map deltas (doors/taken/created/corpses/containers/MVARs/**NPC positions** — ordinal-keyed, Version-2 saves that refuse mismatches), critter_p_proc heartbeat, spatial trap scripts (engine gates, create_object_sid script binding, critter_damage), use_obj_on (item-then-target precedence), gmovie caption cards from .sve, override_map_start, cross-script externals, ~70 real engine externals, per-map stub histograms.
- **Combat**: melee + single-shot guns (engine to-hit subset incl. distance/PE, ammo AC/DR mods, min-ST, crowd penalty; LoF walls-block/critters-count; magazines, caliber-matched partial reloads; hitscan visuals — engine-accurate for 10mm-class), armor, drugs, AP turns, scripted aggro, same-team joiners, ally turns (companions fight with their own gear), enemies target nearest of dude+allies, team kills pay XP, weapon/death sfx, per-vertex floor lighting (newr1 3.34 ms avg — headroom everywhere).
- **Party (minimum cut)**: party_add/remove/party_member_obj real; followers travel outside map deltas with per-map script re-binding; follow = their own critter_p_proc. NO companion inventory/level/dialog management (documented).
- **The dude**: premade gcd sheets (menu picker), levels (HP only — no skill points/perks), male art (hmjmps).
- **Research assets**: `tools/int_analyze.py`, stub histograms, fallout2-ce in `reference/`, headless harness (`--fight/--recruit/--use-on/--buy/--sell/--give/--rng-seed/--save-now`... all transcripted).
- **Known gaps, on record**:
  - Random encounters: worldmap.txt FULLY decoded in phase-7 track B (subtile grid → tables → weighted entries; cadence 1 step = 30 game-min, 1500 ms real throttle; encounter maps differ by `saved=No` — must skip our delta slot — plus `random_start_point_N` and runtime spawns). Sized M, never built. UNVERIFIED: `saved=No` semantics rest on one parsed map.
  - Combat depth: no burst (`_compute_spray` cones), no aimed shots/crit tables, no knockdown, no throwing (rung a — needs the projectile animator), no explosives (the temple door is damage_p_proc + metarule(49)==explosion — still only openable by lockpick bypass), no lighting tier in to-hit, AI ignores ai.txt packets (flat min_to_hit 30, no distance prefs, no fleeing).
  - Progression: level-ups award HP only — the engine grants skill points (stat.cc/editor.cc) and tag skills exist in the gcd; no SPECIAL creation screen (M per phase-7 track D); no perks (ranked scope-creep, skip unless argued); no female dude (hfjmps unchecked).
  - Companions: can't trade items with them, no level proto swaps (party.cc), no wait/dismiss dialog path, and the LEGITIMATE recruitments are gated (Sulik = $350/Maida quest GVARs, Vic = radio + Metzger payment) — our --recruit is test plumbing.
  - Steal skill, doctor/first-aid engine path (skillUse — scripts only intercept edge cases), reaction system (reaction_influence stubbed, barter mod unaffected).
  - CombatEngine extraction to Formats still pending (honest-M, ICombatPresenter sketched in phase-7 track C notes); no CI; no perf-canary test.

## Constraints

- Hobby project, AI pair programming; milestones demoable + headless-testable.
- Legal: SUL conditions stand; no assets ever; the publish decision/push is the user's.
- C#/MonoGame stays; fallout2-ce in `reference/` is the spec; **port — don't invent**. No custom shaders (BasicEffect is fine — the floor pass proved it).

## Directions to research

1. **Random encounters (the named spillover)** — finish the design: verify `saved=No` against a SECOND encounter map + the maps.txt flag semantics in fallout2-ce (map.cc save path); the spawn composition format (`pid/ratio/Script` entries, `random_start_point_N` placement, team assignment); ambush vs neutral encounters (who starts hostile — encounter table `Combat`/`Ambush` flags?); fleeing the map (exit grids on all edges? walk-off?); the dude's worldmap position resume. Concrete milestone plan over our worldmap screen + existing spawn/script plumbing. Also: does our delta system need a "transient map" mode or does skipping VisitedMaps for `saved=No` maps suffice?
2. **The second hour — Vic's rescue as the vertical slice**: empirically audit the chain Klamath (Sulik's $350 + Maida dialog GVARs; Vic's radio in the Den; Metzger's $1000/barter; Vic joins) — disassemble the gating scripts (kcMaida, kcSulik, dcVic, dcMetzge, the radio item script) and produce the ranked break-list: which GVAR/caps/dialog paths work TODAY on our externals, which need new ones (name opcodes), where the quest XP lands. Is "finish Vic's rescue legitimately" a coherent phase, and what does it pull in (companion dismiss/wait? karma/reputation gates?)?
3. **Combat depth II** — size each separately with the crit-table reality check: burst (`_compute_spray` combat.cc:3680 — cones via three LoF walks, per-round accounting); aimed shots + criticals (`attackComputeCriticalHit`, the per-bodypart crit tables — how big is the table port REALLY? count the rows); knockdown/knockback (anim + tile shove); throwing (projectile object flight — the rock/spear/grenade rung, now that guns exist is it still a dead end?); explosives + metarule(49) (the temple door, dynamite timers); AI packets from ai.txt (parse it — min_to_hit, distance prefs, best_weapon — which fields move the Den/Klamath fights?). Recommend a subset that maximizes felt depth per line of code.
4. **Character progression**: skill points per level (stat.cc formula incl. IN; tag-skill double-rate), spend-UI minimal form (text allocator vs auto-spend — phase-7 ranked auto-spend invisible, re-evaluate now that skills gate guns/lockpick/traps/barter); SPECIAL creation screen sized honestly (we never write gcd — all in-memory); female dude (hfjmps art completeness check + gcd gender field + sfx gender). What ships the most player agency for S/M?
5. **Companion management minimum**: trade-with-follower (barter UI reuse at 1:1?), wait/dismiss via their real dialog nodes (disassemble Sulik/Vic Node для wait), level proto swaps (party.cc partyMemberIncLevels — needed or cosmetic at our scope?), companion death → permanent (already) + bark lines. Which 2 items remove the most friction?
6. **Post-release ops (quick)**: GitHub Actions CI for build + non-data tests (the FALLOUT2_DIR guard already splits them); the perf canary as a test; issue templates pointing at the scope section; anything in the SUL/upstream/MonoGame picture since mid-2026 (web check, brief).
7. **Anything we're not seeing** — push back if a higher-leverage phase 8 exists.

## Cross-cutting

- Save format: encounters (worldmap position/state) and skill points both touch it — plan ONE Version-3 bump if needed, or argue additive-V2 suffices.
- The CombatEngine extraction (honest-M): does whichever direction wins make it cheaper or more urgent? If combat depth II wins, extraction-first may pay for itself in tests.
- Heartbeat + spatials + party now all run per tick — re-run `--bench` on a busy map with a party member and an active fight if cheap.

## Deliverable

Comparison table (effort / payoff / risk / fun), ONE recommended path with rationale (combinations allowed — e.g. encounters + ai.txt packets as "the dangerous road", or Vic's rescue + companion management as "the buddy movie"), M0..M5 milestones (each demoable + headless-testable, with specific fallout2-ce files and line numbers), pivot thresholds, explicit unverified flags. Where a direction depends on script behavior, name the exact scripts to disassemble and what to look for.
