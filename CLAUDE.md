# Fallout 2 Map Viewer PoC — Prime Directives

Proof-of-concept **Fallout 2 map viewer** in **C# / .NET + MonoGame (DesktopGL)**.

## Mission

1. Read original Fallout 2 game data from the user's legally owned copy (`--game-dir`, default `./game-data`). **Never copy, embed, or commit any game assets into this repository.**
2. Parse: **DAT2** archives (`master.dat`, `critter.dat`, `patch000.dat`), **FRM** sprites, **PAL** palettes (`color.pal`), **MAP** files, minimal **PRO** prototypes.
3. Render one complete map (default `artemple.map`) in a resizable window: floor tiles, toggleable roofs, static scenery/walls with correct z-sorting.
4. Palette color cycling (slime, fire, shoreline, monitors, alarm) at original speeds.
5. Camera: pan with mouse drag / arrow keys, optional integer zoom.

**Out of scope (do NOT build):** critters/NPCs, scripting, combat, pathfinding, UI, sound, worldmap, save/load.

## Authoritative reference

`reference/fallout2-ce` (cloned, gitignored) — port logic from there, never guess.
Key files: `src/dfile.cc` (DAT2), `src/db.cc` (VFS: loose files override DAT), `src/art.cc` (FRM),
`src/color.cc`/`src/palette.cc` (PAL + cycling), `src/map.cc` (MAP), `src/proto.cc` (PRO),
`src/tile.cc` (**hex/square grid ↔ screen math, draw order — most important for rendering**).

When porting, add a comment with the source: `// ported from fallout2-ce src/tile.cc tileToScreenXY()`.
If a format detail can't be confirmed from fallout2-ce sources, **stop and ask** instead of guessing.

## Layout

- `src/Hexwaste.Formats` — pure .NET class library, zero MonoGame deps, unit-testable.
- `src/Hexwaste.Viewer` — MonoGame DesktopGL app.
- `tools/DatDump`, `tools/FrmDump` — CLI demo/debug tools.
- `tests/Hexwaste.Formats.Tests` — xUnit; tests needing real game files are guarded by env var `FALLOUT2_DIR` (skip when unset) so CI passes without assets.
- `game-data/` — extracted GOG game data (gitignored). `master.dat`, `critter.dat`, `patch000.dat`, `data/` live at its root.

## Milestones (commit after each)

Phase 1 (DONE): M1 DAT2 reader, M2 PAL+FRM, M3 MAP parsing, M4 static floor
render, M5 objects + z-sorting + roofs, M6 palette cycling.

Phase 2 — "walking simulator" (per research report
`compass_artifact_…_text_markdown.md`; NO combat, NO script VM — hard scope line):

0. **P2-M0** — DONE. Benchmark on newr1.map (heaviest: 2841 objects):
   avg 3.6 ms / p95 6.2 ms / max 13.6 ms full frame with cycling active —
   far under the 16 ms threshold. **Decision: CPU palette conversion stays;
   no shader, no Wine.** Simulation is wall-time driven, fixed 60 Hz update
   kept; `--bench N` measures uncapped frame cost; FPS shown in title.
1. **P2-M1** — static critters: FID→FRM name via critters.lst + anim-code
   suffix (`src/art.cc` `artBuildFilePath()`/`_art_get_code()`), correct
   direction, z-sorted with solid objects.
2. **P2-M2** — idle/breath animation + walk cycle in place (`src/animation.cc`);
   FRM frame offsets accumulate across frames.
3. **P2-M3** — mouse picking: per-pixel alpha hit-test in reverse draw order,
   hover shows PID/FID (`src/object.cc`, `src/tile.cc` screen↔hex).
4. **P2-M4** — dude movement: A* on hex grid (`src/path.cc`), blocking objects,
   walk along path, camera follow.
5. **P2-M5** — hardcoded interactions, no VM: doors (open/close animation),
   exit grids (map/elevation transition), stairs/ladders.

