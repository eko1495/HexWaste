#!/usr/bin/env bash
# Full-scale quest-driver census harvest (docs/plan-quest-driver.md §11).
#
# Runs the quest-driver across ALL maps in the game (not just the 22 town hubs of the first
# census) and auto-harvests clean, NEW quest completions as golden candidates — the payoff of
# the driver pipeline: turn "which quests does the driver auto-complete?" into a batch answer.
#
# Two phases, both display-driven (viewer, single-occupancy → serial):
#   1. DISCOVER  — `--quest-drive-all` per map lists every gvar a map's NPCs write + a status
#                  (COMPLETED / activated / stuck). Batch state is shared across quests in one
#                  process, so a COMPLETED here can be a false positive (accumulated state).
#   2. VERIFY    — for each non-golden candidate, a FRESH single `--quest-drive <gvar>` on its
#                  map (own clean process, no accumulated state) gives the authoritative
#                  `completed=` verdict + a runnable recipe. This is the false-positive guard.
#
# Output:
#   docs/qa-sweep/harvest.md          — per-map matrix + the fresh-verified new completions
#   docs/qa-sweep/harvest-recipes.txt — the `quest-drive-cmd:` recipe for each new completion
#   (raw logs under the scratch dir given by $HARVEST_LOG, default ./.harvest-log)
#
# Usage:  scripts/quest-harvest.sh [discover|verify|all]   (default: all)
#         MAPS="a.map b.map" scripts/quest-harvest.sh       (limit to a map subset)
# Needs game data (FALLOUT2_DIR, default ./game-data) and a display (DISPLAY, default :0).
set -uo pipefail
cd "$(dirname "$0")/.."

MODE="${1:-all}"
GAME="${FALLOUT2_DIR:-$(pwd)/game-data}"
CREATE="--create 5,5,5,5,5,5,5:0,4,5:0"
LOG="${HARVEST_LOG:-$(pwd)/.harvest-log}"
OUT="docs/qa-sweep"
CAND="$LOG/candidates.tsv"   # map \t gvar \t batch-status
mkdir -p "$LOG" "$OUT"

# gvars already locked as goldens — never re-harvest (scripts/quest-golden.sh --get-global set).
GOLDEN_GVARS=" 71 80 100 102 106 182 197 198 303 371 390 391 393 450 459 493 497 501 550 551 619 631 "

is_golden() { [[ "$GOLDEN_GVARS" == *" $1 "* ]]; }

# All maps in the archive (Maps\NAME.map entries), lowercased + deduped — unless MAPS overrides.
all_maps() {
  if [ -n "${MAPS:-}" ]; then printf '%s\n' $MAPS; return; fi
  dotnet run --project tools/DatDump -c Debug --no-build -- --game-dir "$GAME" list 2>/dev/null \
    | grep -ioE "Maps\\\\[A-Za-z0-9_]+\.map" | sed 's#.*\\##' | tr 'A-Z' 'a-z' | sort -u
}

drive() {  # args passed to the viewer; echoes the quest-drive* lines
  timeout 180 env DISPLAY="${DISPLAY:-:0}" FALLOUT2_DIR="$GAME" \
    dotnet run --project src/Hexwaste.Viewer -c Debug --no-build -- \
    --game-dir "$GAME" --no-audio $CREATE "$@" 2>/dev/null \
    | grep -E "^(quest-drive:|quest-drive-all:|quest-drive-cmd:|  gvar=)"
}

discover() {
  echo "== DISCOVER =="
  : > "$CAND"
  local maps; maps="$(all_maps)"
  local n; n="$(printf '%s\n' "$maps" | wc -l | tr -d ' ')"
  local i=0
  while read -r map; do
    [ -z "$map" ] && continue
    i=$((i+1))
    local out; out="$(drive --goto-map "$map" --quest-drive-all)"
    printf '%s\n' "$out" > "$LOG/discover-$map.log"
    local summary; summary="$(printf '%s\n' "$out" | grep -E '^quest-drive-all: (completed|map)=' | tr '\n' ' ')"
    printf '[%3d/%3d] %-14s %s\n' "$i" "$n" "$map" "${summary:-no-quests}"
    # candidate gvars: COMPLETED or activated (stuck makes zero progress → can't complete fresh)
    printf '%s\n' "$out" | grep -E '^  gvar=' | while read -r line; do
      local g st; g="$(sed -E 's/.*gvar=([0-9]+).*/\1/' <<<"$line")"
      st="$(sed -E 's/.*[0-9]->[0-9]+ ([A-Za-z]+).*/\1/' <<<"$line")"
      [ "$st" = "stuck" ] && continue
      is_golden "$g" && continue
      printf '%s\t%s\t%s\n' "$map" "$g" "$st" >> "$CAND"
    done
  done <<< "$maps"
  echo "candidates: $(wc -l < "$CAND" 2>/dev/null || echo 0) (map,gvar) pairs → $CAND"
}

