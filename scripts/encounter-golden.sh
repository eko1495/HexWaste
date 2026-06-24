#!/usr/bin/env bash
# Golden transcript regression net for phase-10 — random encounters + companions.
#
# Re-runs deterministic headless encounter/companion scenarios (--rng-seed) and
# (a) asserts each is reproducible run-to-run and (b) diffs the transcript lines
# against the committed fixtures in tests/golden-encounter/. Mirrors
# combat-golden.sh; run it after touching the worldmap/encounter/companion code.
#
# Usage:  scripts/encounter-golden.sh [check|record]   (default: check)
#   record  — (re)capture the fixtures from current behaviour (the baseline)
#   check   — fail if any run is nondeterministic or differs from its fixture
#
# Requires a real display (the MonoGame app needs a GraphicsDevice — it cannot run
# on a headless CI runner) and game data (FALLOUT2_DIR, default ./game-data).
set -uo pipefail
cd "$(dirname "$0")/.."

MODE="${1:-check}"
GAME="${FALLOUT2_DIR:-$(pwd)/game-data}"
FIX="tests/golden-encounter"
mkdir -p "$FIX"

# name | harness args (each is a deterministic, self-exiting startup-action run)
SCENARIOS=(
  "encounter-arro-rats|--encounter desert1.map ARRO_Rats 5 --rng-seed 1"
  "encounter-war-party|--encounter desert1.map ARRO_War_Party 4 --rng-seed 7"
  "encounter-scorpions|--encounter desert1.map ARRO_Sm_Scorpions 4 --rng-seed 2"
  # P16-M4: lock the per-member If()/Distance fidelity on real data — ARRO_Spore_Plants'
  # Dead Primitive Female is gated behind lowercase "if (Rand(5%))" (a case-sensitivity bug
  # made it spawn 100%); at seed 13 the roll passes so the corpse appears (the flat line),
  # while its Distance-pinned plant-item siblings stay gated out.
  "encounter-spore-plants|--encounter desert1.map ARRO_Spore_Plants 4 --rng-seed 13"
  # P76-M3: difficulty skews the spawn group size (worldmap.cc:3692). HARD adds +2 to each
  # sub-entry's critterCount, so the same seed plans a bigger group (ARRO_Rats 5 -> 6) than
  # Normal. Inert at Normal (the encounter-arro-rats fixture above is the Normal control).
  "encounter-rats-hard|--difficulty hard --encounter desert1.map ARRO_Rats 5 --rng-seed 1"
  # P16-M1: travelling the Arroyo->Den leg now DETECTS the ARRO_Rats encounter ahead
  # (Outdoorsman), grants the avoid XP, and (headless default) engages it. The avoid
  # variant declines -> travels on -> walks into the next (undetected) ambush.
  "travel-arroyo-den|--travel-from 184 133 1 --rng-seed 2"
  "travel-arroyo-avoid|--encounter-answer no --travel-from 184 133 1 --rng-seed 2"
  # P16-M2: leaving an encounter map mid-leg auto-resumes travel toward the destination
  # (the engine's isWalking) — no worldmap re-click; here the resumed leg rolls again.
  "travel-resume|--travel-resume 204 143 1 --rng-seed 2"
  # P17-M2: the ANIMATED travel path (the moving dot) drains the SAME leg as the sync
  # resolve — same encounter + worldPos as travel-arroyo-den — while terrain cadence makes
  # cadence-ticks (26) exceed pixel-steps (20): mountains hold the dot some ticks.
  "travel-step|--travel-step 184 133 1 --rng-seed 2"
  # P17-M4: saving MID-travel round-trips the dot worldPos + the in-flight destination
  # (load resumes toward it) — a documented divergence from the engine's drop-stopped reload.
  "travel-save-mid|--character combat --map artemple.map --travel-save-mid 184 133 1 5 --rng-seed 2"
  # P28-M4: the perk picker. At level 3 the dude has 1 pick over 14 eligible perks; picking row 0
  # takes it and closes the picker. At level 6 (2 picks) it stays open after one pick. Level 1 = 0.
  "perk-pick|--character combat --map arcaves.map --perk-pick 3 0 --perk-pick 6 0 --perk-pick 1 0 --rng-seed 1"
  # P28-M2: perk infrastructure + selection (perk.cc perkCanAdd + the table-driven stat perks).
  # Bonus HtH Damage (idx 2, melee +2/rank, needs ST6/AG6/level3) is level-GATED at lvl 2, eligible
  # at 3 (melee 8->10, atop Narg's Heavy Handed +4), STACKS to rank 2 (->12); More Criticals (idx 8)
  # stays stat-GATED (Narg LK4 < req6) even at lvl99. picks = level/3.
  "perk-gates|--character combat --map arcaves.map --perk-probe 2 2 --perk-probe 2 3 --perk-probe 2 3 --perk-probe 8 99 --rng-seed 1"
  # P28-M1: optional-trait effects (trait.cc traitGetStatModifier/SkillModifier), applied live.
  # No traits is inert (baseline); Gifted +1 all SPECIAL & -10 all skills; Bruiser+Kamikaze stack
  # (STR+2/AP-2/AC->0/SEQ+5); Good Natured shifts combat (-10) vs social (+15) skills.
  "trait-none|--character combat --map arcaves.map --trait-probe -1 -1 --rng-seed 1"
  "trait-gifted|--character combat --map arcaves.map --trait-probe 15 -1 --rng-seed 1"
  "trait-bruiser-kamikaze|--character combat --map arcaves.map --trait-probe 1 5 --rng-seed 1"
  "trait-goodnatured|--character combat --map arcaves.map --trait-probe 10 -1 --rng-seed 1"
  # P26: gory death animations (actions.cc _pick_death). A solid burst/laser/explosion kill
  # gives the corpse a gore variant by damage type — DancingAutofire(26)/SlicedInHalf(28)/
  # BigHole(23) — when the critter ships that art. A denbus2 human (pid 0x1000004) does (gore=True);
  # an arcaves scorpion does NOT, so it faithfully falls back to FALL_BACK (gore=False).
  "gore-human|--map denbus2.map --death-probe 8667 --rng-seed 1"
  "gore-scorpion|--map arcaves.map --death-probe 20529 --rng-seed 1"
  # P25: dialogue IQ-gating. The dude's real INT now gates giq_option dumb/smart options
  # (interpreter_extra.cc _op_giq_option) instead of a hardcoded 5. Vic's greeting offers 1
  # option to a dim dude (IN 2 — smart options gated out) vs 4 to a bright one (IN 9). The
  # probe reports only the option COUNT, never the copyrighted option text.
  "iq-gate-dumb|--map denbus2.map --iq-probe 17070 2 --rng-seed 1"
  "iq-gate-smart|--map denbus2.map --iq-probe 17070 9 --rng-seed 1"
  # P74-M3: has_skill (0x80AA) now returns the critter's real effective skill % (was a stub 0) — the
  # only script path to a skill value. Narg's Small Guns 43 / First Aid 8 (state-only).
  "has-skill-probe|--character combat --map denbus2.map --has-skill-probe -1 0 --has-skill-probe -1 6 --rng-seed 1"
  # P24: carry weight + encumbrance. A light load (1 SMG = 7 lbs vs the combat char's
  # 250 lb capacity) is unencumbered with no AP penalty; 60 SMGs (420 lbs) is over -> the
  # stat.cc:198 max-AP penalty = (420-250)/40+1 = 5. Proves the weight field parses + the
  # InventoryWeight stack runs on real protos. (--give bypasses the pickup gate by design.)
  "weight-light|--character combat --map arcaves.map --give 9 --weight-probe --rng-seed 1"
  "weight-heavy|--character combat --map arcaves.map --give 9:60 --weight-probe --rng-seed 1"
  # P22: worldmap subtile fog-of-war. Travelling the arroyo->den corridor reveals subtiles
  # along the Bresenham path — the start + destination flip to VISITED (clear), the trail's
  # radius-1 neighbourhood to KNOWN (fogged). The reveal draws no RNG, so every other travel
  # golden stayed byte-identical (silent reveal + this dedicated probe, the P21 pattern).
  "worldmap-fog|--fog-probe 184 133 1 --rng-seed 2"
  # P16-M3: an X-FIGHTING-Y encounter spawns its two groups on DISTINCT teams (1 & 2) and
  # opens a brawl — the factions fight each other (cross-team targeting), not just the dude.
  "encounter-fight|--encounter-fight desert1.map ARRO_Spore_Plants 3 ARRO_Silver_Geckos 2 --rng-seed 3"
  # P73: a dude-ABSENT brawl — two factions ringed adjacent fight it out on their own while the dude
  # only watches. Proves the spectator open (EnemyTurn), termination (ended), and that the dude is
  # never targeted (dudeHp stays full). A faithful flee-draw: hurt critters flee, so it hits the cap.
  "brawl-watch|--brawl-watch desert1.map ARRO_War_Party 2 ARRO_Cannibals 2 --rng-seed 3"
  "companion-lifecycle|--map arcaves.map --companion 20529 --rng-seed 1"
  "trade-roundtrip|--map arcaves.map --trade 20529 7 --rng-seed 1"
  "companion-persist|--map arcaves.map --companion-persist 20529 --rng-seed 1"
  "companion-dismiss-persist|--map arcaves.map --dismiss-persist 20529 --rng-seed 1"
  # Legitimate Vic recruit (#10 M1 + M-radio) — denbus2, fully VM-driven, no GVAR
  # cheat: give Vic the radio (pid 266), his dialog runs the real inventory externals
  # (obj_is_carrying_obj / obj_carrying_pid_obj / rm_obj_from_inven) to set the
  # radio-fixed bit GVAR446|0x400000; that unlocks Metzger's $1000 buy (free-bit
  # GVAR445 handshake); then Vic's talk_p_proc party_add recruits him. The radio
  # ITEM (--give 266) is the one documented content gap — it has no in-slice source.
  "vic-recruit|--map denbus2.map --give 41:2000 --give 266:1 --talk-seq 17070 1,1,1 --talk-seq 15278 2,2,1,1 --talk-seq 17070 2,1 --party-count --rng-seed 1"
  # P11 M4 + P12 M0/M1/M2 — the HUD bar buttons fire their panel actions (INV/MAP/CHA
  # wired in P11; SKILLDEX in P12-M0; PIP in P12-M1; OPT in P12-M2).
  "hud-buttons|--character combat --map denbus2.map --hud-click INV --hud-click MAP --hud-click CHA --hud-click SKILLDEX --hud-click PIP --hud-click OPT --rng-seed 1"
  # P12 M0 — the Skilldex use-skill picker: lockpick a scripted door (use_skill_on_p_proc
  # honours the script — the door stays locked, not blindly unlocked), First Aid self at
  # full HP (healthy already, no roll), and the Sneak stance toggle. All deterministic.
  "skilldex-skills|--map denbus2.map --use-skill 9 9510 --use-skill 6 -1 --use-skill 8 -1 --rng-seed 1"
  # P78-M1 — Steal: the dude lifts an item from Metzger (denbus2 @15278) via the real skill check
  # (skill.cc skillsPerformStealing). Seed 2 succeeds (markItems 3->2, no aggro); seed 1 is caught,
  # so Metzger turns hostile (aggro=True, combat opens) and nothing is taken. State-only (counts + pid).
  "steal-success|--character combat --map denbus2.map --steal 15278 0 --rng-seed 2"
  "steal-caught|--character combat --map denbus2.map --steal 15278 0 --rng-seed 1"
  # P15 M1 — the HUD weapon slot cycles the attack mode (single->burst) for a burst gun.
  "weapon-mode-cycle|--map arcaves.map --give 9 --use-item 9 --hud-click WEAPON --rng-seed 1"
  # P18 M0/M1 — in-combat movement costs AP per hex. AP 8 reaches the 4-hex target (4 left);
  # AP 2 TRUNCATES at 2 hexes (gate halts the walk); a crippled leg costs 4 AP/hex so 8 AP
  # only covers 2 hexes — the P14-M3 MovePointCost now bites the player (the SCOPE asymmetry).
  "combat-walk-full|--map arcaves.map --combat-walk 20529 20534 8 --rng-seed 1"
  "combat-walk-truncated|--map arcaves.map --combat-walk 20529 20534 2 --rng-seed 1"
  "combat-walk-crippled|--map arcaves.map --combat-walk 20529 20534 8 cripple --rng-seed 1"
  # P15 M2 — item-panel row CLICK == its number key: open the inventory (HUD INV), click an
  # empty row (out of bounds -> no-op, consumed=false) then row 0 (equips, same as pressing 1).
  "panel-click-equip|--character combat --map denbus2.map --give 9 --hud-click INV --panel-click 0 5 --panel-click 0 0 --rng-seed 1"
  # P15 M3 — the Options/Pip-Boy menu rows are clickable (Skilldex parity). Each row's
  # centre must hit-test back to its own index (hit==row), then dispatch: Options row 4 =
  # Resume (closes); Pip-Boy row 0 = Rest (opens rest menu), rest-menu row 9 = Back, status
  # row 1 = Automap. All side-effect-free rows so the state line is map-independent.
  "menu-click-options|--character combat --map denbus2.map --menu-click options 4 --rng-seed 1"
  "menu-click-pipboy|--character combat --map denbus2.map --menu-click pipboy 0 --menu-click pipboy-rest 9 --menu-click pipboy 1 --rng-seed 1"
  # P15 M0 — the Pip-Boy automap object census (the dots it plots): deterministic
  # per-type object counts + the dude tile for a fixed map (no RNG).
  "automap-arcaves|--map arcaves.map --automap --rng-seed 1"
  # P71 — automap fog persistence: reveal a far tile (20000), census, save+load, census again;
  # the far reveal must survive the round-trip (tiles stays 127, not the ~61 spawn disc).
  "automap-persist|--map arcaves.map --save-path /tmp/hexwaste-automap-persist.json --reveal 20000 --automap --save-now --load-now --automap --rng-seed 1"
  # P21 — script-driven lighting + reg_anim: artemple's map_enter calls set_light_level(100)
  # (now pins the ambient) and reg_anim_animate_forever on its two firepits (now reaches the
  # animator; redundant with FRM auto-loop on the slice, faithful for the critter case). Both
  # were arity-stubbed no-ops before. The probes report the results.
  "script-light|--map artemple.map --light-probe --reg-anim-probe --rng-seed 1"
  # P46 map_update_p_proc (SCRIPT_PROC 23): the engine runs it once on load after map_enter
  # (map.cc:1010-1011) then every 600 game ticks. On the slice its sole observable payload is a
  # set_light_level — arcaves' map_update dims the cave to the "cavern" level 50 (ambient 40960) that
  # map_enter left at max; the other slice maps re-set max (inert). light-arcaves proves the live
  # dimmed ambient; map-update-arcaves is the diagnostic (levels=[50], 1 light call, no new stubs).
  "light-arcaves|--map arcaves.map --light-probe --rng-seed 1"
  "map-update-arcaves|--map arcaves.map --map-update-probe --rng-seed 1"
  # P47 inventory drag-and-drop equip (inventory.cc _switch_hand): --drag-equip <fromRow> <slot>
  # drives the real drag-to-slot equip path. slot 0=weapon / 2=armor / -1=drop. Reports pid +
  # equipped flag + AC/DT/DR (state-only). weapon equips in-hand; armor applies its AC/DT/DR bonus;
  # a wrong-type drop (weapon onto the armor slot) is rejected; drop removes from the bag.
  "drag-equip-weapon|--character combat --map arcaves.map --give 9 --drag-equip 0 0 --rng-seed 1"
  "drag-equip-armor|--character combat --map arcaves.map --give 3 --drag-equip 0 2 --rng-seed 1"
  "drag-equip-reject|--character combat --map arcaves.map --give 9 --drag-equip 0 2 --rng-seed 1"
  "drag-equip-drop|--character combat --map arcaves.map --give 9 --drag-equip 0 -1 --rng-seed 1"
  # P48 multi-slot save UI (loadsave.cc 10-slot LSGAME): one JSON file per slot under --save-dir.
  # --save-slot/--load-slot/--slots-probe drive the real save-to/load-from-slot path; --reset-slots
  # clears the dir for a deterministic probe. Round-trip: save slot 3 then load it (party-count
  # matches); load an empty slot is a no-op; the probe reports each slot's state (L<level>/empty).
  "save-slot-roundtrip|--character combat --map denbus2.map --give 41:500 --save-dir /tmp/hexwaste-p48-rt --reset-slots --save-slot 3 --party-count --load-slot 3 --party-count --load-slot 5 --rng-seed 1"
  "save-slots-probe|--character combat --map denbus2.map --save-dir /tmp/hexwaste-p48-sp --reset-slots --save-slot 0 --save-slot 5 --slots-probe --rng-seed 1"
  # P49 called-shot click dialog (combat.cc calledShotSelectHitLocation): V opens it, 1-9/click picks a
  # hit location. --aim-click <row> drives the real SelectAimRow; reports the location + to-hit penalty
  # per the engine's CALLED.frm button order (head/eyes/r-arm/r-leg/torso/groin/l-arm/l-leg/uncalled).
  "aim-click|--map arcaves.map --aim-click 0 --aim-click 1 --aim-click 4 --aim-click 6 --aim-click 8 --rng-seed 1"
  # P50 companion combat-control / AI-disposition window (game_dialog.cc:3354). --companion-tactics
  # <hex> <row> <count> drives the real window-cycle path + reports the EFFECTIVE disposition/knobs the
  # ally AI honours. row 0 cycles the disposition (Berserk→Aggressive→Defensive→Coward→Custom); rows 1-4
  # cycle the custom knobs (forcing Custom). The arcaves scorpion@20529 is the stand-in critter.
  "companion-tactics|--map arcaves.map --companion-tactics 20529 0 2 --companion-tactics 20529 1 2 --companion-tactics 20529 3 1 --rng-seed 1"
  # P51 area-attack + best-weapon rows (5/6): cycle area-attack x3 (Never->Sometimes->BeCareful->BeSure) and
  # best-weapon x4 (NoPref->Melee->...->Ranged), exercising the engine's last 2 combat-control settings.
  "companion-tactics-aw|--map arcaves.map --companion-tactics 20529 5 3 --companion-tactics 20529 6 4 --rng-seed 1"
  # P12 M1 — the Pip-Boy rest options: a timed rest (6h heals proportionally) then an
  # until-healed rest from near-death to full. --hurt sets up the wound; deterministic
  # clock math + heal amounts (artemple has no enemy near the entry, so rest is allowed).
  "pipboy-rest|--map artemple.map --hurt 20 --rest-for 360 --hurt 20 --rest-for -1 --rng-seed 1"
  # #10 M2 — a legitimately-recruited Vic levels up his proto as the dude gains levels
  # (PartyLevelUp wired into AwardXp; party.txt member 13, level_minimum 5).
  "vic-levelup|--map denbus2.map --give 41:2000 --give 266:1 --talk-seq 17070 1,1,1 --talk-seq 15278 2,2,1,1 --talk-seq 17070 2,1 --grant-xp 60000 --party-count --rng-seed 1"
  # #10 M3 — the scripted recruit + its proto level-up survive a save/load round-trip:
  # the party-count line is identical before and after (members=2, no duplication; Vic
  # keeps his levelled stage HP). Saves to /tmp so nothing lands in the repo.
  "vic-save-roundtrip|--map denbus2.map --give 41:2000 --give 266:1 --talk-seq 17070 1,1,1 --talk-seq 15278 2,2,1,1 --talk-seq 17070 2,1 --grant-xp 60000 --save-path /tmp/hexwaste-m3golden.json --party-count --save-now --load-now --party-count --rng-seed 1"
  # P30 A-M0/M1/M2 — the sneak state probes (deterministic): the periodic SKILL_SNEAK roll under a
  # fixed seed (the isolated _sneakRng), the two-layer flag/working state, and the Silent Death facing
  # test (behind hex 0/0 → mult 4; front 0/3 → mult 2).
  "sneak-state|--map artemple.map --sneak-roll 1 --sneak-probe 1 --backstab-probe 0 0 --backstab-probe 0 3 --rng-seed 1"
  # P30 A-M3 — the live NPC detection gate (isWithinPerception): a non-sneaking dude is always seen
  # (PE*5 cone); an actively-sneaking dude quarters the range, so the same distance goes undetected far
  # but detected close. Pure decision over the dude's real Sneak skill (20).
  "sneak-detect|--map artemple.map --detect-probe 7 30 1 0 0 --detect-probe 7 30 1 1 1 --detect-probe 7 5 1 1 1 --detect-probe 7 7 0 0 0 --rng-seed 1"
  # P31 karma/reputation: the PC-stat karma/rep (get_pc_stat 4/3), the generic-reputation title
  # (GVAR_PLAYER_REPUTATION=GlobalVars[0] via genrep.txt), a town standing (Arroyo gvar 47), and an
  # earned karma title (gvar 3) — all script-/harness-set (the engine never auto-awards karma).
  "karma|--map artemple.map --set-karma 50 5 --set-global 0 100 --set-global 47 30 --set-global 3 1 --karma-probe --rep-title 100 --town-rep 30 --karma-titles --rng-seed 1"
  # P32-M1 vault13.gam GVAR seeding: --create runs StartNewGame which seeds the non-zero globals
  # (Arroyo rep 47:=50, FIND_VIC 619:=1, Salvatore counter 134:=100; everything else 0).
  "gvar-seed|--map artemple.map --create 5,5,5,5,5,5,5:0,4,5:0 --get-global 0 --get-global 47 --get-global 619 --get-global 134 --get-global 50 --get-global 81 --get-global 91 --get-global 137 --get-global 55 --get-global 135 --get-global 136 --get-global 216 --get-global 284 --get-global 56 --get-global 461 --rng-seed 1"
  # P33 critter_attempt_placement: relocate a map critter to a different tile via the real placement path
  # (denbus2 has a critter at 14716; move it to 14000). On entry the engine fires this op same-tile (a
  # no-op), so the slice goldens are unchanged — this proves the actual relocate.
  "critter-place|--map denbus2.map --place-probe 14716 14000 --rng-seed 1"
  # P33-M1 reg_anim movement: drive a reg_anim_func batch (begin -> obj_move_to_tile -> end)
  # on a map critter via the executor (no shippable script fires the move ops, so synthesize
  # it). denbus2's merchant at 14716 walks to the reachable 14718. The VM dispatch is inert on
  # every slice map (only reg_anim_animate_forever for scenery fires at map_enter), so the
  # other goldens — incl. script-light's --reg-anim-probe — are unchanged.
  "reg-anim-move|--map denbus2.map --reg-anim-move 14716 14718 --rng-seed 1"
  # P34-M1 is_in_combat (0x8128) + critter_state (0x80FB): the two stubbed externals the slice's
  # critter_p_proc heartbeats fire every tick now read real state. denbus2's merchant at 14716 is
  # un-engaged + upright + uninjured -> inCombat=0, state=0 (NORMAL); hex<0 reports is_in_combat only.
  "critter-state|--map denbus2.map --critter-state-probe 14716 --critter-state-probe -1 --rng-seed 1"
  # P34-M2 hurt_too_much flee gate (combat_ai.cc:3076): a crippled/blinded critter whose AI packet
  # lists that damage flag flees, not just one below min_hp. Set the blind bit (0x40=64) on the arcaves
  # scorpion at 20529 -> its packet's hurt mask (blind) matches -> wouldFlee=1. INERT by default (no
  # slice golden enemy carries a crip/blind bit on a turn it takes), so all other goldens are unchanged.
  "hurt-too-much-flee|--map arcaves.map --hurt-too-much-probe 20529 64 --rng-seed 1"
  # P34-M3 run animation: the dude RUNS by default (ANIM_RUNNING=19), walking only under the 3 engine
  # guards (animation.cc animationRegisterRunToTile): crippled leg, sneaking-without-Silent-Running, or
  # missing run art. Pure decision (RunGuard.MovementAnimCode), Draw/anim-only -> all goldens unchanged.
  "run-probe|--character combat --map arcaves.map --run-probe --rng-seed 1"
  # P34-M4 typed combat outlines: during combat every visible living critter is outlined by team
  # (red hostile / green friendly), LoS-gated (combat.cc _combat_update_critter_outline_for_los). The
  # probe positions the dude adjacent to 20529 and classifies each arcaves critter: clear-LoS scorpions
  # (team 4 != dude team 0) = hostile; blocked-LoS + beyond PE*5 = none. Draw-only -> goldens unchanged.
  "outline-typed|--character combat --map arcaves.map --outline-probe 20529 --rng-seed 1"
  # P77: the remaining-AP dodge (stat.cc:215) on real data — the enemy opens combat (zero RNG), so the
  # not-yet-acted dude dodges at his FULL maxAp 7 (an enemy attacking him faces +7 AC) while the acting
  # scorpion (maxAp 5) gets 0; that scorpion-maxAp-5 is exactly the 47->42% drop in the combat goldens.
  "ac-dodge|--map arcaves.map --ac-dodge-probe 20529 --rng-seed 7"
  # P34-M5 combat sfx: the faithful sfxBuildCharName/sfxBuildWeaponName composers + per-map ambient.
  # A scorpion (base mascp2) resolves to MASCP2* names whose .acm SHIP (audible, like the original game);
  # the per-map ambient_sfx list parses (arcaves=water, denbus2=dogbark). Audio is off in goldens (--no-
  # audio) so the playback is headless-inert; the probe reports composed NAMES (asset identifiers) only.
  "sfx-probe-scorpion|--map arcaves.map --sfx-probe 20529 --rng-seed 1"
  "sfx-probe-human|--map denbus2.map --sfx-probe 8667 --rng-seed 1"
  # P34-M6 reaction anims: the defender's hit-react / dodge / knockdown-fall / get-up anim selection
  # (actions.cc _show_damage_to_object). The denbus2 human (ships HIT_FROM_BACK art) flips 14->15 + fall
  # 20->21 when struck from behind (attacker rot 3 vs def rot 2); a scorpion that LACKS back art stays at
  # hit=14 even from behind (the art-existence fallback). Draw/anim-only -> goldens byte-identical.
  "reaction-anims-human|--map denbus2.map --reaction-probe 8667 0 --reaction-probe 8667 3 --rng-seed 1"
  "reaction-anims-scorpion|--map arcaves.map --reaction-probe 20529 3 --rng-seed 1"
  # P35 combat_p_proc (SCRIPT_PROC_COMBAT): the per-turn in-combat script hook (fixedParam=4) now runs
  # at the top of each combatant's turn (combat.cc:3243); script_overrides() forfeits the turn. LIVE-but-
  # INERT on the slice: the arcaves scorpion (script 19) DEFINES combat_p_proc but its body gates on
  # fixed_param==2 (the on-hit poison hook, a separate golden-risk milestone), so the fp=4 call is a no-op
  # (hasProc=True, overridden=False, no RNG) -> the --fight goldens stay byte-identical; the denbus2 slave
  # (script 906) defines no combat_p_proc (hasProc=False).
  "combat-proc-scorpion|--map arcaves.map --combat-proc 20529 --rng-seed 1"
  "combat-proc-slave|--map denbus2.map --combat-proc 11670 --rng-seed 1"
  # P35 fp=2 on-hit hook: the arcaves scorpion's combat_p_proc (fixed_param==2) poisons whom it stings,
  # via the now-dispatched poison(0x8122) + target_obj=defender plumbing (combat.cc:4729). Deterministic
  # under --rng-seed (the script's do_check uses the seeded _scriptHost.Rng): seed 2 applies +1 poison to
  # the dude. critterAdjustPoison is dude-only + poison-resistance reduced; the misc.msg "poisoned" line +
  # the delayed EVENT_TYPE_POISON damage tick are documented divergences (silent counter only).
  "combat-proc-poison|--character combat --map arcaves.map --combat-proc-hit 20529 --rng-seed 2"
  # P35-M3 poison-over-time: the poison counter now ticks HP damage on the game clock (poisonEvent
  # Process, critter.cc:378 — dude-only, -2 poison + -1 HP per tick at 10*(505-5*poison) game-ticks,
  # re-queued until poison<=0). Deterministic (pure clock math, no RNG): poison 1 over 10 game-min = 1
  # tick (-1 HP); poison 10 over 60 game-min = 5 ticks (-5 HP). No existing golden both poisons the dude
  # AND advances the clock past a tick interval, so all other goldens are byte-identical.
  "poison-tick|--character combat --map arcaves.map --poison-tick 1 10 --poison-tick 10 60 --rng-seed 1"
  # P36 MULTIHEX: the +15 to-hit vs a multihex defender (combat.cc:4443) + BuildSpawn now propagates the
  # proto's OBJECT_MULTIHEX (0x800) onto encounter spawns. The Large Radscorpion (pid 0x1000006) is multihex
  # and spawns in KLAD_Scorpions (Klamath-Den route, 30% ratio); the small Radscorpion (0x1000005, the
  # arcaves --fight critter) is NOT, so the combat goldens stay byte-identical. The probe proves the flag.
  "multihex-probe|--map arcaves.map --multihex-probe 1000006 --multihex-probe 1000005 --rng-seed 1"
  # P37 drug stat effects (item.cc _perform_drug_effect + the EVENT_TYPE_DRUG wear-off queue): Buffout
  # (pid 87) gives an immediate ST+2/EN+3/AG+2 and schedules a 360-min down-kick (-4 each) then a 1080-min
  # restore (+2/+1/+2) that NET TO ZERO (the comedown). The probe reports the active _drugBonus per stat +
  # the pending count: at min 0 the up-kick (pending 2); at +400 the dur1 down-kick fired (now negative,
  # pending 1); at +700 (total 1100) dur2 restored it to zero (pending 0). No golden gives/uses a stat
  # drug, so all other goldens stay byte-identical (the -2 stimpak heal RNG is unchanged).
  "drug-stat|--character combat --map arcaves.map --give 87 --use-item 87 --drug-probe 87 0 --drug-probe 87 400 --drug-probe 87 700 --rng-seed 1"
  # P38 drug addiction + withdrawal (item.cc _item_d_take_drug addiction tail :2822 + the
  # EVENT_TYPE_WITHDRAWAL onset/recovery chain). --addict-probe <pid> <seed> <gameMin> seeds the
  # ISOLATED addiction RNG, gives+uses the drug (the faithful roll), advances the clock, fires
  # onset/recovery, reports the addiction GVAR + withdrawal stat penalty + pending count. Buffout(87)
  # seed 1 HITS: addicted (gvar22=1), the symptom onset applies the withdrawal perk (ST-2/EN-2/AG-3,
  # decoded from PerkTable), then recovery 7 game-days later clears it. Seed 2 MISSES (no addiction).
  # Jet(259) withdrawal is PERMANENT — the penalty persists past recovery (gvar stays 1, no reverse).
  # The roll draws ONLY from the dedicated _addictionRng, so the other goldens stay byte-identical.
  "addict-buffout-active|--character combat --map arcaves.map --addict-probe 87 1 9000 --rng-seed 1"
  "addict-buffout-recover|--character combat --map arcaves.map --addict-probe 87 1 20200 --rng-seed 1"
  "addict-jet-permanent|--character combat --map arcaves.map --addict-probe 259 1 30000 --rng-seed 1"
  "addict-miss|--character combat --map arcaves.map --addict-probe 87 2 9000 --rng-seed 1"
  # P38 kill counters (killsIncByType / GET_KILL_COUNT — the faithful adjacency to the dropped
  # karma auto-award; the engine tracks kills by type beside the XP award, gated on the dude/team
  # kill). The seed-7 arcaves fight kills 2 Radscorpions (KILL_TYPE 6); --kills-probe -1 reports the
  # tally. No RNG / no Console output added → the combat goldens stay byte-identical.
  "kill-counter|--map arcaves.map --fight 20529 --rng-seed 7 --kills-probe -1"
  # P39 skill books (item.cc booksInitVanilla + proto_instance.cc _obj_use_book): reading a book raises
  # its skill by (100-effective)/10 points (diminishing returns, nothing at effective 100), ×1.5 with
  # Comprehension. --use-book gives+reads one book. Guns and Bullets (102→Small Guns, TAGGED for Narg so
  # +5 pts = +10%): 43->53, then a 2nd read off the raised skill gives +4 (53->61); Scout Handbook
  # (86→Outdoorsman, untagged so +8 pts = +8%): 16->24. No RNG (pure skill math) → deterministic.
  "use-book|--character combat --map arcaves.map --use-book 102 --use-book 102 --use-book 86 --rng-seed 1"
  # P40 selectable ammo type (item.cc weaponUnload + the per-box reload): the player can switch a
  # weapon's loaded ammo (unload Shift+R, then reload with a chosen box). --load-ammo unloads + reloads
  # with the given pid, reporting the loaded type + the combat-relevant mods. A 10mm pistol (8) switches
  # 10mm AP (30, dr-25/mult1/div2 = armor-piercing) <-> 10mm JHP (29, dr+25/mult2/div1 = anti-unarmored).
  # The ammo combat math was already wired+consumed, so this only adds CONTROL -> goldens byte-identical.
  "ammo-select|--character combat --map arcaves.map --give 8 --use-item 8 --give 30 --give 29 --load-ammo 30 --load-ammo 29 --rng-seed 1"
  # P42 enemy chem_use stimpak healing (combat_ai.cc _ai_check_drugs): a hurt BIPED enemy with a healing
  # item + a chem_use packet quaffs it mid-fight (2 AP). --ai-heal-probe gives the critter a stimpak,
  # drops it to 1 HP, and runs the real TryNpcHeal — the swarm Den maps never let the dude win a clean
  # 1-on-1 vs a stimpak NPC, so this is the deterministic live proof. The golden-fight enemies (arcaves
  # scorpion pkt8/quadruped, denbus2 peasant pkt14 — both chem_use=clean) never heal → goldens byte-identical.
  "ai-heal|--map denbus1.map --ai-heal-probe 16910 --rng-seed 1"
  # P43 AI inventory weapon switch (combat_ai.cc _ai_switch_weapons → _ai_best_weapon): when a critter's
  # wielded gun goes dry it draws the carried backup its best_weapon preference favours. --ai-weapon-probe
  # forces the equipped gun dry and runs the real CritterInventoryWeapons → fold → EquipWeapon path. denbus1
  # 17261 is a Tough Guard (pkt22 = ranged_over_melee, consistent map/runtime) with a backup 0x5 → switches.
  # No --fight golden reaches a multi-weapon NPC (the golden-fight scorpions are non-biped + carry nothing →
  # the switch never fires → all combat goldens byte-identical), so this is the live proof.
  "ai-weapon-switch|--character combat --map denbus1.map --ai-weapon-probe 17261 --rng-seed 1"
  # P45 floating combat text (text_object.cc port): the float-text layer's STATE over a real critter
  # — count/lifetime/cap/anchor + the engine float_msg colours. Draw-only (the layer is never ticked
  # or drawn headless), so every other golden stays byte-identical; this proves the spawn + constants.
  "float-text-probe|--map arcaves.map --float-text-probe 20529 --rng-seed 1"
  # P72-M3 AI combat taunts (_combatai_msg): the taunt config (chance/color/ranges) + deterministic
  # message-id picks under a seed (state-only — combatai.msg ids, never the text). The golden-fight
  # scorpion (pkt8 chance=0) NEVER taunts → byte-identical combat; the Den slave (pkt33 chance=25)
  # taunts on the seeded roll (runMsg=2009) — proving the parse + chance gate + range pick.
  "taunt-scorpion|--map arcaves.map --taunt-probe 20529 7 --rng-seed 1"
  "taunt-slave|--map denbus2.map --taunt-probe 11670 11 --rng-seed 1"
  # P53 dialogue voiceover: the speech-name compose + play-gate (scripts.cc _scr_get_msg_str_speech).
  # A forced audio composes sound\speech\<id>.acm (wouldPlay=1); a REAL Metzger line (list 46 = script
  # 45+1, msg 100) confirms the slice's empty audio field through the parser (wouldPlay=0 = faithful
  # silence). PATHS/ids only, never the message text. Inert: no slice line is voiced.
  "speech-probe|--map denbus2.map --speech-probe 0 0 dcmetz01 --speech-probe 46 100 -"
  # Per-map content-coverage smoke scan: object census + the unwired-external surface each map's scripts
  # fire (map_enter + map_update). The silent-quest-gap detector for adding a new city — and the cross-map
  # regression net for the ViewerGame.cs partial split (behaviour-preserving => these stay byte-identical).
  "smoke-artemple|--map artemple.map --smoke"
  "smoke-arcaves|--map arcaves.map --smoke"
  "smoke-denbus2|--map denbus2.map --smoke"
  "smoke-denbus1|--map denbus1.map --smoke"
  "smoke-kladwtwn|--map KLAMALL.map --smoke"
  # Vault City (P54): the first new location — all 4 maps fully covered (stubs=0) after wiring
  # day/debug_msg (M1) + elevation/obj_on_screen/critter_injure/anim (M2).
  "smoke-vctyctyd|--map vctyctyd.map --smoke"
  "smoke-vctydwtn|--map vctydwtn.map --smoke"
  "smoke-vctycocl|--map vctycocl.map --smoke"
  "smoke-vctyvlt|--map vctyvlt.map --smoke"
  # P54-M5: the VC dialogue VM runs end-to-end on real VC content (option counts only, never the
  # copyrighted dialogue text) — proves NPCs talk. Lynette (17100) + Greg (13705) on the Council.
  "vc-dialogue|--map vctycocl.map --iq-probe 17100 5 --iq-probe 13705 5 --rng-seed 1"
  # Gecko (P55): the SECOND new location — all 4 maps fully covered (stubs=0) for free (Vault City's
  # P54 wiring already covered the shared externals). Reachable via --travel 5 (Area 05, start_state=On).
  "smoke-gecksetl|--map gecksetl.map --smoke"
  "smoke-geckpwpl|--map geckpwpl.map --smoke"
  "smoke-geckjunk|--map geckjunk.map --smoke"
  "smoke-gecktunl|--map gecktunl.map --smoke"
  # P55-M2: scripted scenery now fires use_p_proc — the Gecko reactor terminal (script 515 @18677),
  # the reactor (529 @12666) and the valve (846 @16264) become usable (was a no-op 'picked:' fall-through).
  "gecko-reactor-use|--map geckpwpl.map --use-hex 18677 --use-hex 12666 --use-hex 16264 --rng-seed 1"
  # P55-M4: the Gecko reactor-quest dialogue VM runs (option counts only) — Gordon (18878) + a power-
  # plant NPC (24063). The reactor-optimize COMPLETION + the VC-McClure bridge (GVAR 82) are content
  # navigation, documented residuals (the machinery — use_p_proc + set_global_var + the GVARs — is wired).
  "gecko-dialogue|--map geckpwpl.map --iq-probe 18878 5 --iq-probe 24063 5 --rng-seed 1"
  # P55-M5: Lenny (138 @16701) talks AND is a real data\party.txt companion (member=1, levelMin=10) — so
  # recruitment is the proven Vic-pattern (the radscorpion 0x1000005 is the member=0 control). The recruit
  # DRIVE (navigating his quest-gated node) is the residual; the party_add machinery is wired.
  "gecko-lenny|--map gecksetl.map --iq-probe 16701 5 --party-probe 0x100006B --party-probe 0x1000005 --rng-seed 1"

  # Modoc (P56): the THIRD new location — all 4 maps fully covered (stubs=0) after M2 wired the last two
  # externals (set_map_start 0x80A8, kill_critter_type 0x80EE). Reachable for free via the generic ArriveAt
  # (city.txt [Area 03] Modoc, start_state=On, entrance_0 "Modoc Main Street" -> modmain.map). The quest
  # spine (talk/use_p/use_obj_on/use_skill_on/timed_event) was ALREADY wired; the only unwired procs Modoc
  # defines are map_exit_p_proc + push_p_proc — PRE-EXISTING engine-wide residuals (denbus2 already defines
  # them, never fired across the whole Arroyo->Den slice), not quest-blocking.
  "smoke-modmain|--map modmain.map --smoke"
  "smoke-modinn|--map modinn.map --smoke"
  "smoke-modwell|--map modwell.map --smoke"
  "smoke-modshit|--map modshit.map --smoke"
  # P56-M5: the Modoc dialogue VM runs end-to-end — Balthas (script 96 @12323, the "Jonny in the Well"
  # quest-giver) + Grisha (100 @28710). The well (miWell 572 @17520) fires its scripted use_p_proc (the
  # quest mechanic; scenery-use, P55-M2). All 6 Modoc GVARs (TOWN_REP 52, JONNY_STATE 114, JONNY_TILE 115,
  # TOOL_FLAG 118, ROSE_FLAG 123, JONNY_HOME 129) are 0 on a fresh game. The quest DRIVE (navigating
  # Jonny's rescue) is content — the documented residual; the machinery is wired.
  "modoc-dialogue|--map modmain.map --iq-probe 12323 5 --iq-probe 28710 5 --rng-seed 1"
  "modoc-well|--map modmain.map --use-hex 17520 --rng-seed 1"

  # Broken Hills (P57): the FOURTH new location. The TOWN proper (BROKEN1/BROKEN2) was ALREADY stubs=0 —
  # zero new town externals (cheaper than Modoc). M1 wired the only two genuinely-new externals, both on the
  # random-encounter SUB-maps: set_exit_grids (0x80E6 — retarget exit-grid objects on an elevation; bhrnddst)
  # + wield_obj_critter (0x80DA -> opWieldItem — a critter equips an item, weapon->right-hand via EquipWeapon;
  # bhrndmtn arms its 4 spawned critters). Both INERT on every shippable map (no golden loads a BH map) ->
  # all combat + prior encounter goldens BYTE-IDENTICAL. Reachable free via ArriveAt ([Area 06], start_state=
  # On, entrance_0 "Broken Hills 1" -> BROKEN1; entrance_1 -> BROKEN2 is a STATIC exit grid, no code).
  "smoke-broken1|--map BROKEN1.map --smoke"
  "smoke-broken2|--map BROKEN2.map --smoke"
  "smoke-bhrnddst|--map bhrnddst.map --smoke"
  "smoke-bhrndmtn|--map bhrndmtn.map --smoke"

  # New Reno (P58): the FIFTH new location + the biggest yet (11 maps, the mob-family city). M0 reachable
  # free via ArriveAt ([Area 07], start_state=On, entrance_0 "New Reno 1" tile 25105 -> Newr1; inter-map
  # movement is STATIC exit grids, no code). M1 wired the FIVE genuinely-new externals (the most of any city,
  # all ported verbatim, all INERT on the slice -> combat + prior encounter goldens BYTE-IDENTICAL):
  # obj_art_fid (0x8149, query->Fid; Newr2), critter_is_fleeing (0x8151, Maneuver&0x04) + critter_set_flee_
  # state (0x8152, set/clear the bit; Newr4/Newrst), mark_area_known (0x80B2, reveal a worldmap area; INERT
  # since all NR areas start On; Newrcs/Newrst/Newrgo) + game_time_advance (0x80FC, +ticks then the poison/
  # drug/withdrawal catch-up; NewRvb). No new proc (16/15 families, all wired). Smoke subset = the strip +
  # the 5 stub-driver maps (the other 5 NR maps are trivially stubs=0).
  "smoke-newr1|--map Newr1.map --smoke"
  "smoke-newr2|--map Newr2.map --smoke"
  "smoke-newr4|--map Newr4.map --smoke"
  "smoke-newrst|--map Newrst.map --smoke"
  "smoke-newrvb|--map NewRvb.map --smoke"
  "smoke-newrcs|--map Newrcs.map --smoke"
  # P58-M2: the NR dialogue VM runs (Newr1 NPCs script 452 @11280 = 4 options, 326 @12114 = 2). Myron (the
  # Mordino chemist, script 436 @19327 on Newrst) IS a real data\party.txt member (member=1, levelMin=6) so
  # recruitment is the proven Vic/Lenny/Marcus party_add machinery (the radscorpion 0x1000005 is the member=0
  # control). NR GVARs on a fresh game: TOWN_REP_NR 55 / MADE_MAN 230 / PRIZEFIGHTER 231 / PORN_STAR 232 /
  # MYRON 284 = 0, but the FOUR crime-family counters (SALVATORE 134 / BISHOP 135 / MORDINO 136 / WRIGHT 216)
  # seed to 100 in vault13.gam (already written by SeedGlobalVars; they count DOWN as you wrong a family) —
  # the gvar-seed golden below asserts them. The quest DRIVE (made-man / prizefighter / Myron's Jet-lab
  # recruit) is content — the residual; the machinery (dialogue VM + the family GVARs + party_add) is wired.
  "nr-dialogue|--map Newr1.map --iq-probe 11280 5 --iq-probe 12114 5 --rng-seed 1"
  "nr-myron|--map Newrst.map --party-probe 0x10000A0 --party-probe 0x1000005 --rng-seed 1"

  # NCR (P59): the SIXTH new location — and the CHEAPEST: ZERO new engine code. The now-large wired set
  # (VC+Gecko+Modoc+BH+NR) covers everything NCR's scripts fire, so all 5 maps are stubs=0 with no wiring.
  # (P66 CORRECTION: smoke-encrctr was originally mis-grouped here — ENCRCTR is the ENCLAVE REACTOR, a false
  # grep-match on "eNCRctr"; it's now correctly in the Oil Rig block below. NCR proper = NCR1-4 + NCRENT.)
  # M0 reachable free ([Area 10], start_state=On, entrance_0 "NCR: Bazaar" via ArriveAt; inter-map = static
  # exit grids). NO new external. NO new proc: NCR1 DEFINES combat_is_over_p_proc (SCRIPT_PROC_COMBAT_IS_OVER
  # =27, script 447 SCCop) but that enum slot is VESTIGIAL — the engine NEVER scriptExecProc's it anywhere
  # (scripts.h:76-77 are the only refs), so Hexwaste faithfully NOT firing it is CORRECT, not a gap (same for
  # combat_is_starting=26). All NCR GVARs (TOWN_REP_NCR 57 + the quest flags 168/170/172/196) are 0 on a
  # fresh game (no P58-style non-zero seed). No party.txt companion (no classic recruit in NCR). The dialogue
  # VM runs (NCR1 script 582 @14725 = 5 options, 466 @18720 = 4). Quest drive (Tandi / the Vault-15 squatters
  # / the brahmin-rustling) = content residual; the machinery is wired.
  "smoke-ncr1|--map NCR1.MAP --smoke"
  "smoke-ncr2|--map NCR2.MAP --smoke"
  "smoke-ncr3|--map NCR3.MAP --smoke"
  "smoke-ncr4|--map NCR4.MAP --smoke"
  "smoke-ncrent|--map NCRENT.MAP --smoke"
  "ncr-dialogue|--map NCR1.MAP --iq-probe 14725 5 --iq-probe 18720 5 --rng-seed 1"

  # San Francisco (P60): the SEVENTH new location — and the SECOND straight ZERO-engine-code city (the wired
  # set now covers it outright, the NCR steady-state). All 7 maps (SFChina/SFChina2 the Shi Chinatown, SFDock,
  # SFElronb the Hubologist base, SFTanker the PMV Valdez, + 2 shuttle maps) census stubs=0 with NO wiring.
  # M0 reachable free ([Area 14], start_state=On, entrance_0 "San Fran China" via ArriveAt; inter-map = static
  # exit grids). NO new external, NO new proc (15/14 wired families, no engine-dead-proc trap), NO seeding
  # (TOWN_REP_SF 61 + the SAN_FRAN_* quest flags 361/363/365/366/444 all 0 on a fresh game), no party.txt
  # companion (no classic recruit in SF). The dialogue VM runs (SFChina script 813 @20504 = 5 options, 819
  # @20703 = 5). Quest drive (the Shi/Hubologist faction war, the tanker's fuel/nav for the endgame) = content
  # residual; the machinery is wired.
  "smoke-sfchina|--map SFChina.map --smoke"
  "smoke-sfchina2|--map SFChina2.map --smoke"
  "smoke-sfdock|--map SFDock.map --smoke"
  "smoke-sfelronb|--map SFElronb.map --smoke"
  "smoke-sftanker|--map SFTanker.map --smoke"
  "smoke-sfshutl1|--map SFSHUTL1.map --smoke"
  "smoke-sfshutl2|--map SFSHUTL2.MAP --smoke"
  "sf-dialogue|--map SFChina.map --iq-probe 20504 5 --iq-probe 20703 5 --rng-seed 1"

  # Redding (P61): the EIGHTH new location — and the THIRD straight ZERO-engine-code city (the steady-state).
  # All 6 maps (REDDOWN downtown, REDDTUN tunnels, REDMENT/REDMTUN the Kokoweef mine, REDWAME/redwan1 the
  # Wanamingo mine) census stubs=0 with NO wiring. M0 reachable free ([Area 13], start_state=On, entrance_0
  # "Redding Downtown" via ArriveAt; inter-map = static exit grids). NO new external, NO new proc (13 wired
  # families). THE P58 TRAP STRUCK AGAIN: GVAR_TOTAL_WANAMINGOS (461) seeds to 20 on a fresh game (the mine's
  # initial creature count, already written by SeedGlobalVars — you clear them for the quest), NOT 0; the
  # other Redding GVARs (TOWN_REP_REDDING 56 / QUEST_REDDING_PROBLEM 94 / MAYOR 334 / SHERIFF 387 /
  # WANAMINGO_OCCUPADO 389) are 0. No party.txt companion. The dialogue VM runs (REDDOWN script 809 @17063 =
  # 5 options, 681 @15312 = 4). Quest drive (the mine-ownership war / the Wanamingo extermination / Jet) =
  # content residual; machinery wired. (gvar-seed below extended to assert TOTAL_WANAMINGOS=20.)
  "smoke-reddown|--map REDDOWN.MAP --smoke"
  "smoke-reddtun|--map REDDTUN.MAP --smoke"
  "smoke-redment|--map REDMENT.MAP --smoke"
  "smoke-redmtun|--map REDMTUN.MAP --smoke"
  "smoke-redwame|--map REDWAME.MAP --smoke"
  "smoke-redwan1|--map redwan1.map --smoke"
  "redding-dialogue|--map REDDOWN.MAP --iq-probe 17063 5 --iq-probe 15312 5 --rng-seed 1"

  # Vault 15 (P62): the NINTH new location — the FOURTH straight ZERO-engine-code city, but (unlike NCR/SF)
  # it HAS a companion. All 4 maps (VAULT15 the squatter camp / "The Squat A", V15ENT the entrance, V15SENT
  # the east entrance, V15_ORIG the deep original-vault levels) census stubs=0 with NO wiring. M0 reachable
  # free ([Area 09], start_state=On, entrance_0 "The Squat A" via ArriveAt; inter-map = static exit grids).
  # NO new external, NO new proc (15/14 wired families). All Vault 15 GVARs 0 on a fresh game (TOWN_REP_VAULT_
  # 15 294 / V15_SEED_STATUS 293 / V15_DARION_DEAD 172 / V15_KILL_DARION 474 — no non-zero seed this time).
  # COMPANION: pid 0x10000A2 (script 556 @12684) IS data\party.txt [Party Member 7] pMDoc (member=1, levelMin
  # =0) -> recruitment is the proven Vic/Lenny/Marcus/Myron party_add machinery (radscorpion 0x1000005 = the
  # member=0 control). The dialogue VM runs (the Doc @12684 = 2 options, script 583 @14084 = 2). Quest drive
  # (Darion's raiders / the NCR squatter deal / the Doc recruit) = content residual; the machinery is wired.
  "smoke-vault15|--map VAULT15.MAP --smoke"
  "smoke-v15ent|--map V15ENT.map --smoke"
  "smoke-v15sent|--map V15SENT.MAP --smoke"
  "smoke-v15orig|--map V15_ORIG.map --smoke"
  "v15-dialogue|--map VAULT15.MAP --iq-probe 12684 5 --iq-probe 14084 5 --party-probe 0x10000A2 --party-probe 0x1000005 --rng-seed 1"

  # Sierra Army Depot (P63): the TENTH new location — the FIRST in this batch to need engine code. 3 maps
  # (depolv1 the Battlefield, depolva Levels 1-3, depolvb Level 4); [Area 08], start_state=Off (a discovered-
  # via-quest location, not worldmap-visible from game start — maps load/walk directly; worldmap discovery is
  # via mark_area_known [P58], content-gated). depolva fired TWO new externals (now wired, all 3 maps stubs=0):
  # tile_contains_obj_pid (0x80BB, opTileContainsObjectWithPid — a query: 1 if any object at (tile,elev) has
  # the pid; ALSO fired by artemple, so smoke-artemple re-recorded as the stub drops) + animate_stand_reverse_
  # obj (0x80CD, opAnimateStandReverse — cosmetic !combat-gated stand anim; the engine plays it REVERSED, we
  # play forward via the P54 Anim path, a documented Draw-only simplification). NO new proc. Sierra GVARs all
  # 0 on a fresh game (TOWN_REP_SIERRA 53 / contamination-timer 149 / 150/152/153/157 — no seed trap). It's a
  # robot/combat DUNGEON (no dialogue talkers — Skynet's dialogue is content-gated behind assembling the body),
  # but SKYNET (pMCyberdog, pid 0x1000088) IS a data\party.txt member (member=1, levelMin=9) -> the Sierra
  # companion (radscorpion 0x1000005 = the member=0 control). Quest drive (assemble Skynet / the brain-bot
  # fight / the evac holodisk) = content residual; the machinery is wired.
  "smoke-depolv1|--map depolv1.map --smoke"
  "smoke-depolva|--map depolva.map --smoke"
  "smoke-depolvb|--map depolvb.map --smoke"
  "sierra-skynet|--map depolva.map --party-probe 0x1000088 --party-probe 0x1000005 --rng-seed 1"

  # Military Base / Mariposa (P64): the ELEVENTH new location — back to ZERO-engine-code. 3 maps (mbclose the
  # caved-in entrance, mbase12 Levels 1-2, mbase34 Levels 3-4) census stubs=0 with NO wiring. M0 reachable free
  # ([Area 12], start_state=On, entrance_0 "Military Base Entrance" via ArriveAt; inter-map = static exit
  # grids). NO new external, NO new proc (14 wired families). It's a pure super-mutant COMBAT DUNGEON: no
  # dialogue talkers (the mutants are combat-only at IN 5), no party.txt companion, and the single GVAR
  # (MILITARY_BASE_FLAGS 215) is 0 on a fresh game. So the deliverable is reachable + walkable + fully wired
  # (smoke-only). Quest drive (fight through to the FEV vats / destroy the base / Melchior) = content residual.
  "smoke-mbclose|--map mbclose.map --smoke"
  "smoke-mbase12|--map mbase12.map --smoke"
  "smoke-mbase34|--map mbase34.map --smoke"

  # Navarro (P65): the TWELFTH new location — the Enclave coastal base. ZERO-engine-code (even the Enclave
  # endgame base is covered by the wired set). 1 big map (NAVARRO; patch000.dat override -> the VFS resolves
  # the patch). M0 reachable: [Area 15], start_state=Off (discovered-via-quest like Sierra; the map loads/walks
  # directly, worldmap discovery via mark_area_known [P58], content-gated). NO new external, NO new proc (16
  # wired families incl. the map_exit/push residuals). Enclave GVARs all 0 on a fresh game (TOWN_REP_ENCLAVE
  # 62 / ENCLAVE_TIMER 434 / 431/432/441 — no seed trap). The dialogue VM runs (script 721 @25900 = 2 options;
  # most NPCs are hostile Enclave soldiers, silent at IN 5). COMPANION: K-9 (the cyberdog) is content-gated —
  # the pMCyberdog body (pid 0x1000088, the SAME party.txt member=1 that Skynet can use) is NOT a static
  # NAVARRO critter (the in-world K-9 swaps to the body on recruit), so the machinery is wired but the recruit
  # is content. Quest drive (steal the vertibird plans / the FEV / disguise as Enclave) = content residual.
  "smoke-navarro|--map NAVARRO.map --smoke"
  "navarro-dialogue|--map NAVARRO.map --iq-probe 25900 5 --rng-seed 1"

  # Enclave Oil Rig (P66): the THIRTEENTH new location + the FINAL endgame map — ZERO-engine-code (the
  # "Enclave = external-risk" prediction was wrong twice; the wired set covers the WHOLE game). 7 maps (encdock
  # the dock arrival, encdet Detention, encgd Guard Barracks, encpres Presidential [Richardson], encrctr the
  # Reactor, enctrp the Trap Room, encfite the End Fight [Frank Horrigan]; ENCPRES has a patch000 override).
  # M0 reachable: [Area 16] Enclave, start_state=Off (endgame; maps load/walk directly, worldmap discovery via
  # mark_area_known [P58], content-gated). NO new external (all 7 stubs=0), NO new proc (encfite=14 / encpres=13
  # wired families, no engine-dead-proc trap). Enclave/Oil-Rig GVARs all 0 on a fresh game (ENCLAVE_ALARM 433 /
  # REACTOR 435 / COMPUTER 440 / MARTIN 441 — no seed trap). No party.txt companion (the endgame). The dialogue
  # VM runs on the Presidential level (script @12320 = 3 options, @13684 = 3 — President Richardson + the Enclave
  # computer/advisor; the detention/soldier NPCs are silent at IN 5). smoke-encrctr is here (NOT NCR — the P59
  # false-match fix; it's the Enclave Reactor). Quest drive (the FEV/self-destruct / Horrigan / Richardson) =
  # content residual; the machinery is wired. The game's full original-map set now loads + walks + talks.
  "smoke-encdock|--map encdock.map --smoke"
  "smoke-encdet|--map encdet.map --smoke"
  "smoke-encgd|--map encgd.map --smoke"
  "smoke-encpres|--map encpres.map --smoke"
  "smoke-encrctr|--map ENCRCTR.MAP --smoke"
  "smoke-enctrp|--map enctrp.map --smoke"
  "smoke-encfite|--map encfite.map --smoke"
  "oilrig-dialogue|--map encpres.map --iq-probe 12320 5 --iq-probe 13684 5 --rng-seed 1"
  # P57-M2: the BH dialogue VM runs — Marcus (script 599 @18284, the mutant sheriff) 7 options + a townsfolk
  # (594 @10685) 5; Marcus is a real data\party.txt member (member=1, levelMin=12) so recruitment is the
  # proven Vic/Lenny party_add machinery (NOT custom content). BROKEN1/2 proc census = 14 families, all the
  # quest spine already wired (map_exit/push the pre-existing residuals). All 6 BH GVARs (TOWN_REP 54, FRAUD
  # 147, ENEMY 309, READ_FRANCIS_NOTE 524, MARCUS_DEAD 526, CARAVAN 562) are 0 on a fresh game; no seeding.
  # The quest DRIVE (uranium fraud / Francis / Marcus recruit) is content — the residual; machinery is wired.
  "bh-dialogue|--map BROKEN1.map --iq-probe 18284 5 --iq-probe 10685 5 --party-probe 0x10000A1 --rng-seed 1"

  # P69-M1: the Awareness perk gates the examine combat-intel. Hexwaste previously showed a critter's HP
  # unconditionally; now examine shows HP/AC + the wielded weapon ONLY with PERK_AWARENESS (proto_instance.cc
  # :294). State-only probe (hex + perk rank + hpLine/weaponLine booleans, never the copyrighted name text):
  # Metzger (denbus2 @15278, wields a weapon) shows hpLine=0/weaponLine=0 WITHOUT the perk, then after
  # --perk-probe 0 6 grants Awareness (minLevel 3), hpLine=1/weaponLine=1. No golden examines a critter via the
  # player path, so all combat + prior encounter goldens BYTE-IDENTICAL.
  "awareness-perk|--map denbus2.map --awareness-probe 15278 --perk-probe 0 6 --awareness-probe 15278 --rng-seed 1"
)

