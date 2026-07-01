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
  "mapupdate-artemple|viewer|--map artemple.map --map-update-probe --rng-seed 1"
  "mapupdate-arvillag|viewer|--map arvillag.map --map-update-probe --rng-seed 1"
  "chain-opening|viewer|--map artemple.map --goto-map arcaves.map --goto-map arvillag.map --goto-map argarden.map --goto-map arbridge.map --rng-seed 1"
)
# (Note: new-game GVAR seeding is already covered by encounter-golden.sh's gvar-seed scenario; a plain
#  --map load intentionally does NOT seed, so a gvar scenario here would just anchor 0/0 — omitted.)

dotnet build src/Hexwaste.Viewer -c Debug >/dev/null || { echo "build failed"; exit 2; }
dotnet build tools/ProcAnalyze -c Debug >/dev/null || { echo "procanalyze build failed"; exit 2; }

run() {
  local kind="$1" args="$2"
  if [ "$kind" = "census" ]; then
    dotnet run --project tools/ProcAnalyze -c Debug --no-build -- --game-dir "$GAME" $args 2>/dev/null \
      | grep -E "procanalyze:|stubbed:"
  else
    timeout 120 env DISPLAY="${DISPLAY:-:0}" FALLOUT2_DIR="$GAME" \
      dotnet run --project src/Hexwaste.Viewer -c Debug --no-build -- \
      --game-dir "$GAME" --no-audio $args 2>/dev/null \
      | grep -E "transit:|map-update:|light:|get-global:|lip-probe:"
  fi
}

fail=0
for entry in "${SCENARIOS[@]}"; do
  name="${entry%%|*}"; rest="${entry#*|}"; kind="${rest%%|*}"; args="${rest#*|}"
  out="$(run "$kind" "$args")"
  if [ "$MODE" = "record" ]; then
    printf '%s\n' "$out" > "$FIX/$name.txt"
    echo "recorded $name ($(printf '%s\n' "$out" | wc -l | tr -d ' ') lines)"
    continue
  fi
  out2="$(run "$kind" "$args")"
  if [ "$out" != "$out2" ]; then echo "NONDETERMINISTIC: $name"; fail=1; fi
  if [ ! -f "$FIX/$name.txt" ]; then echo "MISSING FIXTURE: $name (run 'record' first)"; fail=1; continue; fi
  if diff -u "$FIX/$name.txt" <(printf '%s\n' "$out") >/dev/null; then
    echo "ok  $name"
  else
    echo "DIFF: $name"; diff -u "$FIX/$name.txt" <(printf '%s\n' "$out"); fail=1
  fi
done

[ "$MODE" = "record" ] && exit 0
if [ "$fail" = 0 ]; then echo "golden opening: ALL PASS"; else echo "golden opening: FAIL"; exit 1; fi
