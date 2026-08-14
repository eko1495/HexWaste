#!/usr/bin/env bash
# Stage-1 triage for the maintained-fork fix harvest.
#   docs/superpowers/specs/2026-08-14-fork-fix-harvest-design.md
#
# ENUMERATES every fork commit between our pinned reference (e97087b) and community/main, one TSV
# row each (sha, PR number, subject). It does NOT filter: no file filter and no subject filter are
# applied to the output, and NO judgment is applied here.
#
# What the 2026-08-14 harvest actually did with it: piped this output through
#   grep -iE 'fix|bug|crash|correct|revert'
# which is what produced the 252-row shortlist (docs/research-notes/fork-fix-shortlist.tsv). The
# PORTED pattern below is documented, not applied — the compare API does not carry per-commit file
# lists, so the file filter is a per-candidate step done downstream during the rationale read.
#
# Usage: scripts/fork-triage.sh [base] [head]   (defaults: e97087b main)
set -euo pipefail

BASE="${1:-e97087b}"
HEAD_REF="${2:-main}"
REPO="fallout2-ce/fallout2-ce"
PER_PAGE=250

# Files we ported logic from. Anything outside this list is not our subsystem. Deliberately UNUSED by
# this script (shellcheck SC2034 is expected): the compare API does not carry per-commit file lists,
# so this is applied per-candidate during the downstream rationale read. It lives here as the single
# written-down copy of the pattern, for that step to reuse.
# shellcheck disable=SC2034
PORTED='^src/(tile|combat|combat_ai|critter|object|map|proto|item|skill|perk|interpreter|interpreter_extra|scripts|worldmap|animation|art|color|palette|dfile|db|game_dialog|automap|inventory|party_member|queue|stat|trait|display_monitor|light|actions|pipboy|elevator|endgame|random)\.cc$'

TMPDIR="$(mktemp -d)"
trap 'rm -rf "$TMPDIR"' EXIT

# Fetch one page of the compare API into $2, failing loudly (message + non-zero exit) instead of
# silently contributing zero rows on auth failure, rate limit, network error, or a bad ref.
fetch_page() {
  local page="$1" out="$2"
  if ! gh api "repos/$REPO/compare/$BASE...$HEAD_REF?per_page=$PER_PAGE&page=$page" >"$out" 2>"$TMPDIR/err"; then
    echo "fork-triage: gh api failed fetching page $page of $REPO compare $BASE...$HEAD_REF" >&2
    cat "$TMPDIR/err" >&2
    exit 1
  fi
}

fetch_page 1 "$TMPDIR/page-001.json"
TOTAL="$(jq -r '.total_commits // empty' "$TMPDIR/page-001.json")"
if ! [[ "$TOTAL" =~ ^[0-9]+$ ]]; then
  echo "fork-triage: compare API response for page 1 has no numeric total_commits (got: '${TOTAL}') — cannot determine how many pages to fetch" >&2
  exit 1
fi

# Drive the page count from total_commits instead of a hardcoded ceiling, so the script keeps
# working when the fork grows past whatever fits today.
PAGES=$(( (TOTAL + PER_PAGE - 1) / PER_PAGE ))
[ "$PAGES" -lt 1 ] && PAGES=1

for ((page = 2; page <= PAGES; page++)); do
  fetch_page "$page" "$TMPDIR/page-$(printf '%03d' "$page").json"
done

FETCHED="$(jq -s '[.[] | (.commits // []) | .[]] | length' "$TMPDIR"/page-*.json)"
if [ "$FETCHED" -ne "$TOTAL" ]; then
  echo "fork-triage: fetched $FETCHED commits across $PAGES page(s) but the compare API reports total_commits=$TOTAL — pagination is incomplete, refusing to emit a short list" >&2
  exit 1
fi

jq -sr '
  [.[] | (.commits // []) | .[]] | .[] |
  . as $c |
  (($c.commit.message // "") | split("\n") | (.[0] // "")) as $subj |
  ($subj | capture("#(?<n>[0-9]+)").n // "") as $pr |
  [$c.sha[0:9], $pr, $subj] | @tsv
' "$TMPDIR"/page-*.json | sort -u
