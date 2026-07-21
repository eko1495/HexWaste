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
  # Duntons (talk 16315 1,1,1 → "I'll take care of Torr") sets 102:=1; scaring Torr off (talk 17701
  # opt 2, his runtime tile after the override_map_start arrival) → Node930 → 102:=2 + 71:=1 (Torr
  # flees to the canyon). Exercises the dialogue-triggered scripted map transition end to end.
  "quest-torr-duntons|$CREATE --goto-map kladwtwn.map --talk-seq 24291 1,1,1 --pump-ms 4000 --talk-seq 16315 1,1,1 --get-global 102 --talk-seq 17701 2 --get-global 102 --get-global 71 --quest-probe --rng-seed 1"
  # Rescue Torr (Klamath, GVAR 391) — closes Klamath 6/6. The full chain: the klagraz event
  # (as in quest-torr-duntons) displaces Torr (71:=1); back in town his mother Ardin (22885,
  # 1,1,1,1,1,1) asks you to find him → 391:=1; the canyon Torr now appears (KLACANYN 15287, needs
  # 71:=1 AND 391:=1) → "let's get out of here" (talk 1,1 → Node940 follow flag); the escort-sim
  # (--teleport to delivery tile 19450 + --escort-pump) fires his leave_player → 391:=2. --rng-seed
  # fixes the event-repositioned tiles. Exercises the load_map event + Ardin activation + escort-sim.
  "quest-torr-rescue|$CREATE --rng-seed 1 --goto-map kladwtwn.map --talk-seq 24291 1,1,1 --pump-ms 4000 --talk-seq 16315 1,1,1 --talk-seq 17701 2 --pump-ms 2000 --goto-map kladwtwn.map --talk-seq 22885 1,1,1,1,1,1 --get-global 391 --goto-map KLACANYN.map --pump-ms 1500 --talk-seq 15287 1,1 --teleport 19450 0 --escort-pump 15287 10 --get-global 391 --quest-probe"
  # Deliver a meal to Smitty for Mom (Den, GVAR 450) — a clean two-NPC delivery, full lifecycle
  # 0→1→3 via the real dialogue (no --set-global). Accept from Mom (denbus2 24479, 2,1,1 → "I'll
  # bring it right over" → 450:=1, she hands over the meal); deliver to Smitty (denbus1 22137, 2,1
  # → "I brought your meal from Mom's" → Node008 → 450:=3, completed). Crosses denbus2→denbus1.
  "quest-mom-meal|$CREATE --goto-map denbus2.map --get-global 450 --talk-seq 24479 2,1,1 --get-global 450 --goto-map denbus1.map --talk-seq 22137 2,1 --get-global 450 --quest-probe --rng-seed 1"
  # Collect money from Fred (Den, GVAR 371) — a multi-NPC NEGOTIATION, full lifecycle 0→1→2 via
  # the real dialogue (no --set-global / no faked caps). Rebecca (denbus1 17662, work option 2,1,1)
  # sets the Fred-debt task (371:=1); Fred (denbus1 25479, 1,2,2,1,1,2,3 — demand the FULL amount
  # down his negotiation tree) pays the full $200 (his Node986 item_caps_adjust(200)) + sets the
  # 446 task bit; back at Rebecca, "About that job… Yes, I did" (2,1,1,1 — the "Yes" option only
  # appears with caps>=200 AND the 446 bit) → Node011 → 371:=2 completed. The book sub-task (Derek,
  # desc 205) stays open — 371 is a shared gvar with two display thresholds.
  "quest-fred-money|$CREATE --goto-map denbus1.map --get-global 371 --talk-seq 17662 2,1,1 --get-global 371 --talk-seq 25479 1,2,2,1,1,2,3 --talk-seq 17662 2,1,1,1 --get-global 371 --quest-probe --rng-seed 1"
  # Find Cornelius's gold watch for Farrel (Modoc, GVAR 106) — first Modoc golden. Item-return,
  # full lifecycle 0→4→8 via the real dialogue. Farrel (modinn 25088), accused of stealing the
  # watch, hooks the quest via his watch-defense greeting (3,1,1 → "Watch?… Would you help?" →
  # 106:=4); carrying the gold pocket watch (item 257) his greeting adds opt4 "Is this the watch?"
  # → "Yes, this is it!" → 106:=8 (completed, clears his name). The watch acquire (found in the
  # outhouse) is the --give shortcut, like Anna's locket. 106 is Farrel-side; 105 is Cornelius-side.
  "quest-modoc-watch|$CREATE --goto-map modinn.map --get-global 106 --talk-seq 25088 3,1,1 --get-global 106 --give 257:1 --talk-seq 25088 4,1 --get-global 106 --quest-probe --rng-seed 1"
  # Deliver beer & booze to Lydia (Vault City, GVAR 497) — first VC golden. Item-delivery, full
  # lifecycle 0→1→2 via the real dialogue. Lydia (vctydwtn 26306) laments VC's synthetic-only
  # booze; down the "what's on tap → real alcohol" chain (1,1,1,1,1,1) she asks for a case of ten
  # each → 497:=1; carrying 10 beer (124) + 10 booze (125), her info menu gains opt6 "I have that
  # shipment of alcohol you wanted" (2,6 → Node032, obj_carrying check on 124+125) → 497:=2.
  "quest-lydia-booze|$CREATE --goto-map vctydwtn.map --get-global 497 --talk-seq 26306 1,1,1,1,1,1 --get-global 497 --give 124:10 --give 125:10 --talk-seq 26306 2,6 --get-global 497 --quest-probe --rng-seed 1"
  # Deliver tools to Valerie (Vault City, GVAR 493) — item-delivery, full lifecycle 0→1→2. The
  # grumpy VC maintenance worker Valerie (vctydwtn 21096) needs a wrench + pliers for her failing
  # lathe; down her repair chain (1,1,1,1,1,1,1) she agrees to let you look → 493:=1; carrying the
  # wrench (384) + pliers (75), her greeting adds "You have tools?" (1,1 → Node023 obj_carrying
  # check on 384+75) → 493:=2.
  "quest-valerie-tools|$CREATE --goto-map vctydwtn.map --get-global 493 --talk-seq 21096 1,1,1,1,1,1,1 --get-global 493 --give 384:1 --give 75:1 --talk-seq 21096 1,1 --get-global 493 --quest-probe --rng-seed 1"
  # Get a plow for Mr Smith (Vault City, GVAR 80) — a two-NPC purchase chain, 0→3→6. Smith
  # (vctyctyd 14078), a poor farmer denied VC citizenship, needs a plow; accept (2,1,1) then commit
  # on a re-talk (4,1,1 → "I'll take the money" → "one's near the Guns & Ammo store" → 80:=3, which
  # unlocks Harry's plow line). Harry (VCHarry 12513) then offers "You still selling that plow?"
  # (only at 80>=3); buy it for $800 (2,2,1 → deal → "Drop it off with the Smiths") → 80:=6. Caps
  # via --give 41:1000. Exercises a gvar-gated cross-NPC option (Harry's line appears only at 80=3).
  "quest-smith-plow|$CREATE --goto-map vctyctyd.map --get-global 80 --talk-seq 14078 2,1,1 --talk-seq 14078 4,1,1 --get-global 80 --give 41:1000 --talk-seq 12513 2,2,1 --get-global 80 --quest-probe --rng-seed 1"
  # Rescue Amanda's husband Joshua (Vault City, GVAR 459) — a 2-map, 2-NPC quest, 0→1→2→3. NOT an
  # escort: the "rescue" is a bribe. Amanda (vctyctyd 22673) — her jailed husband is a VC Servant;
  # 1,1,1,1,1,1 → she names Officer Barkus → 459:=1. Barkus (vctydwtn 14896, the Servant Assignment
  # Center) — "looking for Joshua…negotiate his release" (1,1,4,1) → his donation tiers; he's greedy
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
  # guard one floor down (newr2 elev1 17075), whose carrying-336-gated option ("I have a suitcase
  # for him from Mr. Moore", talk-seq 3) sets the guard-vetted flag and waves you up. Bishop (newr2
  # elev2 17678) then greets you by the case and takes it (opt1 "Here you go") -> 321:=2, completed.
  # Crosses vctydwtn->newr2 elev1->newr2 elev2, proving the delivery pattern spans towns and floors.
  "quest-moore-briefcase|$CREATE --goto-map vctydwtn.map --get-global 321 --talk-seq 17485 1,2,1,1,2,2,2 --get-global 321 --goto-map newr2.map:17075:1 --talk-seq 17075 3,1 --goto-map newr2.map:17678:2 --get-global 321 --talk-seq 17678 1 --get-global 321 --quest-probe --rng-seed 1"
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
