#!/usr/bin/env bash
# Golden endgame-slideshow regression net — P100 (Point 1: endgame slideshow + ending selection).
#
# The victory finale is content-gated (no map on the playable slice fires endgame_slideshow), so we
# prove the selector deterministically via probes: --endgame-probe forces a controlling GVAR and dumps
# the victory slides it selects (endgame.txt gvar==value), --death-ending-probe dumps the death
# narration chosen for a reason + RNG seed (enddeath.txt). Diffs stdout against tests/golden-endgame/.
#
# Usage:  scripts/endgame-golden.sh [check|record]   (default: check)
# Requires a real display (MonoGame GraphicsDevice) + game data (FALLOUT2_DIR, default ./game-data).
set -uo pipefail
cd "$(dirname "$0")/.."

MODE="${1:-check}"
GAME="${FALLOUT2_DIR:-$(pwd)/game-data}"
FIX="tests/golden-endgame"
mkdir -p "$FIX"

# name | harness args (each self-exiting; grep to the deterministic probe lines only)
SCENARIOS=(
  "endgame-arroyo|--endgame-probe 408 1"
  "endgame-den3|--endgame-probe 410 3"
  "endgame-none|--endgame-probe"
  "death-generic-7|--death-ending-probe 0 7"
  "death-generic-42|--death-ending-probe 0 42"
)

dotnet build src/Hexwaste.Viewer -c Debug >/dev/null || { echo "build failed"; exit 2; }

run() {
  timeout 90 env DISPLAY="${DISPLAY:-:0}" FALLOUT2_DIR="$GAME" \
    dotnet run --project src/Hexwaste.Viewer -c Debug --no-build -- \
    --game-dir "$GAME" --no-audio $1 2>/dev/null | grep -E "slide:|endgame-probe:|death-ending-probe:"
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
    echo "DIFF: $name"; diff -u "$FIX/$name.txt" <(printf '%s\n' "$out"); fail=1
  fi
done

[ "$MODE" = "record" ] && exit 0
if [ "$fail" = 0 ]; then echo "golden endgame: ALL PASS"; else echo "golden endgame: FAIL"; exit 1; fi