Phase 3 (DONE, per docs/phase3-research-report.md): M0 AAF fonts + MSG + examine,
M1 static lighting (LightGrid port incl. the 36-case occlusion switch;
CPU tints — per-object exact, per-square floor approximation), M2 worldmap
travel (city.txt/maps.txt lookup names), M3 sound (full ACM decoder port,
door sfx names, footstep approximation, maps.txt music; music is LOOSE files
under <game>/sound/music), M4 ambient life (fidget per _dude_fidget; wander
is a documented fake), M5 micro INT VM (39 core ops + 181 arity-mapped
externals; examine override path only — use_p_proc/map_enter NOT wired).
Scripts.lst is 0-based; message_str list ids are scripts.lst index + 1.

Phase 4 (DONE, per docs/phase4-research-report.md): M0 VM foundations (real
rolls — stub-0 = critical-failure trap; script context; LVARs are LAZY
slices, pristine maps store offset -1), M1 text dialog (gsay loop, options
bind by procedure index), M2 locked doors + lockpick + RunMapEnter (map
script = header.ScriptIndex-1), M3 world-mutation externals + loot/
inventory panels (inventoryFid icons; RunMapEnter snapshots its list — 
stocking scripts mutate it), M4 GameClock (engine has NO day/night curve;
ours is custom) + JSON delta save/load (containers restock by design),
M5 polish (outlines, roof fade, egg-fade approximation, scroll clamp).
GOTCHA: GPU backbuffer readback races — screenshots must render via a
RenderTarget2D (ViewerGame._screenshotTarget). Per-vertex floor lighting
(BasicEffect quads) remains the known deferred upgrade.

Phase 5 (DONE, per docs/phase5-research-report.md): M0 foundations (real
caps/timer/tile externals — pay-caps stub gave goods away; timers are
dialog-gated, cleared on map exit, 1:1 tick source), M1 multi-map
persistence (per-map deltas keyed by LOAD-ORDER ORDINALS — MAP object Ids
collide; LVAR slices keyed by map NAME import before map_enter on revisits,
firstRun=0; container snapshots overwrite restock; fixes the ~590 KB/
transition ScriptHost leak), M2 critter stats (proto stat block + the 11
MAP combat ints; CritterState = base+bonus), M3 player combat (roll before
animate, damage on completion; corpse = anim+28, NO_BLOCK + flat → loot
panel works unchanged), M4 AI turns (AP-budgeted approach, same-team
joiners within 20 hexes, game over → F9), M5 ship-prep (renamed
FalloutPoc→Hexwaste, SUL license + NOTICE, docs/ provenance,
scripts/release.sh, game-dir probing).

Phase 6 (DONE, per docs/phase6-research-report.md — "The Opening Hour"):
M0 hygiene (OnStubbedExternal finally hooked — it never was; SaveState
Version=1 refuse-mismatch; DeadOrdinals — kills persist, sid=-1 BEFORE
map_enter like the engine), M1 real dude (premade\player.gcd = the
critter proto stat-block layout + name/tags/traits; real get_critter_-
stat/has_trait/do_check/get_pc_stat — fixes every stat-gated dialog),
M2 critter_p_proc heartbeat (1 script per 10 Hz tick round-robin,
gated; real critter_add_trait/attack/anim_busy/rotation_to_tile —
unprovoked aggro IS script-driven), M3 kills matter (destroy/damage
procs; XP engine-side from proto exp, paid at combat END, forfeited on
death; level-up EN/2+2 HP), M4 winnable combat (weapon/armor/drug proto
payloads; equip = item flags 0x1/0x2/0x4000000 — MAP NPC weapons just
work; armor mutates bonus stats; stimpak = -2-marker random heal), M5
barter (export.cc vars session-scoped on ScriptHost — per-VM before,
never connected; gdialog_barter flag-only, arg OVERWRITES set_barter_-
mod; stock lives in the shop BOX at trade time because our dialog model
runs the talk epilogue early — session tracks the box; price =
cost×2×(mod+100)/100×(160+npcB)/(160+dudeB), sells at face).
GOTCHAS: map_enter must run HIDDEN scripted objects (shop boxes);
the dude's bag is ALIASED to dude.Inventory (caps externals); --attack
is a free-swing primitive (resets combat), --fight runs real turns.

