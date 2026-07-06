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
  static lighting (light pools, day/night clock, script-set ambient — caves dim
  to their cavern level on entry), roofs/egg transparency,
  sound (a C# port of the Interplay ACM decoder — music, sfx, footsteps),
  ambient NPC life (fidgets, wander, script-driven brahmin behavior).
- **Scripts**: a micro INT-bytecode VM with a real script host — map-entry
  scripts lock doors and stock containers, examine/dialog/lockpick/timer
  procedures run for real, script timers fire (doors auto-close behind you).
- **Dialog**: full `gsay` conversation trees with keyboard choices, and the
  iconic Fallout 2 animated talking head above the reply (the real per-NPC head
  art, idling on its fidget animation) whenever a script supplies one.
- **Persistent world**: per-map deltas keyed to the pristine map files —
  loot a footlocker in the Den, walk to the Temple and back, it stays
  looted; F5/F9 saves the whole visited world as JSON.
- **Combat**: turn-based fights with engine-accurate sequencing (outcome
  rolled before the animation, damage applied when it completes) and
  initiative-ordered turns (every combatant interleaved by their Sequence
  stat each round — a faster foe acts before you), AP
  budgets, melee weapons and armor (equip flags straight from the MAP
  files — enemies use their own spears), healing items, death falls (a
  hefty burst, laser, or explosion leaves a suitably gory corpse) and
  lootable corpses that stay dead across map travel, AI turns, same-team
  joiners, and script-driven aggro — temple ants jump you on sight. Enemies
  read their `ai.txt` packets: they close to a viable shot, flee when badly
  wounded, draw a carried backup weapon (per their packet's weapon preference)
  when their gun runs dry, and — if they're the type that carries chems — gulp a
  stimpak mid-fight when you've hurt them, instead of fighting to the last hit
  point. Critical hits (from in-game
  day 2, like the engine) roll off the real Fallout 2 crit tables — bonus damage,
  armor bypass, the occasional instant kill; press V for the called-shot dialog
  and click a body part — eyes/head/legs — to trade accuracy for a far better
  critical. And criticals have
  consequences (the engine's massive-critical roll): a blow can blind, cripple a
  limb, knock a target out cold (it wakes after a spell), or cost it a turn — a
  blinded foe fights at −25, and the Skilldex Doctor skill mends limbs and eyes.
  Misses can backfire too: a critical FAILURE makes a fighter fumble — lose the
  turn, drop or wreck the weapon, hurt itself, even wing a bystander (your own
  fumbles only bite once you're a seasoned wastelander — day 6, like the engine).
  Moving in combat costs action points per hex (you can't free-walk the map mid-
  fight), a crippled leg costs 4× so it crawls, and a crippled arm blocks a
  two-handed weapon (both arms block any weapon). Action points you don't spend
  aren't wasted: leftover AP harden you into a temporary dodge — every unspent
  point adds to your Armor Class until your next turn, so a fighter who blows
  everything attacking is easy to hit, while one who holds back is slippery (an
  unarmed brawler with Hand-to-Hand Evade dodges twice as hard). Big melee blows knock the
  target sprawling back along the hex line; a crit leaves it prone (+40 to hit,
  3 AP to stand). Throw a spear or rock (Throwing skill, range scaled by
  Strength) — it lands recoverable on the ground — or lob a grenade for an
  area blast with falloff and knockback. With a burst gun (10mm SMG, Tommy
  Gun, combat shotgun) hold the trigger (B): the magazine sprays a cone of
  rounds — in a duel a few find the target and the rest chew up the scenery,
  the same spread the engine rolls. You choose what to load: unload (Shift+R) and
  use another ammo box to switch between, say, armor-piercing and hollow-point —
  AP punches through a foe's armor, JHP doubles the damage to an unarmored one.
  Every blow floats its damage over the target — a red number for a hit (a lighter
  shade when it's you taking it), yellow for a critical, a white "Missed" for a
  whiff — rising and fading above the struck critter (the real Fallout 2 prints
  combat results to the message log; Hexwaste floats them too, reusing the engine's
  own text-object timing, placement and colours).
  Sneak (the Skilldex Sneak skill toggles it):
  a periodic skill roll decides whether you're really hidden, and while you are an
  NPC's perception range shrinks — sneak wide around a foe and its scripted aggro
  never fires; with the Silent Death perk a melee strike from behind hits for 4×.
  Chems give a temporary edge — Buffout, Jet and the like boost your SPECIAL stats
  for a while, then wear off through a comedown that leaves you briefly weaker before
  it settles back — but lean on them and you can get addicted: after a while withdrawal
  saps the matching stats until it passes (Jet's is permanent without the antidote).
  Kills pay XP at combat end and are tallied by type on your character sheet; levels
  raise your HP. The game difficulty (`--difficulty easy|normal|hard`) scales how hard
  the wasteland hits back — on Easy everything not on your team deals 75% damage, on Hard
  125% (the engine's combat-difficulty modifier; your own and your companions' blows are
  untouched). Lose, and F9 puts you back.
- **Game shell**: the authentic Fallout 2 front door — the real `mainmenu.frm`
  main menu (mouse + keyboard), the `pickchar.frm` premade selector (portrait, SPECIAL,
  bio), the `edtrcrte.frm` character-creation editor (point-buy SPECIAL with steppers,
  skill tagging, optional traits, a stat/skill description card), a scrolling
  `credits.txt` roll, and the iconic `death.frm` death screen when you fall.
- **Character**: create your own (SPECIAL, gender, optional traits, tagged skills)
  in the authentic creation editor, or pick a premade from the selector; level-ups
  grant skill points you spend on the character sheet (C),
  and stat-gated dialog runs the right branches — including IQ-gated options, so a
  low-Intelligence character gets the dumb lines and a bright one the smart ones.
  Skill books (Guns and Bullets, Scout Handbook…) raise their skill when read, with
  diminishing returns up to 100 — and a smarter character reads them faster.
  Rest (Z) heals over time. The character sheet and Pip-Boy show your karma and
  reputation — a reputation title (`genrep.txt`), per-town standings (Vilified…
  Idolized) and earned karma titles, read straight from the engine's stats/GVARs
  (the engine never auto-awards karma, so on this slice it stays neutral unless a
  quest sets it).
- **Traits & perks**: the 16 optional traits do real things (Gifted, Bruiser,
  Heavy Handed, Good Natured, One Hander, Fast Shot, Finesse, Jinxed…) — chosen at
  character creation or baked into a premade, and they feed combat (to-hit, AP,
  damage, criticals) and your per-level skill points (Educated, Skilled, Gifted).
  You pick a perk every 3 levels (G on the
  character sheet) from the ones you qualify for — Toughness, Action Boy, Sniper,
  Bonus Rate of Fire, Lifegiver and more, shown on the sheet with your traits.
- **Encumbrance**: items have weight, and your Strength sets a carry limit
  (shown on the inventory panel and Pip-Boy, red when you're over). Overload and
  you can't pick up / loot / buy more, and you lose action points in combat.
- **Barter**: real shopkeeper trade (Tubby's stock box and all) at the
  engine's price formula.
- **Worldmap**: click-to-travel between areas (`maps.txt`/`city.txt`) — a party
  dot crosses the map, paced by terrain: crossing mountains genuinely costs more
  game time (and car fuel) per pixel, exactly like the original's walk loop, not
  just a slower animation. The trip can be saved/resumed mid-walk. The map is fogged: subtiles you haven't been near stay
  black, ones you've glimpsed are dimmed, and the corridors you've walked are
  clear — and hidden sub-areas (Car Outta Gas, the toxic caves) only put a marker
  on the map once you've explored near them. The wasteland bites — travel rolls
  random encounters (`worldmap.txt` tables) and drops you onto a transient
  encounter map with the named group spawned in formation (rats, scorpions, war
  parties, slavers). A high Outdoorsman spots
  the encounter ahead and offers a Yes/No to avoid it (for XP); walk off the
  edge and travel auto-resumes toward your destination. An X-FIGHTING-Y
  encounter spawns its two groups on opposing teams so you stumble into a
  brawl already in progress — watch them thin each other out, or wade in.
- **Locations**: the opening hour (Arroyo → Temple → Klamath → Den) plus
  **Vault City**, **Gecko**, **Modoc**, **Broken Hills**, **New Reno**, **NCR**,
  **San Francisco**, **Redding**, and **Vault 15** — the first nine towns past the slice.
  Travel there from the worldmap and every map (VC: Courtyard / Downtown / Council / Vault;
  Gecko: Settlement / Power Plant / Junkyard / Tunnels; Modoc: Main Street / Inn / Well /
  Outhouse; Broken Hills: the two town halves + desert/mountain sub-maps; New Reno: the
  four strip maps + interiors / boxing arena / chop shop / stables / casinos, 11 in all;
  NCR: Bazaar / Downtown / Council / entrance + courthouse; San Francisco: the Shi
  Chinatown / docks / Hubologist base / the PMV Valdez tanker / shuttle; Redding: Downtown
  + tunnels + the Kokoweef and Wanamingo mines; Vault 15: the squatter camp + entrances +
  the deep original-vault levels) loads, walks, and transitions; every external their
  scripts fire is wired, and their NPCs talk via the real dialogue VM. A new town is mostly
  *content*: the data-driven engine routes, loads, and renders it for free — the per-town
  work is just wiring the handful of script externals it needs (the `--smoke <map>` dev
  command scopes them), and each town makes the next cheaper (Gecko needed *no* new
  externals; Modoc four; Broken Hills' town none; New Reno, the biggest, five; NCR, San
  Francisco, Redding, and Vault 15 none — the wired set now covers them outright; the
  **Sierra Army Depot** robot dungeon needed two, the **Mariposa Military Base**
  super-mutant dungeon none, **Navarro** the Enclave base none, the **Enclave Oil Rig**
  endgame none) — the entire original-game map set now loads, walks, and runs its scripts.
  Quest machinery is in place — Vault
  City's citizenship test, Gecko's reactor terminal, Lenny's / Marcus's / Myron's / the
  Vault 15 Doc's / Sierra's Skynet recruitment (real `party.txt` companions), and Modoc's
  "Jonny in the Well" dialogue + scripted well; finishing a specific quest is content
  navigation, not engine work.
- **Companions**: recruit, then a control hub (talk to them) — wait here /
  follow / trade / dismiss / rejoin; a 1:1 flat item trade to gear them up; and
  a combat-control window (the authentic `CONTROL.frm` art) to set their tactics
  (aggressive/defensive disposition, who to target, when to flee, how close to stay,
  when to burst-fire, which weapon to favour) — which the ally AI obeys.
- **HUD**: the authentic Fallout 2 interface bar (`iface.frm`) along the bottom —
  the green message monitor (a 100-line scroll-back history — click its top/bottom
  halves to page through it), the equipped-weapon slot with its attack-mode
  label, lit action-point pips, the HP/AC readout in the original digit font, the END
  TURN / END COMBAT buttons during a fight, and clickable INV/OPT/MAP/CHA/SKILLDEX/PIP
  tabs (the keyboard shortcuts still work). The SKILLDEX tab opens the use-skill picker —
  choose a skill (Lockpick, First Aid, Doctor, Steal, Traps, Science, Repair, Sneak)
  and click a target to apply it. The PIP tab opens the Pip-Boy: a status page (the
  real Fallout 2 calendar date, level, HP/AC, an embedded mini-map) and a rest menu
  (timed rests or rest-until-healed); press A for the full-window automap, which
  reveals as you explore (fog-of-war). The OPT tab
  (or Esc) opens the options/pause menu: save, load, main menu, quit, resume — and the
  10-slot save/load picker renders its authentic `LSGAME.frm` window. The inventory
  (`INVBOX.frm` paperdoll), container looting (`loot.frm`), merchant barter
  (`barter.frm`) and companion trade (`trade.frm`) all render on their real Fallout 2
  window chrome. Every
  panel and menu is fully mouse-navigable — click a row in the inventory/loot/barter/
  trade lists (PgUp/PgDn page past the 9th item) or in the Pip-Boy/Options menus; the
  keyboard shortcuts still work alongside. The called-shot picker is the real
  body-diagram window — the target's wireframe called-shot portrait with per-species
  part names and the live to-hit % rendered in the original digit bezels — and
  elevators open their authentic per-location panel art with the animated floor
  gauge sweeping during the ride. With the Empathy perk your dialogue options are
  tinted by the NPC's reaction. Maps fade in from black as you travel between them.

## Controls

| Input | Action |
| --- | --- |
| mouse drag / arrow keys | pan (hold Shift for fast) |
| mouse wheel | zoom the world 1×–4× (integer magnify; the HUD stays native) |
| click open ground | walk there (A* on the hex grid) |
| click door / container / item / stairs | use / loot (click a row or 1–9 take, A take all) / pick up / travel |
| click a critter | talk (real scripted dialog, 1–9 to choose; shopkeepers open a barter panel) |
| right-click | examine (critters show HP/AC) |
| F | attack the hovered critter (starts combat) |
| B | spray a burst at the hovered critter (needs a burst gun: SMG/Tommy/combat shotgun) |
| V | open the called-shot dialog (click a body part to aim) |
| Space | end combat turn |
| L | lockpick the hovered door (the Skilldex Lockpick skill) |
| S | Skilldex — pick a skill (1–8), then click a target to use it |
| P | Pip-Boy — status page, rest (R for the rest menu, 1–9 pick a duration), and the Archives quest log |
| I | inventory (drag an item onto the weapon/armor slot to equip, or click / 1–9 use/equip/consume, Shift+1–9 drop) |
| C / K | character sheet (spend level-up skill points) |
| Z | rest to heal (when no enemies are near) |
| F5 / F9 | quicksave / quickload (default slot) |
| R / Shift+R | reload the equipped gun (2 AP in combat) / unload it (to switch ammo type) |
| F4 / T / PgUp / PgDn | roofs / walk-cycle / elevation (PgUp/PgDn page an open item panel) |
| [ / ] | ambient light (night ↔ day) |
| M | worldmap |
| Esc | options / pause menu (save / load → a 10-slot picker / main menu / quit / resume) |

A large set of `--flags` exists for headless testing (screenshots, scripted
dialog/combat transcripts, deterministic RNG); see `src/Hexwaste.Viewer/Program.cs`.

## Building & testing

```sh
dotnet build          # .NET 10 SDK
dotnet test           # set FALLOUT2_DIR=/path/to/game for the data-backed tests

# Golden regression nets — deterministic headless transcripts diffed against
# committed fixtures (need a display + your game data). Run after touching
# combat or worldmap/encounter code:
scripts/combat-golden.sh      # combat transcripts (check | record)
scripts/encounter-golden.sh   # worldmap / encounter / companion / panel state lines
```

The data-backed unit tests skip cleanly when `FALLOUT2_DIR` is unset, so a
plain `dotnet test` passes without any game assets.

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
