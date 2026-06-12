# Changelog

## v0.6.0 — 2026-06-13 (first public release)

The opening hour of Fallout 2 plays end to end on the user's own game data.

- **World**: DAT2 archives, FRM sprites + palette cycling, AAF fonts, MAP/PRO
  parsing validated on 150+ maps, full static lighting with day/night clock,
  sound (a C# port of the Interplay ACM decoder — music, sfx, footsteps),
  ambient NPC life, worldmap click-to-travel.
- **Scripts**: a micro INT-bytecode VM and script host — map-entry scripts
  lock doors and stock shops, dialog trees (`gsay`), lockpicking, script
  timers, a critter_p_proc heartbeat (temple ants ambush you on sight),
  cross-script external variables, ~60 real engine externals.
- **Character**: stats from `premade\*.gcd` sheets gate dialog the way the
  designers wrote it; kill XP at combat end; level-ups raise HP.
- **Combat**: turn-based with the engine's sequencing (roll before the
  animation, damage when it completes), AP budgets, melee weapons and armor
  (enemies use their own MAP-equipped spears), healing items, lootable
  corpses that stay dead, same-team joiners, game over → load.
- **Persistent world**: per-map deltas over pristine map files — loot,
  doors, corpses and shop stock survive map travel; versioned JSON saves.
- **Barter**: real shopkeeper trade at the engine's price formula.
- **Front door**: main menu with premade-character picker; a death screen.

Requires an original copy of Fallout 2 (GOG/Steam). No game assets included.
Licensed under the Sustainable Use License v1.0 (see LICENSE.md, NOTICE.md).
