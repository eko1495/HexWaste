#!/usr/bin/env bash
# Headless build of the trace-patched fallout2-ce. Needs: cmake 4.x (~/.local/bin), gcc, make.
set -euo pipefail
cd "$(dirname "$0")/../.."
export PATH="$HOME/.local/bin:$PATH"
WT=reference/fallout2-ce-oracle
DEPS="$PWD/reference/fallout2-ce/build/_deps"
PATCH_SHA=$(sha1sum tools/fo2ce-trace/trace.patch | cut -c1-12)
cmake -S "$WT" -B "$WT/build-oracle" -G "Unix Makefiles" \
  -DCMAKE_BUILD_TYPE=RelWithDebInfo \
  -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
  -DCMAKE_C_FLAGS="-std=gnu11" \
  -DFALLOUT_VENDORED=ON \
  -DSDL_X11=OFF -DSDL_WAYLAND=OFF -DSDL_OPENGL=OFF -DSDL_OPENGLES=OFF \
  -DFETCHCONTENT_SOURCE_DIR_SDL2="$DEPS/sdl2-src" \
  -DFETCHCONTENT_SOURCE_DIR_ZLIB="$DEPS/zlib-src" \
  -DFETCHCONTENT_SOURCE_DIR_FPATTERN="$DEPS/fpattern-src" \
  -DFETCHCONTENT_FULLY_DISCONNECTED=ON \
  -DORACLE_PATCH_SHA="$PATCH_SHA"
cmake --build "$WT/build-oracle" -j"$(nproc)"
ls -l "$WT/build-oracle/fallout2-ce"
