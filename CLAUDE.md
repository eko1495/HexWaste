# Fallout 2 Map Viewer PoC — Prime Directives

Proof-of-concept **Fallout 2 map viewer** in **C# / .NET + MonoGame (DesktopGL)**.

## Mission

1. Read original Fallout 2 game data from the user's legally owned copy (`--game-dir`, default `./game-data`). **Never copy, embed, or commit any game assets into this repository.**
2. Parse: **DAT2** archives (`master.dat`, `critter.dat`, `patch000.dat`), **FRM** sprites, **PAL** palettes (`color.pal`), **MAP** files, minimal **PRO** prototypes.
3. Render one complete map (default `artemple.map`) in a resizable window: floor tiles, toggleable roofs, static scenery/walls with correct z-sorting.
4. Palette color cycling (slime, fire, shoreline, monitors, alarm) at original speeds.
5. Camera: pan with mouse drag / arrow keys, optional integer zoom.

**Out of scope (do NOT build):** critters/NPCs, scripting, combat, pathfinding, UI, sound, worldmap, save/load.

## Authoritative reference

`reference/fallout2-ce` (cloned, gitignored) — port logic from there, never guess.
Key files: `src/dfile.cc` (DAT2), `src/db.cc` (VFS: loose files override DAT), `src/art.cc` (FRM),
`src/color.cc`/`src/palette.cc` (PAL + cycling), `src/map.cc` (MAP), `src/proto.cc` (PRO),
`src/tile.cc` (**hex/square grid ↔ screen math, draw order — most important for rendering**).

When porting, add a comment with the source: `// ported from fallout2-ce src/tile.cc tileToScreenXY()`.
If a format detail can't be confirmed from fallout2-ce sources, **stop and ask** instead of guessing.

## Layout

- `src/FalloutPoc.Formats` — pure .NET class library, zero MonoGame deps, unit-testable.
- `src/FalloutPoc.Viewer` — MonoGame DesktopGL app.
- `tools/DatDump`, `tools/FrmDump` — CLI demo/debug tools.
- `tests/FalloutPoc.Formats.Tests` — xUnit; tests needing real game files are guarded by env var `FALLOUT2_DIR` (skip when unset) so CI passes without assets.
- `game-data/` — extracted GOG game data (gitignored). `master.dat`, `critter.dat`, `patch000.dat`, `data/` live at its root.

## Milestones (commit after each)

Phase 1 (DONE): M1 DAT2 reader, M2 PAL+FRM, M3 MAP parsing, M4 static floor
render, M5 objects + z-sorting + roofs, M6 palette cycling.

Phase 2 — "walking simulator" (per research report
`compass_artifact_…_text_markdown.md`; NO combat, NO script VM — hard scope line):

0. **P2-M0** — DONE. Benchmark on newr1.map (heaviest: 2841 objects):
   avg 3.6 ms / p95 6.2 ms / max 13.6 ms full frame with cycling active —
   far under the 16 ms threshold. **Decision: CPU palette conversion stays;
   no shader, no Wine.** Simulation is wall-time driven, fixed 60 Hz update
   kept; `--bench N` measures uncapped frame cost; FPS shown in title.
1. **P2-M1** — static critters: FID→FRM name via critters.lst + anim-code
   suffix (`src/art.cc` `artBuildFilePath()`/`_art_get_code()`), correct
   direction, z-sorted with solid objects.
2. **P2-M2** — idle/breath animation + walk cycle in place (`src/animation.cc`);
   FRM frame offsets accumulate across frames.
3. **P2-M3** — mouse picking: per-pixel alpha hit-test in reverse draw order,
   hover shows PID/FID (`src/object.cc`, `src/tile.cc` screen↔hex).
4. **P2-M4** — dude movement: A* on hex grid (`src/path.cc`), blocking objects,
   walk along path, camera follow.
5. **P2-M5** — hardcoded interactions, no VM: doors (open/close animation),
   exit grids (map/elevation transition), stairs/ladders.

After each milestone: run tests, run the app if possible, update README progress checklist, conventional commit.

## Critical gotchas

- **Two grids**: floor/roof = 100×100 *square* grid; objects = 200×200 *hex* grid. Different coord→screen formulas; port both from `tile.cc`. Fallout's projection is oblique/trimetric, NOT standard 2:1 isometric.
- **Draw order**: floor → flat objects → non-flat objects in hex tile order → roofs.
- **PAL values are 0–63**: multiply by 4 and clamp for 8-bit RGB.
- **Roofs render shifted up 96 px** relative to their square tile.
- **FRM frame offsets accumulate** across frames; orientations may share the same data offset.
- **Transparent color = palette index 0.**
- Palette cycling must NOT re-decode whole textures per frame (killed jsFO). Keep 8-bit index data; prefer a palette-lookup shader with a 256×1 palette texture updated each cycle tick.
- DAT2 vs DAT1: Fallout 2 only (little-endian DAT2, zlib). Fallout 1 (DAT1, LZSS) is out of scope.

## Legal guardrails

- `.gitignore` excludes `*.dat`, `*.map`, `*.frm`, `*.pal`, `game-data/` — keep it that way.
- README must state: requires original Fallout 2 copy, no assets included, not affiliated with Bethesda Softworks.
- No "Fallout" in any distributable/package ID (internal `FalloutPoc` namespace OK for private PoC; README has TODO to rename before public release).

## Working style

- Small, reviewed steps over big-bang generation.
- Dependencies allowed: MonoGame, xUnit, SixLabors.ImageSharp (dump tools only). **Ask before adding anything else.**
- Streaming reads from DAT2 (`DeflateStream` at the right offsets); lazy-load FRMs with an LRU cache. Do not extract everything to memory.