# Keep only the deterministic transcript lines (drop map-load / animate / stub /
# dialog-text noise — NEVER capture REPLY/OPTION game-asset strings).
FILTER='^(encounter|travel-from|companion|dismiss-persist|trade:|party:|party-count:|set-global:|hud-click:|use-skill:|has-skill:|steal:|maxhp:|hurt:|rest:|automap:|reveal:|taunt:|weapon-mode:|panel-click:|menu-click:|travel-resume:|travel-step:|travel-save-mid:|worldmap-fog:|weight:|iq-probe:|death-probe:|trait-probe:|perk-probe:|perk-pick:|combat-walk:|light:|map-update:|drag-equip:|save-slot:|load-slot:|slots:|aim-click:|tactics:|reg-anim:|encounter-fight:|brawl:|brawl-watch:|sneak-probe:|sneak-roll:|backstab-probe:|detect-probe:|karma-probe:|set-karma:|rep-title:|town-rep:|karma-titles:|get-global:|place-probe:|reg-anim-move:|critter-state:|hurt-too-much:|run-probe:|outline:|ac-dodge:|sfx-probe:|reaction-probe:|combat-proc:|combat-proc-hit:|poison-tick:|multihex-probe:|drug-probe:|addict-probe:|kills-probe:|book:|ammo-select:|unload:|ai-heal-probe:|ai-weapon-probe:|float-text:|speech-probe:|smoke:|scenery-use@|party-probe:|awareness-probe:|  spawn|  flat|  wait:|  follow:|  dismiss:|  rejoin:)'

