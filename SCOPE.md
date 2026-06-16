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
  trees), examine, locks/lockpick, use / use-on-object, pickup, timers, spatial
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
  area explosions, and throwing (spears, grenades, recoverable, can crit);
  armor, drugs, lootable corpses, scripted aggro, same-team joiners, a minimum
  party member, barter, kill XP, level-ups, per-map persistent world, versioned
  JSON save/load, a main menu, character creation, rest.
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
  via `talk_p_proc` when present), so there is no engine work to do.
- **Perks, traits, skill points beyond the gated skills**, the full character
  editor, and worldmap car travel.
- **Anything needing assets we can't ship** — Hexwaste requires *your own* legal
  copy of Fallout 2. We never include or distribute game data.

If a feature is "out" above, an issue asking for it will be closed with a link
here. Bug reports about features that **are** in scope are very welcome.

The authoritative, evolving in/out list lives in `CLAUDE.md`; the per-phase
research reports in `docs/` record why each decision was made.
