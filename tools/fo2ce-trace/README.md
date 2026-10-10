# fo2ce trace oracle

Builds a trace-patched, headless copy of vanilla fallout2-ce (`e97087b`) so its event traces can be
compared with Hexwaste. A virtual clock (10 ms per `tickersExecute`) makes runs deterministic.

## Scripts

- `worktree.sh [--reset]` - create/reset the gitignored worktree `reference/fallout2-ce-oracle` at `e97087b` and apply `trace.patch`.
- `export-patch.sh` - regenerate `trace.patch` from the worktree (new files included). **Run before committing.**
- `build.sh` - headless cmake build into `reference/fallout2-ce-oracle/build-oracle`.
- `run.sh [key=value ...]` - run from `reference/fallout2-ce/run` with `[oracle]enabled=1` and each `[oracle]key=value` (e.g. `exit_after_ms=5000`).

The worktree is disposable - `trace.patch` is the source of truth.

## Prerequisites

cmake 4.x in `~/.local/bin`, gcc/g++/make (no ninja), the `reference/fallout2-ce` clone (with its configured
`build/_deps`) and its `run/` dir holding the game data. No X11/Wayland headers needed (offscreen SDL).

## Build findings

- Dependency sources are reused offline: `FETCHCONTENT_SOURCE_DIR_SDL2`, `_ZLIB`, `_FPATTERN` (declare names `sdl2`, `zlib`, `fpattern`) are honoured, together with `FETCHCONTENT_FULLY_DISCONNECTED=ON`.
- gcc defaults to C23, which breaks vendored SDL 2.26.1 (`bool` typedef); `build.sh` passes `-DCMAKE_C_FLAGS=-std=gnu11`.
- Headless needs `SDL_VIDEODRIVER=offscreen` (software renderer; svga.cc skips the GL hint/flag) and the oracle tolerating the offscreen driver's lack of relative-mouse mode (dinput.cc).
- Virtual clock details: `delay_ms` advances it without sleeping; `FpsLimiter::throttle` advances it one 10 ms step (otherwise `paletteInit` divides by a 0 ms fade).