echo "Building viewer..."
dotnet build src/Hexwaste.Viewer -c Debug >/dev/null || { echo "build failed"; exit 2; }

run() {
  timeout 90 env DISPLAY="${DISPLAY:-:0}" FALLOUT2_DIR="$GAME" \
    dotnet run --project src/Hexwaste.Viewer -c Debug --no-build -- \
    --game-dir "$GAME" --no-audio $1 2>/dev/null | grep -E "$FILTER"
}

fail=0
for entry in "${SCENARIOS[@]}"; do
  name="${entry%%|*}"; args="${entry#*|}"
  out="$(run "$args")"
  if [ "$MODE" = "record" ]; then
    printf '%s\n' "$out" > "$FIX/$name.txt"
    echo "recorded $name ($(printf '%s\n' "$out" | wc -l | tr -d ' ') lines)"
    continue
  fi
  out2="$(run "$args")"            # determinism: second run must match the first
  if [ "$out" != "$out2" ]; then
    echo "NONDETERMINISTIC: $name"; fail=1
  fi
  if [ ! -f "$FIX/$name.txt" ]; then
    echo "MISSING FIXTURE: $name (run 'record' first)"; fail=1; continue
  fi
  if diff -u "$FIX/$name.txt" <(printf '%s\n' "$out") >/dev/null; then
    echo "ok  $name"
  else
    echo "REGRESSION: $name"; diff -u "$FIX/$name.txt" <(printf '%s\n' "$out") | head -30; fail=1
  fi
done

[ "$fail" -eq 0 ] && echo "golden encounter: ALL PASS" || echo "golden encounter: FAILURES"
exit $fail
