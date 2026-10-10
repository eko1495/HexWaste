#!/usr/bin/env bash
# Run the headless oracle binary from the game run dir. Every argument is an [oracle] key=value
# override, passed through fallout2-ce's own [section]key=value argv mechanism (config.cc:79).
set -euo pipefail
cd "$(dirname "$0")/../.."
BIN="$PWD/reference/fallout2-ce-oracle/build-oracle/fallout2-ce"
RUN=reference/fallout2-ce/run
args=("[oracle]enabled=1" "[misc]SkipOpeningMovies=2")
for kv in "$@"; do args+=("[oracle]$kv"); done
cd "$RUN"
SDL_VIDEODRIVER=offscreen SDL_AUDIODRIVER=dummy timeout 1800 "$BIN" "${args[@]}"
