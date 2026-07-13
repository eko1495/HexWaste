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
