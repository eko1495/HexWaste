#!/usr/bin/env bash
# Create (or reset) the gitignored oracle worktree of the fallout2-ce reference clone at the
# authoritative vanilla commit e97087b, and apply tools/fo2ce-trace/trace.patch on top.
# Usage: tools/fo2ce-trace/worktree.sh [--reset]
set -euo pipefail
cd "$(dirname "$0")/../.."
CLONE=reference/fallout2-ce
WT=reference/fallout2-ce-oracle
BASE=e97087b
PATCH="$PWD/tools/fo2ce-trace/trace.patch"

if [ "${1:-}" = "--reset" ] && [ -d "$WT" ]; then
  git -C "$CLONE" worktree remove --force "$(cd "$WT" && pwd)"
fi
if [ ! -d "$WT" ]; then
  git -C "$CLONE" worktree add --detach "$(pwd)/$WT" "$BASE"
  if [ -s "$PATCH" ]; then
    git -C "$WT" apply --index "$PATCH"
  fi
fi
echo "$WT"
