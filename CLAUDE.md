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
engine's screen Bresenham [SUPERSEDED by P13-M1: LineOfFire is now the faithful
screen-Bresenham port]; dude art hmjmps — hmwarr has no gun sets,
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
tweened sprite (UPDATE: the screen-tween landed in #11, and throws crit as of
P13-M3 — both these p9 notes are now superseded); the artemple door-blast beat is WIRED
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
The main target's exposure = max(centerRounds, mainTargetRounds), ~3 of
10 for an SMG in a duel. (The left/right cone lines + up-to-6 collateral "extras"
were the named deferred upgrade — now DONE in P13-M2; the main-target exposure
above is retained as the documented approximation so 1-on-1 bursts stay
byte-identical.) CombatEngine.TryBurst behind a B keybind + --burst harness flag
+ 2 golden fixtures.
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
M1 Pip-Boy status + rest (DONE). The PIP button (or P) opens the authentic
PIP.FRM (640x480) centred, with the date/time top-left (pipboy.cc 20,17 / 155,17
positions — our game-day + clock, NO full calendar since GameClock tracks only
ticks: documented simplification), a STATUS content page (name/level/XP/HP/AC/AP)
and a REST sub-page (R toggles it). Rest options (pipboy.cc PipboyRestDuration
subset) map to game-minutes; the timed heal is Progression.HpHealedResting =
minutes*rate/180 (the exact inverse of RestHoursToHeal — unit-tested), "Until
healed" reuses RestToHeal, "Until morning/evening" advance to the next 06:00/
18:00. RestToHeal refactored to share RestBlockReason (combat + local-safety
gate) with RestForMinutes; behaviour/Console output unchanged. Harness:
--rest-for <min|-1|-2|-3> (reusing the pre-existing --hurt to set up a wound);
golden pipboy-rest (timed + until-healed from near-death) + hud-buttons gained
pipboy=/the PIP click. Automaps/archives/holodisks/alarm stay out (content-gated).
M2 options/pause menu (DONE — P12 COMPLETE). Esc (CHANGED from quit→pause) or the
OPT button opens DrawOptions: the authentic OPBASE.FRM (164x217) centred with the
options.cc showOptions actions minus Preferences (no preferences system): Save (S),
Load (L), Main Menu (M → QuitToMainMenu = _combat.Reset + _menu=Title), Quit (Q →
Exit), Resume (Esc/D). hud-buttons gained options=/the OPT click. RIDER (the survey's
stale-doc fix): SCOPE.md + README reconciled — burst fire, Vic's rescue, companion
level-ups, and the HUD panels moved from "out"/unmentioned to "in" (at the time, only
the burst collateral cone remained out — SUPERSEDED: it shipped in P13-M2). GOTCHA: FrmDump's
--info is NOT a flag — it dumps a rendered PNG named "--info.png" into cwd (a legal-
guardrail trap); *.png is gitignored but delete it anyway.

Phase 13 (IN PROGRESS — "Combat Presentation + Burst Fidelity"; the projectile
screen-tween was already DONE as #11): M0 HexGrid.FromScreenEmbedding (DONE) —
the tileFromScreenXY inverse + the 512-byte _tile_mask corner LUT, ported with
camera offsets zeroed (verbatim mirror of the proven Camera.ScreenToHex/BuildTile-
Mask); the shared primitive both the Bresenham LoF and the cone's end-tile walk
need. Round-trip unit-tested: FromScreenEmbedding(ScreenEmbedding(t)+(16,8))==t.
M1 screen-Bresenham line-of-fire (DONE) — LineOfFire.Trace rewritten from greedy-
hex to the pixel-Bresenham of animation.cc:1951 _make_straight_path_func wrapped by
combat.cc:5897 _combat_is_shot_blocked: walk the screen line between tile centres
(+16,+8), FromScreenEmbedding per pixel, blocker-check on tile changes; SIGNATURE
UNCHANGED so all 5 callers + the host's ShootBlockerAt are untouched. GOTCHA: the
pixel cursor maps to the TARGET tile for a few steps before the exact-centre break,
so Trace must skip BOTH endpoints (tile != fromTile && tile != toTile) — the
engine's equivalent is the outer "obstacle != targetObj" guard; without it a wall/
critter on the target's own tile would false-block/false-count. Retained simpli-
fications: host-side NO_BLOCK/SHOOT_THRU/dead-critter filter + the dropped +1
MULTIHEX crowd bump (combat.cc:5921). DE-RISK PROVEN: both goldens BYTE-IDENTICAL
after the swap (Trace draws no RNG; the fixtures' shots are d=1/open-ground) — the
clean checkpoint that the M0 inverse is byte-correct.
M2 burst collateral cone (DONE). RollBurst now fires the center/left/right lines
(_compute_spray combat.cc:3766-3784): round split adds leftRounds=n/3, rightRounds=
n-center-left (engine statement order, BEFORE the centerRounds-=1); ConeCollateral
computes the pivot (dist<=3 ? TileNumBeyond(att,def,3) : def) + rotation RotationTo
(pivot, attacker) + left/right tiles (rot±1) + end-tiles via the new HexGrid.TileNum-
Beyond (_tile_num_beyond port); ShootCollateral walks each line REUSING LineOfFire.
Trace (the collecting callback — Trace already counts critters + resumes past + stops
at walls) and rolls per-round d100 ≤ each victim's own to-hit, accumulating non-target
critters as PendingBurst.Extras (cap 6, dedup+accumulate on repeat). ResolveBurst's
ApplyBurstExtras lands collateral HP/damage-proc/kill. DOCUMENTED APPROXIMATIONS: the
MAIN target keeps the v1 centre-exposure model (so a 1-on-1 stays byte-identical — the
cone lines are empty → ZERO extra RNG draws → the burst goldens are unchanged); the
line sweep reuses the greedy/Bresenham Trace (only end-tiles use exact TileNumBeyond);
_check_ranged_miss not ported. Collateral emitted as separate "burst-extra:" transcript
lines (only when present) so the 1-on-1 burst line is untouched. Verified: fake-host
BurstConeCatchesACollateralBystanderOnALine (a bystander on the discovered left line
takes collateral) + BurstWithNoBystandersHasNoCollateral (the invariant) + both goldens
byte-identical.
M3 thrown weapons can crit (DONE — P13 COMPLETE). TryThrow now runs the SAME
day-gated 2nd-d100 crit upgrade as single-shot (combat.cc randomRoll — throws crit
too): the hit roll became the delta form (chance - d100; the identical single draw,
so day-1 throws stay byte-identical), and on a hit from day 2 a 2nd d100 ≤ delta/10 +
critChance upgrades to a crit (severity → CriticalTables.Lookup at LocationUncalled
(8, penalty 0) → damage multiplier + flags). PendingThrow gained CritFlags; ResolveThrow
logs "Critical hit!" + honours DAM_DEAD (instant kill). Throws are uncalled (torso) and
never knock back (projectiles), so no called-shot UI / no knockback — documented. day-1
throw fixtures byte-identical (CriticalsEnabled false → crit block skipped → no extra
draws); verified by fake-host ThrownWeaponCanCritFromDay2 + ThrownWeaponDoesNotCritOnDay1.
GOTCHA: a burst's per-line collateral budget for the CENTRE line is centerRounds minus
the defender's hits (0 in a MinRng all-hit duel) — collateral on the centre line only
fires when the defender doesn't absorb the whole centre budget; left/right budgets are
independent. No --burst harness reaches a multi-critter cone on the shippable maps (the
cone is narrow + the harness teleports to a fixed approach), so collateral has no real-
data golden — the fake-host test is the deterministic proof.
P13 FOLLOW-UP (Skilldex authentic art, DONE): DrawSkilldex now renders the real
SKLDXBOX.FRM panel + SKLDXOFF/SKLDXON button art (skilldex.cc layout: title at 55,14;
8 buttons at bar-local 15,45+i*36; values at the readouts) instead of the text flyout
(kept as the fallback when art is absent). Skill names centred on each button (AAF
font), the hovered row lights SKLDXON, a left-click on a row arms the skill (additive
to the 1-8 keys). GOTCHA (same as iface.frm): SKLDXBOX ships BAKED "223 %%" placeholder
digits — field-blanked to the recess colour (32,32,32) and overwritten with the real
right-aligned value. Draw-only + additive mouse input → goldens unchanged.

Phase 14 (IN PROGRESS — "Combat Consequences": honor the crit flags the tables
emit but CombatEngine masked — lose-turn/crippled/blind/knockout — + a timed-event
queue + crippled-limb model): M0 crit-table 5-tuple + mask widen (DONE). gen_critical
_tables.py now emits the full row (mult, flags, massiveStat, statMod, massiveFlags) —
only the 2 message-id columns dropped; CriticalEffect widened to 5 fields, Lookup
stride 2→5, checksum regenerated. CriticalTables.HonoredFlags widened from the p9 set
(knockdown/dead/bypass/critical) to also carry KnockedOut/LoseTurn/CripLimbs/Blind
(the engine _set_new_results mask, combat.cc:4809). CritTag lists the honored effects.
KEY FINDING: M0 is BYTE-IDENTICAL across ALL 14 combat fixtures incl. the 3 day-2
crit ones — because the new effects (CRIP/BLIND/KNOCKED_OUT) live in the MASSIVE-
critical column (applied via a secondary stat-roll, wired in M4), NOT the base-row
flags the day-2 crits actually hit; so widening the mask + tag is pure inert plumbing
(no CombatResults write yet — that lands in M2 with its clearing).
M1 EventQueue (DONE): pure queue.cc port (Schedule/Process/Remove, SFALL dedup,
snapshot-on-process), 8 unit tests, not wired yet.
M2 knockout-wake + turn-skip (DONE): a combat-owned _combatTick advances 50/round
(NOT ICombatHost.ClockTicks — decided against, no clock dep on the engine; headless
--fight advances rounds so wakes fire); ApplyCritStatus writes KnockedOut/LoseTurn/
CripLimbs/Blind to CombatResults from a crit (+ schedules the 10*(35-3*EN)-tick wake);
SkipTurnIfIncapacitated forfeits a KO/lose-turn critter's turn (lose-turn one-shot,
KO persists); OnCombatEvent wakes (clear KO, leave prone → stands next turn); +40 to
hit a KO'd target; KillCritter/Reset/EndCombat clear the queue + force-wake. public
KnockOut(critter) seam for tests/scripts. BYTE-IDENTICAL on all fixtures (status flags
only originate from the massive roll, M4 — dormant until then); verified by 3 fake-host
turn-skip/wake tests. M3 crippled-leg move cost + blind effects (DONE): CritterState.MovePointCost (leg crip
→ 4×/8× per-hex AP, critter.cc:1349, applied to the AI approach budget — 1× intact so
byte-identical). DOCUMENTED CUT (final-pass review): the crippled-leg slowdown applies
to NPCs ONLY — the dude's in-combat movement is NOT AP-gated per hex (a pre-existing
PoC simplification: the dude free-walks via WalkTo, ViewerGame.cs ~1974, with no combat
AP charge), so a crippled-leg dude isn't slowed. AP-gating dude combat movement is its
own feature, not a P14 item. CritterState.Perception → PE-5 when blind (stat.cc:191); ComputeToHit
→ blind attacker -25 (combat.cc:4470) + RangedMath.ToHitChance gains attackerBlind for
the ×12 distance penalty (combat.cc:4383, only the positive-penalty branch). DEFERRED:
the crippled-ARM weapon-gate (niche, needs a two-handed proto flag) — the bit is set +
Doctor-healable (M5). 8 pure status tests; byte-identical (effects only fire on set
bits, M4). M4 massive-crit secondary stat-roll (DONE): MassiveUpgrade(eff, defender) — a FAILED
d10 stat roll (_rng.Next(1,11) > defender.Stat(massiveStat)+statMod; combat.cc:4134
statRoll) ORs in the massive flags; wired into BOTH RollAttack + TryThrow crit blocks,
after the severity roll. The ONE new RNG draw — only on an actual crit on a row with
massiveStat != -1, so day-1 (no crit) is byte-identical. Re-recorded 2 day-2 fixtures
(aim-eyes-day2 now shows CRITICAL(blind) → the scorpion's enemy-attack drops 67→42 =
the -25; the all-aimed-eyes run vs 2 scorpions deterministically loses on the shifted
stream — RNG-divergence, not a bug; crit-day2 unchanged — its torso row has no massive
stat). 2 SequenceRng tests (forced KO on fail / resisted on EN-10). M5 Skilldex Doctor limb-fix (DONE — P14 COMPLETE): Formats.Combat.SkillHealing.HealLimbs
rolls the Doctor skill% (d100) against each present crippled limb / blindness in the
gHealableDamageFlags order (blind, L-arm, R-arm, R-leg, L-leg; skill.cc:69-75), clearing
the CombatResults bit on success (the engine reads it live, no resync needed). Wired
into TryHeal for skill 7 ONLY (gate: HP<max OR crippled), before the HP heal. CORRECTION:
First-Aid does NOT heal limbs (skill.cc:574 = HP only) — the task premise was engine-
inaccurate; only Doctor mends limbs (Repair does on robots — none in slice, inert). 3
SkillHealing unit tests; goldens byte-identical (no Doctor-on-crippled scenario). P14
COMPLETE: crit consequences (knockout+wake, lose-turn, crippled limbs, blind) are live,
driven by the massive-crit roll, with the Doctor cure; SCOPE.md/README reconciled.

