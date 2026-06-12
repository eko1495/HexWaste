# Research Prompt — Fallout 2 PoC Phase 4: After "The World Becomes Real"

> Paste everything below this line into Claude Desktop (enable web search).
> Note: phases 2 and 3 were researched and then built exactly as recommended;
> the phase-3 report's empirical method (disassembling real scripts instead of
> guessing) is the bar for this one too.

---

I have a Fallout 2 "living world" PoC and want a researched recommendation for phase 4. Compare directions, recommend one path, and break it into demoable milestones.

## Current state (all done, tested — 67 xUnit tests, committed)

- **C# / .NET 10 + MonoGame DesktopGL**, Linux + Windows. `FalloutPoc.Formats` (pure parsers/logic) + `FalloutPoc.Viewer`.
- **Phase 1–2:** DAT2 VFS, FRM/PAL/MAP/PRO parsers (all 150+ maps), full renderer (floors/objects/roofs, hex z-sorting, palette cycling), critters with animations, per-pixel picking, A* pathfinding with walk animation, doors that block/unblock, exit grids, stairs/ladders, map transitions.
- **Phase 3:** native AAF font text + examine (names/descriptions from `pro_*.msg`), **static lighting engine** (full `_obj_adjust_light` port with occlusion; per-object tints exact, per-square floor approximation; day/night ambient), **worldmap click-to-travel** (city.txt/maps.txt), **sound** (complete C# port of the Interplay ACM decoder — likely the only one in existence; door sfx, footsteps, per-map music), **ambient NPC life** (engine-faithful fidget, faked radius wander), and a **micro INT-script VM**: `.int` parser + interpreter, ~55 core opcodes, all 181 externals arity-mapped (stack-safe stubs), currently wired ONLY to `look_at_p_proc`/`description_p_proc` — scripted examine text works on real game scripts.
- **Key assets I have for empirical research:** `tools/int_analyze.py` (byte-exact .int disassembler), a working VM that logs every stubbed external per procedure run, and the full fallout2-ce source tree in `reference/`.
- Out of scope so far: combat, dialogs, inventory/loot UI, VM beyond examine (`use_p_proc`/`map_enter` not wired), save/load, elevators, egg transparency, time-of-day simulation.

## Constraints (unchanged)

- Hobby project, AI pair programming; each milestone independently demoable + testable headless (`--screenshot`, `--examine`, `--advance-ms` harness exists).
- Legal: no assets distributed; no "Fallout" in a published name.
- C#/MonoGame stays; fallout2-ce is the authoritative reference; port — don't invent.
- Combat remains presumed-out unless the evidence says a minimal subset is genuinely small (be honest; don't force it in).

## Directions to research

For each: scope, fallout2-ce files, effort/risk, demo moment, and what prior art says. Where the question is "how big really?", demand the empirical method: run the disassembler over real scripts and count, or trace which externals the VM stubs during actual runs.

1. **VM expansion to `use_p_proc` + `map_enter`** — locked doors with lockpick rolls, script-stocked containers, NPC placement at map entry. The phase-3 report measured ~25 externals for this; the open question is HOST capability cost, not opcodes: which externals mutate the world (`create_object`, `move_to`, `destroy_object`, `add_obj_to_inven`), which need persistent state (local/map/global vars — note: LVARs come from the MAP file's localVars block we already parse; how does `script->localVarsOffset` map into it?), which need timers (`add_timer_event` — is it ignorable?). Deliver: a dependency-ordered list of host capabilities with per-item cost, and the cut line for "doors+containers work".
2. **Inventory & loot** — open/loot containers (inventories already parsed), pick up ground items, a simple inventory panel. Research: the original loot/inventory window art (`art\intrface\` FRMs — which ones), item icons via `inventoryFid`, weight/size display from PRO data, and what minimal interaction set feels complete (take / take-all / drop?). Does equipping (armor/weapon changes dude FID via art codes we already have) come cheap with it?
3. **Dialog (text-only)** — `talk_p_proc` with the gsay externals (`gsay_start/reply/option/end`, `giq_option`): how far does a TEXT dialog tree get without talking heads and without reaction/skill systems (stub rolls)? Empirically: disassemble 2–3 real NPC dialog scripts (a Den villager, a shopkeeper) and count externals in `talk_p_proc` closures; check what DarkFO managed for dialogs and what its devblog says broke. Is the dialog UI art (`di*.frm`) needed or is a text panel acceptable for a PoC?
4. **Game time + scheduled world** — game clock (ticks), day/night ambient tied to it (lighting already supports ambient), `game_time_hour` external returning real values, NPC schedules? (probably script-driven — verify). Small but high-immersion; what's the engine's time model (`gameTimeGetTime`, ticks per second, how scripts read it)?
5. **Save/load** — persist dude position/map, opened doors, global/map vars, container changes. Compare: porting the original save format (complex, compatible) vs. a custom JSON snapshot (cheap, PoC-honest). What does the original save actually serialize per map (`.sav` structure)?
6. **Renderer polish pack** — egg transparency (the translucent ellipse when the dude walks behind walls — how `_obj_render_object` uses the egg + OBJECT_TRANS flags and the intensityColorTable blend tables; feasible with CPU RGBA textures + a mask?), object outlines on hover (replacing our yellow tint), per-vertex floor lighting (custom quad batch — the phase-3 deferred upgrade), scroll-blocking map edges.
7. **Combat, honestly re-checked** — given working pathfinding/animations/VM: what is the TRUE minimal turn-based combat (one weapon type, no AI beyond approach-and-attack, no criticals)? Which subsystems does `combat.cc` drag in (stats from PRO critter data, skills, action points)? Expectation per prior research is "still too big" — confirm or refute with file-level evidence, don't hand-wave either way.

## Cross-cutting

- Whether any of these force UI infrastructure (panels, buttons, scrollbars) — and if so, whether porting the original `intrface` art-based UI or building a minimal custom one is less total work across directions 2+3+5.
- MonoGame: anything new since mid-2026 relevant to us (DesktopVK stable? still irrelevant for our CPU path?).
- Community check: has Gecko (the cross-platform map editor) or fallout2-ce moved in ways that change priorities (e.g. fallout2-ce gaining features that make some direction redundant)?

## Deliverable

Same as before: comparison table (effort / payoff / risk / fun), ONE recommended path (combinations allowed) with rationale, M0..M5 milestone breakdown (each demoable + headless-testable, with the specific fallout2-ce files per milestone), pivot thresholds, and explicit "couldn't verify" flags instead of guesses.
