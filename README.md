# Hexwaste

A C# / .NET + MonoGame (DesktopGL) re-implementation of a slice of the
Fallout 2 engine: it loads the original data files and gives you a living
world — real maps, real scripts, dialog, looting, a persistent world across
map travel, save/load, and turn-based unarmed combat where the wasteland
fights back.

> **Requires an original copy of Fallout 2 (GOG/Steam). No game assets are
> included or distributed.** "Fallout" is a trademark of Bethesda Softworks;
> this project is not affiliated with or endorsed by Bethesda and names the
> game only to describe interoperability with your own legally obtained data.

![The Den](docs/screenshots/den-street.png)

| | |
| --- | --- |
| ![Temple at night](docs/screenshots/artemple-night.png) | ![Combat](docs/screenshots/combat.png) |

## Quick start

```sh
dotnet run --project src/Hexwaste.Viewer -- --game-dir "/path/to/Fallout 2"
```

The game directory is the install folder containing `master.dat`,
`critter.dat` and `patch000.dat`. Without `--game-dir`, Hexwaste probes the
usual GOG/Steam install paths and a `game-data/` folder next to the
executable.

Prebuilt self-contained builds (Linux x64 / Windows x64) are produced by
`scripts/release.sh` — see `docs/RELEASING.md`.

## What works

- **World**: DAT2 archives, FRM sprites + palette cycling, AAF fonts, full
  static lighting (light pools, day/night clock), roofs/egg transparency,
  sound (a C# port of the Interplay ACM decoder — music, sfx, footsteps),
  ambient NPC life (fidgets, wander, script-driven brahmin behavior).
- **Scripts**: a micro INT-bytecode VM with a real script host — map-entry
  scripts lock doors and stock containers, examine/dialog/lockpick/timer
  procedures run for real, script timers fire (doors auto-close behind you).
- **Dialog**: full `gsay` conversation trees with keyboard choices.
- **Persistent world**: per-map deltas keyed to the pristine map files —
  loot a footlocker in the Den, walk to the Temple and back, it stays
  looted; F5/F9 saves the whole visited world as JSON.
- **Combat**: turn-based fights with engine-accurate sequencing (outcome
  rolled before the animation, damage applied when it completes), AP
  budgets, melee weapons and armor (equip flags straight from the MAP
  files — enemies use their own spears), healing items, death falls and
  lootable corpses that stay dead across map travel, AI turns, same-team
  joiners, and script-driven aggro — temple ants jump you on sight. Kills
  pay XP at combat end; levels raise your HP. Lose, and F9 puts you back.
- **Character**: the dude's stats come from `premade\player.gcd`, so
  stat-gated dialog runs the right branches.
- **Barter**: real shopkeeper trade (Tubby's stock box and all) at the
  engine's price formula.
- **Worldmap**: click-to-travel between areas (`maps.txt`/`city.txt`).

## Controls

| Input | Action |
| --- | --- |
| mouse drag / arrow keys | pan (hold Shift for fast) |
| click open ground | walk there (A* on the hex grid) |
| click door / container / item / stairs | use / loot (1–9 take, A take all) / pick up / travel |
| click a critter | talk (real scripted dialog, 1–9 to choose; shopkeepers open a barter panel) |
| right-click | examine (critters show HP/AC) |
| F | attack the hovered critter (starts combat) |
| Space | end combat turn |
| L | lockpick the hovered door |
| I | inventory (1–9 use/equip/consume, Shift+1–9 drop) |
| C / K | character sheet (spend level-up skill points) |
| Z | rest to heal (when no enemies are near) |
| F5 / F9 | save / load |
| R | reload the equipped gun (2 AP in combat) |
| F4 / T / PgUp / PgDn | roofs / walk-cycle / elevation |
| [ / ] | ambient light (night ↔ day) |
| M | worldmap |
| Esc | quit |

A large set of `--flags` exists for headless testing (screenshots, scripted
dialog/combat transcripts, deterministic RNG); see `src/Hexwaste.Viewer/Program.cs`.

## Building

```sh
dotnet build          # .NET 10 SDK
dotnet test           # set FALLOUT2_DIR=/path/to/game for the data-backed tests
```

## Layout

- `src/Hexwaste.Formats` — engine + format code (DAT2/FRM/PAL/MAP/PRO/INT VM,
  script host, combat math, save model); no MonoGame dependencies, fully
  testable headless
- `src/Hexwaste.Viewer` — the MonoGame DesktopGL front end
- `tools/` — CLI dump/debug tools (DatDump, FrmDump, MapDump)
- `tests/` — xUnit suite
- `docs/` — provenance: the research reports and prompts that drove each
  phase, and `RELEASING.md`

## License & attribution

Hexwaste is a modified derivative of
[fallout2-ce](https://github.com/alexbatalov/fallout2-ce) (the
reverse-engineered engine re-implementation); ported routines carry
`// ported from fallout2-ce ...` comments. It is licensed under the
**Sustainable Use License v1.0** (`LICENSE.md`, `NOTICE.md`): free of charge,
non-commercial use and distribution only.
