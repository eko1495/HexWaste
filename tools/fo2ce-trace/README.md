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

## End-to-end workflow

All outputs land in gitignored places (`reference/`, `game-data/oracle-saves/`, `scratch/oracle-runs/`).

1. **Build the oracle binary** - `tools/fo2ce-trace/worktree.sh` (first time, or `--reset`), then
   `tools/fo2ce-trace/build.sh`.
2. **Make the vanilla saves** - `scripts/oracle-make-save.sh <fixture>` replays
   `tests/golden-oracle/<fixture>/recipe.txt` in fo2ce and copies the slot to `game-data/oracle-saves/<fixture>/`.
   Only needed once per fixture (or when its recipe changes).
3. **Self-check fo2ce** - `scripts/oracle-selfcheck.sh <fixture>` must print `SELFCHECK OK` (two same-seed
   runs, byte-identical traces). A failure means a nondeterminism leak in the patch; fix that before
   trusting any comparison.
4. **Compare** - `scripts/oracle-run.sh <fixture>` builds Hexwaste.Viewer + TraceDiff once, runs fo2ce
   then Hexwaste (`--load-vanilla-save --rng fo2ce --fixture --scenario --trace --no-audio`), and prints
   `IDENTICAL (N lines)` (exit 0) or `DIVERGED at fo2ce line N` with the preceding context and the
   mismatching pair (exit 1). Traces and logs: `scratch/oracle-runs/<fixture>/{fo2ce,hexwaste}.{trace,log}`.
   If either engine exits non-zero the runner stops with exit 3 and names the engine and code (fo2ce:
   4 load fail, 5 dude dead/KO, 6 bad scenario, 7 end-turn cap, 124 timeout; Hexwaste: 2 bad flags,
   5 dude dead/KO, 7 end-turn cap, 8 unhandled exception, 124 timeout) - except both exiting 5, a
   legitimate shared outcome, which is still diffed. `ORACLE_TIMEOUT` (seconds, default 1800) bounds each
   engine run; klamath-hour takes about 2 minutes in fo2ce.

The M1 results and the open findings are in `docs/research-notes/oracle-m1-findings-2026-10.md`. As of M1
every fixture diverges at line 2 (engine differences in the proc stream), so a later bug is found by
comparing two Hexwaste traces or by projecting one record kind (e.g. `grep ' ATTACK '`), not as the
lockstep first divergence.