Phase 7 (DONE, per docs/phase7-research-report.md — "Ship It, Then Arm
the Wasteland"): M0 v0.6 front door (menu + gcd picker + death screen,
README screenshots, CHANGELOG, v0.6.0 tag; publish = user's git push
per docs/RELEASING.md), M1 V2 saves (MovedOrdinals NPC positions
replayed BEFORE map_enter; SavedItem ammo fields, -1 = derive from
proto; override_map_start; V1 refuses), M2 guns (10mm-class = HITSCAN,
muzzle flash baked in FRM 'j' — zero animator features; to-hit
combat.cc:4314 subset; LoF = greedy hex walk DEVIATION from the
engine's screen Bresenham; dude art hmjmps — hmwarr has no gun sets,
engine has NO weapon-art fallback; R=reload, roofs moved to F4),
M3 traps (spatial records kept in MapFile; RunSpatialsAt gated like
_scr_SpatialsEnabled; create_object_sid BINDS scripts via AllocateSid;
critter_damage real; use_obj_on item-then-target precedence; gmovie =
caption card from .sve), M4 party minimum (followers travel OUTSIDE
map deltas, follow script re-bound per map — follow logic is 100%
script-side critter_p_proc; allies act after hostiles; enemies target
nearest of dude+allies; team kills pay XP), M5 per-vertex floors
(BasicEffect quads, corner light from NW/NE/SW/SE neighbor hexes;
newr1 3.34 ms avg — faster than the sprite path).
Spillover to phase 8: random encounters (worldmap.txt decoded, maps
need saved=No delta-skip), burst/aimed shots, companion management.

