# Changelog

## v0.14.0 — 2026-07-11 — cutscenes play

The headline is full **Interplay MVE video** support: the engine's cutscenes now
decode and play in-engine. Around it, the gap-batch fidelity run (armor art, the
Preferences screen, localization) and the campaign-QA path finder.

- **Movies play**: a from-scratch Interplay MVE decoder — the whole `_nfPkDecomp`
  block codec (all 16 opcodes) plus Interplay DPCM audio — ported literally from
  fallout2-ce and validated **pixel-exact against ffmpeg across all 13 game
  cutscenes** (~9,500 frames). `play_gmovie` now plays the real `.mve` full-screen
  with sound (intro, endings, the temple guardian) instead of a caption card, and
  an **F10 debug browser** previews any cutscene in the archive.
- **The dude wears their armor**: the world sprite follows equipped armor
  (leather / metal / combat / power), and HUD indicator pills show the poisoned /
  radiated / level-up conditions.
- **Preferences**: the authentic Preferences screen (Options → Preferences) with
  backed detail/volume settings and a live master-volume slider.
- **Localization**: a language switch (`--language`) that substitutes the
  `text\<lang>\` message files, plus the worldmap town-tab label art.
- **Campaign QA**: a quest-path finder that traces the dialog/trigger route to
  each quest's completion flag — the tooling for authoring per-town quest fixtures.
- **Elevators**: a same-map elevator ride now switches floor **in place** (no map
  reload), preserving script state and per-map deltas.

## v0.13.0 — 2026-07-07 — every ledger closed

The whole post-v0.12 arc: the endgame/QA closeout, two door-pathing and four
fidelity batches, the item pipelines, and a six-phase run that finished every
remaining backlog item. Nothing tracked is open.

- **Quests, verified**: a static bytecode census over all 1263 shipped scripts
  proves 107 of the 110 Pip-Boy quests completable end to end; the other three
  are the original game's own content bugs, pinned by test. New quest-golden
  e2e suite; per-map script-proc census tooling.
- **The Highwayman, whole**: buy it, drive it (terrain drains real fuel), pop
  the trunk (persistent storage), see it parked on the town map where you left
  it, and watch the animated car monitor with its fuel bar on the worldmap.
- **The worldmap, dressed**: the authentic chrome window — a scrolling 1:1 map
  view behind the real frame, city circles + hotspot markers, the alphabetized
  town-tab rail with quick-travel buttons, the date/time readout, the day/night
  dial — and the TOWN/WORLD switch flips to each city's townmap with clickable
  district entrances. Crossing mountains genuinely costs more game time per
  pixel, exactly like the original's walk loop.
- **Authentic pickers**: the elevator panel (per-location art, animated floor
  gauge, ride sound), the called-shot body-diagram window (target wireframe,
  per-species part names, live to-hit in the original digit bezels), and the
  chargen name/age plates with their pop-up editors.
- **Talking heads react**: script mood nudges shift a head between its good/
  neutral/bad animation families with the proper transitions, and voiced lines
  lip-sync with the matching phoneme set.
- **Combat & world fidelity**: NPCs run in combat (per-routine gates), weapons
  draw/holster with armed idle art, doors/containers/pickups/elevators make
  their real sounds — attenuated by distance and Perception when off-screen —
  and roofs hide per building via flood fill instead of a global toggle.
- **Item pipelines**: self-use scripted items, using items ON critters (medical
  bags, drugs on a target), charged items (Geiger counter, motion sensor,
  Stealth Boy invisibility), usable explosives, and NPC drug/heal AI.
- **Engine hardening**: script module globals persist across procs for the map
  visit (the New Reno prizefight coordinator pattern), exploit gates (in-combat
  inventory AP cost, Pipboy combat block, authentic rest rules, worldmap
  unreachable in combat), a stale-handle diagnostic guarding future content,
  and the outline-texture cache now evicts with its FRMs (the last known leak).

## v0.12.0 — 2026-06-16 — make the chrome click, then watch the world move

The arc beyond v0.11: UI completeness, worldmap authenticity, and the final
fidelity items from the fallout2-ce gap analysis. The in-scope backlog is now
exhausted (what's left is out-by-design — see `SCOPE.md`).

- **UI completeness**: every HUD button, item-panel row, and Options/Pip-Boy menu
  row is mouse-clickable (with PgUp/PgDn paging past the 9th item); the weapon slot
  cycles the attack mode; a full-window Pip-Boy automap plots the map (fog-of-war as
  you explore), with the real Fallout 2 calendar date and an embedded mini-map.
- **Worldmap comes alive**: a party dot crosses the map, paced by terrain, and the
  trip saves/resumes mid-walk; encounters are named, a high Outdoorsman offers a
  Yes/No avoid, travel auto-resumes after one, and an X-FIGHTING-Y encounter drops
  you into a brawl already in progress. Subtile fog-of-war hides where you haven't
  been and gates the hidden sub-area markers until you explore near them.
- **Combat movement symmetry**: in-combat movement costs the dude AP per hex (a
  crippled leg crawls), a crippled arm blocks a two-handed weapon, and fleeing AI
  uses real retreat pathing.
- **Carry weight & encumbrance**: items have weight, Strength sets a carry limit
  (shown red when over), overload blocks pickup/loot/barter and costs combat AP.
- **Dialogue IQ-gating**: the dude's real Intelligence gates the dumb/smart options.
- **Gory deaths**: a hefty burst, laser, or explosion leaves a suitably gory corpse
  (sliced / charred / big-hole / exploded) by damage type, where the art ships.
- **Render & script fidelity**: object translucency (glass/steam/energy/red/wall),
  script-driven map lighting (`set_light_level`) and looping `reg_anim`, and the
  line-of-fire is the engine's screen-space Bresenham.

## v0.11.0 — the opening hour, fully armed

Closes the last of the backlog (#1–#15) and the post-v0.10 feature arc.

- **Authentic HUD bar** (the real `iface.frm`): green message monitor, equipped-
  weapon slot + attack-mode label, AP pips, HP/AC in the original digit font, the
  END TURN / END COMBAT buttons, and clickable INV/OPT/MAP/CHA/SKILLDEX/PIP tabs
  with their pressed-art and an HP/AC digit-roll.
- **Companion — Vic**: his rescue is wired end-to-end (the radio-gated Metzger
  buy, the VM-driven `party_add`), with `party.txt` proto level-ups that ride the
  dude's level-ups and survive save/load.
- **Panels**: the Skilldex use-skill picker (authentic `SKLDXBOX` art), the Pip-Boy
  (status + a rest menu), and an Esc/OPT options/pause menu (save / load / main
  menu / quit / resume).
- **Combat presentation**: line-of-fire is now the engine's screen-space Bresenham
  (was a greedy-hex approximation); burst fire sprays the real left/right collateral
  cone; thrown weapons can land day-gated criticals.
- **Combat consequences**: a critical can knock a target out (with a timed wake),
  cost it a turn, cripple an arm/leg, or blind it — driven by the engine's
  massive-critical stat roll. A crippled leg slows movement, blindness is −25 to
  hit (and −5 Perception), a knocked-out critter is +40 to be hit, and the
  Skilldex **Doctor** skill mends crippled limbs and eyes.

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
