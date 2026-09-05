---
name: run-golden-suite
description: Use after changing Hexwaste engine or game-script logic, before claiming the change is safe — to pick which golden regression suite(s) under scripts/*-golden.sh cover the touched code and whether to run check or record mode.
---

# Running the golden test suites

## Core principle

Hexwaste verifies game-logic fidelity with headless golden transcripts, not
just unit tests: each suite re-runs deterministic scenarios and diffs stdout
against committed fixtures in `tests/golden-*/`. `check` mode (default)
verifies nothing changed; `record` mode recaptures fixtures from current
behavior and should only follow a reviewed, intentional diff — never a blind
fix for a failing check.

## Which suite covers what

| Script | Covers | Touch it after changing |
|---|---|---|
| `combat-golden.sh` | Deterministic headless fights, full transcript | CombatEngine / combat math |
| `encounter-golden.sh` | Worldmap random encounters + companions | worldmap/encounter/companion code |
| `endgame-golden.sh` | Endgame slideshow + death-ending selection | endgame/ending-selector logic |
| `opening-golden.sh` | Opening spine (artemple→arcaves→arvillag→argarden→arbridge): census, map_update, chained transitions, new-game GVARs | map loading, map_enter/map_update scripts, GVAR seeding |
| `census-sweep.sh` | Static per-region external-demand census (silent quest-gap tripwire) | script external wiring, `tools/ProcAnalyze` |
| `quest-golden.sh` | End-to-end quest lifecycle via the real dialogue VM | quest scripts, dialogue system |
| `quest-harvest.sh [discover\|verify\|all]` | Harvest/replay quest completion recipes across maps | quest content sweeps |
| `harness-selftest.sh` | Meta: does the harness itself fail when something's broken (hermetic, CI-run, no game data) | the golden harness (`golden-lib.sh`) itself |

All suites (except `harness-selftest.sh` and the census axis) need
`FALLOUT2_DIR` game data; most also need a real display (MonoGame
GraphicsDevice) — they aren't part of the data-free CI test split.

## Workflow

1. Identify what you touched and match it against the table — when in doubt
   (e.g. you touched `tile.cc`-derived grid math or the dialogue VM, which are
   cross-cutting), run more suites rather than fewer; they're cheap headless
   diffs.
2. Run in `check` mode: `scripts/<suite>-golden.sh` (or `check` explicitly).
3. A failing diff means one of two things — decide which before touching
   fixtures:
   - **A regression** (unintended behavior change) → fix the bug, re-run
     `check` until clean.
   - **An intended behavior change** → re-run in `record` mode, then
     **diff-review the fixture changes yourself** before committing. Never
     record blind — that bakes a bug in as the new baseline.
4. Commit fixture changes alongside the code change that caused them, in the
   same commit/PR, so the diff is reviewable together.

## Common mistakes

| Mistake | Why it's wrong |
|---|---|
| Recording without reviewing the fixture diff | Silently bakes a regression in as the new "expected" behavior |
| Skipping a suite because the change "looks unrelated" | Cross-cutting code (grid math, dialogue VM, script externals) affects suites you wouldn't expect |
| Treating `census-sweep.sh` / `opening-golden.sh`'s census axis like the others | These are VM-free and display-free — a failure there points at script wiring, not runtime state |
