#!/usr/bin/env bash
# Scaffold a fresh comparison run's artifact directory:
#   scratch/compare-runs/<slug>-<timestamp>/{fo2ce,hexwaste}/notes.md
# Prints the run directory path as the LAST line of stdout, so callers can capture it with
# e.g. `RUN_DIR="$(scripts/compare-run-init.sh my-scenario | tail -1)"`.
#
# Usage: scripts/compare-run-init.sh <scenario-slug>
set -euo pipefail
cd "$(dirname "$0")/.."

SLUG="${1:?usage: $0 <scenario-slug>}"
TS="$(date +%Y%m%d-%H%M%S)"
RUN_DIR="scratch/compare-runs/${SLUG}-${TS}"

mkdir -p "$RUN_DIR/fo2ce" "$RUN_DIR/hexwaste"
: > "$RUN_DIR/fo2ce/notes.md"
: > "$RUN_DIR/hexwaste/notes.md"

echo "$RUN_DIR"