Phase 15 (DONE — "Make the Chrome Click", UI completeness; the UI audit found
all 8 HUD bar buttons fire, but the weapon slot is inert + Options/Pip-Boy rows are
key-only + item panels keyboard-only + the Pip-Boy lacked Automap/Archives): M0 Pip-Boy
full-window automap (DONE). DrawAutomap renders the authentic AUTOMAP.FRM (519x480) with
every current-elevation object as a colored dot (automap.cc automapRenderInMapWindow:
ax = 449 - 2*(tile%200), ay = 2*(tile/200) + 8 — the engine's flat-buffer v10 decomposed),
colored by FID type (wall grey / scenery green / critter red / item yellow / misc cyan;
dead critters skipped), dude = bright marker; opened from the Pip-Boy (A). DOCUMENTED
SIMPLIFICATIONS: fog-of-war faked all-visible (no OBJECT_SEEN); the per-type colors are
readable approximations of the engine's _colorTable indices; the embedded Pip-Boy mini-
automap stays out (needs automap.db RLE). Harness --automap opens it + prints a
deterministic object census (golden automap-arcaves). Draw-only + additive → other
goldens byte-identical. M1 weapon-slot interactive (DONE): the HUD weapon slot (interface.cc:505 rect 267,26,
188,67) is now a HudButton — clicking it (or N) cycles the attack mode single<->burst
for a burst-capable gun (CycleWeaponMode; non-burst stays single). The mode label goes
LIVE (was faked from the proto nibble): SINGLE/BURST for a burst gun, else AttackModeName.
F now fires with the selected mode (burst when set). DOCUMENTED DIVERGENCE: the engine's
weapon slot left-click FIRES at a held target; we have no held-target model (combat
targets the hovered critter via F/B), so the slot left-click CYCLES the mode instead
(the engine's right-click semantics) — firing stays on F/B-on-hover. Additive (the
--attack/--burst harness calls TryAttack/TryBurst directly, not F or _weaponMode) ->
goldens byte-identical; golden weapon-mode-cycle (--give 9 SMG -> click WEAPON -> Burst).
M2 item-row clicking + overflow paging (DONE): the four item panels (inventory/loot/
barter/trade) share one ItemPanel model (CurrentItemPanels) tagged by ItemPanelKind so a
row CLICK routes to the same action its number key fires (buy/sell/take/give/use; Shift+
click drops in inventory). ItemRowRect is the ONE geometry helper the renderer + hit-test
(TryClickItemPanel) both go through — geometry-only, no Draw dependency, so the headless
--panel-click <side> <row> harness drives the real path. A shared _panelPage window (reset
to 0 on every panel open; PgUp/PgDn while a panel is open — those keys do elevation only
when NO panel is up) makes the 10th+ item reachable; the number keys take the page offset.
Page-0 number keys + Draw-only paging = existing fixtures byte-identical; golden panel-
click-equip (out-of-bounds no-op + a valid equip click). M3 clickable Options/Pip-Boy rows
(DONE — Skilldex parity): OptionsRowRect/OptionsRowAt + PipboyContentOrigin/PipboyRowRect/
PipboyRowAt are geometry-recompute helpers (the SkilldexRowAt pattern) shared by render +
hit-test; the Pip-Boy action rows render in a FIXED band below the variable status text
(reserve 9 lines status / 2 rest) so the geometry is computable. PipboyRows() is the single
(label,action) list the click + page share (Rest rows call DoRest WITHOUT closing, matching
the number keys); Options dispatch routes a row click to the same Save/Load/Main/Quit/Resume
the keys fire. Harness --menu-click <options|pipboy|pipboy-rest> <row> asserts each row's
centre hit-tests back to its own index then dispatches (goldens menu-click-options +
menu-click-pipboy, side-effect-free rows -> map-independent). Draw-only + no new action
paths -> 286 Formats tests + combat + all 19 encounter goldens green. Spillover: per-member
companion trade priced-barter, the embedded Pip-Boy mini-automap (automap.db RLE), inventory
drag-and-drop equip slots (we use click-to-use), the worldmap-screen tab wiring.

Phase 16 (DONE — "The Road Watches Back", worldmap + encounter authenticity; the wasteland
was silent + inert): M0 encounter-name banner (DONE). EncounterTable.Index (0-based load
order = the subtile's encounterType, worldmap.cc:1384/1962) + EncounterEntry.EntryIndex (the
enc_NN number, parsed from the key so a gap can't shift it, :1404) + EncounterResult.MessageId
= 3000 + 50*tableId + entryId (:3511); worldmap.msg loaded lazily -> the banner names the
encounter ("Ambush! A group of spore plants.") instead of the bare literal. Reconciled the
stale EncounterSpawner docstring (per-member If()/Distance/Tile ARE honored — the old "not
parsed" note was wrong). M1 Outdoorsman detect + Yes/No avoid (DONE): the detect roll now
FLAGS the result (EncounterResult.Detected + AvoidXp = max(0,100-detectValue), worldmap.cc:
3475-3477) instead of silently nulling — SAME single rng draw, so the stream is byte-identical;
only a detection's OUTCOME changed. The viewer awards the XP then pops a Y/N (DrawEncounterPrompt,
resolved in Update); N resumes the leg via TravelTo (re-detect or undetected ambush). Headless
resolves synchronously via _autoEncounterAnswer (TravelFrom defaults engage so it never hangs);
--encounter-answer/--force-outdoorsman harness. GOTCHA: the Arroyo->Den leg now DETECTS ARRO_Rats
(previously the silent-avoid skipped it and the leg ran on to spore plants) — travel-arroyo-den
re-recorded to the engaged rats. M2 auto-resume travel (DONE): _travelDestination remembers an
engaged-encounter-interrupted leg's target; leaving the transient map (ApplyTransition Map<0)
sets a deferred _resumeTravelDest -> the next Update continues TravelTo, no worldmap re-click.
--travel-resume harness. DEFERRED (documented): the terrain-difficulty step cadence (worldmap.cc
:4318) presupposes an ANIMATED party dot — our travel is instantaneous (ResolveLeg computes the
whole leg, then loads), so there's no per-step dot to slow. M3 X-FIGHTING-Y team brawl (DONE):
SpawnInstruction.Team — a FIGHTING situation puts each sub-group on a DISTINCT team (group 0->1,
1->2, ...; engine uses per-group team_num/proto, only one shipping group sets it, so we assign
sequential teams — a documented divergence). CombatEngine cross-team targeting: a critter also
targets the nearest HOSTILE on a DIFFERENT team — appended AFTER the dude+party loop, skipping
the actor's own team, so a single-enemy-team fight (EVERY combat golden) is BYTE-IDENTICAL. New
StartBrawl entry point (does NOT touch BeginCombat/AddJoiners) opens the brawl on the dude's
turn. --encounter-fight harness. GOTCHA: ENCOUNTER_SITUATION_FIGHTING is only in the engine's
enum + parse — never used behaviorally; the fight is emergent from proto teams + AI, so we
realize it via team assignment. DOCUMENTED LIMITATION: the brawl runs within dude-involved
combat (he can watch by passing); a fully independent NPC-vs-NPC fight with the dude absent
needs the non-dude-centric turn loop (deferred). M4 per-member If()/Distance + the case-bug
(DONE): locking the now-honored fidelity on real data surfaced a genuine bug — CondRx matched
"If(" case-SENSITIVELY, but ARRO_Spore_Plants' Dead member gates behind the ONLY lowercase
"if (Rand(5%))" in worldmap.txt, so its condition was dropped and the corpse spawned 100%
(10/10 seeds) instead of 5% (~1/10). Fix: RegexOptions.IgnoreCase (the engine's keyword match
is case-insensitive); adds 1 rng draw ONLY for that member, so every non-spore-plants golden
stays byte-identical. The --encounter census now prints each flat (If()-gated) member at its
Distance pin (pid/tile/dist/dead) + splits the mislabeled corpses=N into items=N + corpses=N.
New golden encounter-spore-plants; the per-member If()/Distance LOGIC + Counter one-shot budget
were already unit-tested (EncounterSpawnerTests/WorldEncountersTests). 291 Formats tests, 23
encounter + 14 combat goldens green. Spillover: animated worldmap travel (the dot + terrain
cadence), the fully-independent NPC-vs-NPC brawl loop, the special-encounter circle pin.

Phase 17 (DONE — "The World Visibly Moves", animated worldmap traversal): travel was
INSTANTANEOUS (click -> compute the whole leg -> load); now a party dot crosses the worldmap.
M0 stepwise TravelLeg iterator (DONE): refactored WorldmapTravel.ResolveLeg from whole-leg-
compute into a pure per-pixel-step TravelLeg.Step() (holds the Bresenham cursor + the
WorldEncounters Δ3 anchor across calls); ResolveLeg is now a DRAIN-loop over Step() — one
Step() == one old iteration, same RNG draws in the same order, so all 5 callers + the goldens
are BYTE-IDENTICAL. Proven by GameDataFact StepwiseDrainMatchesResolveLeg. The de-risk
checkpoint (the proven P13-M1 pattern). M1 terrain cadence (DONE, pure): WorldmapFile parses
[Data] terrain_types=Desert:1,Mountain:2,... -> TerrainDifficulties + TerrainTravelDifficultyAt;
TerrainCadence ports wmPartyWalkingStep's _terrainCounter (cycles 1..4, steps a pixel only when
counter/difficulty>=1, so 1/2/3/4 -> 4/3/2/1 of every 4 ticks advance — mountains slow the dot).
PURE pacing — it does NOT touch the game clock or the encounter rolls, so animation speed is
independent of encounter fidelity. M2 animated dot (DONE): live play ANIMATES the leg — Update
drains Step() over wall-time (TravelTickMs=30), terrain-paced; the clock advances per pixel
(same total as sync); an encounter pauses the dot (the avoid prompt), Esc/click halts (a fresh
click re-routes). The headless harness keeps the SYNCHRONOUS whole-leg resolve (byte-identical)
via _animateTravel=false (set in the TravelFrom/TravelResume actions). TravelTo's encounter+
arrival tails extracted into shared HandleLegEncounter + ArriveAt so both paths end identically.
WorldmapScreen.DrawPartyDot. --travel-step harness drives the REAL StepAnimatedTravel headlessly
(golden travel-step: animated path matches the sync ARRO_Rats at worldPos 204,143, cadence
visible 26 ticks > 20 pixels). M3 unified travel surface (DONE): the survey's "second travel
surface" was a phantom — every click already routes through the ONE TravelTo (encounters since
P10/16, the dot since M2); reconciled the stale WorldmapScreen docstring ("no encounters, no
travel time"). The party dot now also renders as a persistent "you are here" whenever a worldmap
position is known, not just mid-travel. The lone remaining worldmap simplification is no subtile
fog-of-war reveal (separate feature). M4 save/restore mid-travel (DONE): SaveState.Travel-
DestinationAreaId (additive-V2, -1=none) captures an in-flight leg's target; LoadGame drops the
stale leg+prompt (cursors are meaningless post-reload) and queues an auto-resume via the P16-M2
_resumeTravelDest machinery. DIVERGENCE (documented): the engine drops you STOPPED on a mid-walk
reload; we resume, consistent with the P16-M2 post-encounter auto-resume. --travel-save-mid
harness (golden travel-save-mid: saved at dot 188,135 -> round-trips + resumes toward area 1).
294 Formats tests, 25 encounter + 14 combat goldens green. Spillover: subtile fog-of-war reveal
(worldmap exploration), the fully-independent NPC-vs-NPC brawl loop.

Phase 18 (DONE — "Combat Movement Symmetry"): the dude free-walked the whole map for free on
its combat turn while NPCs paid AP, and the P14-M3 crippled-leg slowdown never touched the
player. M0+M1 AP-gated dude combat movement (DONE): the _dude.TileChanged closure deducts
CritterState.MovePointCost per hex from _combat.DudeAp DURING COMBAT (CombatEngine.SpendDudeAp,
clamped 0) and HALTS the walk when the next hex is unaffordable; the click-to-walk is refused
when AP can't afford a hex. Out of combat, movement is free (no AP model). MovePointCost's 4x/8x
crippled-leg cost (P14-M3, NPC-only) now charges the dude — closing the SCOPE asymmetry; Doctor-
heal (P14-M5) restores it. FIX: DudeController.Update touched _rotations AFTER TileChanged,
NPE-ing when a handler Stop()s the walk (the survey-flagged "AP-truncation desync") — guarded.
GOTCHA: the combat goldens NEVER walk the dude (--fight teleports adjacent + attacks), so AP-
gating MOVEMENT is inert there → all combat goldens byte-identical; the new behaviour is proven
by the --combat-walk harness (goldens combat-walk-full AP8→4 hexes / -truncated AP2→2 hexes /
-crippled AP8 4-per-hex→2 hexes). M2 crippled-arm weapon gate (DONE): WeaponProtoStats.IsTwoHanded
(extendedFlags 0x200); CombatEngine.WeaponBlockedByCrippledArms (combat.cc:5655) — both arms
crippled blocks ANY weapon attack, one arm blocks a TWO-HANDED weapon, unarmed never gated;
wired into the dude's TryAttack/TryBurst/TryThrow. DOCUMENTED CUT: the NPC AI attack is not
gated (NPCs rarely lose an arm + it'd churn the sensitive day-2 crit goldens). Inert for an
un-crippled dude → byte-identical; fake-host CrippledArmsGateWeaponAttacks. M3 faithful AI flee
(DONE): TryFlee ported from combat_ai.cc _ai_run_away — head directly AWAY (threat→self rotation,
or ±1), as far as AP allows, to a tile reached by a REAL A* path (was greedy neighbour-stepping
that snags on walls); the run uses the whole turn. Flee draws NO RNG → attack rolls byte-identical;
denbus2-fight-flee re-recorded for the faithful retreat tiles only (SAME outcome: rounds=5, dude
dies identically). M4 (DONE): the movement goldens (M0's combat-walk trio) + the one affected
re-record (M3's flee) ARE the deliverable; NO RNG-divergence occurred (dude doesn't walk in
--fight, flee is deterministic). 295 Formats tests, 28 encounter + 14 combat goldens green.
Spillover: AP-gating the dude's OUT-of-combat move (a separate feature), the NPC crippled-arm gate.

Phase 20 (DONE — "Doc Truth + Presentation Polish", a breather phase): M0 stale-doc
reconciliation (DONE): SCOPE.md dropped the "AP-gated player movement is OUT" bullet (P18
shipped it) + refreshed "What's in" (combat AP-gating/crippled-arm/X-FIGHTING-Y/flee-pathing;
worldmap dot/avoid/resume/save-mid-travel); CLAUDE.md marked the P10 "v1 cuts" list SUPERSEDED
(wait/dismiss persistence #2/#3, per-member If()/Distance P10+P16-M4, X-FIGHTING-Y P16-M3,
projectile tween #11 all shipped — only Vic's radio item source remains). Pure docs. M1 embedded
Pip-Boy mini-map (DONE): INVESTIGATION found automap.db is a GENERATED save artifact (engine
writes MAPS\AUTOMAP.DB as you explore — not in the game data, our PoC never writes it), so the
specified RLE-decode has nothing to decode. DIVERGENCE: render the mini-map from LIVE objects
(the P15-M0 source) in the Pip-Boy status page's left column instead — AutomapColor shared helper
(DrawAutomap refactored onto it, golden-safe), DrawPipboyMiniMap col→x(mirrored)/row→y scaled.
Draw-only. M3 Pip-Boy real calendar (DONE): GameClock.DateAt/DateString port scripts.cc
gameTimeGetDate (walk months from the FO2 start July 25 2241 — sfall_config.cc start year 2241/
month 6/day 24, output +1; the old GameClock comment wrongly said "June 24"); the Pip-Boy shows
"July 25, 2241" not "Day 1". Draw-only + pure date math. M2 automap fog + colors (DONE): colors
aligned to the engine's IN-GAME _colorTable (walls→pure green [992], scenery→dark green [480];
DIVERGENCE: we still show critters/items + a WHITE dude, which the in-game map hides/paints-red,
for a more useful map). Fog: _seenObjects accumulates objects within AutomapSightRadius (14 hexes)
of the dude — revealed at spawn + per hex, cleared per map; the automap + mini-map plot only seen
objects (SIMPLIFICATION: proximity not LoS, not save-persisted). The --automap census now reports
the seen dots (arcaves 186/1843 at spawn); automap-arcaves re-recorded. M4 burst collateral
real-data golden (DONE — NOT inert): the standard --burst's fixed dir-3 approach never aligned the
narrow cone with a bystander, so added --burst-at <fromHex> <targetHex> to aim it; golden denbus2-
burst-collateral bursts a Den slave (11670) from across the cluster (13270) and sweeps TWO real
bystanders (Handsome@12670 + Cute@11272) onto the left/right cone lines — the first real-data
proof of the P13-M2 cone (the fake-host test was the only one before). 296 Formats tests, 28
encounter + 15 combat goldens green. Spillover: true-LoS + save-persisted automap fog, the
automap.db write side (generate the explored-tile RLE), the in-Pip-Boy date calendar page.

Phase 21 (DONE — "Script-driven map effects", from the fo2ce gap analysis): wire two arity-
stubbed external families that the slice ACTUALLY fires (verified via the OnStubbedExternal log
on artemple/arcaves map_enter). M0 lighting (DONE): set_light_level (0x80E9) + obj_set_light_level
(0x8107) were stubbed though the LightGrid existed since P3-M1. IVmExternals.SetLightLevel/
SetObjectLightLevel + IntVm dispatch + ScriptHost LightLevelRequested/ObjectLightRequested
callbacks. set_light_level -> LightGrid.AmbientFromLightLevel (opSetLightLevel's two-segment lerp,
0->MIN/50->MID/100->MAX) sets _lightGrid.Ambient + pins it (AmbientFixed, so the day/night clock
stops overriding). artemple's map_enter calls set_light_level(100) -> max+pinned (CONFIRMED live).
obj_set_light_level sets the object's light fields (intensity*65636/100, the engine's literal) +
OBJECT_LIGHTING flag + rebuilds (MapObject light fields made mutable; no slice map uses it but it
shares the wiring + the tested AddObjectLight path). The callbacks are SILENT (set_light_level
fires on EVERY map_enter -> would spam every golden); --light-probe reports the post-map_enter
ambient. M1 reg_anim (DONE, with honest scope finding): the slice fires reg_anim ONLY as
reg_anim_animate_forever (artemple+arcaves; denbus2 none), and EVERY target is SCENERY (firepits,
a waterfall) our multi-frame FRMs already auto-loop -> visually redundant on the slice. Wired it
anyway (0x8126: critters get an anim-coded looping FID = lights up for free; scenery loops its
FRM); silent callback (no spam into the arcaves combat goldens). DEFERRED (no slice content, would
be dead code): reg_anim_func begin/end queue + the MOVEMENT ops (obj_move/run_to_tile/obj) — the
substantive "scripted on-entry NPC movement" reg_anim feature, which no shippable map exercises.
--reg-anim-probe reports the registrations. Golden script-light covers both (artemple: ambient
max+pinned + 2 firepits). 297 Formats tests (+ AmbientFromLightLevel), 29 encounter + 15 combat
goldens green (all byte-identical bar the new script-light; the silent callbacks were the key).
Spillover: the reg_anim movement ops + begin/end sequencing (light up when a map uses them).

Phase 22 (DONE — "The Map Remembers Where You've Been", worldmap subtile fog-of-war; the
"lone remaining worldmap simplification" from the gap analysis): M0 reveal model + persistence
(DONE). New pure Formats.Map.WorldmapFog = the per-subtile UNKNOWN/KNOWN/VISITED grid the engine
keeps on wmTileInfoList[].subtiles[][].state (840 cells = 20 worldmap tiles x 7x6 subtiles).
Ported wmSubTileMarkRadiusVisited (radius 1 — the PERK_SCOUT radius-2 branch is OUT, no perks):
the 3x3 ring -> KNOWN (never downgrading an already-VISITED cell, the wmMarkSubTileOffsetVisitedFunc
guard), the centre -> VISITED, + the SUBTILE_FILL_S/W strip spread (the real worldmap.txt uses ONLY
Fill_W, the western ocean columns — so the W-spread is the only one that fires; ported both anyway).
Subtile.Fill parsed from worldmap.txt field f[1] (was dropped before) via SubtileFill.Parse. The
reveal rides INSIDE the pure TravelLeg: ctor reveals the start, Step() reveals each new Bresenham
pixel — so BOTH the synchronous ResolveLeg drain (goldens) AND the animated dot reveal the SAME
subtiles, and ArriveAt covers the roll-less first travel. CRITICAL: the fog draws ZERO RNG (pure
position math), so passing it never perturbs the encounter stream — every existing travel golden
stayed BYTE-IDENTICAL (verified). Persistence: SaveState.RevealedSubtiles (sparse flat-index->state
dict, additive within V2 — a fresh game saves {}); _worldFog nulled alongside _worldmap on new-game/
load then re-imported (same lazy-reparse pattern as EncounterCounters). Harness --fog-probe <x> <y>
<area> drains a leg WITH the fog (ignoring encounter outcomes so the WHOLE corridor maps) -> the
"worldmap-fog:" golden line (arroyo->den seed 2: 289 steps, start+arrived VISITED, 10 visited/26
known). M1 render + marker gate (DONE): WorldmapScreen.Draw overlays per-subtile veils (UNKNOWN =
opaque black, KNOWN = alpha-120 black ~ the engine's intensityColorTable[..][75] dim, VISITED =
clear), drawn over terrain but UNDER markers/dot. Area markers + HitTest now gated on IsDiscovered
= city.txt start_state=On (the 14 major cities, visible from game start like the real game — the
engine's city->state init) OR the location subtile revealed (the 35 Off sub-areas: Car Outta Gas /
Klamath Toxic Caves appear once you explore near them). DOCUMENTED APPROXIMATION: marker discovery
is tied to subtile reveal rather than the engine's separate circle-hotspot detect (worldmap.cc:3068)
— a clean derive-from-fog choice, no second city-state subsystem / save field. Draw-only + additive
mouse-gate -> goldens byte-identical. 7 WorldmapFog unit tests (ring/centre/W-spread/export-import/
off-grid + a GameDataFact TravelLeg-reveals-the-real-arroyo->den-path); 304 Formats tests, 30
encounter + 15 combat goldens green. The worldmap fog moves from the gap-analysis backlog to DONE.

Phase 23 (DONE — "See-Through", object translucency; from the fo2ce gap analysis): glass/steam/
energy/red/wall objects rendered OPAQUE; now they alpha-blend. DE-RISK FINDING (the headline): the
whole shippable slice has EXACTLY ONE genuinely-translucent object — a TRANS_STEAM at denbus1 hex
28105 (pid 0x100001D). The hundreds of "TRANS_NONE" objects across every map are OPAQUE — TRANS_NONE
(0x8000) is the engine's "render solid, never fade near the dude" flag, NOT a translucent effect
(object.cc:5067 switch has no NONE case -> default opaque blit). Glass/energy/red/wall: ZERO slice
objects. User opted for the full faithful impl anyway (the 4 empty types light up for free if content
appears). M0 (DONE): pure Formats.Proto.TransType {None,Wall,Glass,Steam,Energy,Red} + Translucency.
FromFlags (the object.cc:943 priority: TRANS_NONE wins->opaque, else wall/glass/steam/energy/red);
ProtoInfo.Translucency computed from the already-parsed Flags. M1 (DONE): DrawObjects folds a per-type
(tint,alpha) into the object's light tint before the existing premultiplied-AlphaBlend Draw (the same
Color*float path the egg-fade uses). The 5 tints are the engine's _colorTable blend SEEDS (object.cc:
3467-3471 RGB555 -> RGB8) softened halfway to white; the per-pixel luminance weighting + exact 8-bit
palette composite COLLAPSE to one uniform alpha per type — a DOCUMENTED DIVERGENCE (SpriteBatch over
RGBA has no 8-bit destination buffer to blend into; the real _dark_translucent_trans_buf_to_buf reads
grayTable[src]<<8 + dst through the blend table). TransType cached per-pid in the viewer. VERIFIED: a
before/after screenshot diff shows ONLY the steam object's pixels changed (the wisp now ~50% see-
through) and nothing else regressed. Draw-only -> all goldens byte-identical. Harness --center <hex>
(screenshot camera); MapDump gained a translucency census (skips the TRANS_NONE noise). 12
TranslucencyTests (each bit, the NONE-wins priority, the engine decode order, mask, ProtoInfo); 316
Formats tests, 30 encounter + 15 combat goldens green. Translucency moves from the gap-analysis backlog
to DONE. Remaining feasible backlog: item encumbrance, dialog IQ-gating, blood/gore splats.

