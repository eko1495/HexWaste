# Changelog

## v0.10.0 — 2026-06-14

The wasteland bites back — random encounters and companions.

- **Random encounters**: travelling the worldmap now rolls encounters along
  the way (the real `worldmap.txt` tables, the engine's Δ3 / daypart-frequency /
  weighted-pick chain). When the wasteland bites, an encounter map loads with
  the group spawned in formation — rats and scorpions huddled, a war party in a
  wedge with wielded spears, slavers, bounty hunters — and they aggro on sight.
  Walk off the map edge to return to the worldmap and carry on; re-clicking
  resumes the trip.
- **One-shot encounters persist**: consumed special encounters and your
  worldmap position round-trip through save/load (additive within the V2 save).
- **Companions**: recruit a follower, then talk to them for a control hub —
  *wait here* / *follow me* / *trade* / *dismiss* / *rejoin* (and *talk* for
  their own dialog). Dismiss restores their original team and leaves them put;
  rejoin (if they're alive) brings them back.
- **Companion trade**: a flat 1:1 inventory swap (no caps, no haggling) to hand
  your companion better gear — and giving away worn armour correctly takes its
  bonus off you first.
- **Fixes**: party members no longer duplicate across an F5 save; the encounter
  spawn, travel, and companion lifecycle are all deterministic under `--rng-seed`.

## v0.9.0 — 2026-06-13

Combat depth II — the fight gets tactical.

- **Extract-first refactor**: the turn machine is now an engine-free
  `Hexwaste.Formats.Combat.CombatEngine` behind a host interface — unit-tested
  with no GraphicsDevice for the first time, and locked by a golden-transcript
  harness that diffs combat output byte-for-byte.
- **Smarter enemies**: NPCs read their `ai.txt` behaviour packets — they close
  to a viable shot and flee when badly wounded instead of fighting to the last
  hit point; a fled enemy disengages so combat actually ends.
- **Critical hits**: from in-game day 2 (like the engine), attacks roll off the
  real Fallout 2 critical tables — bonus damage, armour bypass, the occasional
  instant kill.
- **Aimed shots**: cycle a called shot (V) to eyes/head/legs/etc. — harder to
  hit (+1 AP) but a far better critical.
- **Knockdown & knockback**: big melee blows shove the target sprawling back
  along the hex line; a crit leaves it prone (+40 to hit, 3 AP to stand).
- **Explosives & throwing**: throw a spear or rock (Throwing skill, range scaled
  by Strength — it lands recoverable) or lob a grenade for an area blast with
  damage falloff and knockback.

## v0.8.0 — 2026-06-13

The character comes alive — progression, creation, and survivability.

- **Skills grow**: level-ups award skill points (5 + 2*IN, banked); a
  character sheet (C/K) spends them past the engine's cost ramp. Two
  shipped-build bugs fixed: tagged skills now get the +20 and double-rate
  they were missing, and the female premade renders + screams female.
- **Character creation**: roll your own at New Game — allocate SPECIAL,
  pick a gender, tag three skills, with a live derived-stat readout. Or
  pick a premade. Created characters save self-contained.
- **Rest to heal** (Z): recover HP over game-hours when no enemies are
  near — no more permanent attrition death-spiral.
- **Merchants restock**: shop stock refreshes after a few game-days, so a
  gun build can't permanently run dry. Looted world containers stay looted.
- **Ops**: GitHub Actions CI (build + data-free tests), issue templates,
  and a public SCOPE.md.

## v0.7.0 — 2026-06-13

- **Guns**: single-shot pistols/rifles with the engine's ranged to-hit
  (distance/perception falloff, ammo AC/DR modifiers, min strength, crowd
  penalty), line-of-fire (walls block, bystanders eat the −10), magazines
  and caliber-matched reloading (R). The Den shoots back with its own
  MAP-equipped weapons. Weapon attack and death sounds.
- **Party**: recruit a companion — they follow via their own scripts,
  travel with you across maps, fight on your side with their own gear,
  and survive save/load. (No companion inventory/level management yet.)
- **Traps & tools**: the Temple of Trials spear corridors spring and
  disarm; use-item-on-object works (crowbars, keys); movie calls show
  caption cards with the original subtitles.
- **World correctness**: NPC positions persist across map travel;
  override_map_start honored; saves are Version 2 (V1 refuses cleanly).
- **Renderer**: per-vertex floor lighting — light pools bleed across tile
  seams like the original.
- **Front door**: main menu with premade-character picker; a real death
  screen (v0.6.0 items, first tagged release).

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
