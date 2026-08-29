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

source "scripts/golden-lib.sh" || exit 2
# The scenario field is a bare map filename, so --map lives in the runner's EXTRA.
golden_runner procanalyze 0 tools/ProcAnalyze/bin/Debug/net10.0/ProcAnalyze \
  "procanalyze:|stubbed:" "--map"
DOUBLE_RUN=0
GOLDEN_RECORD_COUNT=0
GOLDEN_MISSING_HINT=""
GOLDEN_RESULT_HOOK=census_load_check

# A map that emitted no census line failed to LOAD; reporting that as a fixture
# mismatch would bury the cause. Runs before the record/compare branch, as it did
# in the original loop.
census_load_check() {
  local name="$1" out="$2" map="$3"
  if [ -z "$out" ]; then
    echo "LOAD-FAIL: $name ($map emitted no census line)"
    return 1
  fi
  return 0
}

# Coverage assertion: a suite that quietly lost a scenario still reports ALL PASS
# over the hole. Update this deliberately when adding or removing a fixture.
GOLDEN_EXPECT_SCENARIOS=16

golden_run_all || exit 2
[ "$MODE" = "record" ] && { [ "$GOLDEN_FAIL" = 0 ] && exit 0 || exit 1; }
if [ "$GOLDEN_FAIL" = 0 ]; then echo "census sweep: ALL PASS"; else echo "census sweep: FAIL"; exit 1; fi
