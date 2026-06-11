# Research Prompt — Fallout 2 Map Viewer PoC: What Next?

> Paste everything below this line into Claude Desktop.

---

I have a working proof-of-concept **Fallout 2 map viewer** and I want a researched recommendation on where to take it next. Please research the options, compare them, and propose a prioritized roadmap.

## Current state (done, working, tested)

- **C# / .NET 10 + MonoGame (DesktopGL)**, runs on Linux and Windows. Clean two-layer architecture:
  - `FalloutPoc.Formats` — pure .NET parsers, no engine dependencies, 26 xUnit tests: DAT2 archives (zlib, layered VFS: loose `data/` > `patch000.dat` > `critter.dat` > `master.dat`), FRM sprites, PAL palettes, MAP files (validated against all 150+ maps shipped with the game), minimal PRO prototypes, FID/PID helpers.
  - `FalloutPoc.Viewer` — renders any map: floor tiles, scenery/wall/misc objects with correct hex-grid z-sorting, roofs (toggleable, 96 px offset), camera pan, elevation switching, palette color cycling at original speeds (slime/fire/shoreline/monitors/alarm).
- All binary-format logic was ported from **fallout2-ce** (https://github.com/alexbatalov/fallout2-ce), which remains the authoritative reference, with `// ported from ...` comments at each site.
- CLI debug tools: DatDump (archive list/extract), FrmDump (FRM→PNG), MapDump (map summary). Headless `--screenshot` mode for visual regression testing.
- Not implemented (was explicitly out of scope): critters/NPCs, animations beyond palette cycling, lighting, scripting, pathfinding, combat, UI, sound, worldmap, save/load, egg/transparency effects, mouse picking.

## Constraints

- Hobby project, one developer + AI pair programming; prefer milestones that produce something visible/testable every few sessions.
- Must stay legal: no game assets distributed, original GOG copy required, project name must not contain "Fallout" if ever published.
- Stack stays C#/.NET/MonoGame unless there's a compelling reason to change.
- fallout2-ce stays the reference implementation for any engine behavior.

## What I want researched

1. **Candidate continuation directions** — for each: what it involves technically, which fallout2-ce subsystems to port, estimated complexity, what the "demo moment" looks like, and what prior art exists (study post-mortems of DarkFO, Falltergeist, jsFO, and how fallout2-ce/fallout1-ce structure these subsystems):
   - a) Critter rendering + idle animations (composed FRM names, animation codes, direction handling) — the most visible missing piece.
   - b) Lighting engine (per-hex light levels, light maps, day/night) — what made the original's look.
   - c) Interactive walkthrough mode: mouse picking, dude movement with pathfinding on the hex grid, doors/stairs/elevators/exit grids actually working (a "walking simulator" without combat).
   - d) Script engine (INT bytecode interpreter) — how big is this really? What does fallout2-ce's interpreter look like? What's the minimal subset to make doors/dialog triggers work?
   - e) Sound (ACM format decoding, ambient + sfx).
   - f) Tooling direction instead: turn this into a modder-oriented map/asset inspector (web export? export to Tiled/glTF? diffing mods?). Who would actually use this — check what tools the Fallout modding community (NMA, FO2 Restoration Project, sfall/sfall-rs communities) currently lacks.
   - g) Full engine reimplementation ambition check: is there any niche fallout2-ce doesn't already fill (it runs the full game)? Be honest about whether "yet another engine" has value vs. tooling/education/portfolio value.
2. **Technical debt worth paying first** — e.g., palette-lookup shader (MonoGame mgfxc on Linux requires Wine — verify current state, or evaluate raw GLSL via Veldrid/raylib-cs/FNA alternatives), texture atlasing for draw-call reduction, a proper sprite-batching strategy for 2000+ objects per map.
3. **A recommended roadmap**: pick the best direction (or combination), break it into 4–6 milestones in the same style as before (each independently demoable, committed, tested), and list the specific fallout2-ce source files to study per milestone.
4. **Risks**: where did previous fan projects die, and which of those traps does each direction walk into?

## Deliverable

A report with: a comparison table of directions (effort / payoff / risk / fun), your single recommended path with rationale, the milestone breakdown for it, and links/references for everything non-obvious. If something can't be verified, say so explicitly rather than guessing.
