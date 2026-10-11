#!/usr/bin/env bash
# Trace oracle (M1): run one fixture in the trace-patched fallout2-ce e97087b and in Hexwaste
# from the same vanilla save + seed + scenario, then report the first divergence.
# Prereqs: tools/fo2ce-trace/build.sh; scripts/oracle-make-save.sh <fixture>.
# Usage: scripts/oracle-run.sh <fixture>
# Exit: 0 IDENTICAL, 1 DIVERGED, 2 usage/missing input, 3 an engine exited non-zero.
# Traces + engine logs land in scratch/oracle-runs/<fixture>/.
set -euo pipefail
cd "$(dirname "$0")/.."
FIX="${1:?usage: $0 <fixture>}"
SCEN="$PWD/tests/golden-oracle/$FIX/scenario.txt"
SAVE="$PWD/game-data/oracle-saves/$FIX"
OUT="$PWD/scratch/oracle-runs/$FIX"; mkdir -p "$OUT"
[ -f "$SCEN" ] || { echo "missing $SCEN" >&2; exit 2; }
[ -f "$SAVE/SAVE.DAT" ] || { echo "missing $SAVE - run scripts/oracle-make-save.sh $FIX" >&2; exit 2; }
rm -f "$OUT/fo2ce.trace" "$OUT/hexwaste.trace"

# Never leave an engine running if we are interrupted or fail part-way.
cleanup() { pkill -f "build-oracle/fallout2-ce" 2>/dev/null || true
            pkill -f "Hexwaste.Viewer.*--fixture $FIX" 2>/dev/null || true; }
trap 'cleanup; exit 130' INT TERM

echo "== build (Hexwaste.Viewer + TraceDiff)"
dotnet build src/Hexwaste.Viewer -c Debug -v q -nologo > "$OUT/build.log" 2>&1 \
  || { echo "Hexwaste build failed - see $OUT/build.log" >&2; exit 2; }
dotnet build tools/TraceDiff -c Debug -v q -nologo >> "$OUT/build.log" 2>&1 \
  || { echo "TraceDiff build failed - see $OUT/build.log" >&2; exit 2; }

echo "== fo2ce $FIX"
RUN=reference/fallout2-ce/run
SLOT=$(find "$RUN" -maxdepth 3 -type d -iname SAVEGAME | head -1)
[ -n "$SLOT" ] || SLOT="$RUN/data/SAVEGAME"
rm -rf "${SLOT:?}/SLOT10"; mkdir -p "$SLOT/SLOT10"; cp -r "$SAVE/." "$SLOT/SLOT10/"
set +e
bash tools/fo2ce-trace/run.sh fixture="$FIX" scenario="$SCEN" trace="$OUT/fo2ce.trace" \
  > "$OUT/fo2ce.log" 2>&1
FO=$?
echo "== hexwaste $FIX"
# The built apphost directly (as golden-lib.sh does): `timeout dotnet run` would kill only the
# dotnet driver and orphan the viewer child.
FALLOUT2_DIR="$PWD/game-data" timeout "${ORACLE_TIMEOUT:-1800}" src/Hexwaste.Viewer/bin/Debug/net10.0/Hexwaste.Viewer \
  --game-dir "$PWD/game-data" --no-audio --load-vanilla-save "$SAVE" --rng fo2ce \
  --fixture "$FIX" --scenario "$SCEN" --trace "$OUT/hexwaste.trace" > "$OUT/hexwaste.log" 2>&1
HW=$?
set -e

# fo2ce: 0 ok, 2 trace open failed, 4 load fail, 5 dude dead/KO, 6 bad scenario, 7 end-turn cap (124 = timeout).
# Hexwaste: 0 ok, 124 timeout, 2 bad flags, 5 dude dead/KO, 6 bad scenario, 7 end-turn cap, 8 unhandled exception.
# Both 5 is a legitimate shared outcome (the dude died in both) - still diff the traces.
if [ "$FO" -ne 0 ] || [ "$HW" -ne 0 ]; then
  if [ "$FO" -eq 5 ] && [ "$HW" -eq 5 ]; then
    echo "note: both engines exited 5 (dude dead/KO) - diffing traces"
  else
    [ "$FO" -ne 0 ] && echo "FAIL: fo2ce exited $FO (log: $OUT/fo2ce.log)" >&2
    [ "$HW" -ne 0 ] && echo "FAIL: hexwaste exited $HW (log: $OUT/hexwaste.log)" >&2
    exit 3
  fi
fi

dotnet run --no-build --project tools/TraceDiff -c Debug -- "$OUT/fo2ce.trace" "$OUT/hexwaste.trace"
