#!/usr/bin/env bash
# Run a fixture's fo2ce scenario twice with the same seed; the traces must be byte-identical.
# Usage: scripts/oracle-selfcheck.sh <fixture>
set -euo pipefail
cd "$(dirname "$0")/.."
FIX="${1:?usage: $0 <fixture>}"
OUT="$PWD/scratch/oracle-runs/selfcheck-$FIX"; mkdir -p "$OUT"
RUN=reference/fallout2-ce/run
SLOT=$(find "$RUN" -maxdepth 3 -type d -iname SAVEGAME | head -1)
[ -n "$SLOT" ] || SLOT="$RUN/data/SAVEGAME"
for pass in 1 2; do
  rm -rf "${SLOT:?}/SLOT10"; mkdir -p "$SLOT/SLOT10"
  cp -r "game-data/oracle-saves/$FIX/." "$SLOT/SLOT10/"
  bash tools/fo2ce-trace/run.sh fixture="$FIX" \
    scenario="$PWD/tests/golden-oracle/$FIX/scenario.txt" trace="$OUT/pass$pass.trace"
done
if cmp -s "$OUT/pass1.trace" "$OUT/pass2.trace"; then
  echo "SELFCHECK OK $FIX ($(wc -l < "$OUT/pass1.trace") lines)"
else
  echo "SELFCHECK FAIL $FIX"; diff "$OUT/pass1.trace" "$OUT/pass2.trace" | head -20; exit 1
fi
