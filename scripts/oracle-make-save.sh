#!/usr/bin/env bash
# Build a fixture's vanilla save with the headless oracle fo2ce, then copy the slot out to
# game-data/oracle-saves/<fixture>/ (gitignored — saves embed game data).
# Usage: scripts/oracle-make-save.sh <fixture>
set -euo pipefail
cd "$(dirname "$0")/.."
FIX="${1:?usage: $0 <fixture>}"
RECIPE="$PWD/tests/golden-oracle/$FIX/recipe.txt"
RUN=reference/fallout2-ce/run
SLOT=$(find "$RUN" -maxdepth 3 -type d -iname SAVEGAME | head -1)
[ -n "$SLOT" ] || SLOT="$RUN/data/SAVEGAME"
rm -rf "$SLOT/SLOT10"
bash tools/fo2ce-trace/run.sh make_save="$RECIPE"
SLOT=$(find "$RUN" -maxdepth 3 -type d -iname SAVEGAME | head -1)
[ -f "$SLOT/SLOT10/SAVE.DAT" ] || { echo "no SAVE.DAT produced in $SLOT/SLOT10" >&2; exit 1; }
DEST=game-data/oracle-saves/$FIX
rm -rf "$DEST"; mkdir -p "$DEST"
cp -r "$SLOT/SLOT10/." "$DEST/"
ls -l "$DEST"
