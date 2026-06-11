# Fallout 2 Map Viewer PoC

A proof-of-concept map viewer for Fallout 2, written in C# / .NET + MonoGame (DesktopGL).

> **Requires an original copy of Fallout 2 (GOG/Steam). No game assets are included
> or distributed. Not affiliated with Bethesda Softworks.**

Point it at your game directory (the one containing `master.dat`, `critter.dat`,
`patch000.dat`):

```sh
dotnet run --project src/FalloutPoc.Viewer -- --game-dir ./game-data --map artemple.map
```

## Progress

- [x] M1 — DAT2 archive reader (`master.dat` list/extract) + DatDump CLI
- [x] M2 — PAL + FRM parsers + FrmDump (FRM → PNG)
- [x] M3 — MAP parser (`artemple.map` summary)
- [x] M4 — Static floor render + camera pan
- [x] M5 — Objects + z-sorting + roof toggle
- [x] M6 — Palette color cycling

All six milestones are complete.

## Controls

| Input | Action |
| --- | --- |
| mouse drag / arrow keys | pan (hold Shift for fast) |
| hover / click | highlight object under cursor; click prints its PID/FID |
| click open ground | dude walks there (A* on the hex grid, camera follows) |
| click a door (adjacent) | opens/closes it — open doors stop blocking paths |
| click stairs/ladder (adjacent) | travel to their destination (may load another map) |
| walk onto an exit grid | map transition (e.g. Temple of Trials → Arroyo bridge) |
| R | toggle roofs |
| T | toggle critter walk cycle (in place) |
| PgUp / PgDn | switch elevation |
| Esc | quit |

Extra CLI flags: `--screenshot out.png` (render one frame and exit),
`--no-roofs`, `--advance-ms N` (pre-advance palette cycling — for testing),
`--bench N` (measure N uncapped frames, print timing report, exit),
`--walk` (start with critters walking — for testing),
`--pick X,Y` (print the object at a screen point — for testing),
`--goto TILE` (walk the dude to a hex tile after load — for testing),
`--door TILE` (toggle the door at a hex tile after load — for testing).

## Phase 2 — walking simulator

On top of the original viewer scope, the PoC now renders critters (composed
FRM names, correct directions), plays FRM animations (looping fires, critter
walk cycles), supports per-pixel mouse picking, moves a player stand-in with
A* hex pathfinding, and handles doors/exit grids/stairs **without any script
VM** — interactions are hardcoded, per the phase-2 research recommendation.
Combat, dialogs, and the INT script engine remain explicitly out of scope.

## Implementation notes

- Palette cycling updates a 256-entry palette per the original `cycle.cc`
  tables/periods and re-uploads **only** the textures whose FRMs contain
  cycling indices (229–255). The palette-lookup-shader variant was skipped
  because MonoGame's effect compiler needs Wine on Linux; the re-upload path
  touches a handful of small textures per tick, which is ample for a PoC.
- Critters/NPCs are not rendered (out of scope; their FRM names are composed
  from animation codes).

## Layout

- `src/FalloutPoc.Formats` — binary format parsers (DAT2/FRM/PAL/MAP/PRO), no MonoGame deps
- `src/FalloutPoc.Viewer` — MonoGame DesktopGL viewer
- `tools/` — CLI dump/debug tools
- `tests/` — xUnit tests (tests needing real game data skip unless `FALLOUT2_DIR` is set)

Binary formats are ported from [fallout2-ce](https://github.com/alexbatalov/fallout2-ce),
the reverse-engineered reimplementation of the original engine.

> **TODO before any public release:** rename project/package IDs to not contain
> the word "Fallout" (currently internal-only `FalloutPoc` namespaces).
