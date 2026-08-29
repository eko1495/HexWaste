#!/usr/bin/env bash
# Golden opening-spine regression net — P100 (Point 2: QA tooling + harden the opening).
#
# Locks the opening spine artemple→arcaves→arvillag→argarden→arbridge byte-for-byte, on three axes:
#   (A) census   — the static silent-quest-gap detector (tools/ProcAnalyze): per-map referenced-external
#                  surface split wired/stubbed. VM-free + display-free → the deterministic backbone.
#   (B) mapupdate — the viewer --map-update-probe: proves map_enter+map_update scripts actually ran.
#   (C) chain     — one process walking the whole spine via chained --goto-map transitions.
#   (D) gvars     — the new-game GVAR seed on the opening map.
# Diffs stdout against tests/golden-opening/. Additive: touches no other golden or fixture.
#
# Usage:  scripts/opening-golden.sh [check|record]   (default: check)
# Requires game data (FALLOUT2_DIR, default ./game-data); (B)/(C)/(D) need a real display.
set -uo pipefail
cd "$(dirname "$0")/.."

MODE="${1:-check}"
GAME="${FALLOUT2_DIR:-$(pwd)/game-data}"
FIX="tests/golden-opening"
mkdir -p "$FIX"

# name | kind (census|viewer) | args
SCENARIOS=(
  "census-artemple|census|--map artemple.map"
  "census-arcaves|census|--map arcaves.map"
  "census-arvillag|census|--map arvillag.map"
  "census-argarden|census|--map argarden.map"
  "census-arbridge|census|--map arbridge.map"
  # P101 (bucket 1b): New Reno — locks the prizefight-adjacent wired-external count (game_ui_disable/enable
  # now wired → stubbed dropped 11→9). Regression net for New Reno's remaining (cosmetic) stub surface.
  "census-newr2|census|--map Newr2.map"
  # P101 (bucket 1c): the .lip lip-sync data chain on the real Arroyo Elder assets (parse → phoneme → frame).
  "lip-elder|viewer|--map artemple.map --lip-probe ELDER aeld1"
  # P101 (bucket 2 M2): the DYNAMIC census — arvillag's scripts fire the gfade stubs when their dialog/use
  # procs are actually driven (⊆ the static ProcAnalyze superset). Confirmed-executed, not just referenced.
  "census-dyn-arvillag|viewer|--map arvillag.map --census --rng-seed 1"
  "mapupdate-artemple|viewer|--map artemple.map --map-update-probe --rng-seed 1"
  "mapupdate-arvillag|viewer|--map arvillag.map --map-update-probe --rng-seed 1"
  "chain-opening|viewer|--map artemple.map --goto-map arcaves.map --goto-map arvillag.map --goto-map argarden.map --goto-map arbridge.map --rng-seed 1"
  # P139: all six main-menu buttons are live (INTRO + OPTIONS were disabled by a stale comment). menu-activate
  # locks the wiring + the enabled=True flag as pure STATE (INTRO opens the iplogo→intro movie queue; OPTIONS
  # opens the PREFSCRN Preferences window). The label-bearing --menu-probe is NOT goldened — it echoes
  # misc.msg game strings (the copyright + button labels), which must stay out of the committed fixtures.
  "menu-intro|viewer|--menu --menu-activate 0"
  "menu-options|viewer|--menu --menu-activate 3"
)
# (Note: new-game GVAR seeding is already covered by encounter-golden.sh's gvar-seed scenario; a plain
#  --map load intentionally does NOT seed, so a gvar scenario here would just anchor 0/0 — omitted.)

dotnet build src/Hexwaste.Viewer -c Debug >/dev/null || { echo "build failed"; exit 2; }
dotnet build tools/ProcAnalyze -c Debug >/dev/null || { echo "procanalyze build failed"; exit 2; }

source "scripts/golden-lib.sh" || exit 2
# Scenario kind selects the runner: "census" -> ProcAnalyze, anything else -> the viewer.
golden_runner census 0 tools/ProcAnalyze/bin/Debug/net10.0/ProcAnalyze \
  "procanalyze:|stubbed:" ""
golden_runner viewer 120 src/Hexwaste.Viewer/bin/Debug/net10.0/Hexwaste.Viewer \
  "transit:|map-update:|light:|get-global:|lip-probe:|census:|menu-activate:" "--no-audio"
SCENARIO_FIELDS=3

golden_run_all
[ "$MODE" = "record" ] && exit 0
if [ "$GOLDEN_FAIL" = 0 ]; then echo "golden opening: ALL PASS"; else echo "golden opening: FAIL"; exit 1; fi