replay() {  # replay a plain recipe (args after the gvar=N) standalone; echoes the two get-global values
  timeout 180 env DISPLAY="${DISPLAY:-:0}" FALLOUT2_DIR="$GAME" \
    dotnet run --project src/Hexwaste.Viewer -c Debug --no-build -- \
    --game-dir "$GAME" --no-audio $CREATE --get-global "$1" $2 --get-global "$1" --quest-probe --rng-seed 1 2>/dev/null \
    | grep -E "^get-global:" | awk '{print $NF}' | tr '\n' ' '
}

verify() {
  echo "== VERIFY (drive → extract recipe → REPLAY recipe standalone) =="
  # The driver's own completed= can be a FALSE POSITIVE: value-branch tie-breaking (#4) mutates
  # gvar state while exploring terminal options, so the driver sees completion but the recorded
  # picks don't reproduce it fresh. The honest test is replaying the emitted RECIPE standalone
  # (exactly what a golden does) and confirming the gvar advances to the driver-reported value.
  [ -s "$CAND" ] || { echo "no candidates ($CAND missing/empty) — run discover first"; return 1; }
  local recipes="$LOG/new-completions.txt"; : > "$recipes"
  local matrix="$LOG/verify-matrix.txt"; : > "$matrix"
  # dedupe (map,gvar); a gvar can surface on several maps
  sort -u "$CAND" | while IFS=$'\t' read -r map g st; do
    [ -z "$g" ] && continue
    local out; out="$(drive --goto-map "$map" --quest-drive "$g")"
    local verdict; verdict="$(printf '%s\n' "$out" | grep -E '^quest-drive: ' | head -1)"
    local completed; completed="$(sed -E 's/.*completed=([01]).*/\1/' <<<"$verdict")"
    local drvend; drvend="$(sed -E 's/.*end=([0-9-]+).*/\1/' <<<"$verdict")"
    if [ "$completed" != "1" ]; then
      printf '%-14s gvar=%-4s batch=%-9s driver=activated (end=%s) — skip\n' "$map" "$g" "$st" "${drvend:-?}" | tee -a "$matrix"
      continue
    fi
    # driver claims completion → extract its recipe and REPLAY it standalone
    local cmd; cmd="$(printf '%s\n' "$out" | grep -E '^quest-drive-cmd: ' | head -1)"
    local recipe; recipe="$(sed -E "s/^quest-drive-cmd: gvar=$g //" <<<"$cmd")"
    local vals; vals="$(replay "$g" "$recipe")"       # "<start> <end>"
    local rstart rend; rstart="$(awk '{print $1}' <<<"$vals")"; rend="$(awk '{print $2}' <<<"$vals")"
    if [ -n "$rend" ] && [ "$rend" = "$drvend" ] && [ "$rend" != "$rstart" ]; then
      printf '%-14s gvar=%-4s batch=%-9s driver=completed end=%s  REPLAY %s->%s  ✓NEW GOLDEN\n' \
        "$map" "$g" "$st" "$drvend" "$rstart" "$rend" | tee -a "$matrix"
      printf '%s\n' "$cmd" >> "$recipes"
    else
      printf '%-14s gvar=%-4s batch=%-9s driver=completed end=%s  REPLAY %s->%s  ✗false-positive\n' \
        "$map" "$g" "$st" "$drvend" "${rstart:-?}" "${rend:-?}" | tee -a "$matrix"
    fi
  done
  echo "== NEW recipe-verified completions (golden-ready) =="
  if [ -s "$recipes" ]; then cat "$recipes"; else echo "(none)"; fi
  cp "$recipes" "$OUT/harvest-recipes.txt" 2>/dev/null || true
}

case "$MODE" in
  discover) discover ;;
  verify)   verify ;;
  all)      discover && verify ;;
  *) echo "usage: $0 [discover|verify|all]"; exit 2 ;;
esac
