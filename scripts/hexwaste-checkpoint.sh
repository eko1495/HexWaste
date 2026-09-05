#!/usr/bin/env bash
# Run one Hexwaste checkpoint: replay a prefix of --goto/--walk/--talk/... actions and
# capture a single screenshot at the end. Hexwaste's CLI applies its action list once per
# process, ending in one --screenshot — so each checkpoint of a scenario is its own invocation
# replaying the full action prefix up to that point. See docs/fo2ce-comparison-playbook.md.
#
# Usage: scripts/hexwaste-checkpoint.sh <output.png> [-- <action flags...>]
#   scripts/hexwaste-checkpoint.sh scratch/compare-runs/x/hexwaste/01-menu.png
#   scripts/hexwaste-checkpoint.sh scratch/compare-runs/x/hexwaste/02-vault.png -- --goto 12345
set -uo pipefail
cd "$(dirname "$0")/.."

OUT="${1:?usage: $0 <output.png> [-- <action flags...>]}"
shift
if [ "${1:-}" = "--" ]; then shift; fi

GAME="${FALLOUT2_DIR:-$(pwd)/game-data}"
mkdir -p "$(dirname "$OUT")"

DISPLAY="${DISPLAY:-:0}" FALLOUT2_DIR="$GAME" \
  dotnet run --project src/Hexwaste.Viewer -c Debug --no-build -- \
  --game-dir "$GAME" --no-audio "$@" --screenshot "$OUT"
