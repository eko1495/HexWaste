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
  "gvar-seed|--map artemple.map --create 5,5,5,5,5,5,5:0,4,5:0 --get-global 0 --get-global 47 --get-global 619 --get-global 134 --rng-seed 1"
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
)

# Keep only the deterministic transcript lines (drop map-load / animate / stub /
# dialog-text noise — NEVER capture REPLY/OPTION game-asset strings).
FILTER='^(encounter|travel-from|companion|dismiss-persist|trade:|party:|party-count:|set-global:|hud-click:|use-skill:|hurt:|rest:|automap:|weapon-mode:|panel-click:|menu-click:|travel-resume:|travel-step:|travel-save-mid:|worldmap-fog:|weight:|iq-probe:|death-probe:|trait-probe:|perk-probe:|perk-pick:|combat-walk:|light:|map-update:|drag-equip:|reg-anim:|encounter-fight:|brawl:|sneak-probe:|sneak-roll:|backstab-probe:|detect-probe:|karma-probe:|set-karma:|rep-title:|town-rep:|karma-titles:|get-global:|place-probe:|reg-anim-move:|critter-state:|hurt-too-much:|run-probe:|outline:|sfx-probe:|reaction-probe:|combat-proc:|combat-proc-hit:|poison-tick:|multihex-probe:|drug-probe:|addict-probe:|kills-probe:|book:|ammo-select:|unload:|ai-heal-probe:|ai-weapon-probe:|float-text:|  spawn|  flat|  wait:|  follow:|  dismiss:|  rejoin:)'

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