Phase 8 (DONE, per docs/phase8-research-report.md — "The Character
Comes Alive"): M0 bug fixes + ops (CritterState now tag-aware — gcd
TaggedSkills add +20 + double-rate per skill.cc:251; female dude art
hfjmps + female death scream when gcd gender baseStats[34]==1; CI yaml
+ issue templates + SCOPE.md), M1 skill growth (Formats/Combat/SkillSet
= gSkillDescriptions + skillGetValue + cost ramp + 5+2*IN points/level
cap 99; K allocator; additive-V2 save: UnspentSkillPoints + DudeSkills
+ Character), M2 character sheet (C/K — SPECIAL+derived+skills, the
allocator enriched, not a 2nd panel), M3 rest-to-heal (Z; pipboy.cc:2113
need/rate*3 truncation, HEALING_RATE=max(EN/3,1); gates on local safety
not the engine's can_rest_here flag — a documented divergence), M4
character creation (GcdFile.Create recomputes derived stats per
stat.cc:554; menu state machine Title/Pick/CreateStats/CreateTags; save
self-contained via DudeBaseStats+DudeTaggedSkills — BUG FIX: SpawnDude
took the generic proto's 30 HP for ALL gcd characters because
GetCritterState keyed on the unset _dude; now reads the gcd directly),
M5 merchant restock (MapDelta.SnapshotDay; a _stockedOrdinals container
with a stale snapshot keeps fresh map_enter stock after RestockDays=3;
world loot stays looted). GOTCHA: premade SPECIAL is ordered S/P/E/C/I/A/L
= baseStats[0..6] (Agility is index 5, NOT 4). Spillover to phase 9:
random encounters, combat depth II (extract CombatEngine FIRST), Vic's
rescue legitimately + companion trade/dismiss.

Phase 9 (DONE, per docs/phase9-research-report.md — "Combat Depth II"):
M0 extract-first (the ~700-line turn machine lifted out of ViewerGame
into Hexwaste.Formats.Combat.CombatEngine behind ICombatHost +
ICombatRng, NO behavior change; the viewer keeps sole ownership of
animator/walkers/draw-lists/_blockedTiles so the walker TileChanged
closure stays correct without an engine callback; the adversarial audit
caught two missing side-effects — NPC-walker TileChanged + script
damage/destroy procs; regression net = scripts/combat-golden.sh golden
transcripts + a clean headless exit (--fight/--attack auto-exit after
their startup actions) + the fake-host CI unit tests the extraction
finally unblocked), M1 AI packets (Formats/Combat/AiPackets parses
data\ai.txt 187 packets; MapObject.AiPacket was parsed since phase-5 but
read NOWHERE; min_to_hit close-or-flee + RAW min_hp flee — combat_ai.cc:3077,
run_away_mode is party-UI/debug only, NOT the combat flee; PruneEscaped-
Hostiles disengages critters fled beyond sight so combat ends. GOTCHA:
arcaves radscorpions are script-spawned at runtime with pkt-8 min-0 — the
static map's pkt-14 never applies; Den slaves pkt-33 min_hp-30 actually
flee), M2 aimed shots + criticals (tools/gen_critical_tables.py generates
the 1080-row crit table from combat.cc into CriticalTables.g.cs, FNV-1a
checksum-guarded; the to-hit roll upgrades SUCCESS→CRITICAL via a 2nd
d100 ≤ delta/10 + (critChance − hit_location_penalty); severity bucket +
STAT_BETTER_CRITICALS; honor the damage multiplier + flags {CRITICAL,
DEAD, KNOCKED_DOWN, BYPASS}, mask the rest; aimed shot +1 AP + the penalty full-ranged/half-
melee. GOTCHA: criticals gate on day≥2 (random.cc randomTranslateRoll,
gameTime/TICKS_PER_DAY≥1) — so the day-1 golden fixtures take ZERO extra
RNG draws and stay byte-identical; the called-shot UI is a V-cycle, not
the engine's click dialog — documented simplification), M3 knockback +
persisting knockdown + explosions (shove dmg/10 along the hex line for
melee/explosion, NEVER guns — combat.cc:4633, !MULTIHEX/!NO_KNOCKBACK;
a crit DAM_KNOCKED_DOWN persists prone — +40 to hit combat.cc:4474, 3 AP
to stand; CombatEngine.Explode = radius+LoS AoE with explosion DT/DR
stats 23/30 + knockback, cap 6 — the ring-spiral simplified to
radius+LoS), M4 throwing (TryThrow reuses the ranged to-hit with the
Throwing skill, range min(maxRange, 3×ST); explosives detonate at the
landing tile via Explode + the misc-10 marker + metarule(49)==EXPLOSION
+ a radius-3 damage_p_proc broadcast = the temple-door path; non-
explosives drop recoverable on the ground; --throw/--aim/--explode
harness hooks. GOTCHA: the projectile flies via the throw anim, not a
tweened sprite; throws don't crit; the artemple door-blast beat is WIRED
but unverified in-game — lockpick stays the advertised opener, per the
content gate). 108→182 Formats tests; an 11-fixture golden combat
harness; tools/ContentAudit (weapon/killtype/packet census) +
gen_critical_tables.py. Spillover to phase 10: burst (DEFERRED — ZERO
burst weapons in the shippable slice), random encounters, Vic's rescue +
companion management, the projectile tween + recoverable persistence +
the verified door beat.

Post-phase-10 backlog (GitHub issues): #9 burst fire (DONE — the phase-9
"ZERO burst weapons" claim was wrong: newr1.map carries 3 lootable burst
guns — 10mm SMG/Tommy Gun/Combat Shotgun — exercised via --give). Ported
combat.cc _compute_spray: rounds = min(loaded, weapon.Rounds); ONE day-gated
inception crit roll (crit-FAIL aborts, crit-SUCCESS +20, bullets still
spent); per-round hit = plain d100≤acc, rounds never crit; fresh damage roll
per hit summed; ammo decremented in ONE batch at resolve (combat.cc:5349),
NOT eagerly like single-shot; AP = secondary ApCost2; burst can't be aimed.
DOCUMENTED DIVERGENCE (like the LoF greedy-hex one): the left/right cone
lines + up-to-6 collateral "extras" are not modelled — only the center line
fires at the target (exposure = max(centerRounds, mainTargetRounds), ~3 of
10 for an SMG in a duel); collateral is the named deferred upgrade. CombatEngine
.TryBurst behind a B keybind + --burst harness flag + 2 golden fixtures.
GOTCHA (review-caught): EndPlayerTurn/UpdateCombat gated only _pendingAttack —
a pending burst/throw could flip to the enemy turn mid-animation (the B+Space
race; the throw half was a latent p9 bug); both now block on all three pending
actions.
#13 companion depth (PARTIAL — level-up FOUNDATION only; banter closed-with-
docs): party.txt EXISTS (data\party.txt; Sulik pid 16777313 section 4,
level_minimum 6, level_up_every 3, 6 stage pids). Ported the PURE logic into
Hexwaste.Formats.Party — PartyTable (party.txt parse) + PartyLevelUp.IncLevel
(party_member.cc:1487-1539 _partyMemberIncLevels decision math: level_up_every
==0 never; pcLevel<level_minimum gate; cap at level_pids_num; numLevelUps%every
levelMod; isEarly skip-until-cycle-boundary; the INVERTED roll randomBetween
(0,100) > 100*levelMod/every = DO NOT advance). DIVERGENCE: engine indexes
level_pids[level] AFTER level++ (skips [0], reads OOB on the last stage — a real
quirk, copyLevelInfo only ever runs here); we apply level_pids in order capped
at the count. NO viewer wiring / save field / harness — no shippable map
recruits a party.txt companion (the Radscorpion test critter pid 0x1000005 is
NOT in party.txt), so wiring would be inert; the logic lights up for free when
a real recruitment lands. Banter = ZERO engine work (talk_p_proc already runs
all dialog externals; it's 100% companion-script content gated on the out-of-
scope Sulik/Vic recruitment quests, same blocker as #10) — SCOPE.md clarified,
no code. 9 unit tests (incl. a GameDataFact real-party.txt parse + the full
Sulik 6-stage cycle + the inverted-roll both-branches).
#10 Vic rescue (M0 DONE + M1 spine PROVEN): M0 fixed the real multi-round dialog
blocker — a non-blocking gsay_end means talk_p_proc's trailing end_dialogue set a
STICKY SessionEnded that killed the first Choose (every option ended the convo);
fix = clear SessionEnded in ResetDialogRound (a real goodbye node re-sets it). The
prior "debunk" was wrong — DialogRealGameDataTests only asserted TERMINATION (1
round passes), never continuation. M1 spine proven end-to-end on denbus2 (BOTH
Metzger hex 15278 script 45 AND Vic hex 17070 script 49 live there — single map):
talk Vic → pay Metzger 1000 caps (item_caps_adjust, caps 2000→1000) → free-bit
GVAR445|0x8000000 handshake → Vic "Come with me"/"Great let's go" → party_add via
the REAL talk_p_proc VM (not the force-recruit harness) → party-count members=2.
GOTCHA (contradicts the p8 note): the cash buy is RADIO-GATED — Metzger only offers
it after GVAR446|0x400000 ("radio fixed"); the vic-recruit golden fixture sets that
bit via --set-global (test plumbing) pending the radio sub-quest (the 3 stubbed
inventory externals + Vic's radio-parts content are a PREREQUISITE, not optional).
New harness: --talk-seq (composable GVAR-persistent talk+choose), --set-global,
--party-count; MapDump now prints per-critter script index; HEXWASTE_DIALOG_DEBUG=1
traces Choose.
M-radio (DONE): the cash buy's radio gate is now VM-set, not faked. Implemented the
3 inventory externals — obj_is_carrying_obj (0x80BA = quantity-by-pid, recursive into
nested containers per inventory.cc objectGetCarriedQuantityByPid), obj_carrying_pid_obj
(0x810D = handle of the first carried item by pid, objectGetCarriedObjectByPid),
rm_obj_from_inven (0x80D9 was already wired). Pop order pid-then-critter (top-first);
object handles are ScriptHost ints (HandleOf/ObjectOf), not engine void*. Recursive
scan extracted to Formats.Map.InventoryScan (CountByPid/FindByPid, unit-tested). With
pid 266 "Vic's Radio" in the bag, dcVic Node004 ("Can you use this radio I found?") →
Node005 rm radio + set_global_var(446,|0x400000) → Metzger's $1000 buy unlocks → FULL
plumbing-free recruit (vic-recruit fixture now --give 266, NO --set-global). GOTCHA:
pid 266 has no in-slice source (multi-step Klamath quest item) — the recruit needs
ONE item-give, the documented residual content gap. M2 (DONE): the #13 level-up
foundation is now LIVE on the real recruit — PartyLevelUp.IncLevel wired into AwardXp
(once per PC level-up, stat.cc:789), party.txt parsed lazily (PartyTable), per-member
PartyLevelUpState tracked, and the advanced stage proto applied as a per-companion
CritterProtoStats OVERRIDE that GetCritterState consults (NOT a shared-cache mutation
— anti-aliasing), HP reset to the new max (party_member.cc:1605). Verified: Vic
(party.txt member 13, level_minimum 5) advances 0x1000175→0x1000176 as the dude
levels (vic-levelup golden fixture); the hub (wait/follow/dismiss/rejoin) opens for
the recruited Vic unchanged (critter-agnostic, #8). A dedicated seeded _partyRng keeps
the roll off the worldmap/combat streams. M3 (DONE — #10 COMPLETE): the scripted
recruit + its proto level-up survive save/load. The duplication trap was already
handled (CaptureMapDelta marks PartyMembers' ordinals TAKEN, scripted recruits go
through the same path); M3 added the level-up persistence — PartyMemberState gained
3 additive-V2 ints (Level/NumLevelUps/IsEarly, party_member.cc:520-538), restored on
load with the stage proto re-applied as the override (HP from saved.Hp). Verified by
vic-save-roundtrip: the party-count line is byte-identical before save and after load
(members=2, no dup; Vic keeps his levelled 78/78). --save-path sets the in-process
--save-now/--load-now file; --party-count now shows each member's HP. #10 (Vic's
legitimate rescue + companion lifecycle on a real recruit) is fully closed; the lone
residual is the radio ITEM having no in-slice source (one --give, content not engine).
P11 authentic HUD bar (#15, DONE M0-M5, per docs/research-notes/p11-hud-scope.md):
the real art\intrface\iface.frm (640x99) pinned bottom-centre at native 1:1 scale
(the camera has no zoom) via the new InterfaceBar class + DrawInterfaceBar. M0 bar
+ log relocation (the green monitor is now the log home; bottom-left is the
bar-hidden fallback). M1 HP/AC via the real NUMBERS.FRM digit blitter (3 colour
bands; HP white/yellow/red by <50%/<25%) over a field-blank to (32,32,32) — GOTCHA:
iface.frm ships BAKED placeholder digits "036"/"-258" + SINGLE/BURST labels + the AP
socket row, so AAF text won't do; AP = lit green pips on the sockets. M2 weapon slot
(inventory FRM centred) + ammo. M3 green monitor (font1.aaf == engine font 101,
tinted green, wrapped, top-anchored). M4 clickable INV/OPT/MAP/CHA/PIP/SKILLDEX
(HudButtons rects, TryClickInterfaceBar consumes the click before world-interaction;
wired to the same actions as the I/M/C keys, additive). M5 active mode-label
(SWING/SINGLE/BURST), hover highlight, ENDTURNU/ENDCMBTU combat buttons (combat-only).
Tooling: --hud-click harness + hud-buttons golden; HEXWASTE_HUD_DEBUG=1 rect overlay.
The bar is Draw-only so every transcript/golden fixture stayed byte-identical.
P11 POLISH (DONE): button press-art (the DN FRMs overlay the baked UP button
while the mouse is held; HudButton rects re-derived verbatim from interface.cc
buttonCreate(x,y,w,h) with gInterfaceBarContentOffset=0 so the art overlays
exactly) + HP/AC digit-roll (the counters step 1/~25 ms toward the live stat;
cosmetic, never printed → goldens byte-identical). HEXWASTE_HUD_FORCE_PRESS=<name>
forces a press for screenshot verification.

Phase 12 (IN PROGRESS — "Operate the Panels", HUD/UI wiring; the 3 SKILLDEX/
PIP/OPT HUD buttons that only Log'd "not wired"): M0 Skilldex use-skill picker
(DONE). The SKILLDEX button (or S) opens an 8-skill flyout (Sneak/Lockpick/
Steal/Traps/First Aid/Doctor/Science/Repair — skilldex.cc gSkilldexSkills
order); picking one (1-8) arms _pendingUseSkill so the next click applies it via
TryUseSkillOn, the generalised use_skill_on_p_proc path lockpick already used
(TryLockpick is now just TryUseSkillOn(9)). Targeted skills (Lockpick/Steal/
Traps/Science/Repair) run the target's script (a scripted door HONOURS its
use_skill_on_p_proc — stays locked, NOT blindly unlocked) + the lockpick unlock
fallback; First Aid/Doctor port skill.cc:546 skillUse (roll dude-skill% vs d100
→ heal 1-5 HP capped at MaxHp, can't-heal-dead/healthy-already guards, 30/60
game-min cost, the 3-uses-per-game-day skillGetFreeUsageSlot cap) — DOCUMENTED
SIMPLIFICATIONS: no Healer perk (min/max heal = 0 → 1-5), no crippled-limb model
(Doctor's limb-fix is skipped), Sneak is a logged stance toggle with no stealth
effect. Heal rolls use a dedicated seeded _skillRng (off the combat/party/wm
streams). Harness: --use-skill <skillId> <hex> (hex<0 = self); golden
skilldex-skills (scripted-door lockpick + self First-Aid-at-full + Sneak, all
deterministic) + hud-buttons re-recorded (the hud-click print gained skilldex=).

Phase 10 (DONE, per docs/phase10-research-report.md — "The Wasteland
Bites Back"): M0 persistence pre-stage (the net: MapList saved=No /
random_start_point parse + IsTransient; the 3-clause transient guards as
LoadMap plumbing — no behavior change yet), M1 WorldmapFile parser +
wmRndEncounterOccurred/Pick roll chain (Δ3 gate, daypart freq, weighted
pick, AND-only If conditions; --encounter-walk demo), M2 save+counters
(additive-V2 WorldPosX/Y/CurrentAreaId + SPARSE EncounterCounters keyed by
table; F9 must null _worldmap so a live-consumed one-shot doesn't leak
into a save that left it pristine), M3 transient-map encounter spawn
(EncounterSpawner.Plan = the pure wmSetupCritterObjs port — formations
surrounding/line/wedge/cone/huddle, ratio/single, Dead corpse, items,
placed-tile dedup, 25-retry A*-reachable gate; SURROUNDING distance is the
PER-MEMBER field — group-level Distance: is dead data in the engine;
spawned after map_enter so the critter_p_proc heartbeat aggros AMBUSH for
free; live worldmap travel rolls along the Bresenham path → loads the
encounter map, re-click resumes; GOTCHA: CaptureMapDelta is the single
_visitedMaps writer — guard transient + party there since SaveGame bypasses
LoadMap's ExtractPartyFromMap), M4 companion lifecycle (metarule(16)
PARTY_COUNT = 1+live-visible-critters; a VIEWER-side control hub —
wait/follow [PumpCritterProcs skip], dismiss [party_remove + restore saved
team + clear sid], rejoin [alive-gated] — chosen over per-companion dialog
nodes for robustness/reuse; the engine has NO dedicated wait/follow/dismiss
UI: recruit/dismiss are plain party_add/party_remove (interpreter_extra.cc
:3943/3956 → party_member.cc:375/426) called from a companion's own
talk_p_proc reply procedure (game_dialog.cc:2080 _gdProcessChoice →
_executeProcedure), and our hub reproduces those exact side effects. The
"partymbr.msg list-14" claim in the phase-10 research notes was UNVERIFIED
and is WRONG for this slice — partymbr.msg does not exist in the game data,
"partymbr" appears nowhere in the fallout2-ce source, and message list 14
resolves to Generic.int/generic.msg (no party strings); the only dedicated
party UI is the AI-disposition combat-control window (game_dialog.cc:3354),
which reads proto.msg/misc.msg and is out of scope. The follow loop stays
100% script-side. (#8: as-written infeasible — no partymbr artifact exists —
so closed as a documentation correction.), M5 1:1 trade panel (the loot panel
pointed at the follower, flat barter-modifier-0 — bypasses priced barter;
GiveToFollower is the only new transfer; UnequipForTransfer reverses the
worn-armor bonus before any give/drop). GOTCHA: companions travel OUTSIDE
map deltas — exclude PartyMembers from EVERY CaptureMapDelta loop (mark
in-place recruits Taken) or an F5 duplicates them on load. Documented v1
cuts: wait/dismiss state is viewer-side not saved; per-member encounter
If()/distance overrides, X FIGHTING Y combat-lock, Vic's radio quest, the
projectile screen-tween.

After each milestone: run tests, run the app if possible, update README progress checklist, conventional commit.

## Critical gotchas

- **Two grids**: floor/roof = 100×100 *square* grid; objects = 200×200 *hex* grid. Different coord→screen formulas; port both from `tile.cc`. Fallout's projection is oblique/trimetric, NOT standard 2:1 isometric.
- **Draw order**: floor → flat objects → non-flat objects in hex tile order → roofs.
- **PAL values are 0–63**: multiply by 4 and clamp for 8-bit RGB.
- **Roofs render shifted up 96 px** relative to their square tile.
- **FRM frame offsets accumulate** across frames; orientations may share the same data offset.
- **Transparent color = palette index 0.**
- Palette cycling must NOT re-decode whole textures per frame (killed jsFO). Keep 8-bit index data; prefer a palette-lookup shader with a 256×1 palette texture updated each cycle tick.
- DAT2 vs DAT1: Fallout 2 only (little-endian DAT2, zlib). Fallout 1 (DAT1, LZSS) is out of scope.

## Legal guardrails

- `.gitignore` excludes `*.dat`, `*.map`, `*.frm`, `*.pal`, `game-data/` — keep it that way.
- README must state: requires original Fallout 2 copy, no assets included, not affiliated with Bethesda Softworks.
- No "Fallout" in any distributable/package ID — DONE: the project is `Hexwaste` everywhere; LICENSE.md (SUL v1.0) + NOTICE.md ship with every artifact (see docs/RELEASING.md).

## Working style

- Small, reviewed steps over big-bang generation.
- Dependencies allowed: MonoGame, xUnit, SixLabors.ImageSharp (dump tools only). **Ask before adding anything else.**
- Streaming reads from DAT2 (`DeflateStream` at the right offsets); lazy-load FRMs with an LRU cache. Do not extract everything to memory.
