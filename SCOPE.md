# Scope

Hexwaste re-implements a **slice** of the Fallout 2 engine in C# / MonoGame,
ported from [fallout2-ce](https://github.com/alexbatalov/fallout2-ce). It is a
hobby project that grows one researched phase at a time, not a complete engine.
This page is the quick answer to "why doesn't it do X?" — please read it before
filing an issue.

## What's in

- **Formats**: DAT2 archives, FRM sprites + palette cycling, PAL, MAP, PRO,
  AAF fonts, MSG, ACM audio, GCD character sheets, worldmap/city/maps tables.
- **Rendering**: hex/square grids with correct z-sorting and draw order, roofs,
  per-vertex floor lighting, static light pools with occlusion, day/night clock,
  egg-style wall transparency, per-type object translucency (glass/steam/energy/
  red/wall), silhouette outlines.
- **A micro INT script VM** + script host: map-entry scripts, dialog (`gsay`
  trees, with IQ-gated dumb/smart options keyed to the dude's real Intelligence),
  examine, locks/lockpick, use / use-on-object, pickup, timers, spatial
  traps, the critter heartbeat, ~70 real engine externals.
- **Simulation**: A* movement, mouse picking, doors/stairs/exit grids, worldmap
  travel as a moving party dot paced by terrain (mountains slow it), with random
  encounters (`worldmap.txt` tables → transient encounter maps, groups spawned in
  formation), an Outdoorsman detect-and-avoid prompt, auto-resume after an
  encounter, save/resume mid-travel, ambient NPC life, sound
  (music/sfx/footsteps/combat).
- **Companions**: recruit (including Vic's legitimate VM-driven rescue), a
  wait/follow/dismiss/rejoin control hub, a flat 1:1 inventory trade panel, and
  per-companion proto level-ups (`party.txt`, live on the recruited Vic).
- **Gameplay**: turn-based melee + gun combat with the engine's depth — to-hit /
  line-of-fire (screen-Bresenham) / ammo+reload, single + burst fire (with the
  left/right collateral cone), AP-gated movement (a crippled leg crawls at 4× the
  AP/hex, a crippled arm blocks a two-handed weapon), AI behaviour packets
  (close-or-flee with real `_ai_run_away` retreat pathing), X-FIGHTING-Y team
  brawls (two spawned groups fight each other and you), critical hits + aimed
  called shots with their consequences (knockout + timed wake, lose-turn, crippled
  limbs, blindness — a Doctor mends limbs/eyes), knockback + persisting knockdown,
  area explosions, throwing (spears, grenades, recoverable, can crit), and gory
  death animations (sliced/charred/big-hole/exploded corpses by damage type);
  stealth (a two-layer Sneak state with a periodic skill roll; active sneaking
  shrinks an NPC's perception range so you can slip past scripted aggro; the Silent
  Death perk quadruples a melee backstab); armor, drugs, lootable corpses, scripted
  aggro, same-team joiners, a minimum
  party member, barter, kill XP, level-ups, per-map persistent world, versioned
  JSON save/load, a main menu, character creation, rest, and carry weight +
  encumbrance (item weights, a STAT_CARRY_WEIGHT capacity, an over-limit combat
  AP penalty, and pickup/loot/barter blocking).
- **Character progression**: the 16 optional **traits** apply real effects
  (Gifted, Bruiser, Small Frame, Kamikaze, Heavy Handed, Good Natured, One Hander,
  Fast Shot, Finesse, Jinxed, …), picked in character creation (up to two) or carried
  by a premade, and
  **perks** — the full 119-perk table, with selection every 3 levels (4 with
  Skilled) gated on level/stats/skills, the data-driven stat perks (Toughness,
  Action Boy, Lifegiver, More/Better Criticals, …) plus wired combat/skill perks
  (Bonus Rate of Fire, Bonus HtH Attacks, Bonus Ranged Damage, Sniper, Slayer,
  Silent Death, Sharpshooter, Swift Learner, Educated, Living Anatomy, Pyromaniac,
  Weapon Handling, Heave Ho). A character-sheet display + a level-up perk picker (the
  authentic PERKWIN window). Per-level skill points follow the full engine formula
  (Educated/Skilled/Gifted).
- **Interface**: the authentic bottom HUD bar plus its panels — inventory,
  character sheet, the Skilldex use-skill picker, the Pip-Boy (status + rest),
  and an in-game options/pause menu.

## What's out (by design, today)

- **Most quest chains** — the opening hour (Arroyo → Temple → Klamath/Den) is
  the target; reputation/karma badges and the slave-run path are not wired.
  Vic's rescue (#10) *is* wired end-to-end, with one residual content gap: the
  radio item (pid 266) is a multi-step Klamath quest reward with no in-slice
  source, so the recruit needs one `--give` to supply it.
- **Companion depth** — recruit/follow/fight/dismiss/rejoin/wait, 1:1 trade, and
  per-companion proto level-ups all work (phases 10 + #10), live on the
  legitimately-recruited Vic (`party.txt` member 13). A second `party.txt`
  recruit (Sulik/Marcus/etc.) needs its out-of-scope recruitment quest; the
  level-up logic lights up for free when one lands. Per-companion quest *banter*
  is 100% companion-script content gated on those same quests (it already runs
  via `talk_p_proc` when present), so there is no engine work to do. Per-companion
  *perks* have save + stat-application infrastructure (it reads the same
  `CritterState` path as the dude), but no shippable companion gains a perk —
  `party.txt` level-ups advance proto stages, not perks — so it stays inert until
  future content lands one.
- **The remaining ~80 perks' specific effects** (the table is complete and the
  stat perks + a curated combat/skill set are wired; the rest — sneak, timed
  buffs, content/dialog, addiction, mutation perks — are data-present), and
  worldmap car travel.
- **Anything needing assets we can't ship** — Hexwaste requires *your own* legal
  copy of Fallout 2. We never include or distribute game data.

If a feature is "out" above, an issue asking for it will be closed with a link
here. Bug reports about features that **are** in scope are very welcome.

The authoritative, evolving in/out list lives in `CLAUDE.md`; the per-phase
research reports in `docs/` record why each decision was made.
