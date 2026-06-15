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
  joiners, and script-driven aggro — temple ants jump you on sight. Enemies
  read their `ai.txt` packets: they close to a viable shot and flee when badly
  wounded, instead of fighting to the last hit point. Critical hits (from in-game
  day 2, like the engine) roll off the real Fallout 2 crit tables — bonus damage,
  armor bypass, the occasional instant kill; aim a called shot (V) at eyes/head/
  legs to trade accuracy for a far better critical. And criticals have
  consequences (the engine's massive-critical roll): a blow can blind, cripple a
  limb, knock a target out cold (it wakes after a spell), or cost it a turn — a
  blinded foe fights at −25, and the Skilldex Doctor skill mends limbs and eyes.
  Moving in combat costs action points per hex (you can't free-walk the map mid-
  fight), a crippled leg costs 4× so it crawls, and a crippled arm blocks a
  two-handed weapon (both arms block any weapon). Big melee blows knock the
  target sprawling back along the hex line; a crit leaves it prone (+40 to hit,
  3 AP to stand). Throw a spear or rock (Throwing skill, range scaled by
  Strength) — it lands recoverable on the ground — or lob a grenade for an
  area blast with falloff and knockback. With a burst gun (10mm SMG, Tommy
  Gun, combat shotgun) hold the trigger (B): the magazine sprays a cone of
  rounds — in a duel a few find the target and the rest chew up the scenery,
  the same spread the engine rolls. Kills pay XP at combat end;
  levels raise your HP. Lose, and F9 puts you back.
- **Character**: create your own (SPECIAL, gender, tagged skills) or pick a
  premade; level-ups grant skill points you spend on the character sheet (C),
  and stat-gated dialog runs the right branches. Rest (Z) heals over time.
- **Barter**: real shopkeeper trade (Tubby's stock box and all) at the
  engine's price formula.
- **Worldmap**: click-to-travel between areas (`maps.txt`/`city.txt`) — a party
  dot crosses the map, paced by terrain (mountains slow it), and the trip can be
  saved/resumed mid-walk. The wasteland bites — travel rolls random encounters
  (`worldmap.txt` tables) and drops you onto a transient encounter map with the
  named group spawned in formation (rats, scorpions, war parties, slavers). A
  high Outdoorsman spots
  the encounter ahead and offers a Yes/No to avoid it (for XP); walk off the
  edge and travel auto-resumes toward your destination. An X-FIGHTING-Y
  encounter spawns its two groups on opposing teams so you stumble into a
  brawl already in progress — watch them thin each other out, or wade in.
- **Companions**: recruit, then a control hub (talk to them) — wait here /
  follow / trade / dismiss / rejoin; a 1:1 flat item trade to gear them up.
- **HUD**: the authentic Fallout 2 interface bar (`iface.frm`) along the bottom —
  the green scrolling message monitor, the equipped-weapon slot with its attack-mode
  label, lit action-point pips, the HP/AC readout in the original digit font, the END
  TURN / END COMBAT buttons during a fight, and clickable INV/OPT/MAP/CHA/SKILLDEX/PIP
  tabs (the keyboard shortcuts still work). The SKILLDEX tab opens the use-skill picker —
  choose a skill (Lockpick, First Aid, Doctor, Steal, Traps, Science, Repair, Sneak)
  and click a target to apply it. The PIP tab opens the Pip-Boy: a status page (date/
  time, level, HP/AC) and a rest menu (timed rests or rest-until-healed). The OPT tab
  (or Esc) opens the options/pause menu: save, load, main menu, quit, resume. Every
  panel and menu is fully mouse-navigable — click a row in the inventory/loot/barter/
  trade lists (PgUp/PgDn page past the 9th item) or in the Pip-Boy/Options menus; the
  keyboard shortcuts still work alongside.

## Controls

| Input | Action |
| --- | --- |
| mouse drag / arrow keys | pan (hold Shift for fast) |
| click open ground | walk there (A* on the hex grid) |
| click door / container / item / stairs | use / loot (click a row or 1–9 take, A take all) / pick up / travel |
| click a critter | talk (real scripted dialog, 1–9 to choose; shopkeepers open a barter panel) |
| right-click | examine (critters show HP/AC) |
| F | attack the hovered critter (starts combat) |
| B | spray a burst at the hovered critter (needs a burst gun: SMG/Tommy/combat shotgun) |
| V | cycle the called-shot location (aimed shot) |
| Space | end combat turn |
| L | lockpick the hovered door (the Skilldex Lockpick skill) |
| S | Skilldex — pick a skill (1–8), then click a target to use it |
| P | Pip-Boy — status page + rest (R for the rest menu, 1–9 pick a duration) |
| I | inventory (click an item, or 1–9 use/equip/consume, Shift+1–9 drop) |
| C / K | character sheet (spend level-up skill points) |
| Z | rest to heal (when no enemies are near) |
| F5 / F9 | save / load |
| R | reload the equipped gun (2 AP in combat) |
| F4 / T / PgUp / PgDn | roofs / walk-cycle / elevation (PgUp/PgDn page an open item panel) |
| [ / ] | ambient light (night ↔ day) |
| M | worldmap |
| Esc | options / pause menu (save / load / main menu / quit / resume) |

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
