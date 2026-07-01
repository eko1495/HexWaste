#!/usr/bin/env bash
# Region-by-region external-demand census sweep — P101 (bucket 2: QA tooling).
#
# Runs the static ProcAnalyze census (VM-free, display-free) over ~1 map per story region and locks the
# wired/stubbed external counts as a regression net: if a map's scripted external demand ever shifts (a
# newly-wired external, or a map's scripts change), the fixture diffs — the "silent quest-gap" tripwire
# across the WHOLE game, not just the opening spine. Complements opening-golden.sh (which locks the
# Arroyo spine + New Reno). Fixtures in tests/golden-census/.
#
# Usage:  scripts/census-sweep.sh [check|record]   (default: check)
# Needs game data (FALLOUT2_DIR, default ./game-data); NO display required (pure ProcAnalyze).
set -uo pipefail
cd "$(dirname "$0")/.."

MODE="${1:-check}"
GAME="${FALLOUT2_DIR:-$(pwd)/game-data}"
FIX="tests/golden-census"
mkdir -p "$FIX"

# name | map — one representative map per story region (ncr1/broken1/denbus1/newr1 = the biggest surfaces).
SCENARIOS=(
  "census-kladwtwn|kladwtwn.map"  # Klamath
  "census-denbus1|denbus1.map"    # Den
  "census-modmain|modmain.map"    # Modoc
  "census-vctyctyd|vctyctyd.map"  # Vault City
  "census-gecksetl|gecksetl.map"  # Gecko
  "census-broken1|broken1.map"    # Broken Hills
  "census-newr1|newr1.map"        # New Reno
  "census-ncr1|ncr1.map"          # NCR
  "census-reddown|reddown.map"    # Redding
  "census-vault15|vault15.map"    # Vault 15
  "census-vault13|vault13.map"    # Vault 13
  "census-sfchina|sfchina.map"    # San Francisco
  "census-mbase12|mbase12.map"    # Military Base
  "census-navarro|navarro.map"    # Navarro
  "census-encdock|encdock.map"    # Enclave (entry)
  "census-encpres|encpres.map"    # Enclave (core)
)

dotnet build tools/ProcAnalyze -c Debug >/dev/null || { echo "procanalyze build failed"; exit 2; }

run() {
  dotnet run --project tools/ProcAnalyze -c Debug --no-build -- --game-dir "$GAME" --map "$1" 2>/dev/null \
    | grep -E "procanalyze:|stubbed:"
}

fail=0
for entry in "${SCENARIOS[@]}"; do
  name="${entry%%|*}"; map="${entry#*|}"
  out="$(run "$map")"
  if [ -z "$out" ]; then echo "LOAD-FAIL: $name ($map emitted no census line)"; fail=1; continue; fi
  if [ "$MODE" = "record" ]; then
    printf '%s\n' "$out" > "$FIX/$name.txt"
    echo "recorded $name"
    continue
  fi
  if [ ! -f "$FIX/$name.txt" ]; then echo "MISSING FIXTURE: $name"; fail=1; continue; fi
  if diff -u "$FIX/$name.txt" <(printf '%s\n' "$out") >/dev/null; then
    echo "ok  $name"
  else
    echo "DIFF: $name"; diff -u "$FIX/$name.txt" <(printf '%s\n' "$out"); fail=1
  fi
done

[ "$MODE" = "record" ] && { [ "$fail" = 0 ] && exit 0 || exit 1; }
if [ "$fail" = 0 ]; then echo "census sweep: ALL PASS"; else echo "census sweep: FAIL"; exit 1; fi
