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
- [ ] M6 — Palette color cycling

## Layout

- `src/FalloutPoc.Formats` — binary format parsers (DAT2/FRM/PAL/MAP/PRO), no MonoGame deps
- `src/FalloutPoc.Viewer` — MonoGame DesktopGL viewer
- `tools/` — CLI dump/debug tools
- `tests/` — xUnit tests (tests needing real game data skip unless `FALLOUT2_DIR` is set)

Binary formats are ported from [fallout2-ce](https://github.com/alexbatalov/fallout2-ce),
the reverse-engineered reimplementation of the original engine.

> **TODO before any public release:** rename project/package IDs to not contain
> the word "Fallout" (currently internal-only `FalloutPoc` namespaces).
