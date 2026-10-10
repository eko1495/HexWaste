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

## Clock model

Virtual time advances only by 10 ms per `tickersExecute` pass, plus `delay_ms(ms)` adding `ms` (it replaces a
real-time block). Nothing else advances time: `FpsLimiter` mark/throttle are no-ops, so time per tick does not
depend on how many frames are presented. `paletteInit` guards its fade-duration division (0 ms on the virtual clock).

**Render-only-loop guard.** A few engine wait loops are gated on `getTicksSince()` but only render
(`gameMouseRefresh`/`renderPresent`/throttle) and never reach `tickersExecute` or `delay_ms`, so on the virtual
clock they would spin forever. `renderPresent` therefore calls `oracleRenderGuard()`: if no tick or `delay_ms`
advanced the clock since the previous present, the clock advances by 10 ms. Loops that tick keep exactly 10 ms per
tick. Audited render-only loops (all covered): `interfaceBarEndButtonsShow`/`Hide` (interface.cc ~1403/~1462, hit
at every `_combat_begin`/`_combat_over`), the dialog fidget and head-transition loops (game_dialog.cc ~2570/~2621).
Combat/animation waits (`_combat_turn_run`, `animationIsBusy` loops, `inputPauseForTocks`) go through `_process_bk`;
`inputBlockForTocks` and the elevator/loadsave throttles use `delay_ms`. Endgame slideshow loops run with
`tickersDisable()` and are outside the scenario scope.

## Scenarios

`run.sh scenario=<file> trace=<file> fixture=<name>` loads `SAVEGAME/SLOT10` headlessly, opens the trace, and runs
the scenario (`seed <int>` first, then `wait <ms>` / `end-turn [count]`). `scripts/oracle-selfcheck.sh <fixture>`
copies `game-data/oracle-saves/<fixture>` into SLOT10, runs twice and requires byte-identical traces. Because control
lives inside fo2ce's own combat loops once combat starts, the runner is a step machine called from every
`oracleTick()`. End-turn presses `KEY_SPACE` while the dude sits in `_combat_input`. Exit codes: 0 done, 4 load failed,
5 main loop returned early (dude dead/quit), 6 bad scenario, 7 end-turn cap (600000 virtual ms) reached.

`run.sh` passes `[sound]initialize=0`: with sound on, two same-seed klamath-hour runs diverged (one brahmin `MOVE`
at ~2.85M virtual ms, identical RNG/PROC/VAR streams) - the sound engine is paced by SDL's real-time audio thread.
With sound off both runs are byte-identical. Sound has no game-logic role the oracle compares.