Phase 24 (DONE — "Every Pound Counts", carry weight + encumbrance; from the fo2ce gap analysis):
item weight was PARSED-THEN-SKIPPED and CARRY_WEIGHT computed-but-never-enforced; now both are
live. RESEARCH (encumbrance-understand workflow, 5 readers + critic) ground-truthed the enforcement
so it wasn't guessed: over-encumbered does THREE things in the engine — (1) a max-AP penalty
(stat.cc:198 — 1 AP per 40 lbs over, +1), (2) run->walk downgrade (animation.cc:646 — N/A here:
Hexwaste has only WalkTo, no run, so DOCUMENTED inapplicable), (3) pickup/loot/barter BLOCKING
(item.cc:313 / inventory.cc:4706/4360). NO movement-speed or worldmap penalty (confirmed absent).
M0 research + design. M1 pure (BYTE-IDENTICAL — the proto read position is unchanged, skip-8+read-4
== the old skip-12, so weapon/cost parsing stays aligned): ProtoDatabase reads the weight int it used
to skip -> ProtoInfo.Weight; CritterStat.CarryWeight=12 + CritterState.CarryWeight (25*ST+25,
stat.cc:571 — no perks [STRONG_BACK/PACK_RAT out], no SMALL_FRAME trait [traits out]); new pure
Formats.Map.InventoryWeight ports item.cc itemGetWeight (base + power-armor/2 [pids 3/232/348/349] +
container recursion + weapon loaded-ammo boxWeight*ceil(rounds/boxSize)) + objectGetInventoryWeight
(sum item*stack; equipped items stay IN the list so they count once, matching the engine's primary
loop — the separate-slot block is an engine artifact) + IsEncumbered (carried>cap) + ActionPointPenalty.
M2 enforcement+display: the AP penalty rides a new ICombatHost.DudeEncumbranceApPenalty() DEFAULT
interface method (0 -> the fake-host combat tests need no inventory model) routed through one
CombatEngine.ResetDudeAp chokepoint (replaces all 7 dude `_dudeAp = MaxActionPoints` sites);
DUDE-ONLY (documented — the player is who over-loads; keeps the sensitive combat goldens stable +
NPCs are authored with sane loadouts). Pickup/loot-single/barter-buy gates (DudeCanCarry; --give
BYPASSES by design = god-mode); take-all is all-or-nothing (engine inventory.cc:4360 + avoids the
per-item gate spinning the loop — extracted TakeAllFromContainer shared by the A key + harness).
Display: "Total Wt: N/M" below the inventory panel + a Carry Weight line on the Pip-Boy status,
RED when encumbered. VERIFIED: --weight-probe goldens (1 SMG=7lbs/cap 250 unenc; 60 SMGs=420lbs ->
encumbered, AP penalty (420-250)/40+1=5 EXACT); the loot gate proven on denbus1 hex 18146 (overloaded
take-all refused, light dude takes all 13); the red readout screenshotted; combat + every other golden
BYTE-IDENTICAL (the dude isn't over capacity in any golden -> penalty 0; display is Draw-only). Harness
--weight-probe + --center <hex>; MapDump gained a container census. 12 InventoryWeightTests; 328 Formats
tests, 32 encounter + 15 combat goldens green. Encumbrance moves from the gap-analysis backlog to DONE.
Remaining feasible backlog: dialog IQ-gating, blood/gore splats.

Phase 25 (DONE — "Speak Your Mind", dialogue IQ-gating; from the fo2ce gap analysis):
giq_option's dumb/smart dialogue options were gated against a HARDCODED intelligence of 5
(DialogIntelligence()=>5 in both IntVm + ScriptHost); now they read the dude's REAL
STAT_INTELLIGENCE. The comparison logic (interpreter_extra.cc _op_giq_option: positive iq = min
INT smart option, negative iq = max INT dumb/stupid option, skip otherwise) was ALREADY a faithful
port (IntVm 0x8121) — only the IN SOURCE was stubbed. M0 research confirmed the engine reads
critterGetStat(gDude, STAT_INTELLIGENCE) (+ Smooth Talker perk rank, OUT — no perk system) and that
the slice FIRES it heavily (instrumented denbus2: iq=4 x33 smart + iq=-3 x9 dumb + iq=1/5), so it's
live content, not dead code. M1: ScriptContext.DialogIntelligence() => _host.CritterStatValue(_dude, 4)
(null dude -> 5, the neutral default); extracted the gate decision to pure Formats.Int.DialogGate.
IqOptionVisible(iq, intelligence) (testable + self-documenting; the VM dispatch now calls it). KEY:
the DEFAULT dude's real IN is 5 — IDENTICAL to the old hardcode — so the vic-recruit/levelup/save
goldens (which navigate Metzger+Vic dialogue via fixed --talk-seq indices) are BYTE-IDENTICAL, and no
--character-combat golden navigates giq dialogue, so nothing churned. Verified: --iq-probe <hex>
<forceIn> reports the greeting's OPTION COUNT (an int — NEVER the copyrighted option text) for a
forced IN; Vic's greeting offers 1 option at IN 2 (smart options gated out) vs 4 at IN 9 (goldens
iq-gate-dumb/iq-gate-smart); Metzger 2 vs 4. 10 DialogGateTests; 338 Formats tests, 34 encounter + 15
combat goldens green. IQ-gating moves from the gap-analysis backlog to DONE. Remaining feasible
backlog: blood/gore splat FRMs (the last small in-scope item).

