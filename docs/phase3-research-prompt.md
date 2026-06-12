# Research Prompt — Fallout 2 PoC Phase 3: After the Walking Simulator

> Paste everything below this line into Claude Desktop (enable web search).

---

I have a working Fallout 2 **map viewer + walking simulator** and I want a researched recommendation for phase 3. Please research the candidate directions, compare them, and propose a prioritized roadmap with the same rigor as before (your phase-2 report correctly predicted effort, risks, and the mgfxc/Wine issue — the recommended path shipped in full).

## Current state (all done, tested, committed)

- **C# / .NET 10 + MonoGame DesktopGL**, Linux + Windows. Two layers: `FalloutPoc.Formats` (pure parsers + hex-grid/A* logic, 46 xUnit tests) and `FalloutPoc.Viewer`.
- **Phase 1 (viewer):** DAT2 archives + layered VFS, FRM/PAL/MAP/PRO parsers (validated against all 150+ shipped maps), floors/objects/roofs with correct hex z-sorting, oblique projection from `tile.cc`, palette color cycling at original speeds.
- **Phase 2 (walking sim):** critter rendering (composed FRM names, all directions), FRM frame animation (looping scenery, walk cycles, accumulating frame offsets), per-pixel mouse picking, a player dude with A* hex pathfinding (`pathfinderFindPath` port: cost 50/step, +10 turn penalty), camera follow, and **hardcoded interactions with no script VM**: doors open/close with their FRM animation and toggle path blocking, exit grids trigger map transitions (resolved via `data\maps.txt`), stairs/ladders teleport/travel. Verified end-to-end: Temple of Trials exit → Arroyo bridge; opening a Den door lets the dude walk inside the building.
- **Perf decision already made:** CPU palette conversion stays (3.6 ms avg full frame on the heaviest map, uncapped ~280 fps); no shader, no Wine, no atlasing needed so far.
- All binary/engine logic is ported from **fallout2-ce** with `// ported from ...` source comments. Out of scope so far: combat, dialogs, script engine (INT VM), sound, lighting, worldmap, save/load, elevators (hardcoded in original exe).

## Constraints (unchanged)

- Hobby project, one developer + AI pair programming; milestones must each produce something visible/demoable.
- Legal: no assets distributed, GOG copy required, no "Fallout" in any published name.
- Stack stays C#/.NET/MonoGame; fallout2-ce remains the authoritative reference.
- The phase-2 hard line "no combat, no script VM" can be RE-EXAMINED now, but only with honest evidence — last time the research showed every project that started with scripts died.

## What I want researched

For each direction: technical scope, which fallout2-ce files/subsystems to port, effort/risk, the "demo moment", and what prior art or post-mortems say:

1. **Lighting engine** (`src/light.cc`, intensity tables, per-hex light, how lightGetTileIntensity feeds object rendering; day/night ambient). Deferred from phase 2 pending the shader decision — that decision landed on CPU rendering, so evaluate: is per-hex lighting feasible on the CPU path (tinting per draw call?) or does it finally force a palette/lighting shader? What did DarkFO's "slow, buggy lighting" actually do wrong?
2. **Ambient NPC life without scripts**: random wander within a radius (the engine's fidget/wander behaviors), facing changes, simple flocking around fires — how much "alive" can the Den feel with zero VM? Which parts of `critter.cc`/`animation.cc` cover idle behavior?
3. **Worldmap**: render the worldmap (worldmap.frm tiles, `worldmap.txt`), click-to-travel between discovered locations, so exit grids marked "worldmap" actually lead somewhere and the whole game world becomes traversable. No encounters, no time simulation. How does fallout2-ce's `worldmap.cc` structure this and what's the minimal subset?
4. **Sound**: ACM decoder port into Formats (libacm BSD/ISC as the spec — verify its current state), ambient sfx + footsteps + door sounds keyed off existing events, music playback. Now that there ARE events (steps, doors, transitions), sound has real demo value, unlike in phase 2. What maps sounds to events without scripts (`gsound.cc`, sfx name building)?
5. **Script engine, re-examined honestly**: with movement/doors/transitions already working, what's the TRUE minimal INT VM subset for (a) `look_at`/`description` text on examine, (b) `use_p_proc` door/container behaviors, (c) map_enter spawn logic? Quantify: how many of the 76 core opcodes does e.g. artemple.int / a Den shop script actually execute on those paths? (You found no published minimal subset last time — this time, propose deriving it empirically: disassemble 2–3 real scripts and count. Evaluate whether a "tracing interpreter that no-ops unknown externals" is a viable incremental strategy, and what DarkFO's partial implementation managed to run.)
6. **Modder tooling pivot** (carried over): with picking + transitions + destination data now parsed, the PoC is close to a cross-platform map *inspector*. Re-evaluate demand: what do FO2 modding communities (NMA, fodev, RPU/sfall-rs circles) lack in 2025/2026, especially on Linux/macOS? Is there a niche for a read-only "map explorer with object inspector + exporter (PNG/Tiled/glTF)" vs. the fallout2-ce mapper rebuild effort?
7. **Containers & items without VM**: open/loot containers (they have inventories already parsed!), pick up ground items, simple inventory panel. How far does this get purely from PRO data + the parsed map inventories? (UI work in MonoGame without the original interface art parsing — or is `intrface` FRM parsing cheap since FRM is done?)

## Also research (cross-cutting)

- **UI/text rendering**: any phase needing on-screen text (examine descriptions, inventory, worldmap labels) needs fonts — Fallout's `.aaf` font format vs. MonoGame SpriteFont (content pipeline friction on Linux again?) vs. FontStashSharp. Which is least friction?
- **MSG files**: game text lives in `text/english/game/*.msg` (item/scenery names for examine, map names). Format complexity? (This unlocks "examine" without scripts — proto MessageId is already parsed.)
- Whether MonoGame 3.8.5/4.0 changed the shader-compilation-on-Linux situation since the last report.

## Deliverable

Same as before: comparison table (effort / payoff / risk / fun), one recommended path (possibly a combination) with rationale, a milestone breakdown (M0..M5 style, each independently demoable and testable headless via the existing `--screenshot`/`--advance-ms` harness), the specific fallout2-ce source files per milestone, decision thresholds for pivoting, and explicit "couldn't verify" flags instead of guesses.
