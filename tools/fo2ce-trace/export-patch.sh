#!/usr/bin/env bash
# Regenerate tools/fo2ce-trace/trace.patch from the oracle worktree (new files included).
set -euo pipefail
cd "$(dirname "$0")/../.."
WT=reference/fallout2-ce-oracle
git -C "$WT" add -A
git -C "$WT" diff --cached e97087b -- . ':!build-oracle' > tools/fo2ce-trace/trace.patch
sha1sum tools/fo2ce-trace/trace.patch