Phase 26 (DONE — "Messy Deaths", gory death animations; the last in-scope gap-analysis item):
corpses always used a fixed FALL_BACK/FALL_FRONT; now a kill picks a GORE variant (sliced /
charred / electrified / dancing-autofire / big-hole / exploded) by the killing blow. RESEARCH
finding: "blood splat" in FO2 is NOT a separate ground object — it's the death ANIMATION (the
corpse FID), chosen by actions.cc _pick_death from damage type + damage + attacker animation, art-
checked by _check_death, gated by the violence-level preference. M0 research. M1: pure Formats.
Combat.DeathAnims.Pick (the _pick_death port: gNormalDeathAnimations/gMaximumBloodDeathAnimations
tables by DAMAGE_TYPE; single normal shots + melee stay FALL_BACK [no gibbing], bursts/lasers/
explosions/thrown-explosives use the table at damage>=15; the BLOODY_MESS trait + Pyro/Flameboy
perks + Molotov + CRITTER_SPECIAL_DEATH are OUT — no trait/perk system) + AttackAnimFor helper.
Threaded the gore context (DamageType + AttackerAnim) into PendingAttack/Burst/Throw + KillCritter
(+damage/damageType/attackerAnim, defaults FALL_BACK for script kills) at all 3 attack sites
(dude/ally/enemy) + 5 KillCritter callers (single FIRE_SINGLE, burst FIRE_BURST, throw THROW_ANIM,
explosion EXPLOSION). The host's PickDeathAnim generalised to the _check_death art-resolve
(desired gore anim if it ships, else FALL_BACK/FRONT) via a new ICombatHost.PickDeathAnim(critter,
desiredAnim) — the corpse SF art is still deathAnim+28 (FALL_BACK 20 -> SF 48, holds for the gore
anims). VIOLENCE fixed at NORMAL (no preferences screen — documented; shows gNormalDeathAnimations
gore without MAX_BLOOD obliteration). SCOPE finding (the translucency-style de-risk): denbus2 humans
(pid 0x1000003/4) SHIP the gore art (burst->DancingAutofire, laser->SlicedInHalf, explode->BigHole,
all gore=True) so it's LIVE; arcaves scorpions (0x1000005) lack it -> faithfully fall back to
FALL_BACK (gore=False) — which is WHY the combat goldens (scorpion kills) stayed BYTE-IDENTICAL, plus
the corpse FID is cosmetic (never in a transcript). VERIFIED: --death-probe <hex> reports the picked
+ art-resolved anim per damage kind (goldens gore-human=gore / gore-scorpion=fallback); a burst-killed
Villager screenshotted as a DancingAutofire gore corpse. 15 DeathAnimsTests; 353 Formats tests, 36
encounter + 15 combat goldens green. GORE moves the LAST feasible in-scope item from the gap-analysis
backlog to DONE — what remains is out-by-design (perks/karma/quests/content) or a scope expansion past
the Arroyo->Klamath->Den slice.

