# Research Prompt — Fallout 2 PoC Phase 5: The Wasteland Bites Back (or: Ship It?)

> Paste everything below this line into Claude Desktop (enable web search).
> Phases 2, 3 and 4 were researched with the empirical method (disassembling
> real game scripts, measuring instead of guessing) and then built exactly as
> recommended. Same bar here. If a question can be answered by reading
> fallout2-ce source or counting opcodes in real scripts, demand that method.

---

I have a Fallout 2 PoC where the world now *responds* — and a decision to make: does phase 5 add **combat** (measured at M last phase), pick a different growth direction, or **consolidate and ship**? Research the options, compare honestly, recommend one path with milestones.

## Current state (all done — 80 xUnit tests, committed)

- **C# / .NET 10 + MonoGame DesktopGL**, Linux + Windows. `FalloutPoc.Formats` (parsers, hex/A*, lighting, micro INT VM, script host — all engine-free and tested) + `FalloutPoc.Viewer`.
- **Phases 1–3:** DAT2 VFS, FRM/PAL/MAP/PRO/AAF/MSG parsers (150+ maps validated), full renderer with hex z-sorting + palette cycling, critters with animations, per-pixel picking, A* pathfinding + walk animation, doors/exit grids/stairs, worldmap click-to-travel, static lighting with occlusion + day/night ambient, complete ACM sound port (sfx + music), ambient NPC fidget/wander, examine with real game text.
- **Phase 4 ("the world responds"):** the micro-VM became a real script host — **map_enter scripts run on every load** (doors locked, containers stocked by their actual scripts), **text dialog** over real `gsay` trees (options bind by procedure index; IQ filter), **lockpicking** via `use_skill_on_p_proc`, **loot/inventory panels** with authentic inven-FRM icons, **game clock** (engine tick model + our own day/night curve — the engine has none) with hour-driven ambient, **F5/F9 JSON save/load** (position, doors, GVARs, bag, clock; containers restock by design), and renderer polish (silhouette outlines, roof fade indoors, egg-style wall transparency, scroll clamps).
- **VM capability today:** ~55 core opcodes + all 181 externals arity-mapped; real implementations for examine/dialog/locks/world-mutation/variables/clock families (~45 externals). Procs wired: map_enter, look_at/description, use, use_skill_on, talk, pickup.
- **Research assets:** `tools/int_analyze.py` (byte-exact disassembler), the VM logs every stubbed external, full fallout2-ce tree in `reference/`, and a headless test harness (`--screenshot`, `--use-hex`, `--talk-hex --choose`, `--save-to/--load-from` …).
- **Known deferred items with reasons on record:** timers/timed_event_p_proc (door auto-close), barter (stubbed with a notice), per-vertex floor lighting (BasicEffect quads, no shader needed), faithful TRANS_* blend LUTs, multi-map save deltas (restock-on-revisit), critter PRO stat-block parsing (we read only proto headers), elevators (hardcoded-in-exe tables).

## Constraints

- Hobby project, AI pair programming; milestones demoable + headless-testable.
- Legal: no assets distributed; **the project must be renamed before any public release** (currently internal `FalloutPoc` namespaces — this matters if "ship it" wins).
- C#/MonoGame stays; fallout2-ce is the reference; port — don't invent.

## Directions to research

1. **Minimal combat** — last phase's file-level estimate: M (~1,900 new LOC; unarmed/one-weapon, vanilla to-hit/damage minus perks/criticals, approach-and-attack AI; biggest risk = sequencing the turn loop against the animation system). Now go deeper, implementation-grade:
   a. The exact turn-loop state machine to port (`combat.cc` `_combat`/`_combat_turn`/`_combat_turn_run`): map it onto OUR architecture (we have per-object walk controllers and a frame-driven animator, not the engine's reg_anim queue) — what is the minimal sequencing model that won't deadlock or fire animations out of order?
   b. Critter stats: the 344-byte CritterProtoData block layout (proto_types.h:335) — exact field offsets for HP/AC/AP/damage thresholds+resistances/unarmed skill so we can extend our ProtoDatabase reader; which stats minimal combat truly reads.
   c. Damage + death: the vanilla formula path, death animation selection (`_correctDeath`, knockback /10), corpse state (critters become lootable containers — our loot panel should just work?).
   d. Hostility: who attacks whom without scripts (team numbers from MAP critter data? `critter.cc` team/hostile checks) — can the Den's gang members or Klamath's geckos be hostile out-of-the-box, or is aggro script-driven (critter_p_proc)? Empirically check 2–3 hostile-critter scripts (geckos, rats in the temple).
   e. Player death = reload save (we have F9) — acceptable game-over loop?
2. **Timers** — `add_timer_event`/`timed_event_p_proc` queue (scripts.cc:800-871): a real game-tick queue is now cheap (we have a clock). What does it unlock beyond door auto-close (NPC behavior re-arms, light schedules?) — survey a few timer-using scripts empirically. S or M?
3. **Barter** — `gdialog_barter` from dialog: the actual barter flow (barter.cc? game_dialog.cc:3198), caps via `item_caps_total/adjust` (we have inventories + caps protos), price math (barter skill — stubbable at fixed modifier?). A minimal "buy/sell at flat prices" panel reusing our loot UI: S/M? What breaks shopkeeper scripts if barter succeeds/fails?
4. **Multi-map persistence** — upgrade the JSON save to per-visited-map deltas (the .SAV-snapshot equivalent, but honest): track door/lock/taken-object/LVAR-slice deltas per map in the session, apply on revisit. This kills the restock quirk and makes the world feel permanent. Estimate + the cleanest delta model given our pristine-reload architecture.
5. **Remaining renderer fidelity** — per-vertex floor light via BasicEffect quads (the long-deferred M), faithful egg mask (egg.frm), TRANS_* blend LUTs. Worth a milestone, or fold the first into combat's milestone slack?
6. **Consolidate & ship** — the alternative path: rename (legal), repo hygiene, README/screenshots/video, binary releases (dotnet publish for Linux/Windows — any MonoGame packaging gotchas?), and an honest "what is this for" positioning (educational reference? modder tool? demo). Check: what do similar projects (fallout2-ce, OpenMW early days, devilutionX) do for "requires original game data" onboarding UX — auto-detect GOG installs? Also: is there ANY licensing risk in shipping our ported-from-GPL-fallout2-ce code? **fallout2-ce's license — check it** (Sustainable Use License? MIT? GPL?) and what that means for our port's distribution.
7. **Anything we're not seeing** — given everything above, is there a higher-leverage phase-5 than combat or shipping? (Random encounters? Party member? Character sheet? Push back if warranted.)

## Cross-cutting

- Performance reality-check: map_enter now runs hundreds of script VMs per map load — measure-worthy? Any other accumulating costs (handle table growth across transitions, LVAR slice dictionary)?
- Test-coverage gaps that phase 5 should close regardless of direction.
- MonoGame/ecosystem delta since June 2026 (quick check).

## Deliverable

Comparison table (effort / payoff / risk / fun), ONE recommended path with rationale (combinations allowed — e.g. combat + timers, or ship + persistence), M0..M5 milestones (each demoable + headless-testable, with specific fallout2-ce files), pivot thresholds, explicit unverified flags. If "ship it" wins any milestone slot, include the legal/rename checklist as a concrete step list.
