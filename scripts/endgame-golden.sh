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

source "scripts/golden-lib.sh" || exit 2
golden_runner viewer 90 src/Hexwaste.Viewer/bin/Debug/net10.0/Hexwaste.Viewer \
  "slide:|endgame-probe:|death-ending-probe:" "--no-audio"

golden_run_all || exit 2
[ "$MODE" = "record" ] && { [ "$GOLDEN_FAIL" = 0 ] && exit 0 || exit 1; }
if [ "$GOLDEN_FAIL" = 0 ]; then echo "golden endgame: ALL PASS"; else echo "golden endgame: FAIL"; exit 1; fi
