#!/usr/bin/env bash
# Stage-1 triage for the maintained-fork fix harvest.
#   docs/superpowers/specs/2026-08-14-fork-fix-harvest-design.md
#
# Lists fork commits between our pinned reference (e97087b) and community/main that touch C++
# sources we actually ported, dropping build/CI/platform/mapper-only churn. NO judgment is applied
# here — every surviving row still needs the Stage-2 rationale read.
#
# Usage: scripts/fork-triage.sh [base] [head]   (defaults: e97087b main)
set -euo pipefail

BASE="${1:-e97087b}"
HEAD_REF="${2:-main}"
REPO="fallout2-ce/fallout2-ce"

# Files we ported logic from. Anything outside this list is not our subsystem. Applied per-candidate
# in step 3 (the compare API does not carry per-commit file lists), exported for that use.
export PORTED='^src/(tile|combat|combat_ai|critter|object|map|proto|item|skill|perk|interpreter|interpreter_extra|scripts|worldmap|animation|art|color|palette|dfile|db|game_dialog|automap|inventory|party_member|queue|stat|trait|display_monitor|light|actions|pipboy|elevator|endgame|random)\.cc$'

for page in 1 2 3 4 5; do
  gh api "repos/$REPO/compare/$BASE...$HEAD_REF?per_page=250&page=$page" 2>/dev/null || true
done | jq -r '
  .commits // [] | .[] |
  . as $c |
  (($c.commit.message // "") | split("\n") | (.[0] // "")) as $subj |
  ($subj | capture("#(?<n>[0-9]+)").n // "") as $pr |
  [$c.sha[0:9], $pr, $subj] | @tsv
' | sort -u
