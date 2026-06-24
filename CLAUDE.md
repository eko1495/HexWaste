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
RNG draws and stay byte-identical; the called-shot UI was a V-cycle, not
the engine's click dialog — SUPERSEDED by P49-M1: V now opens a click dialog), M3 knockback +
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
companion trade priced-barter, the embedded Pip-Boy mini-automap (automap.db RLE),
the worldmap-screen tab wiring. (Inventory drag-and-drop equip shipped in P47.)

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
Toughness->DR, Action Boy->AP, More Criticals->crit, ...) folded into CritterState.Stat
alongside traits (so M3's stat perks are FREE — only combat/skill-path perks need wiring). DudePerkRanks
(int[119]) on the viewer, passed as CritterState's 5th param, persisted (SaveState.DudePerkRanks,
additive-V2 sparse — null when no perk taken); has_trait(type 0) now returns the dude's perkGetRank via
ScriptHost.PerkRankProvider. INERT-BY-DEFAULT holds (zero ranks -> 0 modifier -> all combat + encounter
goldens BYTE-IDENTICAL). VERIFIED: traits + perks STACK (Narg's HeavyHanded +4 melee, then Bonus HtH
Damage perk +2/rank -> 8->10->12); --perk-probe <index> <level> exercises the gates + effect (golden
perk-gates: level-gate at lvl2, eligible+stack at lvl3, stat-gate on More Criticals via Narg's LK4).
16 PerkTests. [P75-M3 DOC-TRUTH CORRECTION: Lifegiver was listed here + above as a folded stat perk, but
its PerkTable Stat=-1 (the [0,0,4,...] is the EN>=4 REQUIREMENT, not an effect) — it was INERT until P75-M3
wired its +4-HP/level at the AwardXp level-up site, NOT a CritterState.Stat fold.] M3 high-impact (combat/
skill-path) perk effects (DONE): the data-driven STAT perks
(Toughness/Action Boy/More+Better Crits/Faster Healing/Bonus HtH Damage/Strong Back/Dodger/
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
M1 reg_anim movement (DONE — the audit's #1 high-leverage stub): wired the reg_anim_func begin/end/clear
queue (0x810E) + the 6 register ops the P21 note deferred — reg_anim_animate/_reverse (0x810F/0x8110),
reg_anim_obj_move_to_obj/_run_to_obj (0x8111/0x8112), reg_anim_obj_move_to_tile/_run_to_tile (0x8113/
0x8114). IntVm dispatch pops match the engine handlers verbatim (reg_anim_func: param then cmd, BEGIN=1/
CLEAR=2/END=3; the move/animate ops: delay, target, obj). IVmExternals gained RegAnimBegin/End/Clear +
RegAnimMoveToTile/MoveToObject/Animate; ScriptContext accumulates a resolved RegAnimAction list between
begin and end (handles → MapObject at registration via ObjectOf) and flushes it to the host on END via
ScriptHost.RegAnimRequested (RegAnimClearRequested for CLEAR → the existing ClearAnimation). The viewer's
ExecuteRegAnim plays the batch: MoveToTile/RunToTile → StartNpcWalk; MoveToObject/RunToObject → StartNpcWalk
to Placement.FreeTileNear(dest) (the P33-M0 reuse); Animate(Reverse) → the animator (critter anim-coded
FID / scenery loop, like reg_anim_animate_forever). DOCUMENTED SIMPLIFICATIONS: the engine plays a batch
SEQUENTIALLY over time — we execute in PARALLEL on END and ignore the per-action delay; run==walk (no
separate run speed/anim); Animate LOOPS rather than playing once (no one-shot primitive); animate_forever
(0x8126) stays the P21 immediate path, NOT queued. INERT ON THE SLICE: no shippable map fires the move/
animate ops at map_enter (only reg_anim_animate_forever for scenery, P21), and reg_anim_func BEGIN/END
wrap an empty batch there (animate_forever fires immediately, outside the batch), so ExecuteRegAnim is
never invoked — all 15 combat + 47 encounter goldens (incl. script-light's --reg-anim-probe) BYTE-IDENTICAL
(reg-anim-move is the 48th, the new fixture).
The engine gates every op on !isInCombat() (interpreter_extra.cc:3460); ExecuteRegAnim mirrors that (skips
the batch mid-combat). Harness --reg-anim-move <fromHex> <toHex> synthesizes a begin→move-to-tile→end batch
on a real map critter (no slice script does); new golden reg-anim-move (denbus2 merchant 14716→14718 walks).
SPILLOVER closed: "the reg_anim movement ops + begin/end sequencing" from the P21 spillover line is now DONE.

Phase 34 (IN PROGRESS — "Make It React", from the fo2ce-portability audit: Tier-1 breadth/feedback +
Tier-2 sfx/reaction polish). The hard engineering is done; this phase wires the stubbed externals the
slice's critter_p_proc fires every tick, the perception/presentation layers drawn on top of correct
combat logic, and the sfx/reaction feedback. M0 design specs via a 6-agent grounding workflow. M1
combat-introspection externals (DONE): wired two stubbed VM externals the slice's heartbeats fire —
is_in_combat (0x8128, opCombatIsInitialized → a new ScriptHost.CombatActiveProvider Func<bool>? backed
by CombatEngine.Phase != Idle, mirrors SneakFlagProvider) + critter_state (0x80FB, opGetCritterState →
the CRITTER_STATE bitfield: DEAD(1) for null/non-critter/dead, else NORMAL(0)|PRONE(2 if FID anim 48-49)
|DAM_CRIP bits for an active critter, or PRONE(2) for an inactive-but-alive one). The pure mapping is
ScriptHost.CritterStateOf(MapObject) — the single source of truth both the VM (ScriptContext.CritterState)
and the --critter-state-probe call; DAM_CRIP == CriticalTables.DamHealable (0x7C); the inactive-death test
uses MapObject.IsDead (DAM_DEAD bit; the HP<=0-without-DAM_DEAD case is unreachable for a polled live
critter — documented). The stack shape is UNCHANGED from the prior arity stubs (0x8128 = 0-pop/1-push,
0x80FB = 1-pop/1-push), so the VM never desyncs; the only change is the returned VALUE, and no slice
script branches on it in a golden-visible way (the OnStubbedExternal census is stderr-only) — all 15 combat
+ 48 encounter goldens BYTE-IDENTICAL (verified by a clean check BEFORE recording, incl. the combat
fixtures where is_in_combat now returns 1 mid-fight). Harness --critter-state-probe <hex> (hex<0 = is_in_
combat only); golden critter-state (denbus2 merchant 14716 un-engaged → inCombat=0/state=0 NORMAL). 8
CritterStateExternalTests lock the bitfield truth table.
M2 hurt_too_much flee gate (DONE): the engine flees on a SECOND condition besides min_hp — when
(CombatResults & ai.hurt_too_much) != 0 (combat_ai.cc:3076). AiPacket gained HurtTooMuch (a DAM_* mask
parsed from ai.txt's hurt_too_much column via AiPacketTable.ParseHurt = the _parse_hurt_str port:
"blind"→DamBlind, "crippled"→DamCripLimbs[0x3C, legs+arms NOT blind], "crippled_legs"→DamCripLegAny,
"crippled_arms"→DamCripArmAny; comma-split, lowercased, trimmed). CombatEngine.TryEnemyAction gained the
clause next to the min_hp gate: if (ai is { HurtTooMuch: not 0 } && (enemy.CombatResults & ai.HurtTooMuch)
!= 0) return TryFlee(...). INERT by default (HurtTooMuch defaults 0 + the AND-gate short-circuits unless a
crip/blind bit is actually set), and no slice golden enemy carries such a bit on a turn it takes (the dude
only blinds via a MASSIVE eye/uncalled crit, which the fixtures never land on a scorpion mid-fight) — all 15
combat + 48 encounter goldens BYTE-IDENTICAL (verified by a clean check). Real ai.txt values confirmed:
packet 8 (Animals/scorpion) "blind"=0x40, packet 14 (Peasants) "crippled, blind"=0x7C, packet 33 (Den slave
coward) "blind"=0x40. Harness --hurt-too-much-probe <hex> <flags> (ORs the flag bits, reports wouldFlee);
golden hurt-too-much-flee (arcaves scorpion 20529 + blind → wouldFlee=1). Tests: AiPacketTests.ParsesHurt
TooMuch* + the GameDataFact real-mask asserts; CombatEngineTests Blind-enemy-flees + the no-matching-bit-
still-attacks control (the AND-gate / byte-identical invariant). DOCUMENTED CUT: the engine's third OR-clause
(CRITTER_MANUEVER_FLEEING) stays unported — Hexwaste has no maneuver model (pre-existing simplification).
M3 run animation (DONE): the dude now RUNS by default (the engine's running pref) instead of always
walking. Pure Formats.Combat.RunGuard.MovementAnimCode ports the 3 guards of animation.cc animationRegister
RunToTile() — walk if a crippled leg (DAM_CRIP_LEG_ANY), or sneaking without Silent Running (PERK 15), or
the run art (ANIM_RUNNING=19) is missing; else run. DudeController gained an optional movementAnimCode
selector (CurrentFid uses it; NPC walkers pass nothing → keep walking ANIM_WALK=1), the dude wires
ViewerGame.DudeMovementAnimCode (computes runArtExists from the dude's ACTUAL weapon-code FID — a documented
divergence from the engine's weaponCode-0 check, so the existence test matches the FRM that loads). The
per-rotation offsets + FRM-driven speed are anim-code-INDEPENDENT (already correct), so only the FID anim-
code changes; CurrentFid is Draw/anim-only (never in a transcript) + draws no RNG → all 15 combat + 48
encounter goldens BYTE-IDENTICAL (incl. the combat-walk fixtures, whose printed tile/AP state is path+AP-
derived, not fps-derived; the crippled variant forces walk anyway). DECISIONS: run applies in combat too
(faithful, AP-cost unchanged by anim-code); the _dude_run sneak-disable side-effect (animation.cc:3007) is
DEFERRED (golden-risk to the P30 sneak suite, not needed for the visual). Harness --run-probe (reports the
code under each guard); golden run-probe (combat char on arcaves: default=19/crippled=1/sneaking=1/silentRun=
19/artExists=1). 5 RunGuardTests lock the guard order + the 19/1 codes.
M4 typed combat outlines (DONE): during combat every visible LIVING critter is outlined by team
(red hostile / green friendly / dim perception-only), LoS-gated — ported from combat.cc _combat_update_
critter_outline_for_los + object.cc _obj_outline_object. Pure Formats.Combat.CombatOutline.TypeFor: clear
LoF to the dude → same-team FRIENDLY else HOSTILE; LoF blocked → PERCEPTION if within the dude's PE×5 reach
(÷2 through glass) else none. The 5-band gradient collapses to the engine's base palette index (243 red /
229 green / 61 dim). The viewer's CombatOutlineType reuses the faithful P13-M1 LineOfFire.Trace (via the
host's ShootBlockerAt) + GetCritterState(dude).Stat(1) for PE; CombatOutlineColor resolves the index to RGB.
Wired into DrawObjects (when _combat.Phase != Idle): a per-critter outline pass; the green hover outline is
now suppressed during combat (one outline per critter). Draw-ONLY + headless-inert (DrawObjects never runs
in the harness) + zero RNG → all 15 combat + 48 encounter goldens BYTE-IDENTICAL. Harness --outline-probe
<fightHex> (zero-RNG: positions the dude adjacent, classifies each living critter — no combat entry, since
the classification is phase-independent); golden outline-typed (arcaves: 2 clear-LoS scorpions hostile, the
rest blocked/far → none). 6 CombatOutlineTests lock the team/LoS/perception/glass branches + the palette
indices. DOCUMENTED: flat red/green for the engine's 5-band gradient; fog-of-war not modeled (all-visible).
M5 combat sfx (DONE — Tier-2 sfx polish): faithful sfxBuild* name composers + combat-event + ambient
wiring. SfxName gained CharName (sfxBuildCharName port: FRM base + the _art_get_code (weapon,anim) pair +
the death/knockout/contact override on the WEAPON char — FALL+Die→'Z', punch/kick+Contact→'Z'; null when
the base is unresolvable) + WeaponName (sfxBuildWeaponName: W{R|A|O|F|H}{soundCode}{variant}{material}XX1;
variant 1 for ready/oota/primary else 2; the old WeaponAttack/WeaponHit are now byte-identical shims).
Wired: OnTargetHit got-hit grunt (anim 14), StartDeathFall death scream (NPCs use faithful CharName —
scorpions→MASCP2*/MASCRP* which SHIP, humans→HMWARR* which DON'T = engine-faithful silence; the DUDE keeps
the P8 HumanDeath HM/HFXXXX fallback so player death audio isn't regressed), OnAttackStarted unarmed-swing
grunt, TryReload weapon-ready, and out-of-ammo via a new default ICombatHost.OnWeaponOutOfAmmo hook (no-op
for the fake host). Ambient: MapList parses ambient_sfx= (a malformed "animal:15 animal:10" token drops
gracefully via first-':' split) → GetAmbientSfx; pure Formats.Map.AmbientSfx.RollIndex (wmSfxRollNextIdx
weighted pick) + RemapBirdForNight (brdchir1/brdchirp→cricket/cricket1 at hhmm hour ≤600/≥1800); a wall-time
TickAmbientSfx in Update (combat-gated, a DEDICATED seeded _ambientRng off the other streams, ~17 s cadence).
ALL sfx is _audio?.PlaySfx → --no-audio makes it headless-inert; the ambient timer is wall-time (the harness
never pumps it) → all 15 combat + 48 encounter goldens BYTE-IDENTICAL. Harness --sfx-probe <hex> (composed
NAMES only — asset identifiers, like object names); goldens sfx-probe-scorpion (MASCP2* + water ambient) +
sfx-probe-human (NFPRIM* + dogbark). DECISIONS: faithful CharName everywhere (humans silent = the original
game) except the dude death keeps HumanDeath; weapon-HIT material defers to 'F' flesh (combat never shoots
scenery/walls — the proto-material parse is deferred). Tests: SfxNameTests (CharName/WeaponName) + AmbientSfx
Tests (roll/bird-remap) + the GameDataFact (MASCRPAO ships, HMWARR* doesn't, denbus2 ambient parses).
M6 reaction animations (DONE — P34 COMPLETE): the defender now visibly REACTS on an attack resolve.
Pure Formats.Combat.ReactionAnims ports actions.cc _show_damage_to_object + animation.cc _dude_standup:
HitReaction (front→ANIM_HIT_FROM_FRONT 14; from behind→HIT_FROM_BACK 15 only if the critter ships that
art, else front), KnockdownFall (front→FALL_BACK 20 else FALL_FRONT 21), StandUp (fell-back→BACK_TO_STANDING
37 else PRONE_TO_STANDING 36), Dodge (ANIM_DODGE_ANIM 13). ICombatHost.OnTargetHit WIDENED to (target,
attacker, knockedDown) — the host picks front/back via SneakAttack.IsHitFromFront(attacker.Rotation, target.
Rotation) + plays a FALL when knockedDown; new default no-op hooks OnTargetDodge (a miss on a non-prone/non-KO
defender, ResolveAttack miss branch) + OnGetUp (StandUpIfProne, after the getup transcript). The viewer
plays the chosen FRM via the animator (PlayActionOnce / PlayFall), guarded by art existence + an already-
mid-fall check (don't override a held FALL with a hit-react, actions.cc:438). NO RNG + NO transcript +
Draw/anim-only → all 15 combat + 48 encounter goldens BYTE-IDENTICAL (the widened call sites only pass extra
args; the fake host's hooks are no-ops/recorders). DOCUMENTED: the DUDE is excluded from reactions (the
engine reacts him too — Hexwaste's camera-anchor dude historically doesn't; a spillover); the _pick_fall
blocked-tile flip is out of scope. Harness --reaction-probe <hex> <attRot>; goldens reaction-anims-human
(denbus2 critter ships back art → 14/fall-20 from front, 15/fall-21 from behind) + reaction-anims-scorpion
(lacks back art → stays 14 even from behind, the fallback). 5 ReactionAnimsTests lock the codes + branches.
P34 COMPLETE — Tier 1 (is_in_combat/critter_state, hurt_too_much flee, run animation, typed combat outlines)
+ Tier-2 sfx/reaction polish (combat sfx + ambient, reaction anims) all shipped; every milestone byte-
identical bar its own new probe fixture. 8 new encounter goldens (56 encounter + 15 combat total); 482
Formats tests green.

Phase 35 (IN PROGRESS — "The Script Takes Its Turn", combat_p_proc; from the audit's hardest backlog
item). M0 grounding via a 4-agent workflow (engine semantics + a real slice .int census + Hexwaste
integration + synthesis). KEY FINDING: combat_p_proc (SCRIPT_PROC_COMBAT=13) is LIVE on the slice but
liveness splits by HOOK — the engine fires FIVE differently (per-turn fp=4, on-hit fp=2, want-to-join
fp=5, the end-of-combat map hook, the dead round-robin). This milestone wires ONLY the PER-TURN hook
(fp=4, combat.cc:3243-3258 _combat_turn): for each scripted (sid!=-1) combatant, at the TOP of its turn
(INSIDE the !incapacitated branch, AFTER SkipTurnIfIncapacitated, BEFORE standup/AI), run combat_p_proc
with scriptSetObjects(sid,NULL,NULL)+fixedParam=4; if the script called script_overrides() the engine
skips the whole standup+AI block (combat.cc:3259) — we mirror it by forfeiting the rest of the turn.
New ICombatHost.RunCombatProc default-([],false) seam; CombatEngine.RunCombatProcOverridesTurn wired into
both TryEnemyAction + TryAllyAction (the engine runs it for EVERY combatant, no party exclusion); the
viewer delegates to ScriptHost.RunObjectProc with source=null (matching scriptSetObjects NULL — GOTCHA:
RunProc couples source==dude_obj, so null also yields dude_obj=0 in combat_p_proc, a documented divergence
from the engine's persistent gDude, INERT on the slice). BYTE-IDENTICAL on all 15 combat + 56 encounter
goldens (verified by a clean combat-golden check BEFORE recording): the only --fight critter that DEFINES
combat_p_proc is the arcaves scorpion (ZClScorp, script 19), but its body gates on fixed_param==2 (the
on-hit poison hook), so the fp=4 per-turn call runs the proc, the fp==2 guard is false, the body short-
circuits → no RNG, no message, Overridden=false → unchanged. DOCUMENTED CUTS: the dude's own per-turn proc
(the engine runs it for gDude too, but Hexwaste's dude turn is player-driven, not a TryXAction — inert, no
slice dude gcd defines it); and the OTHER FOUR hooks stay unported — esp. the fp=2 ON-HIT poison hook (the
genuinely behaviour-LIVE one: scorpion poison + a do_check stat-roll on every sting, golden-RISK, needs a
target param + poison/0x8122 dispatch) is the explicit next milestone. The fp=4 scripts that WOULD act —
ACTemVil (script 748, temple challenger: terminate_combat at ≤half HP) + dcG2Grd (script 36, Den guards) —
aren't in any --fight golden, so byte-identical holds; their override externals (terminate_combat/critter_
add_trait) are still arity-stubbed and would need dispatch when those critters enter a fixture. Harness
--combat-proc <hex> (hasProc/overridden/script-index, state-only); goldens combat-proc-scorpion (script=19
hasProc=True overridden=False — defines-but-inert) + combat-proc-slave (script=906 hasProc=False). 4 fake-
host CombatEngineTests (runs-then-default-AI / override-forfeits-turn / unscripted-skips / KO-skips).
M2 fp=2 ON-HIT hook (DONE — the scorpion's poison sting): after a landed hit (combat.cc:4729-4733
defenderDamage>=0 && DAM_HIT) the ATTACKER's combat_p_proc runs with source=NULL, target=the struck
defender, fixedParam=2. Wired: ScriptContext gained a Target field (target_obj override; null→self) +
ScriptHost.RunCombatProc(self, target, dude, map, fp) — a DECOUPLED runner (source always NULL, target +
dude separate) since RunObjectProc couples source==dude; this also fixes the P35-M1 dude_obj=0 divergence
(combat_p_proc now sees the real dude_obj). poison(0x8122, opPoison→critterAdjustPoison) dispatched: IVm
Externals.Poison + ScriptHost.PoisonRequested → the viewer's ApplyPoison (DUDE-ONLY + poison-resistance
reduced, sets the Poison counter). ICombatHost.RunCombatProc gained an optional target; CombatEngine.Run
OnHitCombatProc fires it from ResolveAttack's hit branch + the burst main/extra hits (the dude attacker is
a no-op — Sid -1 / no gcd proc). The scorpion (ZClScorp) combat_p_proc calls do_check (seeded _scriptHost.
Rng) + random (IntVm fixed seed) + poison(target) — all DETERMINISTIC under --rng-seed. SURPRISE (better
than the spec predicted): BYTE-IDENTICAL on all 15 combat + 56 encounter goldens — the scorpion DOES sting
+ poison the dude in the --fight fixtures, but (a) ApplyPoison is SILENT (the engine's misc.msg "You have
been poisoned!" is a copyrighted game string, deliberately NOT emitted) and (b) the poison counter doesn't
tick HP during the fight (the EVENT_TYPE_POISON delayed-damage tick is a DOCUMENTED simplification — not
wired) and (c) the do_check draws from _scriptHost.Rng, NOT the combat stream, so to-hit/damage/outcome are
unchanged. So no fixture re-records. Harness --combat-proc-hit <atkHex> (fires fp=2 at the dude, reports the
poison delta — deterministic: seed 2 → +1, seed 1 → 0 as the scorpion's do_check decides); golden combat-
proc-poison (seed 2, dudePoison 0→1). DOCUMENTED CUTS: the silent poison message + the poison-over-time HP
tick (the EVENT_TYPE_POISON queue is unwired — the counter is set faithfully but deals no periodic damage
yet); a lethal hit returns early in Hexwaste so fp=2 fires only on a non-lethal hit (moot for poison).
The per-turn (fp=4) + on-hit (fp=2) combat_p_proc hooks are live; the remaining 3 hooks (want-to-join
fp=5, the end-of-combat map hook, the dead round-robin) stay unported (no slice driver).
M3 poison-over-time tick (DONE — "poison actually hurts"): the dude's poison counter now deals periodic
HP damage on the game clock. KEY: the ported EventQueue is COMBAT-SCOPED (combat-tick, cleared on combat
end — the knockout wake), so it's the WRONG tool for poison (which must outlast combat); instead a viewer
game-time schedule (_dudePoisonNextTick) models the engine's single EVENT_TYPE_POISON queue entry off
GameClock.Ticks. SchedulePoison (re)times the next tick to 10*(505-5*poison) ticks (critterAdjustPoison's
_queue_clear_type + queueAddEvent, critter.cc:350-351); ProcessPoison (ported poisonEventProcess, critter.
cc:378 — DUDE-ONLY) fires every due tick in a drain-loop (so a clock JUMP from rest/travel deals the right
count), each: poison -= 2, HP -= 1, GameOver if HP<=0, re-queue from its own fire instant until poison<=0;
driven from UpdateClock (the per-frame clock advance, which also catches up after rest/travel jumps). The
engine's "You take damage from poison." misc.msg line is omitted (copyrighted; silent, the P35 pattern).
ProcessPoison is gated on _dudePoisonNextTick>=0 (only when poisoned), and NO existing golden both poisons
the dude AND advances the clock past a tick interval, so all 15 combat + 59 encounter goldens BYTE-IDENTICAL
(verified). Persisted: SaveState.DudePoison (additive-V2, sparse null when not poisoned; the schedule is
re-derived via SchedulePoison on load). Harness --poison-tick <poison> <gameMinutes> (deterministic, pure
clock math — poison 1/10min → 1 tick -1 HP; poison 10/60min → 5 ticks -5 HP); golden poison-tick. Tests:
DudePoisonRoundTrips (the save). DOCUMENTED CUT: ProcessPoison drives off UpdateClock + rest/travel via the
frame loop (the engine ticks during gameTimeAddTicks); a headless harness that jumps the clock without
pumping a frame relies on the explicit ProcessPoison in the probe.
M4 fp=5 want-to-join hook (DONE): the join decision now runs each candidate's combat_p_proc with
fixedParam=5 + honors its maneuver. Ported _combatai_want_to_join (combat_ai.cc:3165) into CombatEngine.
WantToJoin: a dead/knocked-out critter never joins; one hurt this turn (DamageLastTurn>0) always does;
else its combat_p_proc runs fp=5 (the script may set its maneuver, e.g. by attacking), and the maneuver
decides — CRITTER_MANEUVER_ENGAGING(0x01)→join, DISENGAGING(0x02)/FLEEING(0x04)→don't; else the existing
CombatRules.ShouldJoin heuristic (the danger-source/team-sight proxy). AddJoiners now iterates ALL non-
hostile CombatCritters through WantToJoin (was the inline ShouldJoin), clearing maneuver to NONE on join
(combat.cc:2907). MapObject.Maneuver already existed (parsed obj_pud). The attack external now sets the
attacker ENGAGING (ScriptContext.AttackComplex, interpreter_extra.cc:1860) — the primary maneuver source;
moot on the slice (an attacking critter is already hostile, never a join candidate). INERT on the slice:
no critter handles fp==5 (scorpion fp==2, rat none), so fp=5 is a no-op VM run (no RNG/side-effect, like
P35-M1) → maneuver stays NONE → ShouldJoin decides → the SAME join set; and no non-hostile candidate is
damaged (anything the dude hit is already hostile). All 15 combat + 60 encounter goldens BYTE-IDENTICAL —
the existing joiner fixtures (arcaves scorpions, the X-FIGHTING-Y brawls) are themselves the real-data proof
of the ShouldJoin-fallback path; the maneuver/damage/fp5 branches are proven by 4 fake-host WantToJoin tests
(ENGAGING-joins-far / FLEEING-blocks-near / damaged-joins / fp5-runs). DOCUMENTED RESIDUAL: the FLEEING/
DISENGAGING maneuver SOURCES (the flee/terminate_combat externals, interpreter_extra.cc:4763/4781) stay
arity-stubbed — only ENGAGING-via-attack is wired, so a script can force-join but not script-refuse yet.
The per-turn (fp=4), on-hit (fp=2 poison), and want-to-join (fp=5) combat_p_proc hooks are live.
M5 terminate_combat + the DISENGAGING source (DONE): wired terminate_combat (0x8153, opTerminateCombat) —
the combat-control external a yield script (e.g. a temple challenger's fp=4 at ≤half HP) calls to END the
fight. IVmExternals.TerminateCombat + IntVm 0x8153 dispatch + ScriptContext.TerminateCombat (sets self
DISENGAGING [CRITTER_MANEUVER 0x02, completing M4's residual maneuver source] + ScriptHost.CombatTerminate
Requested) → the viewer's _combat.RequestTerminateCombat(): a _terminateRequested flag (set only in combat,
the engine's isInCombat guard) that UpdateCombat honors at the next turn boundary → EndCombat (ported from
combat.cc _game_user_wants_to_quit=1); cleared in EndCombat/Reset. FORWARD-LOOKING INFRA: the grounding
workflow's claim that ACTemVil (script 748) / dcG2Grd (36) are slice critters is WRONG — MapDump finds NO
script-748 critter in any of the 4 slice maps (arcaves' lone static critter is script 750), so no shippable
critter calls terminate_combat; it's wired faithfully + proven by the fake-host test TerminateCombatFromA
CombatProcEndsTheFight (a fp=4 → RequestTerminateCombat ends the fight). 0x8153 is never hit in a golden +
the flag defaults false → all 15 combat + 60 encounter goldens BYTE-IDENTICAL. Harness --terminate-combat
<hex> (enters combat, drops the critter to ≤half HP, runs its fp=4, reports phase/maneuver — a diagnostic
for future yield-scripted critters; no slice driver so no golden). DOCUMENTED RESIDUAL: the FLEEING maneuver
source (the flee external) is still arity-stubbed (only ENGAGING-via-attack + DISENGAGING-via-terminate are
wired). P35 COMPLETE (M0 grounding, M1 fp=4 per-turn, M2 fp=2 poison sting, M3 poison-over-time, M4 fp=5
want-to-join, M5 terminate_combat). The end-of-combat map hook + the dead round-robin stay unported (no
slice driver). 492 Formats tests; 60 encounter + 15 combat goldens green.

Phase 36 (DONE — "Big Targets", MULTIHEX; the Phase-34 audit's top remaining Tier-2 combat item). Two
gaps closed: (1) ComputeToHit gained the +15-to-hit-vs-a-multihex-defender term (combat.cc:4443 — a big
target is easier to hit; reads defender.Critter.Flags & OBJECT_MULTIHEX, the const already at CombatEngine.
cc:67 used for the P9 knockback immunity); (2) BuildSpawn (the encounter-spawn path) PROPAGATED the proto's
OBJECT_MULTIHEX (0x800) onto the spawn (it hardcoded Flags=0, so a spawned Large Radscorpion was never
multihex → the +15 + knockback immunity silently never applied). SLICE DRIVER (verified, NOT dead code):
the Large Radscorpion (pid 0x1000006, proto flags 0x20000800 = multihex) spawns in KLAD_Scorpions (worldmap.
txt, Klamath-Den route, 30% ratio + a Dead variant); the SMALL Radscorpion (0x1000005, the arcaves --fight
critter) is NOT multihex (flags 0x20000000) — so the +15 is inert on the current combat goldens (they fight
the small one) → all 15 combat goldens BYTE-IDENTICAL, and the spawn-flag propagation doesn't print/affect
the spawn census → all 60 encounter goldens BYTE-IDENTICAL. The feature lights up when a KLAD_Scorpions
encounter with a Large Radscorpion is fought (now +15 to-hit + correctly knockback-immune). Harness
--multihex-probe <pid> (reports the proto's multihex bit); golden multihex-probe (0x1000006 multihex=1 vs
0x1000005 multihex=0). The +15 is a verbatim 1-line port (proportionate to the blind/Sharpshooter to-hit
modifiers — proven by the real-data probe + byte-identical, no calibrated-RNG test). 492 Formats tests;
61 encounter + 15 combat goldens green.

Phase 37 (DONE — "Better Living Through Chemistry", non-HP drug stat effects; the Phase-34 audit's last
slice-driven item — UseDrug previously applied ONLY the HP heal and Log'd "Nothing happens" for every
SPECIAL-boosting chem). Ported item.cc _item_d_take_drug (:2809) + _perform_drug_effect (:2639) + the
EVENT_TYPE_DRUG wear-off queue. M1 proto (BYTE-IDENTICAL — the drug weight int was already skipped, so the
9 trailing ints read in place with no downstream shift): DrugProtoStats widened from (Stats, Amounts) to
also carry Duration1/Amount1, Duration2/Amount2, AddictionChance, WithdrawalEffect, WithdrawalOnset
(proto.cc:1570-1581 order). KEY GROUNDING FINDING: the duration1/duration2 amount tiers are NOT a residual
to skip — they ARE the wear-off: the three tiers per stat NET TO ZERO (Buffout ST +2 immediate / −4 at 360
min / +2 at 1080 min = 0; Jet ST/PE/AP +1/+1/+2 / −4 at 5 min / restore at 1440 min = 0 — the comedown is
the down-then-up ramp). M2 effects: UseDrug applies the immediate effect (ApplyDrugEffect, the
_perform_drug_effect port — stat 35 = current HP heal/clamp/GameOver; stats 0..34 = a BonusStats bonus;
the stats[0]==-2 sentinel = the stimpak random-range heal, REUSING _combatRng so the existing stimpak draw
is byte-identical; stats ≥36 [poison/rad counters] out of scope, only Mentats' minor rad bump — documented)
then schedules the two delayed kicks (ScheduleDrugEvent, skips an all-zero kick). ProcessDrugs drains due
kicks in fire-time order from UpdateClock — the P35-M3 poison-tick game-time pattern (so a rest/travel
clock JUMP fires several at once). PERSISTENCE (the critical risk): BonusStats is REBUILT from base+armor on
load (the drug bonus is NOT in the base block), so a mid-drug save would lose the immediate bonus while the
pending reversals still fire → negative stats; FIX = track the drug's contribution in _drugBonus[35],
persist it (SaveState.DrugBonus, sparse-null) + the pending kicks (SaveState.PendingDrugs, additive-V2),
and RE-APPLY _drugBonus to BonusStats AFTER the sheet rebuild on load. INERT: no golden gives/uses a stat
drug (golden --give pids are weapons/caps/radio: 7/9/25/41/242/266; the -2 stimpak heal RNG is unchanged),
so all 15 combat + 61 encounter goldens BYTE-IDENTICAL. Harness --drug-probe <pid> <gameMinutes> (advances
the clock cumulatively, fires the wear-off, reports the active _drugBonus per stat + pending count); golden
drug-stat (Buffout: min 0 up-kick ST+2/EN+3/AG+2 pending=2 → +400 dur1 fired all-negative pending=1 → +700
[total 1100] dur2 restored to net-zero pending=0). 494 Formats tests (ItemProtoTests GameDataFact asserts
Buffout/Jet durations + the net-zero-per-stat invariant; PersistenceTests DrugBonus/PendingDrugs round-trip);
62 encounter + 15 combat goldens green.

Phase 38 (DONE — "Vices and Tallies", the user's "perks/karma auto-award, addiction/withdrawal, VO"
ask — RESHAPED by a grounded + adversarially-verified workflow [karma-addiction-vo-grounding] against the
prime directive). USER DECISIONS: drop karma auto-award + add kill counters; full addiction/withdrawal;
DEFER VO. KARMA AUTO-AWARD — REJECTED as a non-faithful divergence: the engine has NO kill/quest/combat→
karma hook (pcSetStat stat.cc:611 is the sole gPcStatValues writer, never called with KARMA/REPUTATION; no
set_pc_stat external; combat.cc:4855 kill path runs destroy_p_proc + XP + the kill counter, ZERO karma) —
karma is 100% script-driven (set_global_var), already wired in P31; inventing an auto-award would violate
"port, never guess". VO — faithful but FULLY INERT on the slice (every shippable Den NPC MSG has empty audio
fields; no slice NPC has a head/speech dir; the only voiced NPCs Elder/Hakunin/Sulik are content-gated out)
— DEFERRED until a voiced NPC is in scope. LESSON RE-CONFIRMED: the grounding synthesis mis-decoded the
addiction perks AND the Tragic/Jet GVAR indices — I verified every load-bearing value against the actual
source / Hexwaste's checksum-guarded PerkTable.g.cs (the recurring hallucination guard).
M0 pure Formats.Item.DrugAddiction: the drugPid→addiction-GVAR map (item.cc:144 gDrugDescriptions; verified
game_vars.h indices NUKA=21/BUFFOUT=22/MENTATS=23/PSYCHO=24/RADAWAY=25/ALCOHOL=26/TRAGIC=293/JET=294) +
the faithful Roll (item.cc:2823 chance ×2 ChemReliant /÷2 ChemResistant /÷2 FlowerChild, roll(1..100)≤chance
inclusive); PerkRules.MaxRankPerkEffect = the perkAddEffect maxRank==-1 fold ((Stat,StatModifier) + the
StatReqs[0..6] SPECIAL array as the EFFECT) — decoded from PerkTable.g.cs: Buffout(54)=ST-2/EN-2/AG-3,
Mentats(55)=IN-3/AG-2, Psycho(56)=IN-2, RadAway(57)=RadResist-20, Jet(70)=MaxAP-1/ST-1/PE-1, Tragic(71)=
PE-2/IN-1/LK-1, Nuka(53)=none. M1+M2 (one commit): UseDrug→TryAddict rolls on a dedicated _addictionRng
(isolated → P37 drug-stat + all goldens BYTE-IDENTICAL even though Buffout is now addictive), sets the GVAR,
schedules onset (600*withdrawalOnset ticks); ProcessWithdrawals (drained from UpdateClock) onset→apply the
perk fold into _withdrawalBonus[35]+BonusStats + schedule recovery 7 game-days out FROM THE ONSET'S FIRE
INSTANT (the clock-jump-correct rule, like ProcessPoison — caught + fixed a recovery-scheduling bug);
recovery→reverse + clear GVAR, EXCEPT Jet (PERK_JET_ADDICTION=70 returns early → PERMANENT until pid-260
antidote, one-give residual). NEVER touches _dudePerkRanks. M3 persistence (SaveState.WithdrawalBonus +
PendingWithdrawals, additive-V2 sparse, re-applied AFTER the load sheet-rebuild — the DrugBonus trap) +
char-sheet/Pip-Boy "Addictions:" display (character_editor.cc:4611 gAddictionReputationVars + editor.msg
1004+index). Harness --addict-probe <pid> <seed> <gameMinutes>; goldens addict-buffout-active/-recover/
addict-jet-permanent/addict-miss. KILL COUNTERS (the faithful karma adjacency): CombatEngine.KillCritter
tallies the victim's KILL_TYPE beside the XP accrual (gated identically, combat.cc:4870) via a default-no-op
ICombatHost.RecordKill → _killsByType[19] (critter.cc:152), reset on new-game, persisted (SaveState.Kills
ByType sparse); metarule3 rule 103 GET_KILL_COUNT (killsGetByType) wired (inert — no slice script reads it);
char-sheet "Kills:" display (proto.msg 1450+killType). RecordKill draws no RNG/Console → combat goldens
byte-identical. Harness --kills-probe <killtype>; golden kill-counter (seed-7 arcaves fight = 2 Radscorpions
[6=2]; each gives 60 XP, so +120 = 2 kills, NOT one — verified). 509 Formats tests (DrugAddictionTests 13,
the withdrawal round-trip, the kill-counter fake-host gating); 67 encounter + 15 combat goldens green.

Phase 39 (DONE — "Required Reading", skill books; chosen by a next-feature-grounding workflow that ranked
4 candidates by value/effort/faithfulness/LIVENESS — skill books won [82] as the only one that's faithful,
small, a NEW player-facing capability, AND verified-live on the slice [vs selectable-ammo whose effect is
already live, the crit-FAILURE table gated to day≥6, map_update_p_proc subtle/inert]). A real recursive
inventory-walk found exactly 2 lootable books on the slice: Guns and Bullets (pid 102→Small Guns) in a
denbus1 container @23316, Scout Handbook (pid 86→Outdoorsman) on a KLAMALL critter @26515 (the slice's
Klamath map is KLAMALL.map, not klamath.map). M1 pure Formats.Item.SkillBooks: BookTable (booksInitVanilla
item.cc:3283 — pid→(skill,proto.msg id): 73→Science(12)/802, 76→Repair(13)/803, 80→FirstAid(6)/804,
86→Outdoorsman(17)/806, 102→SmallGuns(0)/805) + BookRaise.Increase = (100−effective)/10, ≤0→0 (the de-facto
cap at effective 100 — NOT skillAddForce's 300 guard), ×150/100 with Comprehension (proto_instance.cc:776);
ReadSeconds = 3600*(11−INT). PerkId.Comprehension=81 (enum index, verified — NOT the line-88 the synthesis
cited). M2 wired into UseInventoryItem (a book branch above the can't-use fallthrough, mirroring UseDrug):
refuse in combat (proto.msg-902 state line, no copyright); read the EFFECTIVE skill via CritterState.Skill
Value, WRITE the BASE points _dudeGcd.Stats.Skills[skill]+=increase (the engine's skillGetValue/skillAddForce
split — so a TAGGED skill gains 2%/point: Narg's Small Guns 43→53 from +5 pts; an untagged Outdoorsman
16→24 from +8); advance the clock ReadSeconds. DOCUMENTED OUT-OF-SCOPE (both in _obj_use_book but unported):
the paletteFadeTo screen fade (no palette fade in our renderer) + scriptsExecMapUpdateProc (map_update_p_proc
unwired). Persistence rides the already-saved DudeSkills. DECISION: ship all 5 book rows (3 — Big Book of
Science/Dean's Electronics/First Aid Book — have no slice instance, documented forward-looking infra). Inert
by default (no golden uses a book; the book branch is gated on the book-pid set + draws no RNG) → all 15
combat + 67 prior encounter goldens BYTE-IDENTICAL. Harness --use-book <pid>; golden use-book (102 twice
showing diminishing returns 43→53→61, then 86 16→24). 528 Formats tests (19 SkillBooksTests: the curve, the
cap, Comprehension ×1.5, read time); 68 encounter + 15 combat goldens green.

Phase 40 (DONE — "Pick Your Round", selectable ammo type; the next-feature-grounding RUNNER-UP [71]).
The combat CONSEQUENCE was already live (loading AP vs JHP shifts to-hit + damage via the wired ammo
AC/DR/mult/div math — CombatMath/RangedMath, consumed at CombatEngine to-hit + damage sites); P40 adds
player CONTROL over the choice. UnloadEquippedWeapon (weaponUnload item.cc:1880 — eject min(loaded,
boxCapacity) into a DISCRETE bag box [a partial count must NOT merge into a full stack via AddToDude
Inventory], leave the remainder, empty the weapon). TryReload refactored: the 3-param ICombatHost method
is now a forwarder to TryReloadWith(…, preferredAmmoPid) — at -1 the body is byte-identical (the R-key /
AI auto-reload path unchanged), ≥0 restricts the bag scan to a chosen ammo pid (the selection). The
no-mixed-mags rule (only reload a matching type into a non-empty weapon) is unchanged, so a type SWAP
needs an empty weapon → unload first. Player paths: Shift+R unloads the equipped weapon; USING an ammo
box (UseInventoryItem ammo branch) reloads the equipped weapon with that type (blocked-→-hint-to-unload on
a different loaded type). SLICE-LIVE (verified by a recursive inventory walk): the 10mm pistol (pid 8) AND
the Klamath pipe rifle (299) both fire 10mm, with 10mm AP (pid 30: ac0/dr-25/mult1/div2 = armor-piercing)
+ 10mm JHP (29: ac0/dr+25/mult2/div1 = anti-unarmored) abundantly lootable across denbus1/2/kladwtwn, and
Den NPCs wear DR-armor so AP genuinely matters. Additive (the auto-reload/--give paths still auto-select;
combat math untouched) → all 15 combat + 68 prior encounter goldens BYTE-IDENTICAL. Harness --load-ammo
<ammoPid> (unload + reload-with-pid, report the loaded type + ac/dr/mult/div); golden ammo-select (pistol
AP↔JHP delta). No new Formats tests (viewer wiring on already-parsed protos + already-tested combat math);
528 Formats tests, 69 encounter + 15 combat goldens green. DOCUMENTED: the unloaded box is discrete (the
engine creates a fresh object too); the engine's prefer-last-loaded-type auto-reload nuance (item.cc:1455)
is approximated by Hexwaste's bag-order scan on an empty weapon (a pre-existing behavior, unchanged).

Phase 41 (DONE — "Fumble", the critical-FAILURE table; the highest-value remaining backlog item — it
completes the crit system [crit-SUCCESS landed P9; failures only honored Jinxed's lose-turn since P29]).
M0 data (BYTE-IDENTICAL — proto un-skip is read4+skip4 == the old skip8): ProtoDatabase reads weapon
criticalFailureType → WeaponProtoStats; gen_critical_tables.py emits the _cf_table[7][5] (combat.cc:1875,
verified all 7 rows e.g. row6col4=Explode|LoseTurn|OnFire=37888) into CritFailTable + the missing DAM
tokens; the FNV-1a checksum folds the new table (runtime ComputeChecksum updated); CriticalTables gained
the crit-fail DAM_* constants + CritFailFlags(failureType, effect); Formats.Combat.CriticalFailure.Severity
(Luck-bucketed d100−5·(LK−5), combat.cc:4203) + Resolve. M1 trigger + effects: the natural ROLL_CRITICAL_
FAILURE upgrade (random.cc randomTranslateRoll — the symmetric mirror of crit-success: a MISS at day≥1
[CriticalsEnabled] draws a d100 ≤ −delta/10) + the Jinxed force (combat.cc:3857, any combatant when the
dude is Jinxed, no day gate) are wired into the consolidated TriggerCritFailure, called after RollAttack on
a miss in all 3 single-attack paths (dude/ally/enemy; RNG ORDER mirrors the engine — the upgrade draw is
the very next after the miss hit-roll, so day-1 non-Jinxed draws NOTHING → byte-identical). KEY DIVERGENCE
FIX: the DUDE's crit-fail EFFECT is now correctly gated to day≥6 (ICombatHost.DudeCritFailuresEnabled,
combat.cc:4190 — the trigger still draws from day 2); non-dude fumbles are ungated (P29's day-2 lose-turn
was the documented divergence, now faithful). Effects honored (attackComputeCriticalFailure): LOSE_TURN
(AP→0, the actor's turn ends), KNOCKED_DOWN (prone), CRIP_RANDOM (a random limb), DROP (weapon to the
ground), DESTROY (weapon gone), LOSE_AMMO (mag spills), HIT_SELF/HURT_SELF (self-damage), EXPLODE (the
weapon detonates at the fumbler), RANDOM_HIT (the wild shot strikes a random nearby critter — can catch a
companion); the _attackFindInvalidFlags mask clears DROP/DESTROY/LOSE_AMMO for an unarmed attacker.
DOCUMENTED SIMPLIFICATIONS: self/collateral damage is a direct HP hit (no on-hit hooks / ammo mods, not a
re-attack); DAM_DUD/DAM_ON_FIRE are cosmetic (no jam/fire model); crit-fail is wired on the SINGLE-attack
path ONLY (burst already aborts on its inception crit-fail; thrown is a residual — both their goldens are
day-1 so unaffected). RE-RECORD: the 2nd-d100-on-miss shifted the 3 day-2 crit goldens (crit-day2/aim-eyes-
day2/knockdown-day2 — the P14-M4 precedent; verified the divergence is a clean RNG shift, fights resolve
sanely) — the 12 day-1 + burst/throw fixtures stayed BYTE-IDENTICAL (clean check BEFORE record). New golden
arcaves-crit-fail-day6 (a day-6 dude's missed punch fumbles to LOSE_TURN, flags=0x8000). 5 CriticalFailure
tests (severity buckets, the verified table, clamp, Resolve-with-Luck, checksum) + 3 fake-host CombatEngine
tests (the day≥6 gate, a table effect [CRIP_RANDOM], the inert no-criticals invariant); 546 Formats tests,
69 encounter + 16 combat goldens green.

Phase 42 (DONE — "Field Medicine", enemy chem_use stimpak healing; the AI-depth runner-up of the
next-feature-grounding-2 workflow). DELIBERATELY CHOSE THE RUNNER-UP over the synthesis's #1 (map_update_
p_proc lighting, 8.8) — I verified the #1's "every map wrongly full-bright → wire the day/night curve"
premise was UNCONFIRMED (it contradicts the established P4 "engine has NO day/night curve; ours is custom"
finding; the slice-map opcode scan showed arcaves has ZERO game_time_hour/month refs [a cave — no day/
night] and the town-map counts were false-positive-inflated; I couldn't confirm map_update drives
set_light_level without building it), and its AmbientFixed rework risks the P21 lighting goldens. Per the
prime directive (don't build on an unverified premise) + the recurring grounding-hallucination lesson, the
solidly-verified runner-up won: ~30 slice human NPCs CARRY stimpaks (pid 40, SubType=2/healing) and
chem_use is a live ai.txt field. Ported combat_ai.cc _ai_check_drugs healing branch (:999-1027): AiPackets
now parses chem_use (gChemUseKeys clean=0/hurt_little=1/hurt_lots=2/sometimes=3/anytime=4/always=5);
Formats.Combat.AiHealing = IsHealingItem (pids 40/144/273, itemIsHealing item.cc:3592) + HealHpRatio
(clean→0/little→60/lots→30/else→50, combat_ai.cc:971). CombatEngine.TryAiHeal runs after the flee gate in
TryEnemyAction (the engine's flee→drugs→attack order): a BIPED (BODY_TYPE_BIPED==0 → quadruped scorpions
never heal) below MaxHp*ratio/100 quaffs a healing item (host ICombatHost.TryNpcHeal, default-false) while
AP≥2, 2 AP each. ViewerGame.TryNpcHeal finds a healing drug in the critter's bag, rolls the stimpak heal on
_combatRng (the -2 range / stat-35, like the dude's), applies capped at MaxHp, consumes one. ENEMIES ONLY
(the dude/allies heal via the UI); the non-healing combat-drug branch (sometimes/anytime/always quaffing
Jet/Psycho) is a documented residual. BYTE-IDENTICAL: the two golden-fight enemies — arcaves scorpion (pkt8
clean + quadruped) and denbus2 peasant (pkt14 clean) — never heal (verified the real ai.txt: pkt8/pkt14
chem_use absent=clean), and TryNpcHeal draws nothing for an empty bag → all 16 combat + 69 prior encounter
goldens unchanged. DOCUMENTED: the slice's stimpak NPCs live in SWARM Den maps where the dude can't win a
clean 1-on-1 (denbus1 @17662 = 11 hostiles, dude dies in 2 rounds), so the live proof is --ai-heal-probe
<hex> (give the critter a stimpak, drop it to 1 HP, run the real TryNpcHeal) + a fake-host test, not a
winnable real fight. Golden ai-heal (denbus1 Average Merchant @16910 heals 1→13). 22 new tests
(AiHealingTests, the chem_use parse + GameDataFact [pkt8/14 clean, pkt12=2, pkt50=4], 2 fake-host heal/
clean-skip tests); 568 Formats tests, 70 encounter + 16 combat goldens green. map_update_p_proc stays on
the backlog pending a proper M0 RunMapUpdate diagnostic to confirm (not assume) its lighting payoff.

Phase 43 (DONE — "Draw Your Backup", AI best_weapon inventory switch; the user's "Full combat AI" ask,
M2 — chem_use M1 already shipped in P42). The engine's _ai_switch_weapons (combat_ai.cc:2596) → _ai_search_
inven_weap (:2002) → _ai_best_weapon (:1817): when a critter's wielded weapon becomes unusable it scans its
CARRIED weapons and wields the best one its ai.txt best_weapon preference allows. GROUNDING (MapDump's new
inventory-weapon census, verified on real data): multi-weapon NPCs DO exist on the slice — denbus1 17261
(Tough Guard pkt22 = ranged_over_melee) with a backup 0x5; kladwtwn/denbus2 pkt12/pkt24/pkt34 NPCs with
backups — while the golden-fight arcaves scorpions are non-biped + carry NO weapons (inert). Ported:
Formats.Combat.WeaponClass (item.cc _attack_subtype/_attack_skill[9] → ATTACK_TYPE + SKILL from extFlags&0xF,
with the SMALL_GUNS→ENERGY[laser/plasma/electrical]/BIG_GUNS[0x100] refinement); Formats.Combat.AiBestWeapon
(the _weapPrefOrderings[9][5] table indexed [best_weapon+1] + the pairwise Prefers: order term, ±5-damage
cost tiebreak, flare deprioritise, the best_weapon==-1/≥UNARMED_OVER_THROW damage override, RANDOM coin);
AiPacket.BestWeapon (-1 default = the engine pre-parse value, same ordering as no_pref) parsed via
ParseBestWeapon. CombatEngine.AiSwitchWeapon folds the candidates (CritterInventoryWeapons host seam) with
the _ai_can_use_weapon filter (both-arms-crippled / one-arm+two-handed gate, skill≥min_to_hit, pref-type
match, ranged-needs-ammo) over an unarmed "punch" seed (attackType UNARMED if dist≤1 else NONE, avgDamage 0
— the engine's weapon1==null), gated on BIPED/ROBOTIC bodies (combat_ai.cc:2004); wired into TryEnemyAction's
dry-gun branch (the slice's clean hook) BEFORE the fists fallback, with EquipWeapon (host) actually wielding
the pick. DOCUMENTED SIMPLIFICATIONS: the avg-damage score omits the weapon-perk ×2 + explosive ×(extras+1)
factors (Hexwaste tracks neither); _combat_safety_invalidate_weapon (ally-in-LoF / over-range Ignore) not
applied; ranged ammo = loaded/proto-default (aiHaveAmmo bag-search approximated); only the dry-gun switch
trigger is wired (the engine also switches on arm-crippled / out-of-range-no-weapon — same helper, not wired,
no slice driver). INERT: the golden-fight scorpions are non-biped + weaponless → the switch never fires →
all 16 combat + 70 prior encounter goldens BYTE-IDENTICAL (verified by a clean check BEFORE recording).
MapDump gained a critter inventory-weapon census (pids + *=wielded). Harness --ai-weapon-probe <hex> forces
the gun dry + runs the REAL switch path (no --fight golden reaches a multi-weapon NPC, so this is the live
proof); golden ai-weapon-switch (denbus1 Tough Guard 17261 0x5E→0x5). KEY FINDING (verified, not guessed):
the kladwtwn multi-weapon NPCs' RUNTIME packets (24 Tough Citizen / 34 Torr, no best_weapon) differ from
MapDump's static read because kladwtwn map_enter spawns/replaces them — both reads are correct; denbus1
17261 (pkt22, consistent) is the clean demonstrator. 45 new test cases (AiBestWeaponTests pairwise + WeaponClass
+ best_weapon parse + GameDataFact pkt12=ranged_over_melee + 4 fake-host CombatEngine switch/fists/non-biped/
skill-gate); 608 Formats tests (548 run + 60 game-data-gated), 71 encounter + 16 combat goldens green.

Phase 44 (DONE — "Initiative", interleaved combat turn order by Sequence; the user's pick from the
"bigger features still to move from f2ce" survey — the biggest remaining combat-fidelity gap). Combat ran
in FIXED BLOCKS (dude → all hostiles → all allies); the engine interleaves EVERY combatant by the
Sequence stat. Ported combat.cc: _combat_sequence (rounds 2+ qsort by _compare_faster = Sequence desc,
Luck tiebreak; drops dead + KO/disengaging to noncom) + _combat_sequence_init (ROUND 1 is attacker-first /
defender-second / dude-third — initiative does NOT apply the opening round; the one who started combat
goes first) + the _combat() round loop iterating the sorted list. CombatEngine: replaced the
_enemyQueue/_allyQueue two-block model with ONE interleaved _order list + _orderIndex; BuildTurnOrder
(round-1 special vs OrderByDescending Sequence/Luck — STABLE for ties, a documented divergence from the
engine's unstable qsort, for golden reproducibility); StepTurnOrder walks the order one actor per Step
(an NPC slot auto-resolves via TryEnemyAction/TryAllyAction unchanged; the DUDE's slot pauses in PlayerTurn
for input — the engine's blocking _combat_turn(gDude)); EndPlayerTurn advances _orderIndex; StartNewRound
ticks the combat clock + wakes + joiners then re-sorts. The phase enum is unchanged (PlayerTurn = the
dude's slot; EnemyTurn = auto-stepping NPC slots — the viewer only reads Idle/PlayerTurn, so both the
headless --fight driver and interactive play work untouched). KEY OUTCOME (de-risk: clean check BEFORE
record): 15 of 16 combat goldens BYTE-IDENTICAL — the arcaves scorpions have Sequence ≤ the dude (Narg),
so dude-first order is unchanged; the reorder only bites when an enemy OUT-sequences the dude. The lone
re-record (denbus2-fight-flee, where Den humans out-sequence Narg) is SANE: same outcome (dude dies to the
24-slave swarm) in 5 rounds instead of 9 — the faster death is the faithful order (the humans now correctly
act before him). 71 encounter goldens BYTE-IDENTICAL (the brawl/combat-proc goldens capture state summaries,
not per-turn order). 73 CombatEngine fake-host tests (HigherSequenceEnemyActsBeforeTheDudeInRoundTwo locks
it: an ap-4 seq-20 enemy attacks in BOTH round 1 and round 2 before the dude's round-2 slot — old model = 1,
new = 2). 609 Formats tests. DOCUMENTED: round-per-round game-clock advance (gameTimeAddSeconds(5)) NOT
wired (combat stays wall-time; _combatTick is the knockout-wake source only, unchanged).

Phase 45 (DONE — "Numbers in the Air", floating/overhead combat text; combat outcomes only
reached the monitor log before). M0 grounding (workflow). KEY FINDING (the headline, verified — not
guessed): Fallout 2 does NOT float combat outcomes — combat.cc _combat_display routes every hit/miss/
crit/damage line to the scrolling MONITOR LOG (displayMonitorAddMessage), ONE colour, no float; the
text_object.cc float layer is real but used only for AI taunts (combat_ai.cc, colour from ai.txt),
skill-use responses (actions.cc, YELLOW), level-up (party_member.cc, WHITE), and the script float_msg
external. So "floating damage numbers" is a DOCUMENTED PRESENTATION DIVERGENCE built on the engine's
real float MECHANISM + its real float_msg/_colorTable colour vocabulary (interpreter_extra.cc:3150-3190;
color.cc RGB555→idx) — NOT an invented _combat_display colour scheme. M1 pure Formats.Combat.FloatText
ports the text_object.cc timing/placement: TEXT_OBJECTS_MAX_COUNT=20 (:19), gTextObjectsBaseDelay 3500
(:48) + gTextObjectsLineDelay 1399 (:51) → LifetimeMs = lineDelay*lines + base (:337, 4899 ms/line);
AnchorOffset (16 − w/2, −(h+60)) = textObjectFindPlacement's primary placement (:379-383, centre on the
32-px tile + lift above the head; the 8-position off-screen-bounds cascade :386-454 is simplified to the
primary anchor — the camera clamps the world); + a rise + alpha fade (presentation — the engine's floats
are STATIC + NON-fading, hold solid then expire :338). M2 viewer CombatTextLayer + wiring: a float is
spawned from Log() by parsing the damage int out of the Hexwaste-AUTHORED "...for N damage." line (NOT a
combat.msg game string — Log is the in-memory monitor buffer, never stdout) and placed over the defender
tracked at OnAttackStarted/OnThrowStarted. WHY the tracked object, not the Log wording: ResolveAttack
keys the hit/miss text on byDude, so an NPC-vs-NPC blow still reads "...hits you..." (the wording can't be
trusted); the tracked object is also the ONLY signal for the dude AS defender, which OnTargetHit/
OnTargetDodge deliberately skip (the camera-anchor dude doesn't visibly react — P34-M6) and which the
"different shade for the dude" needs — so CombatEngine is UNTOUCHED. One float per tile (the engine's
textObjectsRemoveByOwner one-per-owner, :276/460) + the global cap; colours = the real float_msg
constants: damage RED [31744] over an NPC / LIGHT_RED [32074] over the dude (a readability shade — the
engine distinguishes by message-id, NOT colour, so documented), crit YELLOW [32747], miss WHITE [32767],
black fading outline (idx 0, :257). Drawn between the roofs and the HUD bar; ticked wall-time in Update.
KEY DE-RISK: the layer is Draw-only + wall-time-ticked, so the headless harness pumps neither its ticker
nor Draw → ALL 16 combat + 71 prior encounter goldens BYTE-IDENTICAL (verified by a clean check BEFORE
recording the new fixture; the float spawns DO run headless in Log but only mutate an in-memory list,
never the console). DOCUMENTED CUTS: burst collateral bystander floats omitted (the "also catches" line
names a bystander, not the tracked defender — the main target still floats); a non-lethal NPC thrown-hit
and a prone/KO miss have no tracked-defender-matching callback → their float is dropped (rare, cosmetic);
the off-screen placement cascade is the primary anchor only. Harness --float-text-probe <hex> (STATE-only
— count/cap/lifetime/anchor + the colour hex ints, never the message text); golden float-text-probe. 8
FloatTextTests; 622 Formats tests, 72 encounter + 16 combat goldens green.

Phase 46 (DONE — "Let There Be (Less) Light", map_update_p_proc wiring + a latent lighting-clobber
fix; the P42-backlogged item, its M0 diagnostic finally run). M0 grounding (workflow) + a STATIC census
(MapDump + IntProgram.FindProcedure — a pure .int procedure-table read, no bytecode exec) + a RUNTIME
trace (--map-update-probe). FINDINGS: SCRIPT_PROC_MAP_UPDATE=23 fires once on load AFTER map_enter
(map.cc:1010-1011) then every 600 game ticks (mapUpdateEventProcess), on the map script + every object
script that defines it; no combat gate (scripts.cc scriptsExecMapUpdateScripts). The census found
map_update_p_proc is LIVE on EVERY slice map — the map script + many object scripts (doors/boxes/critters
incl. dcVic/Kcsulik/KCTorr/dcG2Grd) define it — so it is NOT dead code (overturns the P42 "might be
inert" worry). The runtime trace found it drives lighting via set_light_level (1 call/map): on 5 of 6 it
re-sets level 100 (=max, INERT — map_enter already pinned max); on ARCAVES it sets level 50 (the P21-
documented "cavern" level) → dims ambient 65536→40960 (62.5%). No unhandled externals (only debug_msg, a
no-op). So the P42 skepticism is RESOLVED: map_update DOES drive lighting (confirmed, not assumed), but
the "day/night curve" framing was wrong — it's a one-shot static cavern set_light_level. M1 wiring (the
user's "full, all scripts" choice): ScriptHost.RunMapUpdate (the faithful scriptsExecMapUpdateScripts —
map script + ALL object scripts) wired into LoadMap right after RunMapEnter (the engine's load sequence);
the periodic 600-tick re-run is DEFERRED (the diagnostic found no time-varying map_update content on the
slice — once-on-load suffices). TWO faithful fixes the diagnostic surfaced as prerequisites: (1) reg_anim_
animate_forever is now IDEMPOTENT per object (the engine has ONE anim slot/object) — artemple's Animfrvr
script defines map_update_p_proc that re-registers the same firepits map_enter already did, so running
map_update doubled the _regAnimForever record (script-light forever 2→4); deduping restores forever=2 +
drops a redundant looping entry. (2) A LATENT P21 BUG: RebuildLighting clobbered the script-pinned ambient
back to InitialAmbient (it IGNORED AmbientFixed, unlike the day/night clock at ViewerGame.cs:8606 which
respects it) — so set_light_level only ever "worked" because every shipped value coincided with max. Fixed:
RebuildLighting now PRESERVES the pinned ambient (the clock's pattern), and AmbientFixed RESETS per map
load so each map re-pins via its own scripts. Net: arcaves' cavern dim (40960) now actually renders (a
real, modest fidelity fix — the cave was lit at 100%). GOLDEN-SAFE: lighting is Draw-only → all 16 combat
+ 72 prior encounter goldens BYTE-IDENTICAL (the lone ambient golden, artemple script-light, is unchanged
— artemple sets max; no other golden reports ambient; script-light stayed byte-identical AFTER the reg-
anim dedupe). Tooling: MapDump gained a per-map map_update_p_proc census (LIVE/ABSENT verdict);
--map-update-probe (state-only runtime trace: lightCalls/levels/ambient-delta/new-stubs); ScriptHost.Run
MapUpdate. 2 new goldens — light-arcaves (the live 40960 dim) + map-update-arcaves (the diagnostic:
levels=[50], 1 light call, no new stubs). 622 Formats tests, 74 encounter + 16 combat goldens green.

Phase 47 (DONE — "Drag the Gear", inventory drag-and-drop equip; the P15-M2 spillover). M0 grounding
(workflow): fo2ce inventory.cc — the window (499x377) + the armor/left/right equip-slot rects + the
press->drag->release state machine + the _switch_hand equip/swap (inventory.cc:2386-2537); Hexwaste's
inventory is a TEXT-LIST panel (no authentic INVBOX.frm window) with a flag-toggle equip model
(FlagInRightHand/FlagWorn + ApplyArmorBonus). M1 pure Formats.Combat.EquipRules: CanEquip (weapon->the
weapon slot, armor->the armor slot, a wrong-type drop rejected — the _switch_hand type guards) +
NaturalSlot + the EquipSlot enum (Weapon/Armor only — Hexwaste equips ONE weapon, so the engine's LEFT-
hand/dual-wield item2 slot is OUT, a documented simplification: it needs the two-handed/item2 proto model
and no shippable content dual-wields). M2 viewer: two equip SLOTS (weapon + armor) rendered as boxes
beside the list (x=420, the free right-panel column) showing the equipped item's icon; a press->drag->
release handler (HandleInventoryDrag) — drag a list row onto a slot = EQUIP, drag a slot item off = UNEQUIP,
a row TAP (no real drag) falls back to the existing click-to-use so click-to-equip is preserved; the
dragged item's ghost icon follows the cursor; reuses the existing flag + ApplyArmorBonus mutations (the
same equip math as UseInventoryItem). Loot/barter/trade keep click-on-press (they transfer, not equip).
DOCUMENTED DIVERGENCE: the slots are boxes beside the text list, not the authentic INVBOX.frm paperdoll
window — an art residual (the Skilldex text-then-art pattern). GOLDEN-SAFE: the panel-click/hud-click
goldens drive TryClickItemPanel/TryClickInterfaceBar DIRECTLY (not the live mouse edges I changed), and
DrawEquipSlots is Draw-only gated on _inventoryOpen -> all 16 combat + 74 prior encounter goldens BYTE-
IDENTICAL. Harness --drag-equip <fromRow> <slot> (slot 0=weapon/2=armor/-1=drop; STATE-only: pid +
equipped flag + AC/DT/DR, never the item name); 4 goldens (weapon equips in-hand; armor applies AC 8->33
DT 0->12 DR 0->40; a weapon onto the armor slot is rejected; drop removes from the bag). 9 EquipRulesTests.
631 Formats tests, 78 encounter + 16 combat goldens green.

Phase 48 (DONE — "Ten Slots", multi-slot save UI; the P5/P7 single-slot residual). M0 grounding
(workflow): fo2ce loadsave.cc — the 10-slot LSGAME screen (SLOT##/SAVE.DAT, per-slot metadata
[description/character/game-date/location], the EMPTY/OCCUPIED/ERROR states, the 224x133 thumbnail);
Hexwaste saves ONE versioned JSON SaveState to SavePath (single slot) with the Options Save/Load rows
firing SaveGame/LoadGame immediately. M1 pure Formats.SaveSlots: Count=10 (LOAD_SAVE_SLOT_COUNT) +
SlotFileName(n) + Describe(SaveState?) -> SlotInfo (occupied / version-mismatch / character / level /
map / date) — the loadsave.cc _DrawInfoBox display reduced to what the JSON SaveState carries. M2 viewer:
a 10-slot picker modal (mirrors the Options window — SaveLoadSlotRect/At, the OptionsRowRect pattern)
opened from the Options Save(S)/Load(L) rows; each row shows the slot's metadata ("combat L3 denbus2.map
July 25, 2241") or "- EMPTY -" / "- OLD VERSION -", colour-coded, 0-9 / click to save into / load from it;
one JSON file per slot (hexwaste-slotN.json) under SaveDir; load refuses an empty / mismatched slot. F5/F9
stay a SEPARATE quicksave on the default SavePath (unchanged). DOCUMENTED DIVERGENCES: a dark text panel,
not the authentic LSGAME.frm art (art residual, the Skilldex text-then-art pattern); no overwrite-confirm
dialog (a click saves directly); no thumbnail; and the Title-screen "continue/load" is a residual — the
in-game picker loads any slot MID-SESSION (the F9/Options-Load path), but a cold-start-from-title load is a
separate flow (Hexwaste's title goes Title->New Game). GOLDEN-SAFE: F5/F9 + --save-now/--load-now (the
vic-save-roundtrip) use the DEFAULT SavePath, UNCHANGED; the Options Save/Load reroute is only driven live
(no golden — menu-click-options tests the Resume row only); the picker is Draw-only -> all 16 combat + 76
prior encounter goldens BYTE-IDENTICAL. Harness --save-dir / --save-slot / --load-slot / --slots-probe /
--reset-slots / --show-saveload (STATE-only: slot + occupied/level, never names); 2 goldens (round-trip
save+load slot 3 + an empty-slot no-op; a slots-probe). 8 SaveSlotsTests. 639 Formats tests, 80 encounter
+ 16 combat goldens green.

Phase 49 (IN PROGRESS — "Aim Small", the called-shot click dialog + [P50] the AI-disposition window;
the user's "full faithful" pick of the next-feature list). M0 grounding (workflow). M1 called-shot click
dialog (DONE): replaces the P9-M2 V-CYCLE with a click dialog (the engine's CALLED.frm body-part picker,
combat.cc:5476 calledShotSelectHitLocation). V now OPENS a modal listing the 8 hit locations in the
engine's button order (head/eyes/right-arm/right-leg/torso/groin/left-arm/left-leg, combat.cc:1894-1907)
+ uncalled, each showing its to-hit PENALTY (combat.cc:172 hit_location_penalty — head -40 / eyes -60 /
torso 0 / arms -30 / legs -20 / groin -30); 1-9 / click picks a location, Esc cancels. The chosen
AimLocation feeds the UNCHANGED TryAttack(target, AimLocation) path (the penalty + crit-table lookup);
SelectAimRow is the one seam the live click + the harness share. DOCUMENTED DIVERGENCES: a single-column
text list, not the authentic CALLED.frm critter-pic overlay (art residual, the Skilldex text-then-art
pattern); the live per-part to-hit % is a residual (the static penalty is shown — it's the defining
per-location stat). GOLDEN-SAFE: --aim (the arcaves-aim-eyes-day2 combat golden) sets AimLocation DIRECTLY,
unchanged; V->dialog is live-only; the dialog is Draw-only -> all 16 combat + 80 prior encounter goldens
BYTE-IDENTICAL. Harness --aim-click <row> (STATE-only: row/loc/penalty + the part name; row -1 just opens
it for a screenshot); golden aim-click (head -40 / eyes -60 / torso 0 / groin -30 / uncalled 0). 639
Formats tests, 81 encounter + 16 combat goldens green. M2-M4 (the full AI-disposition combat-control
window + the ally-AI wiring) ship as P50.

Phase 50 (DONE — "Tactics", the AI-disposition combat-control window + the ally-AI wiring; the user's
"full faithful" pick + P49's second half). M0 grounding (the P49 workflow). KEY FINDING: the engine's
party combat-control window (game_dialog.cc:3354) has 7 LIVE settings, but Hexwaste's TryAllyAction was a
2-line "attack the nearest hostile" with ZERO knobs — so porting the WINDOW alone would be cosmetic. Per
the prime directive (no inert features — cf. P38's karma rejection), wired REAL ally-AI behaviour. M1
pure Formats.Combat.CompanionAi: the enums (Disposition / AttackWho / Distance / RunAway / ChemUse) +
Effective() (a non-Custom disposition PRESETS the knobs — Aggressive/Berserk/Defensive/Coward) + the
decision helpers (ShouldFlee HP-fraction thresholds, PickTarget priority). GOTCHA (record-struct trap):
`new()` zero-inits a record struct (IGNORING the primary-ctor defaults → Berserk/AbjectCoward), so
CompanionAi.Default is built EXPLICITLY as Aggressive/Closest/OnYourOwn/Never/Clean = the pre-P50
behaviour. M2 CombatEngine.TryAllyAction wired through a new ICombatHost.CompanionSettings seam (default
= Default): attack-who target priority (Closest = the old nearest; Strongest/Weakest by HP; WhoeverAttacking
Me DEGRADES to Closest — no per-ally whoHitMe tracker, documented), run-away flee (TryFlee parameterised
to take the actor AP by ref so allies + enemies share the one _ai_run_away path), distance (StayClose
regroups with the dude past 5 hexes / Stay holds / Charge+OnYourOwn close on the target; Snipe back-away is
a residual), chem-use heal (reuses the P42 host TryNpcHeal). The DEFAULT (Aggressive) resolves to the EXACT
pre-P50 behaviour, so it is BYTE-IDENTICAL. M3 viewer: the combat-control window (the OptionsRowRect modal
pattern) opened from the companion hub ("Set your tactics."); 5 cycle-able rows (disposition + the 4 knobs)
+ Done, a detail-row cycle flips disposition to Custom (the engine's model). Persistence: PartyMemberState
+5 additive-V2 ints (default = CompanionAi.Default → old saves byte-identical). RESIDUALS (area-attack +
best-weapon rows) CLOSED in P51; the lone remaining one is a dark text panel, not control.frm art. KEY
DE-RISK: NO shippable golden configures a disposition,
and the default = the old behaviour, so all 16 combat + 81 prior encounter goldens BYTE-IDENTICAL (verified
by a clean check; the behaviour is PROVEN by fake-host tests, the P42/P43 pattern — the slice allies never
fight a configured disposition). Harness --companion-tactics <hex> <row> <count> (drives the real window-
cycle path; STATE-only — the effective enum names); golden companion-tactics. 18 CompanionAiTests + 2
fake-host turn tests (a wounded Coward ally FLEES, the default does not). 659 Formats tests, 82 encounter +
16 combat goldens green. P49 + P50 = the user's "#3" (the called-shot click dialog + the full AI-disposition
window), both shipped.

Phase 51 (DONE — "Full Tactics", closing the P50 ally area-attack + best-weapon residuals; the user's ask).
M0 grounding (workflow): the engine's _ai_pick_hit_mode area-attack thresholds (combat_ai.cc:2287 — ALWAYS /
SOMETIMES [1/secondary_freq] / BE_CAREFUL ≥50% / BE_SURE ≥85% / BE_ABSOLUTELY_SURE ≥95%) + the 8 best_weapon
options (_weapPrefOrderings, indexed [best_weapon+1]); KEY FINDING — Hexwaste's AiBestWeapon + IsBurstWeapon
are zero-dude-coupled (reusable), AiSwitchWeapon reads ai.BestWeapon (needs a value-overload for allies), and
the burst path is dude-coupled only at RollBurst's one attackerIsDude:true. M1 CompanionAi +2 enums: AreaAttack
{Never/Sometimes/BeCareful/BeSure/BeAbsolutelySure/Always} (Never=default = the pre-P51 single-only ally) +
WeaponPref {NoPref..Random, values MATCH the engine enum so the int feeds AiBestWeapon directly} + ShouldArea
Attack (the deterministic thresholds; SOMETIMES is the engine rng). M2 wiring: best-weapon — AiSwitchWeapon
refactored to a (actor, bestWeapon:int, minToHit:int, ...) overload (the AiPacket entry delegates) + called in
TryAllyAction's dry-gun branch with CompanionAi.WeaponPref (was a flat drop-to-fists; the P43 enemy switch now
reaches allies); area-attack — RollBurst parameterised by attackerIsDude (default true → the dude byte-identical)
+ a new TryAllyBurst (the dude's _compute_spray + cone, with the ally AP + attackerIsDude:false, an "ally-burst"
transcript) fired from the single-attack branch when IsBurstWeapon + AreaAttack != Never + the to-hit threshold.
M3 viewer: the tactics window grew 6→8 rows (Area attack + Best weapon), PartyMemberState +2 additive ints
(default 0 = CompanionAi.Default). KEY DE-RISK: AreaAttack.Never + WeaponPref.NoPref = the old behaviour
(TryAllyBurst skipped; AiSwitchWeapon inert with no carried backup), and NO slice golden configures either, so
all 16 combat + 82 prior encounter goldens BYTE-IDENTICAL (incl. the dude burst goldens — RollBurst's default
attackerIsDude:true is unchanged). DOCUMENTED: SOMETIMES uses a fixed 1/3 (allies have no ai.txt secondary_freq);
the area-attack to-hit uses the single ComputeToHit (not HIT_MODE secondary). Behaviour PROVEN by fake-host tests
(an AreaAttack.Always ally bursts / the default never bursts / a dry-gun ally switches to its carried club).
Harness --companion-tactics rows 5/6 + ProbeAllyWeaponSwitch; goldens companion-tactics (report +areaAttack/
bestWeapon) + companion-tactics-aw. 14 new tests. 673 Formats tests, 83 encounter + 16 combat goldens green.
Both P50 residuals are CLOSED — the combat-control window is now the full engine set bar control.frm art.

Phase 52 (DONE — "Dress the Chrome", a presentation-polish cluster grounded by a 4-reader + critic
workflow; every milestone is Draw-only / wall-time-only → both golden suites BYTE-IDENTICAL [the headless
harness pumps neither Draw nor the wall clock], proven by a clean check, not a re-record). M0 verified the
load-bearing facts FrmDump-first (CONTROL.frm 640x190 / LSGAME.frm 640x480 / LSGBOX 290x85 [the reader's
224x133 was WRONG — dump, don't trust] all present; PerkId.Empathy=22 cross-checked against the verified
Educated=18/Slayer=23/Sniper=24 in PerkTable, NOT the perk_defs.h line). M1 Empathy dialogue-reaction
colouring: the Empathy perk tints each option by the NPC reaction (game_dialog.cc gameDialogOptionOnMouse-
Enter:2118). Pure Formats.Int.DialogReaction.Classify (GAME_DIALOG_REACTION_GOOD/NEUTRAL/BAD 49/50/51 →
level, else Neutral); DialogSession.OptionReactions exposes the per-option reaction (already parsed); the
viewer's DrawConversationPanel reads it when DudePerkRank(Empathy)>0, mapping each level to a colour ported
from the engine's _colorTable indices (DOCUMENTED DIVERGENCE: the raw RGB555 those indices encode, no palette-
nearest remap — Hexwaste has no 8-bit dialogue palette). Inert by default (no Empathy → byte-identical).
M2 CONTROL.frm tactics-window art: DrawTactics renders the authentic party combat-control window when the FRM
loads (the Skilldex text-then-art pattern), text panel as the fallback. KEY FINDING: CONTROL.frm is a real
radio/checkbox layout (game_dialog.cc:3389 — TALK@593,41, disposition radios, USE BEST WEAPON/ARMOR check-
boxes), NOT Hexwaste's flat 8-row cycle model, so the readable cycle-rows are overlaid on the authentic chrome
with a subtle backing strip (DOCUMENTED STRUCTURAL DIVERGENCE — they don't bind the engine's individual
widgets). M3 LSGAME.frm + LSGBOX save/load-picker art: DrawSaveLoad renders the authentic 640x480 window (the
slot-list frame + info box are baked into LSGAME per loadsave.cc) with the 10 slot rows at the engine's
window-local (55,87) and the hovered slot's metadata in the info box at (396,254). M4 called-shot LIVE per-
bodypart to-hit %: new side-effect-free CombatEngine.PreviewToHit (mirrors RollAttack's ComputeToHit + the
halved-for-melee location penalty, clamped 0..95, no roll) feeds DrawAimDialog so each hit-location row shows
the live % vs the aimed-at hovered critter beside the static penalty. M5 message-log scrollback: pure
Formats.MonitorScrollback (display_monitor.cc ring window math, Capacity 100) replaces the old 5-line cap; the
green monitor scrolls via the engine's two invisible click-halves (display_monitor.cc:382/391 — top older /
bottom newer); a new message jumps to newest; the bar-hidden bottom-left fallback keeps the recent-5 view.
M6 screen-fade on map transitions: a wall-time black-quad fade-IN ramps over MapFadeSeconds (0.35) after each
LoadMap (DOCUMENTED DIVERGENCE: the engine's paletteFadeTo is a modal palette lerp + fade-OUT-then-in; Hexwaste
has no palette texture and a synchronous load, so a GPU quad fade-IN only; pumped + drawn in Draw, gated out
while screenshotting). GOLDEN-SAFE: all changes are Draw-only / wall-time-only / new-public-method-from-Draw,
so all 16 combat + 83 prior encounter goldens BYTE-IDENTICAL (verified by a clean check). Screenshot-verified
both art windows render. 17 new tests (DialogReactionTests + MonitorScrollbackTests + a PreviewToHit fake-host
test); 690 Formats tests, 83 encounter + 16 combat goldens green. EXCLUDED (critic, documented): inventory
INVBOX.frm (FID-48 filename unconfirmed in fo2ce source + a 2-slot-vs-3-slot-paperdoll mismatch), egg-mask
wall transparency (a large 8-bit blend-table kernel, the P4 no-shader decision stands), Snipe back-away
(combat-logic, wrong theme for a polish phase).

Phase 53 (DONE — "Lend a Voice", dialogue voiceover; grounded by a 5-reader + critic workflow). FAITHFUL
FORWARD-LOOKING INFRA — the engine's VO path is real + fully specified, but VERIFIED INERT on the slice:
Reader 5 + M0 confirmed against the REAL data that every slice dialogue line carries an EMPTY audio field
({id}{}{text}) — Metzger's 240 lines, Vic's 266, all 17 Den NPCs — AND the GOG game-data ships NO
sound\speech\ directory at all (only sound\music\). So it's DOUBLY inert (no field + no asset). This is
NOT karma-auto-award-style invention (P38) — VO has a real engine hook (scripts.cc _scr_get_msg_str_speech),
it's just empty on this content slice. KEY CORRECTION (the readers conflicted, synthesis + my grep settled
it): the PLAYED audio is FLAT sound\speech\<audio>.acm (game_sound.cc:1871, _sound_speech_path); the per-head
sound\speech\<head>\<audio>.lip path is the LIP-SYNC file (lips.cc) — OUT OF SCOPE (no talking head, no .lip
assets). M1 MessageFile audio retention: the parser READ the audio field (the 2nd of {id}{audio}{text}) then
DISCARDED it — now a parallel _audio dict + GetAudio(id) keeps it (non-empty only → the slice stores nothing);
purely additive, GetText unchanged. M2 pure Formats.Sound.SpeechName: Path(audio) => sound\speech\<audio>.acm
(lowercased, the SfxName pattern) + ShouldSpeak(isReply, headIsValid, audio, msgFlags) — the scripts.cc:2757
gate: REPLY-only (a3==1; game_dialog.cc:2239 reply vs :2282 option a3=0), head FID is a HEAD (else a3 forced 0,
:2746), audio non-empty, the 0x01 message flag clear (set → censor beep, not speech). M3 wiring: IVmExternals.
PlayDialogVoice(listId, msgId) default no-op, fired in IntVm on 0x811E gsay_reply + 0x8120 gsay_message ONLY
(the reply opcodes), and only for a message-list ref (msg.Tag==TypeInt, never a literal string) — NOT on
gsay_option (0x811F/0x8121); ScriptContext routes it to ScriptHost.DialogVoiceRequested (the LightLevel/Poison
callback pattern); ScriptHost.LookupAudio(listId, msgId) parallels LookupMessage (same cached message files →
MessageFile.GetAudio); the viewer's PlayDialogVoice looks it up + ShouldSpeak-gates + AudioManager.PlaySpeech
(a one-shot LOOSE read under <gameDir>\sound\speech\, like music — not the DAT; headIsValid assumed true since
Hexwaste renders no head). GOLDEN-SAFE: --no-audio suppresses playback, PlaySpeech only stderrs on miss, the
slice's empty audio short-circuits before any I/O, no RNG — all 16 combat + 84 prior encounter goldens
BYTE-IDENTICAL (incl. the vic-recruit dialog goldens that now fire PlayDialogVoice → empty → no-op); verified
by a clean check before recording. Harness --speech-probe <listId> <msgId> <forcedAudio|-> (PATHS/ids only,
never message text): forced "dcmetz01" → sound\speech\dcmetz01.acm wouldPlay=1 (the mechanism); the REAL
Metzger line (list 46, msg 100) → audio=(empty) wouldPlay=0 through the actual parser (real-data proof of the
slice's faithful silence). Golden speech-probe. 8 VoiceoverTests (SpeechName Path/gate truth-table +
MessageFile audio retention); 698 Formats tests, 84 encounter + 16 combat goldens green. RESIDUAL: lip-sync
(.lip + the talking head) stays out — no assets, no head model; VO lights up free when voiced content installs
loose sound\speech\*.acm.

MAINTENANCE (2026-06-21, "tend the god-object" — a quality pass ahead of adding more cities; grounded by a
4-reader + lead-engineer workflow): two changes, both proven SAFE.
(1) --smoke <map> coverage harness: a headless StartupAction (ViewerGame.Harness.cs) that censuses a map
(critters/containers/doors/scripted objects) + reports the FULL set of UNWIRED externals its scripts fire
(map_enter on load + a map_update pass) — the "silent quest gap" detector for a NEW city: run one command on
the new map, see what it needs that isn't wired. Deterministic + headless (no walk/UI/RNG), state-only output
(counts + external NAMES). 5 per-map smoke goldens (artemple/arcaves/denbus1/denbus2/kladwtwn) are the cross-
map regression net. Example: denbus2 fires use_obj_on_obj + tile_in_tile_rect; KLAMALL fires elevation.
(2) ViewerGame.cs god-object split: the 10,279-line file is now 4,734 (−54%), with the concern partials
ViewerGame.Harness.cs (1,642 — the 100+ --probe StartupAction dispatch, extracted from LoadContent as
RunStartupActions()), .Panels.cs (1,345 — char sheet/perk picker/Skilldex/Pip-Boy/automap/options/saveload
picker/aim dialog/item panels), .CombatHost.cs (852 — the ICombatHost impl + combat glue: weapon/ammo/
reload/corpse/heal/heartbeat/poison/sfx/animation+throw callbacks/reactions/destroy+combat procs),
.SaveLoad.cs (599 — the per-map delta snapshot/replay + JSON SaveGame/LoadGame), .Hud.cs (412 — iface.frm
bar + monitor + digit roll), .Rendering.cs (285 — floor/object sprite draw + outline/translucency),
.Worldmap.cs (283 — travel/transitions/encounter-engage/Outdoorsman), .Chemistry.cs (224 — drugs/addiction)
— plus the pre-existing .CompanionHub/.Party/.Tactics. KEY SAFETY INVARIANT: every move is a PURE same-class
method move (fields stay CENTRAL in ViewerGame.cs) → identical IL → goldens BYTE-IDENTICAL; the build is the
fast inner-loop gate (it catches a missing using / mis-cut method instantly), the full golden suite the final
gate (run after each batch). The harness extract was the one extract-method (a call-site change, not a pure
move) — its golden gate was mandatory. STILL IN THE CORE (deliberately not split — too welded / interleaved /
small): LoadContent (local functions close over fields), Update (input is distributed, not a method group),
LoadMap, the dialog panel, char-creation, the StartupAction record tree (nested types can't move), and the
kills/XP/party-level/skill-points/rest helpers (interleaved between the two CombatHost clusters). Method to
extract a concern: new ViewerGame.<Concern>.cs with `namespace Hexwaste.Viewer;
public sealed partial class ViewerGame { <methods> }` + the file's 9 usings (ImplicitUsings covers System.*);
cut a contiguous method block (a class member's close is the first `^    }$` — inner braces are deeper), build,
then golden-gate.

Phase 54 (DONE — "Vault City", the FIRST new location past the Arroyo→Klamath→Den slice; scoped by a
4-reader + lead-engineer workflow that found the 4 VC maps [vctyctyd Courtyard / vctydwtn Downtown /
vctycocl Council / vctyvlt Vault], the small external gap, and the worldmap route, and corrected several
reader errors [GVAR indices off by ~7; critter_p_proc already wired]). HEADLINE: a city is mostly CONTENT —
the data-driven engine made most of it free. M0 reachability = ZERO code: `--travel 4` already routes the
worldmap dot to the VC Courtyard via the existing ArriveAt (wmAreaFindFirstValidMap → first-ON entrance →
maps.txt lookup → LoadMap at the entrance tile), exactly like the Den. M1 wired two SHARED VM externals
(pure, no seam): day (0x8119, opGetDay — DayFromEpochDay mirroring the existing month 0x8118) + debug_msg
(0x8154 — a dev no-op, pop+discard); effect: arcaves reaches stubs=0, denbus1/2 drop debug_msg, VC Courtyard
4→2. M2 wired the 4 seam-requiring externals → ALL 4 VC maps stubs=0: elevation (0x80EC, via a new
ScriptHost.ElevationProvider — the viewer finds the object's elevation list; KLAMALL also reaches stubs=0),
critter_injure (0x8127 — OR/clear DAM_CRIP 0x7C into CombatResults, honoring DAM_PERFORM_REVERSE 0x800000,
reusing the P14 flag model), anim (0x810C — PlayActionOnce on a critter via a new AnimRequested seam; denbus1
NPCs now animate, Draw-only), obj_on_screen (0x8150 — return 1, no camera headless, DOCUMENTED DIVERGENCE).
M3 (proc census): the minimum needs NO new proc — VC dialogue runs via the already-wired talk_p_proc; the
deeper reactions (on-damage damage_p_proc [the documented golden-RISK — census the slice critters first],
use_p_proc terminals) are deferred. M4 (GVARs): verified — VC globals are all 0 on a fresh game (50/81/91/137
= TownRep/Citizenship/Quest/Enemy), so the dude enters a neutral non-citizen with the quest available; no
seeding code (gvar-seed golden extended). M5 (NPCs talk): the dialogue VM runs end-to-end on real VC content
(Lynette script 127 @17100 → 4 options, Greg 116 → 2, a Courtyard NPC → 2). The citizenship quest's MACHINERY
(dialogue VM + do_check + set_global_var + GVAR storage) is all wired + proven; completing the stat-test
(navigating Lynette's nodes to flip GVAR 81) is CONTENT navigation, the documented residual. GOLDEN-SAFE:
every external is additive (inert on Arroyo→Den except the shared anim/elevation, which are Draw/query-only);
all 16 combat goldens BYTE-IDENTICAL; the smoke goldens track the wiring (arcaves/KLAMALL → stubs=0; denbus1/2
drop their shared stubs; 4 new VC smoke goldens at stubs=0) + the new vc-dialogue golden. OUT (decisive line,
documented): Cassidy recruitment (script 571, NOT in party.txt — needs custom companion content), McClure's
computer-parts quest (chains to Gecko, out-of-slice), any 2nd quest. 698 tests, 16 combat + 99 encounter
goldens green. Vault City is reachable + walkable + fully wired + talking — the first new location is in.

Phase 55 (DONE — "Gecko", the SECOND new location; scoped by a 4-reader + lead-engineer workflow that
ground-truthed + corrected MANY reader hallucinations [the GVAR indices, the reactor script numbers, even
its own "gecksetl stubs=1" claim — VERIFY everything]). PROVES the recipe is cheap to repeat + that each
city makes the next cheaper. M0 reachability = ZERO code (`--travel 5`, Area 05 start_state=On → the
Settlement gecksetl, via the existing ArriveAt). M1 externals = MOOT, stubs=0 across all 4 maps (gecksetl/
geckpwpl/geckjunk/gecktunl) — Vault City's P54 wiring already covered every external Gecko fires (the
synthesis's "gecksetl stubs=1[debug_msg]" was a hallucination — verified stubs=0). M2 = the ONE real change:
scenery use_p_proc. InteractWith fired use_p_proc for containers+doors but a scripted SCENERY object with no
exit-grid Destination fell through to the no-op 'picked:' line — so the reactor TERMINAL (GsTerm 515 @18677),
reactor (gsReactr 529 @12666), valve (GSValve 846 @16264) were inert. Added the scenery use_p_proc branch
(the engine's _obj_use dispatches SCRIPT_PROC_USE for any usable object) + a state-only 'scenery-use@<hex>:
handled' line. BYTE-IDENTICAL: NO golden ever clicks a scenery object (verified by a clean check) → the new
path is unreachable by every fixture. DOCUMENTED LIVE-PLAY GAIN (not a regression): existing-slice scenery
with use_p_proc — denbus2 graves, NR slot machines, wall switches — is now usable (graves ARE diggable in
FO2). M3 GVARs verified all 0 (no seeding). M4: the reactor-quest dialogue runs (Gordon 2 / a plant NPC 4
options); the reactor terminal is usable; the OPTIMIZE completion + the VC-McClure bridge (GVAR
VAULT_GECKO_PLANT=82) are content navigation (the machinery is wired + proven; even with GECKO_ASSIGNED set
the terminal needs the deeper Skeeter/Science path to flip 93/82) — the documented residual, like VC's
citizenship. M5: Lenny (GCLenny 138 @16701, pid 0x100006B) VERIFIED a real data\party.txt companion
(member=1, level_minimum=10; the radscorpion 0x1000005 the member=0 control) → recruitment is the proven
Vic-pattern party_add machinery (NOT custom content like VC's Cassidy); the recruit drive is the residual.
TOOLING: new --party-probe <pid> (reusable companion check) + MapDump scripted-scenery listing (hex+script,
found the reactor terminal). All combat goldens BYTE-IDENTICAL; new goldens gecko-reactor-use/dialogue/lenny
+ 4 smoke. 698 tests, 16 combat + 102 encounter goldens green. KEY LESSON RE-CONFIRMED: each city pre-clears
the next (shared externals accumulate) — Gecko needed ZERO external wiring, just one proc (scenery use) +
the quest/companion content sitting on already-wired machinery.

Phase 56 (DONE — "Modoc", the THIRD new location). Confirms the recipe holds and that the only remaining
per-city cost is the genuinely-new externals each map fires (Modoc fired TWO Vault-City/Gecko hadn't).
M0 reachability = ZERO code (city.txt [Area 03] Modoc, start_state=On, entrance_0 "Modoc Main Street" ->
modmain via the proven ArriveAt — same path as VC/Gecko). M1 the two PURE-QUERY externals: tile_in_tile_rect
(0x80CF — a verbatim HexGrid.TileInTileRect port; the engine's ASYMMETRIC corners c1=(minX,maxY)/c4=(maxX,
minY), args c2/c3 popped-but-IGNORED — interpreter_extra.cc:1447) + critter_inven_obj (0x8106 — type 3 =
Inventory.Count, else the handle of the FlagWorn/RightHand/LeftHand item). Wiring them CHANGES modinn's
map_enter branch (with real values the branch that called kill_critter_type is no longer taken), so smoke-
denbus1/denbus2 re-recorded (they fire tile_in_tile_rect too); combat BYTE-IDENTICAL. M2 the two MUTATING
externals: set_map_start (0x80A8 — repositions the dude/camera to 200*y+x / elevation / rotation; no-op
headless, no dude in the --smoke census) + kill_critter_type (0x80EE — KillCrittersByType ported from
opKillCritterType: deathFrame 0 = silent remove, nonzero = corpse via ConvertToCorpse; count>200 guard; dude
excluded). The engine's _isLoadingGame() guard (:2384) wired FAITHFULLY — a _isLoadingGame flag wraps
LoadGame's LoadMap call (the only window restored scripts replay map_enter/map_update) so a save-restore
never re-destroys critters. INERT on the slice: after M1's branch shift NO map fires kill_critter_type, and
set_map_start is headless-inert — so all 4 Modoc maps census stubs=0 (modinn drops its last two stubs),
combat + encounter BYTE-IDENTICAL. M3 proc census (tools/ProcAnalyze on all 4 maps): Modoc introduces NO new
proc requirement — the whole quest spine (talk/use_p/use_obj_on/use_skill_on/critter_p/map_enter/map_update/
timed_event/damage/combat) is ALREADY wired (CORRECTS the pre-verification note: use_obj_on_p_proc + timed_
event_p_proc are BOTH wired, not OUT). The only unwired procs Modoc defines are map_exit_p_proc + push_p_proc
— PRE-EXISTING engine-wide residuals (denbus2, a shipped Den map, already defines them; never fired across the
whole slice), not quest-blocking. M4 GVARs: all 6 Modoc globals are 0 on a fresh game (verified via --create
+ --get-global at the real enum indices — TOWN_REP 52, JONNY_STATE 114, JONNY_TILE 115, TOOL_FLAG 118,
ROSE_FLAG 123, JONNY_HOME 129; FIND_VIC 619 cross-checked against the known P32 seed). No seeding code (matches
VC/Gecko). M5 quest drive: the dialogue VM runs end-to-end — Balthas (script 96 @12323, the "Jonny in the
Well" quest-giver) offers 3 options, Grisha (100 @28710) 2; the well (miWell 572 @17520) fires its scripted
use_p_proc (scenery-use, the quest mechanic, P55-M2). The quest DRIVE (navigating Jonny's rescue) is content
— the documented residual; the machinery is wired. 6 new goldens (smoke-modmain/modinn/modwell/modshit at
stubs=0 + modoc-dialogue + modoc-well). 694 tests, 16 combat + 108 encounter goldens green. KEY LESSON: a new
city's cost is exactly its genuinely-new externals (Modoc: 4) + content; everything else is free reuse.

Phase 57 (DONE — "Broken Hills", the FOURTH new location; scoped by a 4-reader + lead-engineer workflow
[broken-hills-scope, wf_c30970a0] that ground-truthed the 2 externals verbatim from source, recount-confirmed
the GVAR indices, and caught a scouted Marcus-identity error). HEADLINE: the TOWN proper (BROKEN1/BROKEN2) was
ALREADY stubs=0 — ZERO new town externals (cheaper than Modoc's 4), the data-driven engine handling it for
free. The only genuinely-new code is TWO externals, both on the random-encounter SUB-maps (bhrnddst/bhrndmtn).
M0 reachability = ZERO code (city.txt [Area 06], start_state=On, entrance_0 "Broken Hills 1" -> BROKEN1 via
the proven ArriveAt; entrance_1 -> BROKEN2 is a STATIC exit grid in the .map trailer, the P2-M5 ApplyTransition
path, no code). M1 the two externals, ported verbatim: set_exit_grids (0x80E6, opSetExitGrids:2180 — pops
rotation[DISCARDED by the engine]/tile/destElev/map/elevation; rewrites every exit-grid-pid [0x5000010..17]
object on the SOURCE elevation's Destination to map/tile/destElev, preserving the parsed rotation; bhrnddst
stubs=1->0) + wield_obj_critter (0x80DA -> opWieldItem:1689, the SAME handler as wield_obj — pops item THEN
critter; the critter equips it: weapon -> right hand via the proven P43 EquipWeapon, armor -> worn + dude-only
AC bonus [NPC-armor AC is forward-looking infra — the slice wields weapons only]; bhrndmtn arms its 4 spawned
critters, stubs=1->0). The _isLoadingGame guard is N/A here (neither external destroys). INERT on every
shippable map: no golden loads a BH map (set_exit_grids fires only on bhrnddst, wield only on bhrndmtn) -> all
16 combat + prior encounter goldens BYTE-IDENTICAL (verified by a clean check BEFORE recording). M2 proc census
(tools/ProcAnalyze BROKEN1/2 = 14 proc families, the SAME as Modoc — quest spine all wired; map_exit_p_proc +
push_p_proc the pre-existing engine-wide residuals) + GVARs (all 6 BH globals 0 on a fresh game: TOWN_REP 54,
FRAUD 147, ENEMY 309, READ_FRANCIS_NOTE 524, MARCUS_DEAD 526, CARAVAN 562 — no seeding, matches VC/Gecko/Modoc)
+ dialogue drive (Marcus the mutant sheriff @18284 script 599 = 7 options; a townsperson @10685 = 5; Marcus IS
a real data\party.txt member [member=1, levelMin=12] so recruitment is the proven Vic[P10]/Lenny[P55] party_add
machinery, NOT custom content like VC's Cassidy). The quest DRIVE (uranium fraud / Francis / the Marcus recruit)
is content — the documented residual; the machinery (dialogue VM + GVARs + party_add) is wired. 5 new goldens
(smoke-broken1/broken2/bhrnddst/bhrndmtn at stubs=0 + bh-dialogue). 698 tests, 16 combat + 114 encounter goldens
green. GOTCHA: the .map file is bhrndmtn (maps.txt has a typo "bhrndmnt"); --smoke loads by filename. KEY LESSON
RE-CONFIRMED: a city's cost is its genuinely-new externals + content; here the town needed ZERO and only the
random sub-maps fired 2 — verify every load-bearing fact (Marcus's hex, the GVAR enum indices, the area number)
against live data, the scout's "Marcus = @11689 script 588" was a generic-mutant misread the workflow corrected.

Phase 58 (DONE — "New Reno", the FIFTH new location + the BIGGEST yet: 11 maps, the mob-family city;
scoped by a 4-reader + lead-engineer workflow [new-reno-scope, wf_dc886fc7] that read all 5 engine handlers
verbatim, recount-confirmed the GVAR ordinals + seed values, and confirmed Myron's party.txt section). M0
reachability = ZERO code (city.txt [Area 07], start_state=On, entrance_0 "New Reno 1" tile 25105 -> Newr1 via
ArriveAt; inter-map movement [Newr1<->2<->3<->4 + interiors] is STATIC exit grids, the P2-M5 ApplyTransition
path, no code). M1 wired the FIVE genuinely-new externals — the MOST of any city, all ported verbatim, all
INERT on the slice (no golden loads any NR map -> combat + prior encounter goldens BYTE-IDENTICAL): obj_art_fid
(0x8149, opGetObjectFid:4643 — query, pops object pushes its Fid; the arity table was ALREADY (1,true), the
stub pushed a placeholder 0, so this is a VALUE-fix on Newr2 not a stack-desync — do NOT touch ExternalArity),
critter_is_fleeing (0x8151:4740 — pushes Maneuver & 0x04 [CRITTER_MANUEVER_FLEEING]) + critter_set_flee_state
(0x8152:4756 — pops fleeing THEN critter, sets/clears the bit in place; Newr4/Newrst), mark_area_known
(0x80B2:737 — pops markType/areaId/mode, reveals a worldmap area via WorldFog.MarkRadiusVisited; INERT — every
NR area starts On, so already discovered; mode-1 map-mark + INVISIBLE-hide are documented no-ops; Newrcs/
Newrst/Newrgo), game_time_advance (0x80FC:2761 — pops ticks [1:1, TicksPerDay==864000], bumps _clock.Ticks
then runs ProcessPoison/Drugs/Withdrawals = the engine's queueProcessEvents catch-up, NOT just a clock bump;
NewRvb). No new proc (Newr1=16 / Newr2=15 families, all wired; map_exit/push the pre-existing residuals). All
11 NR maps now census stubs=0. M2 GVARs (KEY CORRECTION — the "all 0 on a fresh game" premise is FALSE for NR:
the FOUR crime-family counters SALVATORE 134 / BISHOP 135 / MORDINO 136 / WRIGHT 216 seed to 100 in vault13.gam,
already written by SeedGlobalVars [P32], they count DOWN as you wrong a family; TOWN_REP_NR 55 / MADE_MAN 230 /
PRIZEFIGHTER 231 / PORN_STAR 232 / MYRON 284 = 0) — gvar-seed golden EXTENDED to assert the family counters.
Dialogue VM runs (Newr1 script 452 @11280 = 4 options, 326 @12114 = 2). Myron (the Mordino chemist, script 436
@19327 on Newrst, pid 0x10000A0) IS a real data\party.txt member (member=1, levelMin=6) so recruitment is the
proven Vic/Lenny/Marcus party_add machinery, NOT custom content; his quest-gated recruit DRIVE (Mordino Jet-
lab) is content — the residual. M3 docs. 698 tests, 16 combat + 120 encounter goldens green (6 smoke subset +
nr-dialogue + nr-myron + the extended gvar-seed). PATCH NOTE: Newr2/Newrst (+ their .int) are in patch000.dat
— the existing VFS already resolves to the patch, no work. GOTCHA: NR is the first city where a fresh-game GVAR
is NON-zero (the family counters) — always check the actual vault13.gam seed, don't assume 0.

Phase 59 (DONE — "NCR", the SIXTH new location + the CHEAPEST: ZERO new engine code). The now-large wired
set (VC+Gecko+Modoc+BH+NR) already covers every external NCR's scripts fire, so all 5 maps (NCR1-4, NCRENT)
census stubs=0 with NO wiring. (P66 CORRECTION: this entry originally listed a 6th map "ENCRCTR" — that was a
false grep-match on "eNCRctr"; ENCRCTR is the ENCLAVE REACTOR [maps.txt Map 133], correctly grouped with the
P66 Oil Rig. NCR proper is 5 maps. The smoke-encrctr golden was always valid [stubs=0]; only its label moved.)
M0 reachability = ZERO code ([Area 10], start_state=On, entrance_0
"NCR: Bazaar" via ArriveAt; inter-map = static exit grids). NO new external. KEY FINDING — the one apparent
"new proc" is a NON-issue: NCR1 (script 447 SCCop) DEFINES combat_is_over_p_proc, but SCRIPT_PROC_COMBAT_IS_
OVER (=27) + _IS_STARTING (=26) are VESTIGIAL enum slots the engine NEVER scriptExecProc's ANYWHERE (scripts.h
:76-77 are the ONLY refs in the whole fo2ce source — verified by grepping every scriptExecProc call); so
Hexwaste faithfully NOT firing them is CORRECT, not a gap — wiring it would DIVERGE from the engine (the prime-
directive trap: a defined-but-engine-dead proc is not a residual to fill). NCR GVARs all 0 on a fresh game
(TOWN_REP_NCR 57 + quest flags 168/170/172/196 — no P58-style non-zero seed). No party.txt companion (no
classic recruit in NCR). The dialogue VM runs (NCR1 script 582 @14725 = 5 options, 466 @18720 = 4). Quest drive
(Tandi / the Vault-15 squatters / brahmin-rustling) = content residual; the machinery is wired. 7 new goldens
(6 smoke at stubs=0 + ncr-dialogue). NO engine code -> all 16 combat + prior encounter goldens BYTE-IDENTICAL.
PROCESS NOTE: scoped FULLY INLINE (zero externals to port -> a grounding workflow had nothing to grind on;
every load-bearing fact [zero externals, the engine-dead proc, GVARs 0, no companion] verified directly against
live data + engine source — the ultracode "unless already verified" carve-out). 698 tests, 16 combat + 127
encounter goldens green. KEY LESSON: as the wired set grows, new cities trend toward zero-code content reuse;
and a map DEFINING a proc isn't proof the engine FIRES it (check scriptExecProc, not just the proc table).

Phase 60 (DONE — "San Francisco", the SEVENTH new location + the SECOND straight ZERO-engine-code city). The
wired set (VC+Gecko+Modoc+BH+NR+NCR) now covers SF outright: all 7 maps (SFChina/SFChina2 = the Shi Chinatown,
SFDock, SFElronb = the Hubologist/Elron base, SFTanker = the PMV Valdez, + 2 shuttle maps) census stubs=0 with
NO wiring. M0 reachability = ZERO code ([Area 14], start_state=On, entrance_0 "San Fran China" via ArriveAt;
inter-map = static exit grids). NO new external. NO new proc (SFChina=15 / SFTanker=14 wired families; no
engine-dead-proc trap this time — the NCR combat_is_over lesson held, nothing unusual defined). NO seeding:
TOWN_REP_SF 61 + the SAN_FRAN_* quest flags (361/363/365/366/444) all 0 on a fresh game. No party.txt companion
(no classic recruit in SF). The dialogue VM runs (SFChina script 813 @20504 = 5 options, 819 @20703 = 5; the
Shi STREET NPCs 743/746 are 0-option guards — the named Shi/Hubologist talkers are the 813/819 scripts). Quest
drive (the Shi/Hubologist faction war, the tanker fuel/nav for the endgame) = content residual; machinery wired.
8 new goldens (7 smoke at stubs=0 + sf-dialogue). NO engine code -> all 16 combat + prior encounter goldens
BYTE-IDENTICAL. Scoped FULLY INLINE (the NCR steady-state: zero externals to port -> no workflow; every fact
verified directly — smoke x7, the proc census, the GVAR seed check, --party-probe). 698 tests, 16 combat + 135
encounter goldens green. STEADY-STATE CONFIRMED: late cities are now pure content reuse; the remaining original-
game towns (Redding, Vault 15, Sierra/Navarro, the deeper Klamath/Den maps) should mostly be zero-code —
--smoke first; only spin up a grounding workflow if a map shows a non-zero stub count.

Phase 61 (DONE — "Redding", the EIGHTH new location + the THIRD straight ZERO-engine-code city). All 6 maps
(REDDOWN downtown, REDDTUN tunnels, REDMENT/REDMTUN the Kokoweef mine, REDWAME/redwan1 the Wanamingo mine)
census stubs=0 with NO wiring. M0 reachability = ZERO code ([Area 13], start_state=On, entrance_0 "Redding
Downtown" via ArriveAt; inter-map = static exit grids). NO new external, NO new proc (REDDOWN/REDMENT = 13
wired families, no trap). THE P58 NON-ZERO-SEED TRAP STRUCK AGAIN (and the playbook caught it): GVAR_TOTAL_
WANAMINGOS (461) seeds to 20 on a fresh game — the Wanamingo Mine's initial creature count (you exterminate
them for the quest; already written by SeedGlobalVars), NOT 0; the other Redding GVARs (TOWN_REP 56 / QUEST_
REDDING_PROBLEM 94 / MAYOR 334 / SHERIFF 387 / WANAMINGO_OCCUPADO 389) are 0. gvar-seed golden EXTENDED to
assert 461=20. No party.txt companion. The dialogue VM runs (REDDOWN script 809 @17063 = 5 options, 681 @15312
= 4). Quest drive (the mine-ownership war / the Wanamingo extermination / Jet) = content residual; machinery
wired. 7 new goldens (6 smoke at stubs=0 + redding-dialogue; gvar-seed extended). NO engine code -> all 16
combat + prior encounter goldens BYTE-IDENTICAL. Scoped fully inline (the steady-state ~6-command loop). 698
tests, 16 combat + 142 encounter goldens green. LESSON RE-CONFIRMED: the non-zero GVAR seed is NOT a New-Reno
one-off — Redding's wanamingo count is the same pattern (a creature/quest tally seeded from vault13.gam); ALWAYS
run the fresh-game --get-global check, never assume 0.

Phase 62 (DONE — "Vault 15", the NINTH new location + the FOURTH straight ZERO-engine-code city — but the
first zero-code city that HAS a companion). All 4 maps (VAULT15 the squatter camp / "The Squat A", V15ENT the
entrance, V15SENT the east entrance, V15_ORIG the deep original-FO1-vault levels) census stubs=0 with NO
wiring. M0 reachability = ZERO code ([Area 09], start_state=On, entrance_0 "The Squat A" via ArriveAt; inter-
map = static exit grids). NO new external, NO new proc (VAULT15=15 / V15ENT=14 wired families, no trap). All
Vault 15 GVARs 0 on a fresh game (TOWN_REP_VAULT_15 294 / V15_SEED_STATUS 293 / V15_DARION_DEAD 172 / V15_KILL_
DARION 474 — no non-zero seed this time; the seed trap is real but not universal). COMPANION (the find): pid
0x10000A2 (script 556 @12684 on VAULT15) IS data\party.txt [Party Member 7] pMDoc (member=1, level_minimum=0)
-> recruitment is the proven Vic/Lenny/Marcus/Myron party_add machinery (the radscorpion 0x1000005 = the
member=0 control). So Vault 15 joins Gecko(Lenny)/NR(Myron)/BH(Marcus) as a companion town — NCR/SF had none.
The dialogue VM runs (the Doc @12684 = 2 options, script 583 @14084 = 2). Quest drive (Darion's raiders / the
NCR squatter deal / the Doc recruit) = content residual; the machinery is wired. 5 new goldens (4 smoke at
stubs=0 + v15-dialogue [dialogue + the Doc party-probe]). NO engine code -> all 16 combat + prior encounter
goldens BYTE-IDENTICAL. Scoped fully inline (the steady-state ~6-command loop). 698 tests, 16 combat + 147
encounter goldens green. Running tally: P54 VC / P55 Gecko(0) / P56 Modoc(4) / P57 BH(2) / P58 NR(5) / P59
NCR(0) / P60 SF(0) / P61 Redding(0) / P62 Vault 15(0).

Phase 63 (DONE — "Sierra Army Depot", the TENTH new location + the first in the endgame batch to need
engine code [breaking the 4-city zero-code streak]). 3 maps (depolv1 the Battlefield, depolva Levels 1-3,
depolvb Level 4); [Area 08], start_state=Off — a DISCOVERED-VIA-QUEST location (not worldmap-visible from game
start; the maps load/walk directly, worldmap discovery is via mark_area_known [P58] — content-gated, a
documented divergence, like the other Off sub-areas). depolva fired TWO new externals (scoped inline — the 2
were simple enough that a grounding workflow would've been ceremonial; both handlers read VERBATIM, the
Hexwaste seams [reverse-anim + tile-scan] already existed, arity already correct, no golden loads a depolv
map): tile_contains_obj_pid (0x80BB, opTileContainsObjectWithPid:1057 — a QUERY, pops pid/elevation/tile,
pushes 1 if any object at (tile,elev) has the pid; scans _solidObjects+_flatObjects) + animate_stand_reverse_
obj (0x80CD, opAnimateStandReverse:1363 — pops object/self, !combat-gated, plays ANIM_STAND[=0]; DOCUMENTED
SIMPLIFICATION: the engine plays it REVERSED [a lie/sit-down], we play forward via the proven P54 Anim path —
cosmetic, Draw-only, never in a golden). GOTCHA (the de-risk caught it): tile_contains_obj_pid is ALSO fired
by artemple's map_enter, so wiring it dropped artemple's stub -> smoke-artemple re-recorded (stubs=1->0, the
Modoc-M1 pattern); NO behavior golden changed (gvar-seed/script-light byte-identical — the query's return value
doesn't gate a golden-visible branch), and all 16 combat goldens byte-identical. All 3 Sierra maps now stubs=0.
NO new proc. Sierra GVARs all 0 on a fresh game (TOWN_REP 53 / contamination-timer 149 / 150/152/153/157 — no
seed trap). It's a robot/combat DUNGEON (no dialogue talkers — Skynet's dialogue is content-gated behind
assembling the body), but SKYNET (pMCyberdog, pid 0x1000088) IS a data\party.txt member (member=1, levelMin=9)
-> the Sierra companion (probed via sierra-skynet golden; not a static map critter — assembled at runtime, the
party-probe confirms membership regardless). 5 goldens (3 smoke at stubs=0 + sierra-skynet + the smoke-artemple
re-record). 694 tests, 16 combat + 151 encounter goldens green. Running tally: ... P62 Vault 15(0) / P63 Sierra(2).

Phase 64 (DONE — "Military Base / Mariposa", the ELEVENTH new location — back to ZERO-engine-code after
Sierra's 2). 3 maps (mbclose the caved-in entrance, mbase12 Levels 1-2, mbase34 Levels 3-4) census stubs=0 with
NO wiring. M0 reachability = ZERO code ([Area 12], start_state=On, entrance_0 "Military Base Entrance" via
ArriveAt; maps resolve via maps.txt Map 049-051 = mbase12/mbase34/mbclose; inter-map = static exit grids). NO
new external, NO new proc (mbase12/mbase34 = 14 wired families, no trap). It's a pure super-mutant COMBAT
DUNGEON: NO dialogue talkers (the mutants are combat-only at IN 5 — verified across all 3 maps), NO party.txt
companion, and the single GVAR (MILITARY_BASE_FLAGS 215) is 0 on a fresh game (no seed trap). So the deliverable
is reachable + walkable + fully wired — smoke-only goldens (a dungeon, like Sierra's combat half but with no
companion). Quest drive (fight through to the FEV vats / destroy the base / Melchior) = content residual. 3
smoke goldens at stubs=0. NO engine code -> all 16 combat + prior encounter goldens BYTE-IDENTICAL. Scoped fully
inline. 694 tests, 16 combat + 154 encounter goldens green. Tally: ... P63 Sierra(2) / P64 Military Base(0).

Phase 65 (DONE — "Navarro", the TWELFTH new location — the Enclave coastal base; ZERO-engine-code [even the
Enclave endgame base is covered by the wired set — the "external-risk" prediction over-estimated]). 1 big map
(NAVARRO; patch000.dat override -> the VFS resolves the patch). M0 reachability: [Area 15], start_state=Off
(discovered-via-quest like Sierra; the map loads/walks directly, worldmap discovery via mark_area_known [P58],
content-gated). NO new external (stubs=0), NO new proc (16 wired families incl. the map_exit/push residuals).
Enclave GVARs all 0 on a fresh game (TOWN_REP_ENCLAVE 62 / ENCLAVE_TIMER 434 / 431/432/441 — no seed trap). The
dialogue VM runs (script 721 @25900 = 2 options; most NPCs are hostile Enclave soldiers, silent at IN 5).
COMPANION (the nuance): K-9 (the cyberdog) is content-gated — the pMCyberdog body (pid 0x1000088, the SAME
party.txt member=1 that Skynet can use; the cyberdog body is SHARED) is NOT a static NAVARRO critter (the in-
world K-9 swaps to the party body on recruit), so the machinery is wired but the recruit is content. Quest
drive (steal the vertibird plans / the FEV sample / the Enclave-armor disguise) = content residual. 2 goldens
(smoke-navarro at stubs=0 + navarro-dialogue). NO engine code -> all 16 combat + prior encounter goldens BYTE-
IDENTICAL. Scoped fully inline. 694 tests, 16 combat + 156 encounter goldens green. Tally: ... P64 Military
Base(0) / P65 Navarro(0). KEY FINDING: the Enclave base needs NO engine code — the only remaining run item is
the Oil Rig (Area 16 Enclave), the final endgame map.

Phase 66 (DONE — "Enclave Oil Rig", the THIRTEENTH new location + the FINAL endgame map; ZERO-engine-code —
the "Enclave = external-risk" prediction was wrong, the wired set covers the WHOLE original-game map set). 7
maps (encdock the dock arrival, encdet Detention, encgd Guard Barracks, encpres Presidential [Richardson],
encrctr the Reactor, enctrp the Trap Room, encfite the End Fight [Frank Horrigan]; ENCPRES has a patch000
override). M0 reachability: [Area 16] Enclave, start_state=Off (endgame; maps load/walk directly, worldmap
discovery via mark_area_known [P58], content-gated). NO new external (all 7 stubs=0), NO new proc (encfite=14 /
encpres=13 wired families, no engine-dead-proc trap). Enclave/Oil-Rig GVARs all 0 on a fresh game (ENCLAVE_
ALARM 433 / REACTOR 435 / COMPUTER 440 / MARTIN 441 — no seed trap). No party.txt companion (the endgame). The
dialogue VM runs on the Presidential level (script @12320 = 3 options, @13684 = 3 — President Richardson + the
Enclave computer/advisor; the detention/soldier NPCs are silent at IN 5). RIDER (P59 correction): smoke-encrctr
was a false grep-match mis-grouped under NCR — ENCRCTR is the Enclave Reactor, now correctly here; the golden
was always valid (stubs=0), only its label moved. Quest drive (the FEV/self-destruct / Horrigan / Richardson)
= content residual; the machinery is wired. 8 goldens (6 new smoke at stubs=0 + the moved smoke-encrctr +
oilrig-dialogue). NO engine code -> all 16 combat + prior encounter goldens BYTE-IDENTICAL. Scoped fully inline.
694 tests, 16 combat + 163 encounter goldens green. Tally: ... P65 Navarro(0) / P66 Oil Rig(0). MILESTONE: with
this run (P63-66 = Sierra/Military Base/Navarro/Oil Rig) the ENTIRE original-game map set — every town, dungeon,
special site, and the endgame — now LOADS, WALKS, transitions, and runs its scripts (every external wired); the
remaining gaps are all CONTENT (quest navigation, content-gated recruits), not engine. The 13-city run
(P54-P66) needed engine code on only 4 cities (Modoc 4 / BH 2 / NR 5 / Sierra 2 = 13 externals total); the
other 9 were pure content reuse.

Phase 67 (DONE — "Paperdoll inventory window", the authentic INVBOX.frm): the inventory panel was a text-
list with two equip-slot BOXES beside it (P47); now it renders the real art\intrface\INVBOX.frm (interface FID
48, 499x377 — the P52 "FID-48 unconfirmed" blocker RESOLVED via intrface.lst[48]=INVBOX.FRM) centred, with the
dude PAPERDOLL in the body view (local 176,37,60,100 — the live critter art, reflects worn armor) and the two
equip slots positioned on the window's authentic slot art (armor 154,183; our single weapon -> the right-hand
slot 245,286; the left-hand slot 154,286 stays decorative, the single-weapon model). The item list moves into
the window's left column. KEY GOLDEN-SAFETY: every window-relative position is gated on _invBox being LOADED,
which only happens on a live Draw — headless (the goldens) _invBox is null so InvBoxOrigin()/InventoryPanelX()
fall back to the ORIGINAL x=40 list + x=420 boxes, exactly what the --panel-click / --drag-equip / hud-click
goldens exercise (those use CurrentItemPanels' X + logical slot args, never screen coords) -> all 16 combat +
156 encounter goldens BYTE-IDENTICAL (verified by a clean check). The INVBOX render reuses InterfaceBar.LoadFrm
(the PERKWIN/OPBASE/SKLDXBOX lazy-??= pattern); the in-window list uses ItemRowRect (render == hit-test) in the
narrow left column. DOCUMENTED DIVERGENCES: the readable text rows are wider than the engine's icon column (so a
long name can extend toward the paperdoll — Hexwaste is a text-list inventory, not an icon grid); no left-hand/
dual-wield slot (the P47 single-weapon model). Screenshot-verified the INVBOX window renders over the real art.
New harness --show-inventory (opens the INVBOX for a screenshot, the --show-create pattern; additive, no golden).

Phase 68 (DONE — "AI-packet enemy distance"): the ai.txt distance= field was PARSED into AiPacket.Distance
since P9 but never CONSUMED for enemies (only the P50/P51 COMPANION distance knob was). Now CombatEngine.Try
EnemyAction honours it: new pure CompanionAi.AiDistanceMode.Parse (the gDistanceModeKeys map: stay_close/charge/
snipe/on_your_own/stay; absent/"random"/unknown -> OnYourOwn, the engine's pre-parse -1 "no preference"). Two
behaviours wired (the two that fit Hexwaste's attack-first per-turn flow): DISTANCE_STAY -> hold position (never
close the gap; attacks only if already in range — combat_ai.cc:1223/2361 _ai_move_away/_ai_move_steps_closer
return -1 for STAY), and DISTANCE_SNIPE -> a ranged sniper closed to melee range (<=2) steps AWAY one hex to
reopen range instead of firing point-blank (combat_ai.cc:3001, simplified: a one-step kite without the combat-
rating gate). DOCUMENTED RESIDUALS (same level as the existing ally-distance code): CHARGE/STAY_CLOSE/ON_YOUR_
OWN all map to the current approach (CHARGE's ignore-fleeing + STAY_CLOSE's leader-regroup don't fit the enemy-
vs-dude attack-first model); the SNIPE kite is one-step + ungated (no _combatai_rating comparison, no multi-step
retreat to range ~10). KEY DE-RISK (verified BYTE-IDENTICAL on all 16 combat + encounter goldens): the golden
enemies — arcaves Scorpion (pkt8) and denbus2 Peasants (pkt14) — carry NO distance field in ai.txt (the engine
default -1), so AiDistanceMode.Parse("") -> OnYourOwn -> they approach exactly as before; only an enemy whose
packet sets distance=stay/snipe diverges, and none is in a golden. 11 new tests (3 fake-host CombatEngine: Stay
holds / absent-distance approaches [the control] / Snipe kites-when-adjacent; an 8-case AiDistanceMode.Parse
theory). 705 Formats tests.

Phase 69 (DONE — the "S-tier trio" from a gap-reaudit workflow; it turned out a DUO + one verified non-issue,
the audit synthesizer having hallucinated 3 facts the inline verification caught). M1 Awareness perk: Hexwaste
showed a critter's HP UNCONDITIONALLY on examine (over-generous vs the engine, which gates it behind PERK_
AWARENESS, proto_instance.cc:294). Now examine reveals "HP: N/M, AC" + the wielded weapon (with ammo for guns)
ONLY with the perk; without it, just name + description like the engine — so the perk is a real choice.
PERK_AWARENESS = index 0 (the synth's "27" was a HALLUCINATION; verified against perk_defs.h/PerkId). Gated via
the existing DudePerkRank seam, inert at rank 0. No golden examines a critter via the player Examine() path (the
--examine harness is a separate diagnostic dump), so BYTE-IDENTICAL; new --awareness-probe (state-only: hex +
perk rank + hpLine/weaponLine booleans, never the copyrighted name) + the awareness-perk golden (Metzger denbus2
@15278: 0/0 without the perk, 1/1 after --perk-probe 0 6 grants it). M2 dude reaction sprites: removed the three
OnTargetHit/OnTargetDodge/OnGetUp early-returns that skipped the dude (the P34-M6 spillover) — the dude now
flinches/dodges/falls/stands like every NPC. No blocker: ResolveSprite already uses the animator state when the
walker isn't moving (the dude is stationary on an attack resolve) and the dude already falls on death via
PlayFall. Anim-only (no transcript/RNG) -> BYTE-IDENTICAL. M3 run-vs-walk speed: VERIFIED NON-ISSUE (the synth's
3rd hallucination) — running ALREADY moves faster. The DudeController is FRM-driven (msPerFrame=1000/fps); the
dude runs by default (run-probe default=19, artExists=1) and CurrentFid resolves to the run FRM, and FrmDump
confirms HMJMPSAT [run] = 20 fps vs HMJMPSAB [walk] = 10 fps (2x). No code change. LESSON RE-CONFIRMED: a
single-agent audit (the 4 reader agents got rate-limited, so the synthesizer fell back to solo inspection)
hallucinates — VERIFY every load-bearing claim inline (3 of its facts were wrong here). 705 Formats tests, 16
combat + 158 encounter goldens green.

Phase 70 (DONE — "Finish the Perk Sheet + Script-Set Flee", the curated-perk-batch + AI-flee residuals
the user asked for). M1 curated perk-effects batch: wired the feasible combat/stat/skill perks whose effects
are HARDCODED in the engine (Stat=-1, so the data-driven PerkRules.StatModifier auto-fold can't express them),
each dude-gated + rank-0 short-circuited so a perk-less dude is BYTE-IDENTICAL. PerkRules.SkillModifier = the
perk.cc perkGetSkillModifier verbatim port (the ~14-perk skill family: Medic/Mr.Fixit/Thief/Master Thief/
Harmless/Speaker/Negotiator/Salesman/Gambler/Ranger/Survivalist/Vault City Training/Expert Excrement Expeditor/
Living-Anatomy-Doctor), folded into CritterState.SkillValue alongside the trait modifier; DOCUMENTED CUT:
Ghost's Sneak bonus is light-gated (objectGetLightIntensity) — no light model in CritterState — so omitted.
Adrenaline Rush (stat.cc:256, +1 ST while current HP < max/2) is a CONDITIONAL stat perk the flat fold can't do
-> wired into CritterState.Stat. Quick Recovery (combat.cc:5396, stand from prone in 1 AP not 3) in
StandUpIfProne + Stonewall (combat.cc:4641, 50% knockdown resist) in ApplyKnockback — both DUDE-ONLY, the
Stonewall RNG draw GATED on rank>0 so the default stream is unchanged. Healer (skill.cc:561, First Aid/Doctor
heal +4*rank min / +10*rank max) in ViewerGame.TryHeal. KEY: every PerkId index verified against the checksum-
guarded PerkTable (Healer=19, AdrenalineRush=79, QuickRecovery=102, Stonewall=104, the skill family) — the
recurring don't-trust-the-enum-line lesson. INERT by default -> all 16 combat + 164 encounter goldens
BYTE-IDENTICAL. 19 new tests (PerkTests SkillModifier theory [14 engine-table cases + inert + overlap-stacking]
+ CombatStatusTests Adrenaline Rush). DOCUMENTED RESIDUALS: Quick Recovery/Stonewall are dude-gated private-path
effects (no clean unit seam without a full knockdown scenario) — golden-verified inert, not unit-tested; ~80
perks stay data-present (the table is complete; the stat perks + this curated set + the P28/P29 batches are
wired). M2 script-set AI flee finish: a critter whose script flagged the CRITTER_MANEUVER_FLEEING bit (via
critter_set_flee_state 0x8152, wired P58 — read by WantToJoin since P35-M4 but never DRIVING a turn) now RUNS on
its own turn. Wired the maneuver-flee clause into BOTH TryEnemyAction (the FIRST OR-clause of _combat_ai's flee
gate, combat_ai.cc:3074, before min_hp/hurt_too_much — order immaterial, all three OR into _ai_run_away) and
TryAllyAction (the engine runs _combat_ai for EVERY combatant; checked before the P50 disposition run-away so a
script override wins). INERT by default — only a quest script sets the bit and NO slice golden critter does ->
all 16 combat + 164 encounter goldens BYTE-IDENTICAL (verified by a clean check). Proven by 2 fake-host tests (a
healthy FLEEING-bit enemy runs instead of attacking + the no-bit control attacks). Closes the P35 residual "the
FLEEING maneuver SOURCE is still arity-stubbed" — set via critter_set_flee_state now drives TryFlee end-to-end
(only ENGAGING-via-attack + DISENGAGING-via-terminate were connected before). 724 Formats tests, 16 combat + 164
encounter goldens green.

Phase 71 (DONE — "The Map Remembers Where You've Been II", faithful automap fog: the walked-tile reveal model
+ persistence; the user's Tier-1 pick #1, RESHAPED by M0 grounding). M0 grounding (the headline + a recurring
verify-don't-guess win): the proposed "true-LoS" reveal is NOT what the engine does — object.cc obj_set_seen()
(:1443, called from objectSetLocation for every moving object, the DUDE dominating) marks the TILE under each
mover, then _obj_process_seen() (:3054) flags objects on those tiles + a neighbor spread as OBJECT_SEEN,
persisted to AUTOMAP.DB. So "seen" is WALKED-TILE accumulation — NO line-of-sight, NO sight radius (object.cc:3099
is the ONLY OBJECT_SEEN writer). Hexwaste's radius-14 proximity was actually MORE generous than the engine, and
porting "true-LoS" would have VIOLATED the prime directive (guessing a mechanism); the user chose the faithful
reshape (persist + path-accumulate). M1 tile-based seen model: _seenObjects (HashSet<MapObject> — object refs that
CAN'T survive save/load) → _seenTiles (HashSet<int>, the engine's tile model AND persistable); RevealAround marks
the disc of radius AutomapSeenRadius=4 around each walked tile (the path corridor — a documented approximation of
_obj_process_seen's ±row/±tile byte-spread, which doesn't map cleanly onto the hex grid); the automap render +
census derive visibility from _seenTiles.Contains(obj.HexTile). Re-recorded automap-arcaves (spawn reveal: the
faithful tight path-disc tiles=61/seen=16 vs the old radius-14 sight-circle seen=186 — expected). M2 persistence:
SeenTiles folded into the per-map MapDelta (additive-V2, empty on a pre-P71 save) — it rides VisitedMaps, so the
fog survives BOTH save/load AND a map revisit in ONE stroke (CaptureMapDelta snapshots _seenTiles; ApplyDelta-
BeforeScripts restores it before SpawnDude re-adds the arrival area). M3 harness + golden: --reveal <hex> (drives
RevealAround as if the dude walked there) makes persistence DISTINGUISHING — automap-persist reveals a far tile
(20000 → tiles 61→127), saves, loads, re-censuses tiles=127 (the far reveal SURVIVES; broken persistence would show
only the ~61 spawn disc). GOLDEN-SAFE: the only behaviour change is the automap reveal model (Draw + the census
probe); no combat/encounter path touched → all 16 combat goldens BYTE-IDENTICAL, every encounter golden byte-
identical bar the 2 automap fixtures (1 re-record + 1 new). PersistenceTests MultiMapModelRoundTrips extended
(SeenTiles round-trips + the additive-empty default). The P20 "proximity not LoS, not save-persisted" simplification
is now CLOSED. 724 Formats tests, 16 combat + 165 encounter goldens green.

Phase 72 (DONE — "Speak Up", float messages: wiring the engine's OTHER float_msg uses onto the P45
CombatTextLayer; the user's Tier-1 pick #2). M0 grounding — the 3 sites + colours: level-up (party_member.cc:
1554, _colorTable[0x7FFF]=white, font 101), skill-use response (actions.cc:1461, _colorTable[32747]=yellow),
AI taunt (_combatai_msg, combat_ai.cc:3302 — per-packet chance/color + message ranges into combatai.msg, with
TWO randomBetween draws [the chance gate + the message pick]). M1 level-up float: AwardXp crossing a level floats
"Level Up" (white) over the dude. M2 skill-response float: a successful First Aid/Doctor heal floats "+N" (yellow)
over the target. Both Draw-only (mutate the in-memory float list, never the console/RNG) → byte-identical. M3 AI
combat taunt (the meaty one): AiPacket extended with chance/color + the attack/run message ranges (parsed from
ai.txt — present on every packet); pure Formats.Combat.CombatTaunt.Pick ports _combatai_msg's chance gate +
inclusive range pick; the viewer's TryTaunt resolves the combatai.msg string + the packet's palette colour and
floats it over the critter. Wired the ATTACK taunt (OnAttackStarted, the attacker) + the RUN taunt (a new
ICombatHost.OnCritterFlee hook from CombatEngine.TryFlee). KEY GOLDEN-SAFETY: a dedicated ISOLATED _tauntRng (the
_skillRng/_sneakRng pattern) keeps the chance/message rolls OFF the combat stream, and the float is Draw-only →
ALL 16 combat goldens BYTE-IDENTICAL even though the Den humans (pkt33 chance=25) taunt on flee in denbus2-fight-
flee; the golden-fight scorpion (pkt8 chance=0) never taunts (short-circuits before any draw). DOCUMENTED
RESIDUALS: MISS/HIT taunts (attacker-vs-defender perspective + per-hit-location ranges) + MOVE + the per-location
hit granularity are deferred — only the two clean self-perspective single-range taunts are wired. M4 probe +
golden: --taunt-probe <hex> <seed> (state-only — chance/color/ranges + the deterministic attack/run msgId picks,
NEVER the text); goldens taunt-scorpion (pkt8 chance=0 → all -1, the silent golden-fight critter) + taunt-slave
(pkt33 → runMsg=2009, the firing taunt — verified combatai.msg GetText(2009) returns a real string so the live
float displays). 728 Formats tests, 16 combat + 167 encounter goldens green.

Phase 73 (DONE — "Let Them Fight", the dude-absent NPC-vs-NPC brawl loop; the user's Tier-1 pick #3 — the
last + most golden-sensitive). The X-FIGHTING-Y brawl (P16-M3) ran only WITH the dude involved (in the turn
order + a target, opening on his PlayerTurn); P73 adds a fully independent faction fight the dude only watches.
M0 grounding: StepTurnOrder's while-loop only RETURNS at the dude's slot (PlayerTurn) or when an NPC acts — so a
dude-absent brawl with NO dude slot would spin StartNewRound forever on a stalemate (the headline risk). M1 the
engine core: StartBrawl gained a dudeSpectator param + a _dudeSpectator flag gating 6 branches — the dude is
EXCLUDED from BuildTurnOrder, never TARGETED (TryEnemyAction's defender selection skips the dude+party so only
the cross-team loop seeds the target, with a null-guard pass when no cross-team target remains), the brawl opens
AUTO-RUNNING (EnemyTurn, no PlayerTurn pause), CombatShouldEnd ends it when ≤1 living TEAM remains,
PruneEscapedHostiles (dude-centric sight) is skipped, and EndCombat awards the dude NO XP (he didn't fight) +
clears the flag. A MaxSpectatorBrawlRounds=100 cap breaks the no-dude-slot stalemate spin (and bounds a slow
fight). All 6 branches are INERT by default → the dude-involved combat/brawl path is byte-identical. M2 harness
+ golden: --brawl-watch <map> <gA> <cA> <gB> <cB> spawns two FIGHTING factions (like --encounter-fight) in
spectator mode, RINGS them adjacent (the encounter formations spawn far apart so they'd never engage), and
drives _combat.Step() with a LARGE pump-dt (collapsing animation time → the full 100-round brawl runs in ~2.5s)
to completion, reporting the winner + rounds + dudeHp (state-only). KEY GOLDEN-SAFETY: _dudeSpectator defaults
false + only the new StartBrawl(dudeSpectator:true) entry sets it → all 16 combat goldens + the dude-involved
encounter-fight brawl BYTE-IDENTICAL (verified by a clean combat check). DOCUMENTED FINDING: a real brawl is a
faithful flee-DRAW — hurt critters flee (min_hp/hurt_too_much) and scatter rather than fight to the death, so it
hits the round cap with survivors on both teams rather than a clean wipe; the fake-host test (hp:1 critters that
die before they can flee) is the deterministic clean-WIN proof. Proven by DudeAbsentBrawlAutoResolvesToOneTeam
WithoutTheDude (the dude untouched, exactly one team survives) + the brawl-watch golden (the dude untouched
through a real flee-draw). 729 Formats tests, 16 combat + 168 encounter goldens green.

Phase 74 (DONE — "Perk/Stat Fidelity", the Tier-1 batch from a grounded gap-reaudit workflow [25 agents,
adversarially verified — the survey/verify/rank pattern]). Four small faithful perk/stat wins, every load-
bearing fact verified against fo2ce source (indices cross-checked vs the enum AND the existing PerkId constants
— the recurring don't-trust-the-note guard; the workflow's adversarial verify earned its keep, catching the
Penetrate≠bypassArmor distinction). M1 Gain-X SPECIAL perks + stat clamp: the 7 Gain STR/PER/.../LCK perks
(84..90, CONTIGUOUS over SPECIAL 0..6, stat.cc:252-309) add +1 to the primary — hardcoded per-case in
critterGetStat (NOT data-driven), wired into CritterState.Stat; + the gStatDescriptions effective-stat CLAMP
(stat.cc:369) as the safety net (a Gain-X/Gifted stack can't push a primary past 10; a 0 primary clamps up to
the engine min 1). GOTCHA: the clamp exposed a fake-host test relying on the IMPOSSIBLE PE=0 — fixed to the
faithful PE=1 (the delta unchanged). Inert by default → byte-identical. M2 weapon perks (the proto perk field
was Skip(4)'d — byte-safe un-skip): Accurate +20 to-hit any attacker (combat.cc:4423); Penetrate cuts the
defender's DT to 20% — DT ONLY, via a NEW `penetrate` param NOT bypassArmor (which cuts DT+DR, combat.cc:4535 —
the workflow's key warning); Knockback halves the shove divisor 10→5 (combat.cc:4651). LIVE: the Combat Shotgun
(pid 242) carries Accurate, so arcaves-burst-shotgun's chance is now correctly 65% not 45% (a FAITHFUL re-record
— the previous golden was wrong; same outcome). M3 has_skill (0x80AA): was an arity stub (every skill-gated
dialogue branch failed) — the ONLY script path to a skill value (get_critter_stat covers stats 0-34 only).
Ported opHasSkill (interpreter_extra.cc:560 → skillGetValue: returns the effective skill VALUE, NOT a bool);
ScriptHost.CritterSkillValue + a SkillResolver the viewer wires to the full CritterState.SkillValue (gcd skills
+ tags + perk/trait mods). LIVE (dcMetzge/dcVic fire it) but the vic-recruit/iq-gate goldens are BYTE-IDENTICAL
— the navigated --talk-seq options resolve the same with real skills as at stub-0; proven live by
--has-skill-probe (Narg's Small Guns 43 / First Aid 8 vs the old 0). M4 Bonus Move free-move AP pool:
_dudeFreeMove = 2*rank (combat.cc:3237) seeded in ResetDudeAp, drained by movement BEFORE real AP in SpendDudeAp
(animation.cc:2610), the viewer walk-halt checks use DudeAp+DudeFreeMove, the HUD shows lighter-green free-move
pips. Inert by default (rank 0 → pool 0 → byte-identical). GOTCHA: SpendDudeAp is movement-ONLY (sole caller the
TileChanged closure), so attacks correctly don't drain the pool. KEY OUTCOME: every milestone byte-identical bar
1 faithful re-record (the shotgun's now-correct Accurate +20). 739 Formats tests, 16 combat + 169 encounter
goldens green. Remaining Tier-2 from the reaudit: AI called shots, Lifegiver +HP/level (+ a doc-truth fix), AC+
remaining-AP dodge, enemy burst selection, itemGetCost pricing, difficulty spawn skew, the time_of_day & ammo-
consolidation bugs.

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
which reads proto.msg/misc.msg and was out of scope (SUPERSEDED — shipped in P50
as the companion-hub "Set your tactics." combat-control window). The follow loop stays
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