Phase 28 (IN PROGRESS — "The Character Sheet Grows Teeth", traits + perks; the first big
scope-expansion past the gap-analysis backlog): the marquee FO2 character-progression layer.
M0 research (traits-perks-understand workflow, 5 readers + design critic). M1 trait effects (DONE):
ported trait.cc traitGetStatModifier + traitGetSkillModifier verbatim into pure Formats.Combat.
TraitModifiers (16 traits; Chem Reliant/Resistant OUT — no addiction system; Sex Appeal has no engine
impl). Applied LIVE in CritterState.Stat/SkillValue via a new traits param (4th, after taggedSkills) —
exactly the engine's per-read critterGetStat behaviour; the SPECIAL->derived propagation (Gifted/
Bruiser raising HP/melee) is baked at character-creation in the engine, NOT at stat-read, so it's a
documented future GcdFile.Create concern (no trait picker yet). The dude's traits flow from _dudeGcd.
Traits at both GetCritterState sites; NPCs/no-traits pass null -> 0 modifier (the INERT-BY-DEFAULT
invariant). has_trait was already wired (type 2 -> DudeTraits). KEY FINDING: the combat premade Narg
(combat.gcd) carries traits [6,15] = HeavyHanded + Gifted, silently ignored until now; M1 makes them
apply, so the 6 --character-combat COMBAT goldens shifted (Gifted -10 all skills -> 57%->47% to-hit,
the RNG stream then cascades) and were RE-RECORDED to the correct trait-applied behaviour. The default-
dude combat goldens + the --character-combat UI/movement/weight encounter goldens stayed byte-identical
(they don't read the combat skill). Harness --trait-probe <id1> <id2> (sets the dude's traits, reports
the live stat/skill effect); 4 goldens (none/gifted/bruiser+kamikaze/goodnatured) + 10 TraitModifiers
tests. M2 perk infrastructure + selection (DONE): tools/gen_perk_table.py parses perk.cc's hardcoded
gPerkDescriptions array -> Formats.Perks.PerkTable.g.cs (119 perks, FNV-1a checksum-guarded, the
gen_critical_tables.py pattern; PerkData record). PerkRules ports perkCanAdd (maxRank cap, minLevel,
the skill/gvar param gates with FIRST_ONLY/OR/AND modes + negative-value "at most", the per-SPECIAL
reqs positive=min/negative=max) + the cadence (3 levels/perk, 4 with Skilled, cap 37 — character_
editor.cc:5713) + the DATA-DRIVEN stat perks via StatModifier (each perk's stat/statModifier × rank:
Toughness->DR, Action Boy->AP, More Criticals->crit, Lifegiver->HP, ...) folded into CritterState.Stat
alongside traits (so M3's stat perks are FREE — only combat/skill-path perks need wiring). DudePerkRanks
(int[119]) on the viewer, passed as CritterState's 5th param, persisted (SaveState.DudePerkRanks,
additive-V2 sparse — null when no perk taken); has_trait(type 0) now returns the dude's perkGetRank via
ScriptHost.PerkRankProvider. INERT-BY-DEFAULT holds (zero ranks -> 0 modifier -> all combat + encounter
goldens BYTE-IDENTICAL). VERIFIED: traits + perks STACK (Narg's HeavyHanded +4 melee, then Bonus HtH
Damage perk +2/rank -> 8->10->12); --perk-probe <index> <level> exercises the gates + effect (golden
perk-gates: level-gate at lvl2, eligible+stack at lvl3, stat-gate on More Criticals via Narg's LK4).
16 PerkTests. M3 high-impact (combat/skill-path) perk effects (DONE): the data-driven STAT perks
(Toughness/Action Boy/Lifegiver/More+Better Crits/Faster Healing/Bonus HtH Damage/Strong Back/Dodger/
+SPECIAL/rad+poison) already work from M2's StatModifier fold; M3 adds the NON-stat combat/skill perks
via a new ICombatHost.DudePerkRank(int) (default 0): Swift Learner (+5%/rank XP, viewer AwardXp,
stat.cc:737), Bonus Rate of Fire / Bonus HtH Attacks (−1 AP ranged/melee, item.cc:1693), Sharpshooter
(+2 PE/rank ranged to-hit, combat.cc:4355), Slayer (every melee/unarmed hit crits) + Sniper (ranged hit
crits on d10≤Luck) in RollAttack's crit block (combat.cc:3866/3891) — all DUDE-ONLY + short-circuited on
rank 0 so a perk-less dude draws no extra RNG (goldens BYTE-IDENTICAL). Formats.Perks.PerkId names the
wired indices. DEFERRED (documented): Jinxed's crit-FAILURE (Hexwaste doesn't model single-shot crit-fail
consequences); Educated (+skill points/level); the rest of the 119 are data-present (stat perks live,
these specific effects pending). VERIFIED: --perk-probe granted Swift Learner -> +1000 XP shows +1050;
Bonus HtH adds unarmed swings; 3 fake-host CombatEngine tests (Bonus HtH AP 10->8, Slayer forces a crit,
the no-perk control stays non-crit). M4 perk-pick UI + char sheet (DONE — P28 COMPLETE): the character
sheet (C/K) now shows the dude's Traits + Perks (names from trait.msg id 100+i / perk.msg id 101+i,
loaded lazily; trait.cc:74 / perk.cc:218) and the effective trait/perk-modified SPECIAL (was the base);
when picks are available (G) a modal perk picker lists the eligible perks (PerkRules.CanAdd-filtered),
1-9 selects one. AvailablePerkPicks = PicksEarned − ranks-taken; EligiblePerks/ChoosePerk are the
selection core. Text-panel picker (the authentic PERKWIN.FRM art is deferred polish, like the early
Skilldex text fallback — documented). Harness --perk-pick <level> <row> drives the real picker
(golden perk-pick: lvl3 1-pick/14-eligible/closes, lvl6 stays open, lvl1 0). Draw-only + harness-gated
+ inert-by-default -> all combat + encounter goldens BYTE-IDENTICAL. P28 COMPLETE: traits (16, live) +
perks (119 table, ~20 stat perks live via the fold + 6 combat/skill perks wired, rest data-present) +
the picker + char-sheet display + save persistence; the marquee character-progression layer is in.
382 Formats tests, 42 encounter + 15 combat goldens green. Spillover: PERKWIN.FRM art, the combat-crit
trait spillover (One Hander/Fast Shot/Finesse-DR/Jinxed), Educated skill points, companion perks.

