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
| click a door (adjacent) | opens/closes it — scripts may lock it (map_enter runs for real) |
| L on a hovered locked door | lockpick |
| click a container/item (adjacent) | loot (1–9 take, A take all) / pick up |
| I | inventory (1–9 drop) |
| right-click an object | examine — name + description in the message log |
| click a critter (nearby) | talk — real scripted dialog (keys 1–9 or click to choose) |
| click stairs/ladder (adjacent) | travel to their destination (may load another map) |
| walk onto an exit grid | map transition (e.g. Temple of Trials → Arroyo bridge) |
| R | toggle roofs |
| T | toggle critter walk cycle (in place) |
| PgUp / PgDn | switch elevation |
| [ / ] | ambient light down/up (night ↔ day) |
| M | worldmap — click a location to travel there |
| Esc | quit |

Extra CLI flags: `--screenshot out.png` (render one frame and exit),
`--no-roofs`, `--advance-ms N` (pre-advance palette cycling — for testing),
`--bench N` (measure N uncapped frames, print timing report, exit),
`--walk` (start with critters walking — for testing),
`--pick X,Y` (print the object at a screen point — for testing),
`--goto TILE` (walk the dude to a hex tile after load — for testing),
`--door TILE` (toggle the door at a hex tile after load — for testing),
`--examine X,Y` (print/log name+description of the object at a screen point — for testing),
`--ambient F` (ambient light fraction 0.25–1.0, e.g. 0.25 for night),
`--worldmap` (open the worldmap on start), `--travel N` (travel to city.txt area N — for testing),
`--no-audio` (mute), `--no-ambient` (freeze NPC fidget/wander — for deterministic screenshots),
`--talk X,Y` / `--talk-hex TILE` + `--choose 1,2,1` (scripted conversation transcript — for testing).

## Phase 3 — the world becomes real

Native AAF font rendering with real game text (names + examine descriptions
from `pro_*.msg`), the full static lighting engine (occluded light pools,
day/night ambient), a click-to-travel worldmap, sound (a complete C# port of
the Interplay ACM decoder — door sfx, footsteps, per-map music), ambient NPC
life (engine-faithful fidget + faked wander), and a **micro INT-script VM**
(39 core opcodes, arity-stubbed externals) that runs real `look_at`/
`description` procedures — scripted examine text works, e.g. the Den's chem
addicts describe themselves with their authentic script lines.

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
