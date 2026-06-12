# Claude Code Prompt — Fallout 2 Map Viewer PoC (MonoGame / .NET)

> Skopiuj wszystko poniżej tej linii jako pierwszy prompt do Claude Code,
> uruchomionego w pustym katalogu projektu.

---

## Mission

Build a proof-of-concept **Fallout 2 map viewer** in **C# / .NET 9 + MonoGame (DesktopGL)**.

The PoC must:
1. Read original Fallout 2 game data directly from the user's legally owned copy (path provided via config/CLI argument, e.g. `--game-dir ~/Games/Fallout2`). **Never copy, embed, or commit any game assets into this repository.**
2. Parse the required binary formats: **DAT2** archives (`master.dat`, `critter.dat`), **FRM** sprites, **PAL** palettes (`color.pal`), and **MAP** files.
3. Render **one complete map** (default: `artemple.map` — Temple of Trials) in a resizable window: floor tiles, roof tiles (toggleable), and static scenery/wall objects with correct draw order (z-sorting).
4. Implement **palette color cycling** (animated palette indices: slime, fire, shoreline, monitors, alarm) so torches/computers animate exactly like in the original.
5. Support basic camera: pan with mouse drag / arrow keys, optional zoom (integer scaling).

Out of scope for this PoC: critters/NPCs, scripting, combat, pathfinding, UI, sound, worldmap, save/load. Do not build any of these. Resist scope creep.

## Primary reference implementation

Before writing any parser, **clone and study `https://github.com/alexbatalov/fallout2-ce`** (reverse-engineered, faithful C++ reimplementation of the original engine). Use it as the authoritative spec for all binary formats and rendering rules. Key files to read:

- `src/dfile.cc` / `src/dfile.h` — DAT2 archive format (zlib-compressed entries, directory tree at end of file).
- `src/db.cc` — virtual file system layering DAT archives + loose files (loose files override DAT contents — replicate this).
- `src/art.cc` / `src/art.h` — FRM format: header, up to 6 orientations, frames-per-direction, per-frame offsets, action offsets, 8-bit indexed pixels.
- `src/color.cc` / `src/palette.cc` — PAL format (768 bytes RGB, values 0–63 → multiply by 4), color cycling tables and cycle speeds.
- `src/map.cc` / `src/map.h` — MAP format: header, global/local vars, up to 3 elevations, 100×100 floor+roof tile grids, object list per elevation.
- `src/proto.cc` — PRO prototypes (needed only minimally, to resolve object FRM IDs and flags like "do not render" / transparency).
- `src/tile.cc` — **the most important file for rendering**: hex grid ↔ screen coordinate math, square tile grid ↔ screen math, draw order, and the relationship between the 200×200 hex grid and the 100×100 square tile grid.

Also consult `https://falloutmod.fandom.com/wiki/MAP_File_Format` and the `fallout1-ce` repo when format details differ between F1 (DAT1, LZSS) and F2 (DAT2, zlib). **Target Fallout 2 only** in this PoC.

When a format detail is ambiguous, port the logic from fallout2-ce rather than guessing. Add a comment with the source file/function name you ported from (e.g. `// ported from fallout2-ce src/tile.cc tileToScreenXY()`).

## Architecture requirements

- Solution layout:
  - `src/FalloutPoc.Formats` — pure .NET class library: DAT2 reader, FRM/PAL/MAP/PRO parsers. **Zero MonoGame dependencies.** Fully unit-testable.
  - `src/FalloutPoc.Viewer` — MonoGame DesktopGL app: rendering, camera, input, palette cycling.
  - `tests/FalloutPoc.Formats.Tests` — xUnit tests for parsers (header parsing, entry counts, known offsets). Tests that need real game files must be skippable (`[SkippableFact]` or env-var guard `FALLOUT2_DIR`) so CI passes without assets.
- .NET 9, `dotnet new mgdesktopgl` template (install `MonoGame.Templates.CSharp` if missing). Must build and run on Linux (primary dev OS: Fedora) and Windows.
- Streaming reads from DAT2 (`DeflateStream` over the right offsets) — do not extract everything to memory; lazy-load FRMs with an LRU cache.
- Convert 8-bit indexed FRM pixels to RGBA textures on load, but **keep the palette index data** so color cycling can be done either by re-uploading affected texture regions or (preferred) by a small shader/effect that does palette lookup from a 256×1 palette texture updated each cycle tick. Choose the palette-texture approach if feasible in MonoGame — it makes cycling free.
- Transparent color: palette index 0.
- Config: game directory via CLI arg `--game-dir` or `appsettings.json`; map name via `--map artemple.map`.

## Milestones (commit after each, in order)

1. **M1 — DAT2 reader**: list contents of `master.dat`, extract any file by virtual path. CLI demo: `dotnet run --project tools/DatDump -- --game-dir ... list`. Unit tests for directory parsing.
2. **M2 — PAL + FRM**: parse `color.pal`, decode an FRM to PNG via a small dump tool (proves pixels + palette are correct). Verify against a known sprite.
3. **M3 — MAP parsing**: parse `artemple.map` header, tile grids, object list; print summary (elevations, tile counts, object counts).
4. **M4 — Static render**: MonoGame window showing floor tiles of elevation 0 with correct rhombus layout and camera pan.
5. **M5 — Objects + z-sorting**: render scenery/walls from the object list in correct hex order; roofs toggle with a key.
6. **M6 — Palette cycling**: animated indices cycling at original speeds (see `palette.cc`/`color.cc` cycle tables).

After each milestone: run tests, run the app if possible, update `README.md` progress checklist, commit with a conventional commit message.

## Critical gotchas (learn from dead projects — DarkFO, Falltergeist, jsFO)

- **Two grids**: floor/roof tiles live on a 100×100 *square* grid; objects live on a 200×200 *hex* grid. They have different coordinate→screen formulas. Port both from `tile.cc`; do not invent your own isometric math — Fallout's projection is oblique/trimetric, not standard 2:1 isometric.
- **Draw order**: floor → flat objects → non-flat objects sorted by hex tile order → roofs. Get this from how fallout2-ce iterates in its render loop.
- **PAL values are 0–63**, multiply by 4 (and clamp) for 8-bit RGB.
- **Roof offset**: roofs render shifted up by 96 px relative to their square tile.
- **FRM frame offsets accumulate** across frames; orientation data may be shared (same data offset for multiple directions).
- Palette cycling done naively (re-decoding whole textures per frame) killed jsFO's performance — hence the palette-lookup shader recommendation.

## Legal guardrails

- `.gitignore` must exclude `*.dat`, `*.map`, `*.frm`, `*.pal`, and any `game-data/` directory from day one.
- README must state: "Requires an original copy of Fallout 2 (GOG/Steam). No game assets are included or distributed. Not affiliated with Bethesda Softworks."
- Project name must not contain the word "Fallout" in any distributable/package ID (repo-internal namespace `FalloutPoc` is acceptable for a private PoC; flag this in README as TODO before any public release).

## Working style

- Start by writing `CLAUDE.md` with these prime directives, the milestone list, and the gotchas above, so future sessions keep context.
- Prefer small, reviewed steps over big-bang code generation. Ask me before adding any dependency beyond MonoGame, xUnit, and (optionally) `SixLabors.ImageSharp` for the PNG dump tool.
- If a binary format detail cannot be confirmed from fallout2-ce sources, stop and tell me instead of guessing.

Begin with M1. First action: clone fallout2-ce into `./reference/fallout2-ce` (add `reference/` to `.gitignore`), read `src/dfile.cc`, then design the `Dat2Archive` API and its tests.