Phase 29 (IN PROGRESS — "Finish the Character", the P28 traits + perks spillover): complete the six
consciously-deferred items so the character-progression layer is whole. Everything keeps the INERT-BY-
DEFAULT invariant (a trait-less/perk-less dude yields zero modifiers → combat + encounter goldens byte-
identical). M1 combat-path trait leftovers (DONE): new ICombatHost.DudeHasTrait(int) (default false;
ViewerGame reads _dudeGcd.Traits) mirrors DudePerkRank. One Hander (combat.cc:4404 — in ComputeToHit,
dude + any WIELDED weapon: two-handed −40 else +20; skipped unarmed/NPC). Fast Shot (item.cc:1679/1825 —
−1 AP for a range>2 weapon in TryAttack's apCost, now floored at 1 like the engine; AND can't aim: a
called shot from a Fast Shot dude is coerced to uncalled, mirroring critterCanAim). Finesse (combat.cc:
4540 — a dude attacker raises the DEFENDER's DR +30 on the non-bypass path; threaded as an extraDr param
through CombatMath.RollDamage/RollWeaponDamage + RangedMath.RollDamage, set at the RollAttack damage
site; the +10 crit-chance UPSIDE was already live via CritterState/TraitModifiers). Jinxed (combat.cc:
3857 — on a dude MISS, d2==1 fumbles into a lost turn, _dudeAp=0). SIMPLIFIED + DOCUMENTED: Jinxed
honours only DAM_LOSE_TURN (not the 7×5 _cf_table drop/explode/cripple/on-fire), is dude-only (the
engine fumbles EVERY combatant when the dude is Jinxed), and gates on CriticalsEnabled (our day-2 proxy)
vs the engine's day-6 crit-failure gate. Finesse-DR is wired on the single-attack RollAttack path only
(burst/throw/explosion Finesse is a documented residual). The d2/extraDr are taken ONLY when the trait
is set, so a trait-less dude draws no extra RNG. Proven by 4 fake-host CombatEngineTests (One Hander
to-hit +20/−40, Fast Shot AP + aim-block, Finesse defender-DR 50→20, Jinxed lose-turn); all 15 combat +
42 encounter goldens BYTE-IDENTICAL (Narg's traits are HeavyHanded+Gifted, none of the M1 four).
M2 Educated + Skilled/Gifted skill points (DONE): ported the FULL per-level skill-point grant
(character_editor.cc:5686) into SkillSet.PointsPerLevel — 5 + 2·IN(with trait mod) + 2·rank(Educated) +
5·Skilled − (Gifted ? 5), floored at 0; the banked cap 99 stays in AwardXp. KEY: the IN is the TRAIT-
modified Intelligence (Gifted's +1 IN, NOT drug/perk bonuses — critterGetBaseStatWithTraitModifier), so
Gifted hits skill points twice (+1 IN → +2 SP, then the explicit −5 = net −3/level). The P28 note's
"−10" was the skill-VALUE penalty (TraitModifiers.GetSkillModifier), DISTINCT from this −5 skill-POINT
penalty. VERIFIED end-to-end: a default IN-5 dude grants 15/level, Skilled 20, Gifted 12 (5+12−5). The
grant line (level-up: skillPoints=) is in NO golden filter + skill points are never spent in a fixture,
so all goldens stay byte-identical; a trait-/perk-less dude is unchanged (defaults = the old 5+2·IN).
6 SkillSet unit tests.
M3 trait picker in character creation (DONE): a new MenuState.CreateTraits step (Stats → TRAITS →
Tags → finish) — a 2-column grid of the 16 trait names (the P28-M4 TraitName/trait.msg loader), Space
toggles (cap 2, OPTIONAL — Enter advances with 0), Esc backs out. GcdFile.Create gained a traits param:
it BAKES the SPECIAL→derived propagation from the TRAIT-modified primaries (Gifted/Bruiser/Small Frame
raise HP/AP/AC/melee/carry/sequence at creation, mirroring critterUpdateDerivedStats), while the base
primary SPECIAL stays UNMODIFIED so CritterState.Stat adds the trait modifier LIVE (no double count) —
and the DIRECT derived modifiers (Kamikaze AC, Heavy Handed melee, Fast Metabolism heal/rad/poison,
Finesse crit) are added live too. Traits is STORED on the sheet. VERIFIED end-to-end: a 5/5/5/5/5/5/5
dude is HP30/AP7; Gifted → HP33/AP8; Bruiser+Small Frame → HP32/AP8 (the screenshot confirms the picker
UI). Harness --create gained an optional 4th ":trait,trait" section; --show-create [stats|traits|tags]
jumps to a step for screenshots. NO golden uses --create + GcdFile.Load is untouched + empty-traits
Create is byte-identical, so all goldens hold. 3 GcdCreate unit tests (Gifted bake + base-unmodified +
live read, Bruiser ST-bake/AP-live split, no-traits-unchanged). DOCUMENTED RESIDUAL: the Gifted +1
primary→SKILL-VALUE propagation isn't applied for created chars (SkillSet.Value reads the unmodified
base — a pre-existing P28 skill model, not new here); premades load their pre-baked skills.
M4 curated perk-effects batch (DONE): wired the feasible combat/skill perks that touch existing
systems, each via ICombatHost.DudePerkRank, dude-only, inert at rank 0. Bonus Ranged Damage (+2/rank,
ranged only — combat.cc:4547; threaded as a rangedDamageBonus param added to the raw roll BEFORE the
×2/÷2 wrapper in RangedMath.RollDamage, so it nets +2/rank). Living Anatomy (+5 vs a living non-robot/
alien target — combat.cc:4619) + Pyromaniac (+5 with a fire weapon — combat.cc:4626): flat post-armor
adds in RollAttack via DudeFlatDamageBonus. Weapon Handling (+3 effective ST vs the gun min-ST to-hit
penalty — combat.cc:4414) in ComputeToHit. Heave Ho (+2 effective ST/rank for the THROW RANGE only,
cap 10 — item.cc:1613) in TryThrow. PerkId gained the 6 indices (BonusRangedDamage=4, HeaveHo=35,
QuickPockets=48, LivingAnatomy=97, Pyromaniac=101, WeaponHandling=106). DOCUMENTED CUTS: Quick Pockets
(−2 inventory-access AP) is inert — we have no in-combat inventory-access AP model; the flat +5 perks
(Living Anatomy/Pyromaniac) + Bonus Ranged Damage are wired on the SINGLE-attack path only (burst/throw
flat-bonus is a residual — they rarely apply on the shippable slice); the remaining ~80 perks stay
data-present (the table is complete, the stat perks + this curated set are wired). 5 fake-host
CombatEngineTests (each perk's effect + its rank-0 control); all 15 combat + 42 encounter goldens
BYTE-IDENTICAL (every perk short-circuits at rank 0).
M5 PERKWIN.FRM art picker (DONE): DrawPerkPicker now renders the authentic PERKWIN.FRM (573x230,
centred) — the eligible-perk list on the left (window-local 45,43 192x129; the hovered row lights), the
hovered perk's name (280,27) + wrapped DESCRIPTION (perk.msg 1101+i, the new PerkDescription loader;
280,70) on the parchment card — falling back to the pre-art text flyout when the FRM is absent (the
Skilldex pattern). PerkWindowOrigin/PerkPickerRowAt are the shared render+hit-test geometry (the
SkilldexRowAt pattern); a left-click on a row takes that perk (additive to the 1-9 keys). Screenshot-
verified over the real art. Draw-only + additive mouse → the perk-pick golden (driven by the PerkPick
harness ACTION, not Draw) is unchanged; all goldens BYTE-IDENTICAL.
M6 companion perks infrastructure (DONE — inert on the slice, by design): SaveState.PartyMemberState
gained int[]? PerkRanks (additive within V2 — null on old saves AND on the shippable slice, so no
version bump); a _companionPerkRanks dict on the viewer; GetCritterState's companion branch now passes
the member's ranks as CritterState's 5th arg (the SAME path the dude uses), so any future companion
perk applies for free. Save/restore wired. FLAGGED: no shippable companion gains perks (party.txt
level-ups advance proto STAGES, not perks — like the #13 party-level-up logic), so this is forward-
looking infrastructure with NO UI; it lights up only when future content levels a companion's perks.
2 tests (a companion CritterState with Toughness rank 2 → DR +20; a PerkRanks save round-trip + the
null-on-legacy default); all goldens BYTE-IDENTICAL (the slice never sets a rank → null → inert). P29
COMPLETE: the six P28 spillover items are done — combat-path traits, the full skill-point formula, the
creation trait picker, a curated perk-effects batch, the authentic PERKWIN art, and companion-perk
infrastructure; the trait/perk character layer is now whole. 392 Formats tests, 42 encounter + 15
combat goldens green.

Phase 30 (IN PROGRESS — "Walk Softly", stealth/sneak; researched via the stealth-karma-research
workflow). USER DECISION: full faithful detection, LIVE (a periodic Sneak roll + Perception/distance
isWithinPerception gate so active sneaking slips you past script aggro). M0 two-layer sneak state
(DONE): pure Formats.Combat.SneakState mirrors critter.cc — the FLAG (dudeHasState, the Skilldex/S
toggle) + Working (_sneak_working, set by the A-M2 periodic roll); IsSneaking = FlagSet && Working
(dudeIsSneaking critter.cc:1236); RescheduleTicks ports sneakEventProcess's ladder verbatim (success→
600; failure retries sooner the higher the skill: >250→100 … >80→400, else 600). Replaced the viewer
_sneaking bool with the flag layer (the Skilldex case-8 toggle + the --use-skill diagnostic now read
_sneak.FlagSet — the printed bool is byte-identical, so the skilldex-skills golden is unchanged).
WIRED using_skill(dude, SKILL_SNEAK=8) to return the FLAG (interpreter_extra.cc:589 opUsingSkill —
reads the flag, NOT Working): new IVmExternals.IsUsingSkill + ScriptHost.SneakFlagProvider + the 0x80AB
dispatch (was the arity stub → 0; no slice script branches on it, so inert). Harness --sneak-probe
<flag> prints sneak-probe: flag=/working=/sneaking=/skill=. 12 SneakStateTests (the truth table + the
7-bucket ladder); all goldens BYTE-IDENTICAL.
M1 Silent Death backstab + facing (DONE): pure Formats.Combat.SneakAttack.IsHitFromFront ports
actions.cc:1512 _is_hit_from_front verbatim (diff=abs(attRot-defRot); front = diff ∉ {0,1,5} → a
behind/side hit is the backstab). Wired the Silent Death multiplier into RollAttack's melee block
(combat.cc:3870-3875 on-hit / 3913-3921 on-crit): a melee/unarmed DUDE hit, with DudePerkRank(SilentDeath)
>0 + the sneak FLAG (new ICombatHost.DudeSneakFlag, NOT active Working — the engine checks the flag) +
from behind + the target not yet engaged (defender.WhoHitMeCid != -1, our proxy for whoHitMe != gDude
since Hexwaste doesn't track live whoHitMe — combat sets it -1 at engage, so the bonus fires once on the
surprise strike) → critMultiplier = 4 on a plain hit, ×2 on a crit. PerkId.SilentDeath=25. Every clause
short-circuits at rank 0 / no-flag, drawing NO extra RNG, so a perk-less or non-sneaking dude is inert —
Narg (combat.gcd) has no Silent Death and never sneaks, so the 6 --character-combat goldens are unchanged.
3 fake-host tests (4x backstab / the behind+sneaking+fresh gate / the x2 crit stack) + --backstab-probe
<attRot> <defRot>. All 15 combat + 42 encounter goldens BYTE-IDENTICAL.
M2 periodic sneak roll + persistence (DONE): a SKILL_SNEAK roll (d100 ≤ the dude's Sneak skill, the
randomRoll success test, skill.cc:479) now sets Working — on flag-enable (dudeEnableState →
sneakEventProcess, immediate) and on the 100 ms critter heartbeat (one reschedule "tick" = one heartbeat,
a documented approximation of the engine's game-time EVENT_TYPE_SNEAK queue). The roll uses a DEDICATED
seeded _sneakRng (the _skillRng/_partyRng/_wmRng isolation pattern) so enabling sneak draws ZERO from the
combat/worldmap/party/script streams — every existing golden stays byte-identical (verified: a record
pass changed ONLY the new fixture). The flag toggle now rolls on enable / clears Working on disable; the
Skilldex golden still prints only the FLAG so it's unchanged. Persisted: SaveState.SneakFlag/SneakWorking
(additive-V2 nullable, null→false on old saves; sparse — null when not sneaking). Harness --sneak-roll
<seed> (deterministic). New golden sneak-state (the roll + flag/working state + the A-M1 facing probes;
default dude Sneak 20 → working=0/next=600). SaveStateRoundTrip + 12 SneakState tests; all goldens green.
M3 NPC detection gate — LIVE (DONE; the behavioral milestone, the user's "full faithful, live" choice):
pure Formats.Combat.PerceptionDetect ports isWithinPerception (combat_ai.cc:3499) — the two-tier range
(with-LoS PE×5 / halved through glass; without-LoS PE×2 in combat else PE) + CanSee (actions.cc:1523
frontal-arc {0,1,5}) + the dude-sneak reduction (actively sneaking ÷4, −1 if Sneak>120; flag-but-not-
working ×2/3). Wired into the scripted-aggro path (ViewerGame AttackRequested → BeginScriptAggro) via
DudePerceivedBy: a scripted attacker that can't perceive the dude does NOT engage. KEY de-risk: the gate
is GATED ON THE SNEAK FLAG (`target==dude && _sneak.FlagSet && !DudePerceivedBy`), so a non-sneaking dude
short-circuits PAST it → every existing golden is BYTE-IDENTICAL (verified by a clean check BEFORE
recording the new fixture, the P13-M1 pattern); only an actively-sneaking dude out of the NPC's reduced
range goes unnoticed. Zero RNG (pure distance/facing). DOCUMENTED CUTS: NPC-vs-NPC sneak is OUT (only the
dude→NPC direction is gated — the engine's target==gDude branch); no lighting/PERK_GHOST term; the forced-
walk-while-sneaking animation is N/A (WalkTo only). Harness --detect-probe <pe> <dist> <canSee> <flag>
<working>; new golden sneak-detect (not-sneaking→detected, sneaking-far→UNDETECTED, sneaking-close→
detected). 6 PerceptionDetectTests; all combat + encounter goldens BYTE-IDENTICAL. P30 (stealth/sneak)
COMPLETE: the two-layer state, Silent Death backstab, the periodic roll + persistence, and live
detection are in. 420 Formats tests.

Phase 31 (IN PROGRESS — "Reputation Precedes You", karma/reputation; researched in the same stealth-
karma-research workflow). USER DECISION: the faithful PC-STAT model (engine stores karma=gPcStatValues[4],
reputation=[3], NOT GVARs; the reputation GVARs are the display layer). KEY engine truth: karma is
READ-ONLY — no engine code auto-awards it on kills/quests, and there is NO karma-gated dialog opcode
(giq is IQ only) — so karma/town/generic-rep are 100% script-driven (set_global_var) or harness-set; the
whole feature is display + script-read, never a combat/dialog behaviour change. M0 get_pc_stat seam
(DONE): pure Formats.Int.PcStat (the stat_defs.h PcStat index map: 0 unspent / 1 level / 2 xp / 3 rep /
4 karma); the dude's _dudeKarma/_dudeReputation fields (default 0) + PcStatProvider now routes 3→rep,
4→karma, 0→unspent (were stubbed to 0; 1/2 were already wired). Karma/rep default 0 → get_pc_stat(3/4)
returns 0 EXACTLY like the stub, so inert; the new 0→unspent route is byte-identical too (verified — the
vic-levelup golden grants XP/unspent but no slice script reads get_pc_stat(0)). Harness --karma-probe
reads through the provider (proves the 0x80A6 seam). PcStatTests locks the index map; all 15 combat + 44
encounter goldens BYTE-IDENTICAL.
M1 generic reputation titles (DONE): pure Formats.Map.GenericReputation.Parse + TitleFor port
character_editor.cc genericReputationInit (7077) + the lookup (5509): data\genrep.txt "threshold msgId"
rows (ws/comma-delimited, '#' comments), sorted DESCENDING by threshold; the title is the highest-
threshold row the value meets (−1 below all). The value is the dude's _dudeReputation PC-stat (the engine
reads GVAR_PLAYER_REPUTATION — a documented unification to one source of truth). Lazy genrep.txt loader
(only on the probe / the M3 char-sheet). Harness --rep-title <value> prints the MESSAGE ID only (never
the copyrighted title string). 4 GenericReputationTests incl. a GameDataFact that parses the real
genrep.txt (descending + valid msg ids). Pure parser + lazy probe → no golden-exercised path changed,
all goldens BYTE-IDENTICAL.
M2 town reputation + karma-title GVARs (DONE): pure Formats.Map.TownReputation.LevelFor ports the 7-band
thresholds (character_editor.cc:5574 — <-30 Vilified … ==0 Neutral … >=30 Idolized; note the asymmetry at
0) + MessageId; Formats.Map.KarmaTitles.Parse ports karmaInit (karmavar.txt: gvar/art/name/desc rows) +
Active (rows whose GVAR is non-zero — character_editor.cc:5537, excluding the gvar-0 generic-rep row).
KEY FINDING: karmavar.txt binds the REAL vault13.gam GVAR ids (0,3,2,1,11,…), NOT the game_vars.h enum
order — and KarmaTitles reads the gvar FROM the file, so it's robust (verified: --set-global 3 1 →
karma-titles active=1 id=1001; the town/karma GVARs the VM already maintains, so reading them adds no
writes/RNG). Both pure + inert (no slice script sets a town/karma GVAR → all 0/Neutral/none). Harness
--town-rep <value> + --karma-titles (message IDs only). 15 TownReputationTests incl. a GameDataFact real
karmavar.txt parse; all goldens BYTE-IDENTICAL.
M3 karma display + save persistence (DONE — P31 COMPLETE): the character sheet (C/K) + the Pip-Boy STATUS
page now show "Karma: N" (PC_STAT_KARMA), "Reputation: <GVAR_PLAYER_REPUTATION> (<genrep title>)", any
earned karma titles, and non-Neutral slice-town standings (shared KarmaDisplayLines). Title STRINGS come
from text\english\game\editor.msg (gCharacterEditorMessageList; lazy EditorMsg loader). MODEL CLARIFIED
(supersedes the B-M1 "unify" note): get_pc_stat(3)=PC_STAT_REPUTATION (_dudeReputation, −20..20) and
get_pc_stat(4)=PC_STAT_KARMA (_dudeKarma, ≥0) are the PC-stats, but the DISPLAYED reputation + genrep
title read GVAR_PLAYER_REPUTATION = GlobalVars[0] (the faithful source, VM-maintained, --set-global 0
-able) — distinct ranges, so kept separate. Persisted: SaveState.DudeKarma/DudeReputation (additive-V2,
sparse null at 0); the generic/town/karma GVARs ride the already-saved GlobalVars dict. Harness
--set-karma <karma> <rep> (pcSetStat clamps: karma≥0, rep −20..20). VERIFIED end-to-end: --set-karma
50 5 + --set-global 0 100/47 30/3 1 → karma-probe 50/5, rep-title(100)=msg 3004, Arroyo=Idolized, 1
earned title. Display Draw-only + harness opt-in → existing goldens BYTE-IDENTICAL (only the new karma
fixture); KarmaAndReputationRoundTrip test. P31 karma/reputation COMPLETE: get_pc_stat seam, generic-rep
titles, town/karma-title GVARs, the char-sheet/Pip-Boy display + save. The engine never auto-awards
karma (no kill/quest hook, no karma-gated dialog), so it's faithful read-only display driven by scripts/
--set-global — that's the whole feature, not a behaviour change. 441 Formats tests.

Phase 32 (IN PROGRESS — "Broaden Compatibility", from the audit). The compatibility audit (a workflow +
a 155-map dynamic sweep) found: all 155 original maps LOAD cleanly (the DAT/MAP/FRM/proto/render pipeline
is general); the gap is scripted BEHAVIOUR — 13/28 procs wired, 93/181 externals wired, and vault13.gam
GVARs UNSEEDED. Highest-leverage incremental wins: GVAR seeding + map-load robustness. M0 proto-read
guard (DONE): the audit flagged MapFile's two uncaught protos.Get(pid).SubType reads (Item/Scenery
trailer switch) as a latent SIGABRT — a missing/corrupt .pro threw out of LoadMap → MonoGame Update →
hard abort. Wrapped them in MapFile.SubTypeOf (→ -1 on a bad proto → the trailer switch reads nothing,
best-effort) + a top-level LoadMap try/catch (FileNotFound/InvalidData/NotSupported/EndOfStream) that
soft-fails: a transition keeps the prior map (teardown runs after the parse), and a failed INITIAL load
falls back to the title menu (Draw now guards the world draw on _map != null — the menu/overlays are
null-map-safe). VERIFIED: a missing map (the audit's mis-probed klamath.map) that SIGABRT'd now exits
cleanly to the menu; all 155 real maps still load identically (happy path: protos.Get succeeds → real
SubType → unchanged). Inert — all 15 combat + 44 encounter goldens BYTE-IDENTICAL. NOTE: no shippable
map actually trips this (the DAT carries every proto); it's latent hardening, not an active-crash fix
(the audit's "Klamath crash" was a measurement artifact — a typo'd filename, not a real incompatibility).
M1 vault13.gam GVAR seeding (DONE): pure Formats.Int.GameGlobalVars.Parse ports game.cc globalVarsRead
(the GAME_GLOBAL_VARS: section, positional index = the i-th non-blank/non-// line, value = sscanf %d
after '='). SeedGlobalVars writes the non-zero seeds into ScriptHost.GlobalVars at StartNewGame (after
the Clear, before the first map_enter), sparse — only the ~12 non-zero values (an unset key reads 0, so
the 684 zero-seeds are implicit) and SILENT (no stdout, so no golden-line risk). KEY FINDING (shrinks
the audit's "unseeded GVARs" gap): the base vault13.gam seeds 684/696 globals to 0; only 12 are non-zero,
and just TWO touch the slice — GVAR_TOWN_REP_ARROYO[47]:=50 (Arroyo starts Idolized → feeds the P31
karma display) and GVAR_FIND_VIC[619]:=1. GOLDEN SAFETY: seeding fires on StartNewGame; the harness
--map debug path goes LoadContent→LoadMap directly (no StartNewGame → no seed), so the vic/dialog goldens
(--map) are untouched; the --create-based goldens DO seed (StartNewGame) but no slice script reads a
seeded var in a golden-visible way → ALL 15 combat + 44 encounter goldens BYTE-IDENTICAL. Harness
--get-global <id>; new golden gvar-seed (--create → 47=50, 619=1, 134=100, 0=0). 2 GameGlobalVars tests
incl. a GameDataFact that asserts the real vault13.gam positional indices (47=50, 619=1, ~12 non-zero).
DOCUMENTED: the bare --map load stays unseeded (a synthetic debug jump); real play (menu → New Game)
seeds. P32 COMPLETE: map-load robustness + GVAR seeding — the two highest-leverage compatibility wins
from the audit.

Phase 33 (IN PROGRESS — "Scripted Map Externals", wiring the high-leverage stubs the slice fires, from
the audit backlog). KEY GROUNDING (a per-map stub census): the slice fires its high-leverage stubs in
INERT ways — artemple/arcaves' reg_anim_func wraps scenery animation, denbus2's critter_attempt_placement
is a SAME-TILE placement (pid 0x0100003A 14716→14716, a no-op; NOT Vic@17070/Metzger@15278) — so wiring
them is forward-looking infra (correct + byte-identical goldens, fake-host/probe-verified), not a slice-
visible change. M0 critter_attempt_placement (DONE): wired 0x80FF (interpreter_extra.cc:2812) — IntVm
dispatch (pops elevation, tile, critter) + IVmExternals.CritterAttemptPlacement + ScriptHost.
PlaceObjectRequested callback → the viewer's PlaceObject relocates the critter (pure Formats.Map.Placement.
FreeTileNear = the tile or a free neighbour, _obj_attempt_placement simplified to radius-1; then pull off
every elevation draw list, RebuildBlockedTiles, InsertSorted into the target elevation, rebuild blocking).
denbus2's same-tile call runs the path as a no-op → all 15 combat + 44 encounter goldens BYTE-IDENTICAL;
--place-probe <from> <to> proves the REAL relocate (denbus2 critter 14716→14000); new golden critter-place
+ 3 Placement tests. DOCUMENTED: the free-tile search is radius-1 (engine spiral-searches wider) + uses
the current elevation's blocking (approximate off-screen).

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
projectile screen-tween. (SUPERSEDED — nearly all of this list later shipped:
wait/dismiss persistence is in the save [#2/#3, SaveState Dismissed/Waiting/
OriginalTeam]; per-member If()/Distance are honored [P10 + the P16-M4 lowercase-
if fix]; X-FIGHTING-Y is wired [P16-M3]; the projectile tween landed [#11]. The
lone residual is Vic's radio ITEM having no in-slice source [one --give].)

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
