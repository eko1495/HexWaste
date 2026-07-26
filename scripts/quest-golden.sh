#!/usr/bin/env bash
# End-to-end QUEST regression net — P103 (proof-of-concept quest e2e driver).
#
# Drives a real quest to completion through the actual game logic (dialogue VM + set_global_var — NOT
# --set-global faking) and asserts the quest lifecycle: the quest GVAR advances and the Pip-Boy quest log
# flips active→completed. This is the template for turning the ~150-quest manual QA into repeatable goldens.
#
# Captured lines are STATE/ID only (get-global / quest-item / quest-probe / party) — never the copyrighted
# dialogue text (the --talk-seq reply/option text is filtered out).
#
# Usage:  scripts/quest-golden.sh [check|record]   (default: check)
# Requires a real display (MonoGame) + game data (FALLOUT2_DIR, default ./game-data).
set -uo pipefail
cd "$(dirname "$0")/.."

MODE="${1:-check}"
GAME="${FALLOUT2_DIR:-$(pwd)/game-data}"
FIX="tests/golden-quest"
mkdir -p "$FIX"

CREATE="--create 5,5,5,5,5,5,5:0,4,5:0"

# The Klamath + Den quest suite. Each scenario drives a real quest via the dialogue VM (set_global_var —
# NOT --set-global faking) + asserts the lifecycle: --get-global (before→after) + --quest-probe (the Pip-Boy
# quest flips hidden→active, and →completed where the quest completes in one interaction). GVARs/hexes are
# from the discovery workflow (verified via MapDump/ProcAnalyze); dialogue option paths nailed with --talk-seq.
# Captured lines are STATE/ID only — never the copyrighted dialogue text.
#
# name | harness args
SCENARIOS=(
  # Free Vic (Den) — FULL lifecycle: new-game seeds 619=1 (active); buying Vic's freedom from Metzger
  # (2000 caps + his radio, then the 3-NPC dialogue) drives 619→2 AND the Den companion quest 100 0→1→2
  # (both completed) + Vic joins. The get-global pairs bracket each quest's before/after.
  "quest-free-vic|$CREATE --goto-map denbus2.map --give 41:2000 --give 266:1 --get-global 619 --get-global 100 --talk-seq 17070 1,1,1 --talk-seq 15278 2,2,1,1 --talk-seq 17070 2,1 --get-global 619 --get-global 100 --quest-probe --party-count --rng-seed 1"
  # Smitty's car part (Den, GVAR 550) — ACCEPT: the car-part dialogue branch activates the quest 0→1
  # (completion is item-gated: bring Smitty the fuel-cell controller). Asserts the quest goes active.
  "quest-smitty-carpart|$CREATE --goto-map denbus1.map --get-global 550 --talk-seq 22137 1,1,1,1,1 --get-global 550 --quest-probe --rng-seed 1"
  # Torr's guard-the-brahmin (Klamath, GVAR 182) — ACCEPT: agreeing to guard Torr's brahmin activates the
  # quest 0→1 (completion is at the grazing fields). Asserts the quest goes active.
  "quest-torr-brahmin|$CREATE --goto-map kladwtwn.map --get-global 182 --talk-seq 24291 1,1 --get-global 182 --quest-probe --rng-seed 1"
  # KILL quest — Rat God (Klamath, GVAR 390): killing Keeng Ra'at (hex 25486, elev 2) fires its
  # destroy_p_proc which unconditionally sets 390=2 (completed). --kill drives the REAL death path
  # (CombatEngine.Kill → destroy_p_proc) deterministically — the quest logic is real, only the cause of
  # death is a debug shortcut (a fresh test char can't win the boss fight). FULL lifecycle 0→2.
  "quest-kill-ratgod|$CREATE --goto-map klaratcv.map:25486:2 --get-global 390 --kill 25486 --get-global 390 --quest-probe --rng-seed 1"
  # B3 (P138): the quest-driver's KILL pass auto-completes a destroy_p_proc quest. Killing Metzger
  # (denbus2 15278) fires his destroy_p_proc → GVAR_QUEST_VIC_DEVICE (100) := 2 (the aggressive Vic-
  # rescue path). --kill now finds the completer on ANY elevation, so a plain --goto-map replays. 0→2.
  "quest-kill-metzger|$CREATE --goto-map denbus2.map --get-global 100 --kill 15278 --get-global 100 --quest-probe --rng-seed 1"
  # B3 harvest (P138): clean destroy_p_proc kill-wins the driver auto-found across all 155 maps, then
  # replay-verified. Explicit "kill X" quests + a creature kill — unambiguous kill-completions.
  # Kill Darion, the Vault 15 raider leader (GVAR_V15_KILL_DARION 474) — vault15 23883. 0→2.
  "quest-kill-darion|$CREATE --goto-map vault15.map --get-global 474 --kill 23883 --get-global 474 --quest-probe --rng-seed 1"
  # Kill Elron, the NCR Hubologist leader (GVAR_NCR_KILL_ELRON_QST 486) — ncr1 22886. 0→2.
  "quest-kill-elron|$CREATE --goto-map ncr1.map --get-global 486 --kill 22886 --get-global 486 --quest-probe --rng-seed 1"
  # Kill Xarn the deathclaw at Navarro (GVAR_NAVARRO_XARN 554) — navarro 22900. 0→2.
  "quest-kill-xarn|$CREATE --goto-map navarro.map --get-global 554 --kill 22900 --get-global 554 --quest-probe --rng-seed 1"
  # Refuel Whiskey Bob's still (Klamath, GVAR 198) — FULL lifecycle 0→1→2→5, via the REAL path (no
  # --set-global). Buy Bob a drink + accept the still job (198→1); carry firewood (pid 286) to the still
  # shack south of town (klatrap, hex 20131) and use it (use_obj_on_p_proc → 198→2); return to Bob, who
  # thanks you on greeting (Node950 → 198→5, completed). Caps (41) fund the drink; the option sequence
  # 2,1,2,1,1,1 is the accept chain. Crosses kladwtwn↔klatrap, proving the quest GVAR persists across maps.
  "quest-bob-still|$CREATE --goto-map kladwtwn.map --give 41:500 --give 286:2 --get-global 198 --talk-seq 22687 2,1,2,1,1,1 --get-global 198 --goto-map klatrap.map --use-on 286:20131 --get-global 198 --goto-map kladwtwn.map --talk-seq 22687 1 --get-global 198 --quest-probe --rng-seed 1"
  # Anna's locket (Den, GVAR 551) — item-return via the REAL dialogue. Anna is a NIGHT-only ghost
  # (visible when game_time_hour <= 400, set in her map_update_p_proc), so --set-hour 2 makes her
  # appear; carrying the locket (item pid 252), giving it (talk opt 2 → Node007 obj_carrying check)
  # sets 551 0→2 (completed) and lays her to rest. Exercises the new --set-hour clock jump.
  "quest-anna-locket|$CREATE --goto-map denbus1.map --give 252:1 --set-hour 2 --get-global 551 --talk-seq 28105 2 --get-global 551 --quest-probe --rng-seed 1"
  # Rescue Smiley (Klamath, GVAR 197) — the first ESCORT golden, full lifecycle 0→2→3 via the REAL
  # script path (no --set-global). Smiley (klatoxcv elev1 hex 18651) joins as a team-follower
  # (dialogue 1,1,1,1,1 → his LVAR follow flag); the escort-sim verbs stand in for the physical walk:
  # --teleport puts the dude at the elev-0 cave-mouth delivery tile (18335), --escort-pump runs
  # Smiley's own critter_p_proc so it opCritterAttemptPlacement's him across floors to the dude and
  # fires leave_player (197→2); returning to Klamath downtown, its map_enter sees 197==2 → 197→3.
  "quest-smiley-rescue|$CREATE --goto-map klatoxcv.map:18651:1 --talk-seq 18651 1,1,1,1,1 --get-global 197 --teleport 18335 0 --escort-pump 18651 8 --get-global 197 --goto-map kladwtwn.map --get-global 197 --quest-probe --rng-seed 1"
  # Rustle the brahmin (Klamath, GVAR 102) — the first SCRIPTED-EVENT golden, full lifecycle 0→1→2
  # via the real load_map path. Accepting Torr's guard job (talk 24291 1,1,1) runs load_map(klagraz,
  # 13); --pump-ms applies the deferred transition (kladwtwn→klagraz). On the fields, siding with the
  # Duntons (talk 16315 1,1,1, the side-with-the-Duntons accept) sets 102:=1; scaring Torr off (talk 17701
  # opt 2, his runtime tile after the override_map_start arrival) → Node930 → 102:=2 + 71:=1 (Torr
  # flees to the canyon). Exercises the dialogue-triggered scripted map transition end to end.
  "quest-torr-duntons|$CREATE --goto-map kladwtwn.map --talk-seq 24291 1,1,1 --pump-ms 4000 --talk-seq 16315 1,1,1 --get-global 102 --talk-seq 17701 2 --get-global 102 --get-global 71 --quest-probe --rng-seed 1"
  # Rescue Torr (Klamath, GVAR 391) — closes Klamath 6/6. The full chain: the klagraz event
  # (as in quest-torr-duntons) displaces Torr (71:=1); back in town his mother Ardin (22885,
  # 1,1,1,1,1,1) asks you to find him → 391:=1; the canyon Torr now appears (KLACANYN 15287, needs
  # 71:=1 AND 391:=1) → his follow-me option (talk 1,1 → Node940 follow flag); the escort-sim
  # (--teleport to delivery tile 19450 + --escort-pump) fires his leave_player → 391:=2. --rng-seed
  # fixes the event-repositioned tiles. Exercises the load_map event + Ardin activation + escort-sim.
  "quest-torr-rescue|$CREATE --rng-seed 1 --goto-map kladwtwn.map --talk-seq 24291 1,1,1 --pump-ms 4000 --talk-seq 16315 1,1,1 --talk-seq 17701 2 --pump-ms 2000 --goto-map kladwtwn.map --talk-seq 22885 1,1,1,1,1,1 --get-global 391 --goto-map KLACANYN.map --pump-ms 1500 --talk-seq 15287 1,1 --teleport 19450 0 --escort-pump 15287 10 --get-global 391 --quest-probe"
  # Deliver a meal to Smitty for Mom (Den, GVAR 450) — a clean two-NPC delivery, full lifecycle
  # 0→1→3 via the real dialogue (no --set-global). Accept from Mom (denbus2 24479, 2,1,1, the
  # delivery accept → 450:=1, she hands over the meal); deliver to Smitty (denbus1 22137, 2,1,
  # the meal hand-over option → Node008 → 450:=3, completed). Crosses denbus2→denbus1.
  "quest-mom-meal|$CREATE --goto-map denbus2.map --get-global 450 --talk-seq 24479 2,1,1 --get-global 450 --goto-map denbus1.map --talk-seq 22137 2,1 --get-global 450 --quest-probe --rng-seed 1"
  # Collect money from Fred (Den, GVAR 371) — a multi-NPC NEGOTIATION, full lifecycle 0→1→2 via
  # the real dialogue (no --set-global / no faked caps). Rebecca (denbus1 17662, work option 2,1,1)
  # sets the Fred-debt task (371:=1); Fred (denbus1 25479, 1,2,2,1,1,2,3 — demand the FULL amount
  # down his negotiation tree) pays the full $200 (his Node986 item_caps_adjust(200)) + sets the
  # 446 task bit; back at Rebecca, the job-done report chain (2,1,1,1 — the confirm option only
  # appears with caps>=200 AND the 446 bit) → Node011 → 371:=2 completed. The book sub-task (Derek,
  # desc 205) stays open — 371 is a shared gvar with two display thresholds.
  "quest-fred-money|$CREATE --goto-map denbus1.map --get-global 371 --talk-seq 17662 2,1,1 --get-global 371 --talk-seq 25479 1,2,2,1,1,2,3 --talk-seq 17662 2,1,1,1 --get-global 371 --quest-probe --rng-seed 1"
  # Find Cornelius's gold watch for Farrel (Modoc, GVAR 106) — first Modoc golden. Item-return,
  # full lifecycle 0→4→8 via the real dialogue. Farrel (modinn 25088), accused of stealing the
  # watch, hooks the quest via his watch-defense greeting (3,1,1, his help-me-find-it ask →
  # 106:=4); carrying the gold pocket watch (item 257) his greeting adds opt4 (present the
  # watch → his confirmation) → 106:=8 (completed, clears his name). The watch acquire (found in the
  # outhouse) is the --give shortcut, like Anna's locket. 106 is Farrel-side; 105 is Cornelius-side.
  "quest-modoc-watch|$CREATE --goto-map modinn.map --get-global 106 --talk-seq 25088 3,1,1 --get-global 106 --give 257:1 --talk-seq 25088 4,1 --get-global 106 --quest-probe --rng-seed 1"
  # Clear the garden rats for Farrel (Modoc, GVAR 110) — second Modoc golden, same NPC's OTHER
  # branch off his shared greeting (chain 2,2,1 vs the watch branch's 3,1,1). Accepting his
  # vermin-help ask (2,2) activates the quest 0→4 (his Node994 accept). The discriminator between
  # the two completion writes (:=3 undone / :=8 confirmed) is NOT tracked in Farrel's own script:
  # each garden-rat critter (mcRat, 10 instances on modgard) decrements a shared per-map counter on
  # its destroy_p_proc; when the counter reaches zero the last rat's death sets GVAR 297 bit 0x80
  # once (one-time bonus xp guard). Farrel's report option (opt2 off his post-activation greeting)
  # branches on that same 297 bit to pick the confirmed completion — 110:=8. Killing all ten rat
  # tiles (--kill, real destroy_p_proc) before reporting is what flips the bit; reporting early
  # (bit unset) would instead land the undone completion (:=3, not exercised here — real path only).
  "quest-modoc-rats|$CREATE --goto-map modinn.map --get-global 110 --talk-seq 25088 2,2,1 --get-global 110 --goto-map modgard.map --kill 14494 --kill 14696 --kill 16892 --kill 17098 --kill 17680 --kill 18684 --kill 21899 --kill 22894 --kill 23887 --kill 24087 --goto-map modinn.map --talk-seq 25088 2,2 --get-global 110 --quest-probe --rng-seed 1"
  # Cornelius's side of the watch quest (Modoc, GVAR 105) — third Modoc golden, the OTHER half of
  # the shared watch quest (106 is Farrel-side). The activation write (105:=4) is gated behind a
  # dedicated Cornelius sub-branch (his own more-questions topic loop → an accusation-acceptance
  # node), driven here BEFORE the Farrel accusation (the Node001 guard is 105==0 OR 106==0 —
  # Cornelius-first is the replay-proven ordering, not script-required). Once 105 is activated, accusing
  # Farrel (106:=4) and returning to Cornelius carrying the watch reaches his second-visit greeting,
  # whose report option now finds 105 already in its activated range and completes both 105:=8 and
  # 106:=8 together (the same node Farrel's own golden uses for 106, but the completion write it
  # makes for 105 depends on 105's value going in — untouched, it stalls at 105:=3, which is what
  # >15 earlier attempts hit). Full lifecycle 0→4→8, real dialogue only.
  "quest-cornelius-watch|$CREATE --goto-map modinn.map --get-global 105 --talk-seq 13490 1,1,5,3,1 --get-global 105 --give 257:1 --talk-seq 25088 3,1,1 --get-global 106 --talk-seq 13490 2 --get-global 105 --get-global 106 --quest-probe --rng-seed 1"
  # Deliver beer & booze to Lydia (Vault City, GVAR 497) — first VC golden. Item-delivery, full
  # lifecycle 0→1→2 via the real dialogue. Lydia (vctydwtn 26306) laments VC's synthetic-only
  # booze; down the drinks-menu → real-alcohol chain (1,1,1,1,1,1) she asks for a case of ten
  # each → 497:=1; carrying 10 beer (124) + 10 booze (125), her info menu gains the
  # shipment-delivery opt6 (2,6 → Node032, obj_carrying check on 124+125) → 497:=2.
  "quest-lydia-booze|$CREATE --goto-map vctydwtn.map --get-global 497 --talk-seq 26306 1,1,1,1,1,1 --get-global 497 --give 124:10 --give 125:10 --talk-seq 26306 2,6 --get-global 497 --quest-probe --rng-seed 1"
  # Deliver tools to Valerie (Vault City, GVAR 493) — item-delivery, full lifecycle 0→1→2. The
  # grumpy VC maintenance worker Valerie (vctydwtn 21096) needs a wrench + pliers for her failing
  # lathe; down her repair chain (1,1,1,1,1,1,1) she agrees to let you look → 493:=1; carrying the
  # wrench (384) + pliers (75), her greeting adds the tools-in-hand option (1,1 → Node023 obj_carrying
  # check on 384+75) → 493:=2.
  "quest-valerie-tools|$CREATE --goto-map vctydwtn.map --get-global 493 --talk-seq 21096 1,1,1,1,1,1,1 --get-global 493 --give 384:1 --give 75:1 --talk-seq 21096 1,1 --get-global 493 --quest-probe --rng-seed 1"
  # Get a plow for Mr Smith (Vault City, GVAR 80) — a two-NPC purchase chain, 0→3→6. Smith
  # (vctyctyd 14078), a poor farmer denied VC citizenship, needs a plow; accept (2,1,1) then commit
  # on a re-talk (4,1,1, the take-the-money commit → she points at the gun-store seller → 80:=3,
  # which unlocks Harry's plow line). Harry (VCHarry 12513) then exposes his plow-sale opener
  # (only at 80>=3); buy it for $800 (2,2,1 → deal → his deliver-to-the-Smiths close) → 80:=6. Caps
  # via --give 41:1000. Exercises a gvar-gated cross-NPC option (Harry's line appears only at 80=3).
  "quest-smith-plow|$CREATE --goto-map vctyctyd.map --get-global 80 --talk-seq 14078 2,1,1 --talk-seq 14078 4,1,1 --get-global 80 --give 41:1000 --talk-seq 12513 2,2,1 --get-global 80 --quest-probe --rng-seed 1"
  # Rescue Amanda's husband Joshua (Vault City, GVAR 459) — a 2-map, 2-NPC quest, 0→1→2→3. NOT an
  # escort: the "rescue" is a bribe. Amanda (vctyctyd 22673) — her jailed husband is a VC Servant;
  # 1,1,1,1,1,1 → she names Officer Barkus → 459:=1. Barkus (vctydwtn 14896, the Servant Assignment
  # Center) — the looking-for-Joshua → negotiate-release chain (1,1,4,1) → his donation tiers; he's greedy
  # and only the $1000 offer (opt1) frees Joshua → 459:=2. Back to Amanda (greeting → Node019) →
  # 459:=3 completed. Caps via --give 41:5000. Crosses vctyctyd↔vctydwtn twice.
  "quest-rescue-joshua|$CREATE --goto-map vctyctyd.map --get-global 459 --talk-seq 22673 1,1,1,1,1,1 --get-global 459 --goto-map vctydwtn.map --give 41:5000 --talk-seq 14896 1,1,4,1,1,1,1 --get-global 459 --goto-map vctyctyd.map --talk-seq 22673 1 --get-global 459 --quest-probe --rng-seed 1"
  # --- Quests found + recipes auto-emitted by the --quest-drive-all batch census, then verified
  # --- fresh (docs/plan-quest-driver.md). Four towns the manual sweep hadn't reached.
  # Get super repair kit for Skeeter (Gecko, GVAR 393) — item 308 to Skeeter (geckjunk 24893).
  "quest-skeeter-kit|$CREATE --goto-map geckjunk.map --get-global 393 --give 308:10 --give 41:5000 --talk-seq 24893 1 --get-global 393 --quest-probe --rng-seed 1"
  # Deliver ten Cat's Paw magazines to Miss Kitty (New Reno, GVAR 501) — item 225 to ncKitty (newr1 23286).
  "quest-kitty-mags|$CREATE --goto-map newr1.map --get-global 501 --give 225:10 --give 41:5000 --talk-seq 23286 3,1,1,2,1,1 --get-global 501 --quest-probe --rng-seed 1"
  # The Slag/ghost-farm investigation (Modoc, GVAR 631) — Jo (mcJo, modmain 20143), item 263.
  "quest-modoc-ghostfarm|$CREATE --goto-map modmain.map --get-global 631 --give 263:10 --give 41:5000 --talk-seq 20143 1,1,3,1,1,1,1,1,1,1,1,2,1 --talk-seq 20143 1 --get-global 631 --quest-probe --rng-seed 1"
  # Jonny missing (Modoc, GVAR 693) — full lifecycle 0->1->2 via the real dialogue (no --set-global).
  # Balthas (mcBaltha, modmain 12323) has a personal-topic greeting branch that surfaces the missing-
  # son thread and activates 693:=1; that branch is gated on live Perception >=6 in the script (a real
  # stat check, not IQ). The fixed chargen SPECIAL is 5/5/5/5/5/5/5, so the option is hidden until a
  # Mentats dose (pid 53, +1 PE while active) is actually taken — the sanctioned --give + a real
  # --use-item, not --set-global faking. Activate (1,1,1,2). Completion is the found-BB-gun branch:
  # item pid 261 (origin: mcbaltha msg 172) carried back to Balthas and reported on his
  # follow-up greeting (1,1) -> 693:=2, completed. No live Jonny/Vegeir NPC needed for this route.
  "quest-jonny-rescue|$CREATE --give 53:1 --use-item 53 --give 261:1 --goto-map modmain.map --get-global 693 --talk-seq 12323 1,1,1,2 --get-global 693 --talk-seq 12323 1,1 --get-global 693 --quest-probe --rng-seed 1"
  # Break Manson & Franc out of prison (Broken Hills, GVAR 303) — multi-NPC (hcMarcus 18284), item 456.
  "quest-bh-jailbreak|$CREATE --goto-map broken1.map --get-global 303 --give 456:10 --give 41:5000 --talk-seq 10685 2,1,1,1 --talk-seq 29285 3,1 --talk-seq 18284 5,1,1 --get-global 303 --quest-probe --rng-seed 1"
  # --- Quests found by the FULL-MAP harvest (scripts/quest-harvest.sh across all 155 maps, then
  # --- recipe-replay-verified). Surfaces the 22-hub census never reached (NCR, Redding mine, SF).
  # NCR Vortis quest (GVAR_NCR_VORTIS_QUEST_STATE 195) — ncrVortis (ncrent 10518), item 343.
  "quest-ncr-vortis|$CREATE --goto-map ncrent.map --get-global 195 --give 343:10 --give 41:5000 --talk-seq 10518 2,1,1,1 --talk-seq 10518 1 --get-global 195 --quest-probe --rng-seed 1"
  # Redding excavator chip (GVAR_REDDING_EXCAVATOR_CHIP 332) — redment 15875/16306, item 422.
  "quest-redding-chip|$CREATE --goto-map redment.map --get-global 332 --give 422:10 --give 41:5000 --talk-seq 15875 4,3,1,1,1,1,1 --talk-seq 16306 4,1,1,1 --get-global 332 --quest-probe --rng-seed 1"
  # SF Elron/Lo Pan letter (GVAR_NCR_ENLONE_LETTER_QST 485) — sfelronb 15469, item 476.
  "quest-sf-elron|$CREATE --goto-map sfelronb.map --get-global 485 --give 476:10 --give 41:5000 --talk-seq 15469 2 --get-global 485 --quest-probe --rng-seed 1"
  # SF spleen quest (GVAR_SAN_FRAN_SPLEEN 367 → 9) — sftanker 23085 (also completable via dnslvrun 26310).
  "quest-sf-spleen|$CREATE --goto-map sftanker.map --get-global 367 --talk-seq 23085 1,1,1 --get-global 367 --quest-probe --rng-seed 1"
  # --- P137 bit-level prerequisite (the negotiation tier the gvar-level driver couldn't crack).
  # Rebecca's 371 completion gates on Fred's demand-full task bit: Fred SETS 446 & 0x8000, Rebecca
  # CHECKS it. Auto-discovered by --quest-drive 371 (drives Rebecca-activate → Fred → Rebecca-complete)
  # and replay-verified. A distinct, shorter path than the manual quest-fred-money above.
  "quest-rebecca-prereq|$CREATE --goto-map denbus1.map --give 471:10 --give 41:5000 --get-global 371 --talk-seq 17662 2,1,1 --talk-seq 25479 - --talk-seq 17662 2,1 --get-global 371 --quest-probe --rng-seed 1"
  # Deliver Moore's briefcase to Bishop (Vault City -> New Reno, GVAR 321) - the first CROSS-TOWN
  # delivery golden, full lifecycle 0->1->2 via the real dialogue (no --set-global). Moore
  # (vctydwtn 17485) hands over the locked briefcase on accepting (his devotion-test dialogue
  # chain 1,2,1,1,2,2,2 -> create_object 336 -> 321:=1). Reaching Bishop cold is a hostile dead
  # end (his greeting Node always ends in an ambush unless pre-vetted); the real path is via his
  # guard one floor down (newr2 elev1 17075), whose carrying-336-gated accept option (talk-seq 3)
  # sets the guard-vetted flag and waves you up. Bishop (newr2 elev2 17678) then takes the case via
  # his accept option (opt1) -> 321:=2, completed.
  # Crosses vctydwtn->newr2 elev1->newr2 elev2, proving the delivery pattern spans towns and floors.
  "quest-moore-briefcase|$CREATE --goto-map vctydwtn.map --get-global 321 --talk-seq 17485 1,2,1,1,2,2,2 --get-global 321 --goto-map newr2.map:17075:1 --talk-seq 17075 3,1 --goto-map newr2.map:17678:2 --get-global 321 --talk-seq 17678 1 --get-global 321 --quest-probe --rng-seed 1"
  # Sabotage Becky's still for Frankie (Den, GVAR 101) - full lifecycle 0->1->2->3->4, real
  # dialogue only (no --set-global on 101 or 445/446). THE SNAG (documented in task-1-report.md):
  # Frankie's price-branch option that starts this quest is gated by giq_option(iq=6, ...) -
  # ported semantics in reference/fallout2-ce src/interpreter_extra.cc _op_giq_option: a POSITIVE
  # iq arg requires critterGetStat(dude, STAT_INTELLIGENCE) + Smooth Talker rank >= iq, checked
  # BEFORE the option is even added - silently, no error. The standard $CREATE character has
  # Intelligence 5, one short, so the option never appears even once the 445/101 bit-conditions
  # are satisfied. Fix: a temporary Mentats (pid 53) dose (+INT) raises the dude to 6, satisfying
  # the check as a legitimate in-game action (not a stat edit) - $CREATE itself stays standard.
  # Chain: Rebecca (denbus1 17662) sells a $5 drink (need caps + the Mentats dose to unlock
  # Frankie's option) -> Frankie (denbus2 14716) price-branch accept (msg 173) -> 101:=1.
  # Rebecca's reveal option is gated on her OWN local var (drinks bought >= 4, not a global) -
  # buy 4 more $5 drinks, then ask; she reveals the still (446|=0x8000000, NOT the 445 bit the
  # spec guessed).
  # Report to Frankie -> 101:=2, he pays $100 + hands his crowbar (pid 20, sanctioned --give) ->
  # denbus1 ELEV1 tile 17062 --use-on 20 -> distill.int use_obj_on_p_proc -> 101:=3. Final report
  # to Frankie (Node012/993 region) -> 101:=4, quest-probe display=2 completed=1.
  "quest-becky-still|$CREATE --goto-map denbus1.map --give 53:1 --use-item 53 --give 41:500 --give 20:1 --get-global 101 --talk-seq 17662 1,1,2 --goto-map denbus2.map --talk-seq 14716 1,1,3,2,1 --get-global 101 --goto-map denbus1.map --talk-seq 17662 1,1,2 --talk-seq 17662 1,1,2 --talk-seq 17662 1,1,2 --talk-seq 17662 1,1,2 --talk-seq 17662 1,2,3 --get-global 445 --get-global 446 --goto-map denbus2.map --talk-seq 14716 1,1,1 --get-global 101 --goto-map denbus1.map:17062:1 --use-on 20:17062 --get-global 101 --goto-map denbus2.map --talk-seq 14716 1 --get-global 101 --quest-probe --rng-seed 1"
  # Lara's gang war (Den, GVAR 454) - FULL lifecycle 0->2->3->4->5->9->11 (quests.txt: all
  # four display/completed rows land, desc 207=1/2, 208=2/3, 209=4/5, 210=6/7). Real
  # dialogue + one real object interaction throughout, no --set-global anywhere. Ladder (traced
  # via ProcAnalyze --quest-paths 454 + operand-level int_disasm, node names + gvar values
  # only): dcLara Node008:=1 (accept the recon job) -> dcLara Node016:=2 -> dcMetzge
  # Node019:=3 (permission) -> dcLara Node023:=4 -> dcTyler Node020:=5 -> dcLara Node027:=6 ->
  # dcLara Node989:=7 / Node990:=9 (alternate branches off Node030) -> terminal :=10/11 via
  # destroy_p_proc on dcTyler/dcMarc/DCG1Grd/dcG2Grd/dcLara or their map_enter_p_proc/DenBus2
  # map_exit_p_proc fallbacks (all forward-only guarded: a write only applies if current 454
  # is lower).
  # KEY FINDING (--bit-scan 445 nailed the setter): dcLara's Node018 (the 454==1 greeting)
  # gates its report-success branch (continuing toward Node016 :=2) on GVAR445 bit 0x20000000.
  # That bit is set by diCrate.int's use_p_proc (any of the denbus2 graveyard crates, e.g.
  # tile 21731) - a one-time discovery bonus (+500 xp) on first use, unrelated-looking but
  # exactly the "find out what's inside" recon: using a crate BEFORE first talking to Lara at
  # denbus1 21514 unlocks her 3rd greeting option (Node006's msg-281 branch) on the
  # very first visit, short-circuiting straight to the report (Node011 -> low-IQ branch ->
  # Node015 -> Node016 :=2) - no return trip needed. From there: dcMetzge (denbus2 15278)
  # gets a new permission-request option once 454>=2 (dcmetzge.msg's chain) -> :=3; back to the
  # denbus1 21514 guard for the follow-up (msg-411 branch) -> :=4; dcTyler (denbus2 24534)
  # gets a new greeting once 454>=4, his chain (msg-451 branch) -> :=5; back to the denbus1
  # 21514 guard again (msg-471 branch, then msg-491's accept option) -> :=6, then immediately
  # -> :=9 (the Node030 opt0/Node990 branch - no further choice needed, dialogue ends). A
  # `--pump-ms` after re-entering denbus2 fires the map_enter_p_proc completion fallback
  # (the map script's own resolution message) -> :=11 (good outcome) with NO --kill required in this
  # branch - the scripted event resolves the war off-screen once 454==9.
  "quest-lara-war|$CREATE --goto-map denbus2.map:21731:0 --use-hex 21731 --goto-map denbus1.map --get-global 454 --talk-seq 21514 1,1,1,1,1,1,3 --get-global 454 --goto-map denbus2.map --talk-seq 15278 2,2,2,2 --get-global 454 --goto-map denbus1.map --talk-seq 21514 1,1,2 --get-global 454 --goto-map denbus2.map --talk-seq 24534 1,1,1 --get-global 454 --goto-map denbus1.map --talk-seq 21514 1,1,1 --get-global 454 --goto-map denbus2.map --pump-ms 3000 --get-global 454 --quest-probe --rng-seed 1"
  # B4 arc centerpiece: Gecko powerplant (GVAR 82) + the VC citizenship grant (GVAR 79/81).
  # Lynette (vctycocl 17100) citizenship-hub branch (msg 252) -> the alternate-commitment offer (msg 339)
  # -> accept the Gecko job (82 0->2, active). Harold (GECKSETL 16705) explains the
  # plant's coolant-valve near-meltdown and the missing Hydroelectric Magnetosphere Regulator
  # part (82->5, informational). McClure (vctycocl 13922, "Bureaucrat 1"/Senior Councilor)
  # confirms VC has the part and sends the dude to Randal for it (82->6). Randal (vctydwtn
  # 23077, "Trader 1"/Chief Amenities Officer) hands over the Hy-Mag part (82->7, no --give
  # needed - script-granted). Festus the reactor ghoul (GECKPWPL 24063) installs it (82->9,
  # completed: quests.txt display>=2/completed>=8) - +4250 xp, dude reaches level 3. Back to
  # McClure with the repair report (msg 134) (only visible once 82>=9) grants VC
  # Citizenship directly: 79 0->4, 81 0->1. GOTCHA: Lynette's OWN citizenship-grant node
  # (076b/076c, 79:=4) is unreachable dialogue-dead-code (its only caller requires 79==4/5
  # already); McClure's Node046 is the real, live grant path. 79:=5 (Lynette Node132) and
  # 88:=5 (Lynette Node114) are NOT reached here - both sit downstream of the separate
  # Bishop-conspiracy/quest-89 (Lynette's holodisk to Westin) chain: Node114's own hub gate
  # needs GVAR88==4 with no drivable writer anywhere in the VC script set, and the item-338
  # shortcut into the finale (Node130) needs GVAR89==3 (Westin's getDisk, cross-town NCR) -
  # confirmed via full call-graph trace of vclynett.int, not a guess. See docs/qa-sweep/
  # gecko.md and vaultcity.md for the traced gate detail (B4 Task 2 territory).
  "quest-gecko-powerplant|$CREATE --goto-map vctycocl.map --talk-seq 17100 2,3 --talk-seq 17100 2,1,1,1,2,1 --get-global 82 --goto-map GECKSETL.map --talk-seq 16705 2,1,1,1,1,1,1,1,1,3 --get-global 82 --goto-map vctycocl.map --talk-seq 13922 1,2,3,5,1,2,1 --get-global 82 --goto-map vctydwtn.map --talk-seq 23077 1,3,1,3 --get-global 82 --goto-map GECKPWPL.map --talk-seq 24063 1,2,1,1,1 --get-global 82 --goto-map vctycocl.map --talk-seq 13922 1,5,2 --get-global 82 --get-global 88 --get-global 79 --get-global 81 --quest-probe --rng-seed 1"
  # B4 Task 2: Lynette's holodisk to Westin (GVAR 89), 0->1->3->4, resuming the Task-1
  # end-state (82=9, 79=4, 81=1, 88=0). RESOLVED the open 88:=4 lead: NO vc/vi/gc/gs script
  # writes it (Task-1's trace was right) - the real setter is the map_update_p_proc of the
  # Raiders2.map special encounter (map script `raiders2.int`), unconditional on dude
  # elevation==2 and GVAR88<4 - reached with a plain --goto-map, no dialogue at all. GATE
  # CHAIN below Node114 (88:=5) was NOT simply "gvar88==4": the hub's own raiders-intel topic
  # (Node107) branches on GVAR_RAIDERS_FLAGS (373) bit0, which is a genuine cross-script bit
  # (`--bit-scan 373`) set only by the Raiders2.map mercenary critters' own destroy_p_proc,
  # gated on a shared GVAR_RAIDERS_COUNT (377, starts at 18) dropping to <=5 - i.e. the raiders
  # encounter must be substantially fought through (source_obj==dude on each kill), not just
  # visited. `--kill` drives this the same sanctioned way as the existing kill-quest goldens
  # (real destroy_p_proc, debug-shortcut cause of death only); 14 of the 17 raiders are killed
  # to cross the <=5 threshold. Once GVAR373 bit0 is set, Lynette's hub offers the raiders-info
  # topic's real branch (Node107->Node072->Node110->Node111->Node114, 88:=5); Node114 checks
  # obj_carrying_pid_obj(dude,447) (Bishop's Holodisk, NOT yet held on first visit) and falls
  # to a flavor branch. --give 447:1 (combat/steal-gated acquisition, sanctioned per the VC-321
  # precedent) then unlocks the hub's msg-394 reveal (88==5 && carrying 447) -> Node136->Node116
  # (88:=6) ->Node119->Node123 (89:=1). SCWestin (ncr3 17892, displays as "McGee") offers his
  # own accept option gated GVAR89==1 && carrying 447; picking it calls `getDisk` directly
  # (89:=3, consumes the item). Returning to Lynette with 89==3 unlocks Node125's opt (89==3
  # exactly) -> Node129: 89:=4 (COMPLETES; also GVAR484:=2). End state: 88=6, 89=4, 79=4, 81=1,
  # 82=9. No --set-global anywhere; no VC-citizen hostility triggered (raiders are a legitimate
  # hostile faction, distinct from the 79:=6 town-hostility guard). State/ID only, no dialogue
  # text. Full gate trace + tile/pid list in docs/qa-sweep/vaultcity.md.
  "quest-lynette-holodisk|$CREATE --goto-map vctycocl.map --talk-seq 17100 2,3 --talk-seq 17100 2,1,1,1,2,1 --goto-map GECKSETL.map --talk-seq 16705 2,1,1,1,1,1,1,1,1,3 --goto-map vctycocl.map --talk-seq 13922 1,2,3,5,1,2,1 --goto-map vctydwtn.map --talk-seq 23077 1,3,1,3 --goto-map GECKPWPL.map --talk-seq 24063 1,2,1,1,1 --goto-map vctycocl.map --talk-seq 13922 1,5,2 --goto-map Raiders2.map:11509:2 --get-global 88 --kill 12288 --kill 12310 --kill 16487 --kill 17087 --kill 17089 --kill 18510 --kill 18905 --kill 19298 --kill 19910 --kill 20101 --kill 21509 --kill 22311 --kill 24108 --kill 24708 --get-global 373 --goto-map vctycocl.map --talk-seq 17100 3,1,1,1,1,1,2 --get-global 88 --give 447:1 --talk-seq 17100 3,1,2,2,2 --get-global 88 --get-global 89 --goto-map ncr3.map --talk-seq 17892 3 --get-global 89 --goto-map vctycocl.map --talk-seq 17100 3,4 --get-global 89 --get-global 88 --get-global 484 --get-global 79 --get-global 81 --quest-probe --rng-seed 1"
  # B4 arc CLOSED: Stark's scouting quest (GVAR 529), 0->1->2 (row 1: display>=1/completed>=2)
  # and 0->3->4 (row 2: display>=3/completed>=4) in ONE Stark conversation, resuming the
  # lynette-holodisk end-state (82=9,79=4,81=1,88=6,89=4). Closes the last open B4 lead: the
  # `lvar8>10` gate on Lynette's Node130a (79:=5, citizenship rank 5) turned out to be a plain
  # farm loop through her Q&A hub's Node087->Node099->Node103->Node103a family (`--talk-seq
  # 17100 1,3,2,2,1`, the same increment-node repeated) - 11 reps cross lvar8>10; CHA>7 via 3
  # real Mentats doses (pid 53, `--give`/`--use-item`, per the B4 Task 3 drug-pipeline note)
  # satisfies the OTHER half of Node130a's gate; Node132 (Lynette's citizenship-rank-5 grant)
  # then fires 79:=5 on the next council-hub visit (`--talk-seq 17100 3,4,1,2`). With 79==5,
  # Stark (vctydwtn 12674) offers his "Patrols?" topic's recon-job branch (Node016 opt2 ->
  # Node050->Node051->Node052->Node053->Node054): the 8-term metarule3(105,x,y,0) AND-chain
  # over the worldmap subtiles around Gecko/NCR gates 529's completion (the rule-105 hook this
  # commit adds, `ScriptHost.SubtileStateProvider` -> `WorldFog.StateAt`). The 8 coords
  # ((1224,171),(1274,172),(1323,173),(1224,223),(1324,225),(1224,274),(1275,274),(1325,273))
  # are visited via 8 real `--travel-from <x> <y> 5` legs (WorldmapTravel marks the leg's own
  # start pixel visited, no engine change beyond the metarule3 wire). Row 2 (NCR leg, GVAR 540)
  # needed one more real find: 540 is set by `NCRENT.map`'s own map_update_p_proc (unconditional
  # on dude elevation==0 && 540==0) - a dedicated NCR-entrance transition map (`--goto-map
  # NCRENT.map`), NOT `ncr3.map` itself (confirmed by a 1448-script `set_global_var 540` sweep:
  # NCRENT.int is the only writer anywhere in the game data). With 540==1 already set, Stark's
  # row-2 setup node (Node058) skips its own "not yet" branch and goes straight through
  # Node060->Node060a->Node061 (529:=4, +500 caps +750 xp +item pid 59) in the SAME visit as row
  # 1's completion (Node054->Node056->Node056a->Node057, 529:=2, +300 caps +350 xp) - the full
  # Stark click sequence is `1,1,2,1,1,1,1,2,1,1,1,1` (12 clicks, static-disasm-verified against
  # `vcstark.int` before driving). No `--set-global` anywhere; full trace in
  # docs/qa-sweep/vaultcity.md's 529 verdict UPDATE.
  "quest-stark-scout|$CREATE --goto-map vctycocl.map --talk-seq 17100 2,3 --talk-seq 17100 2,1,1,1,2,1 --goto-map GECKSETL.map --talk-seq 16705 2,1,1,1,1,1,1,1,1,3 --goto-map vctycocl.map --talk-seq 13922 1,2,3,5,1,2,1 --goto-map vctydwtn.map --talk-seq 23077 1,3,1,3 --goto-map GECKPWPL.map --talk-seq 24063 1,2,1,1,1 --goto-map vctycocl.map --talk-seq 13922 1,5,2 --give 53:3 --use-item 53 --use-item 53 --use-item 53 --goto-map Raiders2.map:11509:2 --kill 12288 --kill 12310 --kill 16487 --kill 17087 --kill 17089 --kill 18510 --kill 18905 --kill 19298 --kill 19910 --kill 20101 --kill 21509 --kill 22311 --kill 24108 --kill 24708 --goto-map vctycocl.map --talk-seq 17100 3,1,1,1,1,1,3 --give 447:1 --talk-seq 17100 3,1,2,2,2 --talk-seq 17100 1,3,2,2,1 --talk-seq 17100 1,3,2,2,1 --talk-seq 17100 1,3,2,2,1 --talk-seq 17100 1,3,2,2,1 --talk-seq 17100 1,3,2,2,1 --talk-seq 17100 1,3,2,2,1 --talk-seq 17100 1,3,2,2,1 --talk-seq 17100 1,3,2,2,1 --talk-seq 17100 1,3,2,2,1 --talk-seq 17100 1,3,2,2,1 --talk-seq 17100 1,3,2,2,1 --goto-map NCRENT.map --get-global 540 --goto-map ncr3.map --talk-seq 17892 3,1 --goto-map vctycocl.map --talk-seq 17100 3,4,1,2 --get-global 79 --travel-from 1224 171 5 --travel-from 1274 172 5 --travel-from 1323 173 5 --travel-from 1224 223 5 --travel-from 1324 225 5 --travel-from 1224 274 5 --travel-from 1275 274 5 --travel-from 1325 273 5 --goto-map vctydwtn.map --get-global 529 --talk-seq 12674 1,1,2,1,1,1,1,2,1,1,1,1 --get-global 529 --quest-probe --rng-seed 1"
)

dotnet build src/Hexwaste.Viewer -c Debug >/dev/null || { echo "build failed"; exit 2; }

run() {
  timeout 120 env DISPLAY="${DISPLAY:-:0}" FALLOUT2_DIR="$GAME" \
    dotnet run --project src/Hexwaste.Viewer -c Debug --no-build -- \
    --game-dir "$GAME" --no-audio $1 2>/dev/null \
    | grep -E "^(get-global:|quest-item:|quest-probe:|party-count:|party:)"
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
  out2="$(run "$args")"
  if [ "$out" != "$out2" ]; then echo "NONDETERMINISTIC: $name"; fail=1; fi
  if [ ! -f "$FIX/$name.txt" ]; then echo "MISSING FIXTURE: $name (run 'record' first)"; fail=1; continue; fi
  if diff -u "$FIX/$name.txt" <(printf '%s\n' "$out") >/dev/null; then
    echo "ok  $name"
  else
    echo "DIFF: $name"; diff -u "$FIX/$name.txt" <(printf '%s\n' "$out"); fail=1
  fi
done

[ "$MODE" = "record" ] && exit 0
if [ "$fail" = 0 ]; then echo "quest e2e: ALL PASS"; else echo "quest e2e: FAIL"; exit 1; fi
