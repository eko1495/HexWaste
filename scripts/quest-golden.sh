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

# name | harness args. The "Free Vic" quest (Arroyo GVAR 619 FIND_VIC): new-game seeds it to 1 (active);
# buying Vic's freedom from Metzger (give 2000 caps + his radio, then the 3-NPC dialogue) drives 619→2
# (completed) + Vic joins. The two get-global 619 (before the dialogue = 1, after = 2) bracket the lifecycle.
SCENARIOS=(
  "quest-free-vic|$CREATE --goto-map denbus2.map --give 41:2000 --give 266:1 --get-global 619 --talk-seq 17070 1,1,1 --talk-seq 15278 2,2,1,1 --talk-seq 17070 2,1 --get-global 619 --quest-probe --party-count --rng-seed 1"
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
