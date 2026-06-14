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
  egg-style wall transparency, silhouette outlines.
- **A micro INT script VM** + script host: map-entry scripts, dialog (`gsay`
  trees), examine, locks/lockpick, use / use-on-object, pickup, timers, spatial
  traps, the critter heartbeat, ~70 real engine externals.
- **Simulation**: A* movement, mouse picking, doors/stairs/exit grids, worldmap
  click-to-travel with random encounters (`worldmap.txt` tables → transient
  encounter maps, groups spawned in formation), ambient NPC life, sound
  (music/sfx/footsteps/combat).
- **Companions**: recruit, a wait/follow/dismiss/rejoin control hub, and a flat
  1:1 inventory trade panel.
- **Gameplay**: turn-based melee + gun combat with the engine's depth — to-hit /
  line-of-fire / ammo+reload, AI behaviour packets (close-or-flee), critical hits
  + aimed called shots, knockback + persisting knockdown, area explosions, and
  throwing (spears, grenades, recoverable); armor, drugs, lootable corpses,
  scripted aggro, same-team joiners, a minimum party member, barter, kill XP,
  level-ups, per-map persistent world, versioned JSON save/load, a main menu,
  character creation, rest.

## What's out (by design, today)

- **Burst fire** — single-shot (and aimed/thrown) only; no burst-capable weapon
  reaches the player in the shippable slice (see `docs/phase9-research-report.md`).
- **Most quest chains** — the opening hour (Arroyo → Temple → Klamath/Den) is
  the target; deeper quests (incl. Vic's radio rescue), reputation/karma badges,
  and the slave-run path are not wired.
- **Companion depth** — recruit/follow/fight/dismiss/rejoin/wait and 1:1 trade
  work (phase 10). The level-up proto-swap *logic* is now ported and unit-tested
  (`Hexwaste.Formats.Party`, from `party.txt` + `party_member.cc`
  `_partyMemberIncLevels`), but it is not wired into the viewer because **no
  shippable map recruits a `party.txt` companion** (Sulik/Marcus/etc. need their
  out-of-scope recruitment quests) — it lights up for free once one does (#13).
  Per-companion quest *banter* is 100% companion-script content gated on those
  same recruitment quests (it already runs via `talk_p_proc` when present), so
  there is no engine work to do — blocked on content like Vic's rescue (#10).
- **Perks, traits, skill points beyond the gated skills**, the full character
  editor, and worldmap car travel.
- **Anything needing assets we can't ship** — Hexwaste requires *your own* legal
  copy of Fallout 2. We never include or distribute game data.

If a feature is "out" above, an issue asking for it will be closed with a link
here. Bug reports about features that **are** in scope are very welcome.

The authoritative, evolving in/out list lives in `CLAUDE.md`; the per-phase
research reports in `docs/` record why each decision was made.
