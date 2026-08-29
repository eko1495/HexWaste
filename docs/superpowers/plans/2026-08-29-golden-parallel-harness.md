# Parallel Golden-Test Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cut the golden suites from ~40 minutes to ~2-4 minutes by invoking the built binary instead of `dotnet run` and running fixtures through a job pool, without weakening any assertion or touching any fixture.

**Architecture:** One shared `scripts/golden-lib.sh` owns the build step, a job pool, per-job scratch directories, ordered output and the verdict. Six suite scripts shrink to declarations — their runners, their `SCENARIOS`, and the wording of their own verdict. The unit of work is a *(fixture, pass)* pair, so the determinism double-run costs a core rather than wall time.

**Tech Stack:** Bash 5.3 (`wait -n` job control), the already-built .NET binaries under `bin/Debug/net10.0/`.

**Design spec:** `docs/superpowers/specs/2026-08-29-golden-parallel-harness-design.md`

## Global Constraints

- **Byte-identical stdout is the acceptance criterion**, not "the suites pass". Every suite's output under the new harness must diff empty against the pre-change baseline.
- **Do not touch any file under `tests/golden-*/`.** No fixture is re-recorded by this work. If a fixture appears to need re-recording, stop — that is a regression in the harness, not a fixture problem.
- **Do not change any scenario's arguments**, with exactly one sanctioned exception: the four `encounter-golden.sh` scenarios that write to hardcoded `/tmp` paths gain a `@SCRATCH@` token (Task 7).
- **Keep the determinism double-run** for the five suites that have it. `census-sweep.sh` has never had one — do not add it.
- **Preserve each suite's exact wording**: its mismatch label (`REGRESSION` vs `DIFF`), whether its diff output is truncated (`| head -30`) or full, its pass/fail verdict strings, and its exit style. These differ per suite and are part of the byte-identical requirement.
- Concurrency comes from `GOLDEN_JOBS`, defaulting to `nproc`.
- The suites need `DISPLAY` set and real game data at `$FALLOUT2_DIR` (default `./game-data`).
- Conventional commits; commit at the end of every task.

---

## The per-suite variation matrix

This is the whole reason a shared library needs parameters. Copy it into your head before writing anything:

| suite | tool | timeout | stdout filter | double-run | mismatch label | diff output | verdict pass / fail | exit |
|---|---|---|---|---|---|---|---|---|
| combat | viewer | 90 | none (raw) | yes | `REGRESSION` | `\| head -30` | `golden combat: ALL PASS` / `FAILURES` | `exit $fail` |
| encounter | viewer | 90 | `$FILTER` (long) | yes | `REGRESSION` | `\| head -30` | `golden encounter: ALL PASS` / `FAILURES` | `exit $fail` |
| quest | viewer | 120 | `^(get-global:\|quest-item:\|quest-probe:\|party-count:\|party:)` | yes | `DIFF` | full | `quest e2e: ALL PASS` / `FAIL` | `exit 1` |
| endgame | viewer | 90 | `slide:\|endgame-probe:\|death-ending-probe:` | yes | `DIFF` | full | `golden endgame: ALL PASS` / `FAIL` | `exit 1` |
| opening | **two runners** | 120 / none | two different filters | yes | `DIFF` | full | `golden opening: ALL PASS` / `FAIL` | `exit 1` |
| census | ProcAnalyze | none | `procanalyze:\|stubbed:` | **no** | `DIFF` | full | `census sweep: ALL PASS` / `FAIL` | `exit 1` |

`census-sweep.sh` also has a rule no other suite has: **empty output is `LOAD-FAIL`**, checked *before* the record branch, and its record mode exits 0/1 on `fail` rather than unconditionally 0.

Counts: 263 fixtures across the five double-run suites (526 invocations) plus census's 16 single-pass = **542 invocations**. (The spec says 558; it assumed census double-runs. 542 is the measured truth.)

---

## File Structure

| File | Role |
|---|---|
| `scripts/golden-lib.sh` | **Create.** The whole shared runner: runner registry, job pool, per-job scratch, ordered emit, verdict. |
| `scripts/endgame-golden.sh` | **Modify.** Task 2 — first migration, 5 fixtures, smallest blast radius. |
| `scripts/combat-golden.sh` | **Modify.** Task 3 — exercises `REGRESSION` + `head -30` + `exit $fail` + no filter. |
| `scripts/census-sweep.sh` | **Modify.** Task 4 — exercises single-pass, the `LOAD-FAIL` hook and ProcAnalyze. |
| `scripts/opening-golden.sh` | **Modify.** Task 5 — exercises two runners and the three-field scenario form. |
| `scripts/quest-golden.sh` | **Modify.** Task 6. |
| `scripts/encounter-golden.sh` | **Modify.** Task 7 — the 188-fixture suite plus the four `@SCRATCH@` scenarios. |
| `.superpowers/sdd/golden-baseline.log` | **Read-only oracle.** Never edit. |

---

### Task 1: Confirm the baseline oracle exists and is usable

Everything downstream is verified by diffing against a baseline captured **before** any change. Without it there is no acceptance criterion, so this task establishes it and nothing else.

**Files:** none modified.

**Interfaces:**
- Produces: `.superpowers/sdd/golden-baseline.log`, containing a `=== <suite> ===` section per suite, each suite's `ok  <name>` lines in declaration order, its verdict line, and a `WALL <suite> <seconds> s` line. Ends with `BASELINE-DONE`.

- [ ] **Step 1: Check whether the baseline is already present and complete**

```bash
cd /home/eko/dev/FPOC
tail -1 .superpowers/sdd/golden-baseline.log 2>/dev/null
grep -cE '^ok ' .superpowers/sdd/golden-baseline.log 2>/dev/null
grep -E '^WALL ' .superpowers/sdd/golden-baseline.log 2>/dev/null
```

Expected: last line `BASELINE-DONE`, `279` ok lines, and six `WALL` lines. **If you get that, skip to Step 3.**

- [ ] **Step 2: Capture it if it is missing or incomplete**

Only if Step 1 did not show `BASELINE-DONE`. This takes ~40 minutes.

```bash
cd /home/eko/dev/FPOC
git stash list   # must be clean of harness changes; the baseline must reflect PRE-change code
git diff --stat HEAD -- scripts/    # must be empty
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
{
  echo "# golden baseline captured $(date -Is) from $(git rev-parse --short HEAD)"
  for s in combat quest endgame opening encounter; do
    echo "=== $s ==="
    /usr/bin/time -f "WALL $s %e s" ./scripts/$s-golden.sh check 2>&1
  done
  echo "=== census ==="
  /usr/bin/time -f "WALL census %e s" ./scripts/census-sweep.sh check 2>&1
  echo "BASELINE-DONE"
} > .superpowers/sdd/golden-baseline.log 2>&1
```

**If `git diff --stat HEAD -- scripts/` is not empty, stop.** A baseline captured after a harness change proves nothing.

- [ ] **Step 3: Record the per-suite baseline numbers in your report**

```bash
cd /home/eko/dev/FPOC
grep -E '^WALL |ALL PASS|FAIL' .superpowers/sdd/golden-baseline.log
```

Copy the six `WALL` values into your report. They are the "before" figures every later task compares against. No commit — the log is gitignored scratch.

---

### Task 2: The shared library, proven on the smallest suite

**Files:**
- Create: `scripts/golden-lib.sh`
- Modify: `scripts/endgame-golden.sh`

**Interfaces:**
- Produces, for every later task:
  - `golden_runner NAME TIMEOUT BIN FILTER EXTRA` — registers a runner. `TIMEOUT` in seconds or `0` for none. `FILTER` is an ERE for `grep -E`, or `""` for raw stdout. `EXTRA` is a literal argument string always inserted before the scenario's own args (e.g. `--no-audio`), or `""`. The first registered runner is the default.
  - `golden_run_all` — parses `SCENARIOS`, runs the pool, emits results in declaration order, and sets `GOLDEN_FAIL` to 0 or 1. Reads the variables listed below.
  - Suite-settable variables, all read by `golden_run_all`: `MODE`, `GAME`, `FIX`, `SCENARIO_FIELDS` (2 or 3, default 2), `DOUBLE_RUN` (1 or 0, default 1), `MISMATCH_LABEL` (default `DIFF`), `DIFF_TRUNC` (a number of lines, or `""` for full, default `""`), `GOLDEN_RESULT_HOOK` (a function name, or `""`).
  - The optional hook is called as `HOOK "$name" "$out"` before the record/compare branch. It returns 0 to continue, or non-zero to mark the fixture failed — having already printed its own message.

**Background you need:**
- `$(...)` strips trailing newlines. Today's scripts capture with `out="$(run ...)"` and then write `printf '%s\n' "$out"`. An *empty* capture therefore writes a single empty line into the fixture. The library must round-trip identically: jobs write with the same capture-then-printf, and the emit phase re-reads with `$(cat ...)`.
- Scenario args must stay **unquoted** at the point of expansion — today's `run() { ... $1 ... }` relies on word splitting, and the args contain meaningful spaces.
- Bash here is 5.3, so `wait -n` is available for the pool.

- [ ] **Step 1: Write the library**

Create `scripts/golden-lib.sh`:

```bash
# shellcheck shell=bash
#
# Shared runner for the golden suites.
#
# A suite script sources this, registers one or more runners, declares SCENARIOS,
# sets the few variables that describe its own wording, then calls golden_run_all.
#
# The unit of work is a (fixture, pass) pair. Both passes of the same fixture may
# run concurrently, which is what makes the determinism double-run cost a core
# instead of wall time.

GOLDEN_JOBS="${GOLDEN_JOBS:-$(nproc)}"

declare -A _GR_TIMEOUT _GR_BIN _GR_FILTER _GR_EXTRA
_GOLDEN_DEFAULT_RUNNER=""

# golden_runner NAME TIMEOUT BIN FILTER EXTRA
golden_runner() {
  _GR_TIMEOUT["$1"]="$2"; _GR_BIN["$1"]="$3"; _GR_FILTER["$1"]="$4"; _GR_EXTRA["$1"]="$5"
  [ -z "$_GOLDEN_DEFAULT_RUNNER" ] && _GOLDEN_DEFAULT_RUNNER="$1"
  return 0
}

# _golden_exec RUNNER SCRATCH ARGS_STRING -> filtered stdout
# ARGS_STRING is deliberately expanded unquoted: the scenarios rely on word splitting.
_golden_exec() {
  local r="$1" scratch="$2" args="$3"
  local t="${_GR_TIMEOUT[$r]}" bin="${_GR_BIN[$r]}" f="${_GR_FILTER[$r]}" x="${_GR_EXTRA[$r]}"
  local -a pre=()
  [ "$t" != "0" ] && pre=(timeout "$t")
  if [ -n "$f" ]; then
    "${pre[@]}" env DISPLAY="${DISPLAY:-:0}" FALLOUT2_DIR="$GAME" TMPDIR="$scratch" \
      "$bin" --game-dir "$GAME" $x $args 2>/dev/null | grep -E "$f"
  else
    "${pre[@]}" env DISPLAY="${DISPLAY:-:0}" FALLOUT2_DIR="$GAME" TMPDIR="$scratch" \
      "$bin" --game-dir "$GAME" $x $args 2>/dev/null
  fi
}

# _golden_job JOBDIR INDEX PASS RUNNER ARGS_STRING
_golden_job() {
  local jobdir="$1" i="$2" pass="$3" r="$4" args="$5"
  local scratch="$jobdir/scratch.$i.$pass"
  mkdir -p "$scratch"
  # @SCRATCH@ lets a scenario that must write to disk get a private directory.
  args="${args//@SCRATCH@/$scratch}"
  local out
  out="$(_golden_exec "$r" "$scratch" "$args")"
  printf '%s\n' "$out" > "$jobdir/$i.$pass.out"
}

golden_run_all() {
  local jobdir; jobdir="$(mktemp -d)"
  local fields="${SCENARIO_FIELDS:-2}"
  local double="${DOUBLE_RUN:-1}"
  local label="${MISMATCH_LABEL:-DIFF}"
  local trunc="${DIFF_TRUNC:-}"
  local hook="${GOLDEN_RESULT_HOOK:-}"

  local -a names=() runners=() argses=()
  local entry name rest
  for entry in "${SCENARIOS[@]}"; do
    name="${entry%%|*}"; rest="${entry#*|}"
    names+=("$name")
    if [ "$fields" = 3 ]; then
      runners+=("${rest%%|*}"); argses+=("${rest#*|}")
    else
      runners+=("$_GOLDEN_DEFAULT_RUNNER"); argses+=("$rest")
    fi
  done

  local running=0 i pass
  for i in "${!names[@]}"; do
    for pass in 1 2; do
      if [ "$pass" = 2 ]; then
        [ "$MODE" = "record" ] && continue
        [ "$double" = 1 ] || continue
      fi
      while [ "$running" -ge "$GOLDEN_JOBS" ]; do wait -n; running=$((running - 1)); done
      _golden_job "$jobdir" "$i" "$pass" "${runners[$i]}" "${argses[$i]}" &
      running=$((running + 1))
    done
  done
  wait

  # Emit in DECLARATION order, so output is byte-identical regardless of completion order.
  GOLDEN_FAIL=0
  local out out2
  for i in "${!names[@]}"; do
    name="${names[$i]}"
    out="$(cat "$jobdir/$i.1.out")"

    if [ -n "$hook" ]; then
      if ! "$hook" "$name" "$out"; then GOLDEN_FAIL=1; continue; fi
    fi

    if [ "$MODE" = "record" ]; then
      printf '%s\n' "$out" > "$FIX/$name.txt"
      echo "recorded $name ($(printf '%s\n' "$out" | wc -l | tr -d ' ') lines)"
      continue
    fi

    if [ "$double" = 1 ]; then
      out2="$(cat "$jobdir/$i.2.out")"
      if [ "$out" != "$out2" ]; then echo "NONDETERMINISTIC: $name"; GOLDEN_FAIL=1; fi
    fi

    if [ ! -f "$FIX/$name.txt" ]; then
      echo "MISSING FIXTURE: $name (run 'record' first)"; GOLDEN_FAIL=1; continue
    fi

    if diff -u "$FIX/$name.txt" <(printf '%s\n' "$out") >/dev/null; then
      echo "ok  $name"
    else
      echo "$label: $name"
      if [ -n "$trunc" ]; then
        diff -u "$FIX/$name.txt" <(printf '%s\n' "$out") | head -"$trunc"
      else
        diff -u "$FIX/$name.txt" <(printf '%s\n' "$out")
      fi
      GOLDEN_FAIL=1
    fi
  done

  rm -rf "$jobdir"
  return 0
}
```

- [ ] **Step 2: Migrate `endgame-golden.sh`**

Replace everything in `scripts/endgame-golden.sh` from the `dotnet build` line to the end of the file with:

```bash
dotnet build src/Hexwaste.Viewer -c Debug >/dev/null || { echo "build failed"; exit 2; }

source "$(dirname "$0")/golden-lib.sh"
golden_runner viewer 90 src/Hexwaste.Viewer/bin/Debug/net10.0/Hexwaste.Viewer \
  "slide:|endgame-probe:|death-ending-probe:" "--no-audio"

golden_run_all
[ "$MODE" = "record" ] && exit 0
if [ "$GOLDEN_FAIL" = 0 ]; then echo "golden endgame: ALL PASS"; else echo "golden endgame: FAIL"; exit 1; fi
```

Leave the file's head — `set -uo pipefail`, the `cd`, `MODE`, `GAME`, `FIX`, `mkdir -p`, and the `SCENARIOS` array — exactly as it is.

- [ ] **Step 3: Verify byte-identical output**

```bash
cd /home/eko/dev/FPOC
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
./scripts/endgame-golden.sh check > /tmp/endgame-new.txt 2>&1; echo "exit=$?"
sed -n '/^=== endgame ===/,/^=== /p' .superpowers/sdd/golden-baseline.log \
  | grep -vE '^=== |^WALL ' > /tmp/endgame-base.txt
diff -u /tmp/endgame-base.txt /tmp/endgame-new.txt && echo "BYTE-IDENTICAL"
```

Expected: `exit=0`, then `BYTE-IDENTICAL`. **A non-empty diff is a stop condition** — do not adjust a fixture to make it pass.

- [ ] **Step 4: Verify it actually got faster**

```bash
cd /home/eko/dev/FPOC
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
/usr/bin/time -f 'new endgame: %e s' ./scripts/endgame-golden.sh check >/dev/null
grep '^WALL endgame' .superpowers/sdd/golden-baseline.log
```

Expected: the new time is clearly below the baseline. With only 5 fixtures the win is modest — the point is that it is not *slower*, which would mean the pool is not running.

- [ ] **Step 5: Prove `record` mode still works — without committing what it writes**

`record` rewrites committed fixtures, so a broken record path is worse than a broken check path:
nobody notices until someone re-records and clobbers 279 files. No later task exercises it, so it
gets checked here, on the smallest suite, and then reverted.

```bash
cd /home/eko/dev/FPOC
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
./scripts/endgame-golden.sh record
git status --short tests/golden-endgame/
git diff --stat tests/golden-endgame/
```

Expected: five `recorded <name> (N lines)` lines, and **`git diff --stat` empty** — re-recording an
unchanged build must reproduce the committed fixtures byte for byte. If any fixture differs, stop:
either the record path or the check path is wrong, and they disagree.

```bash
cd /home/eko/dev/FPOC
git checkout -- tests/golden-endgame/   # leave the tree exactly as you found it
```

- [ ] **Step 6: Prove a timed-out job is reported, not silently passed**

The spec requires this and nothing else in the plan checks it. Force a timeout by registering an
absurdly short one, and confirm the suite fails loudly rather than printing `ok`.

```bash
cd /home/eko/dev/FPOC
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
sed 's/golden_runner viewer 90/golden_runner viewer 1/' scripts/endgame-golden.sh > /tmp/endgame-timeout.sh
chmod +x /tmp/endgame-timeout.sh
bash /tmp/endgame-timeout.sh check; echo "exit=$?"
```

Expected: `DIFF:` lines (the timed-out jobs produced no output, so they mismatch their fixtures),
`golden endgame: FAIL`, and `exit=1`. **A run that prints `ok` under a 1-second timeout means a
timed-out job is being treated as a pass — that is a stop condition.** Delete `/tmp/endgame-timeout.sh`
afterwards; do not commit it.

- [ ] **Step 7: Commit**

```bash
cd /home/eko/dev/FPOC
git add scripts/golden-lib.sh scripts/endgame-golden.sh
git commit -m "test(golden): shared parallel runner, proven on endgame

golden-lib.sh owns the job pool, per-job scratch, ordered emit and the
verdict; suites shrink to declarations. The unit of work is a (fixture,
pass) pair, so the determinism double-run costs a core rather than wall
time. Invokes the built binary instead of dotnet run, which measured
1.16s of pure overhead per invocation.

endgame-golden.sh migrated first as the smallest suite. Output verified
byte-identical against the pre-change baseline."
```

---

### Task 3: Migrate `combat-golden.sh`

The first suite with `REGRESSION`, a truncated diff, no stdout filter, and `exit $fail`. If the library's wording knobs are wrong, this is where it shows.

**Files:**
- Modify: `scripts/combat-golden.sh`

**Interfaces:**
- Consumes: `golden_runner`, `golden_run_all`, `GOLDEN_FAIL`, `MISMATCH_LABEL`, `DIFF_TRUNC` from Task 2.

- [ ] **Step 1: Migrate the script**

Replace everything from `echo "Building viewer..."` to the end of `scripts/combat-golden.sh` with:

```bash
echo "Building viewer..."
dotnet build src/Hexwaste.Viewer -c Debug >/dev/null || { echo "build failed"; exit 2; }

source "$(dirname "$0")/golden-lib.sh"
golden_runner viewer 90 src/Hexwaste.Viewer/bin/Debug/net10.0/Hexwaste.Viewer "" "--no-audio"
MISMATCH_LABEL=REGRESSION
DIFF_TRUNC=30

golden_run_all
[ "$GOLDEN_FAIL" -eq 0 ] && echo "golden combat: ALL PASS" || echo "golden combat: FAILURES"
exit "$GOLDEN_FAIL"
```

Note this suite has **no** stdout filter (the fourth argument is `""`) and, unlike endgame, no `[ "$MODE" = record ] && exit 0` line — its original tail runs the verdict in record mode too. Preserve that.

- [ ] **Step 2: Verify byte-identical output**

```bash
cd /home/eko/dev/FPOC
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
./scripts/combat-golden.sh check > /tmp/combat-new.txt 2>&1; echo "exit=$?"
sed -n '/^=== combat ===/,/^=== /p' .superpowers/sdd/golden-baseline.log \
  | grep -vE '^=== |^WALL ' > /tmp/combat-base.txt
diff -u /tmp/combat-base.txt /tmp/combat-new.txt && echo "BYTE-IDENTICAL"
```

Expected: `exit=0`, `BYTE-IDENTICAL`. The baseline includes the `Building viewer...` line — if the diff shows only that line, your migration dropped it; restore it rather than editing the baseline.

- [ ] **Step 3: Measure**

```bash
cd /home/eko/dev/FPOC
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
/usr/bin/time -f 'new combat: %e s' ./scripts/combat-golden.sh check >/dev/null
grep '^WALL combat' .superpowers/sdd/golden-baseline.log
```

The baseline for this suite was measured at **112.3 s** on 16 cores. Expect single-digit seconds. Report both numbers.

- [ ] **Step 4: Commit**

```bash
cd /home/eko/dev/FPOC
git add scripts/combat-golden.sh
git commit -m "test(golden): run combat through the shared parallel runner

Exercises the wording knobs the library needed: REGRESSION rather than
DIFF, a diff truncated to 30 lines, no stdout filter, and exit \$fail.
Output byte-identical against the baseline."
```

---

### Task 4: Migrate `census-sweep.sh`

The only single-pass suite, the only one with a `LOAD-FAIL` rule, and the only one driving `ProcAnalyze`. It is the test of the hook and of `DOUBLE_RUN=0`.

**Files:**
- Modify: `scripts/census-sweep.sh`

**Interfaces:**
- Consumes: `golden_runner`, `golden_run_all`, `GOLDEN_FAIL`, `DOUBLE_RUN`, `GOLDEN_RESULT_HOOK` from Task 2.
- Produces: nothing consumed later.

**Two traps specific to this suite:**
1. Its scenarios are `"name|mapfile"`, and the original `run()` prepends `--map`. The runner's `EXTRA` argument is where that goes.
2. Its record mode exits `0` or `1` depending on `fail` — unlike the other suites, which exit 0 unconditionally after recording.

- [ ] **Step 1: Migrate the script**

Replace everything from the `dotnet build` line to the end of `scripts/census-sweep.sh` with:

```bash
dotnet build tools/ProcAnalyze -c Debug >/dev/null || { echo "procanalyze build failed"; exit 2; }

source "$(dirname "$0")/golden-lib.sh"
# The scenario field is a bare map filename, so --map lives in the runner's EXTRA.
golden_runner procanalyze 0 tools/ProcAnalyze/bin/Debug/net10.0/ProcAnalyze \
  "procanalyze:|stubbed:" "--map"
DOUBLE_RUN=0
GOLDEN_RESULT_HOOK=census_load_check

# A map that emitted no census line failed to LOAD; reporting that as a fixture
# mismatch would bury the cause. Runs before the record/compare branch, as it did
# in the original loop.
census_load_check() {
  local name="$1" out="$2"
  if [ -z "$out" ]; then
    echo "LOAD-FAIL: $name (emitted no census line)"
    return 1
  fi
  return 0
}

golden_run_all
[ "$MODE" = "record" ] && { [ "$GOLDEN_FAIL" = 0 ] && exit 0 || exit 1; }
if [ "$GOLDEN_FAIL" = 0 ]; then echo "census sweep: ALL PASS"; else echo "census sweep: FAIL"; exit 1; fi
```

**One wording change you must check against the baseline.** The original message is `LOAD-FAIL: $name ($map emitted no census line)` — it includes the map filename, which the hook does not receive. Run Step 2 first: if no fixture is failing, this line never prints and the difference is invisible. **If the baseline does contain a `LOAD-FAIL` line**, stop and report it — the hook signature needs the scenario's args and this plan must be revised.

- [ ] **Step 2: Confirm the baseline has no LOAD-FAIL line, then verify output**

```bash
cd /home/eko/dev/FPOC
grep -c 'LOAD-FAIL' .superpowers/sdd/golden-baseline.log   # expected: 0
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
./scripts/census-sweep.sh check > /tmp/census-new.txt 2>&1; echo "exit=$?"
sed -n '/^=== census ===/,$p' .superpowers/sdd/golden-baseline.log \
  | grep -vE '^=== |^WALL |^BASELINE-DONE' > /tmp/census-base.txt
diff -u /tmp/census-base.txt /tmp/census-new.txt && echo "BYTE-IDENTICAL"
```

Expected: `0`, then `exit=0` and `BYTE-IDENTICAL`.

- [ ] **Step 3: Prove the single-pass rule survived**

The library must not have started double-running this suite. Count the ProcAnalyze processes it launches:

```bash
cd /home/eko/dev/FPOC
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
GOLDEN_JOBS=1 ./scripts/census-sweep.sh check >/dev/null 2>&1 &
sleep 2; pgrep -fc 'ProcAnalyze --game-dir' || true
wait
```

The suite has 16 scenarios; a double-run would produce 32 invocations. Confirm by timing instead if the process count is racy: a `DOUBLE_RUN=0` run should be about half the wall time of the same suite with `DOUBLE_RUN=1` forced. Report which check you used.

- [ ] **Step 4: Commit**

```bash
cd /home/eko/dev/FPOC
git add scripts/census-sweep.sh
git commit -m "test(golden): run the census sweep through the shared runner

The only single-pass suite and the only one with a LOAD-FAIL rule, so it
is what proves DOUBLE_RUN=0 and the result hook. Its scenarios carry a
bare map filename, so --map lives in the runner's EXTRA argument.
Output byte-identical against the baseline."
```

---

### Task 5: Migrate `opening-golden.sh`

The suite that forced the runner model: it dispatches per scenario to two different tools with two different filters, using a three-field `name|kind|args` scenario form.

**Files:**
- Modify: `scripts/opening-golden.sh`

**Interfaces:**
- Consumes: `golden_runner` (registered twice), `SCENARIO_FIELDS=3`, `golden_run_all`, `GOLDEN_FAIL`.

- [ ] **Step 1: Migrate the script**

Replace everything from the first `dotnet build` line to the end of `scripts/opening-golden.sh` with:

```bash
dotnet build src/Hexwaste.Viewer -c Debug >/dev/null || { echo "build failed"; exit 2; }
dotnet build tools/ProcAnalyze -c Debug >/dev/null || { echo "procanalyze build failed"; exit 2; }

source "$(dirname "$0")/golden-lib.sh"
# Scenario kind selects the runner: "census" -> ProcAnalyze, anything else -> the viewer.
golden_runner census 0 tools/ProcAnalyze/bin/Debug/net10.0/ProcAnalyze \
  "procanalyze:|stubbed:" ""
golden_runner viewer 120 src/Hexwaste.Viewer/bin/Debug/net10.0/Hexwaste.Viewer \
  "transit:|map-update:|light:|get-global:|lip-probe:|census:|menu-activate:" "--no-audio"
SCENARIO_FIELDS=3

golden_run_all
[ "$MODE" = "record" ] && exit 0
if [ "$GOLDEN_FAIL" = 0 ]; then echo "golden opening: ALL PASS"; else echo "golden opening: FAIL"; exit 1; fi
```

**Check the scenario kinds before you trust this.** The original dispatches on `kind = "census"` and sends everything else to the viewer. Run:

```bash
cd /home/eko/dev/FPOC
sed -n '/^SCENARIOS=/,/^)/p' scripts/opening-golden.sh | grep -o '|[a-z]*|' | sort -u
```

Every kind that appears must be a registered runner name. If a kind other than `census` appears, register a runner under that exact name with the viewer's settings instead of relying on a default.

- [ ] **Step 2: Verify byte-identical output**

```bash
cd /home/eko/dev/FPOC
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
./scripts/opening-golden.sh check > /tmp/opening-new.txt 2>&1; echo "exit=$?"
sed -n '/^=== opening ===/,/^=== /p' .superpowers/sdd/golden-baseline.log \
  | grep -vE '^=== |^WALL ' > /tmp/opening-base.txt
diff -u /tmp/opening-base.txt /tmp/opening-new.txt && echo "BYTE-IDENTICAL"
```

Expected: `exit=0`, `BYTE-IDENTICAL`.

- [ ] **Step 3: Commit**

```bash
cd /home/eko/dev/FPOC
git add scripts/opening-golden.sh
git commit -m "test(golden): run the opening suite through the shared runner

This is the suite that forced the named-runner model: it dispatches per
scenario to two different tools with two different stdout filters, via a
three-field name|kind|args form. Output byte-identical against the
baseline."
```

---

### Task 6: Migrate `quest-golden.sh`

**Files:**
- Modify: `scripts/quest-golden.sh`

**Interfaces:**
- Consumes: `golden_runner`, `golden_run_all`, `GOLDEN_FAIL`.

- [ ] **Step 1: Migrate the script**

Replace everything from the `run()` definition to the end of `scripts/quest-golden.sh` with:

```bash
source "$(dirname "$0")/golden-lib.sh"
golden_runner viewer 120 src/Hexwaste.Viewer/bin/Debug/net10.0/Hexwaste.Viewer \
  "^(get-global:|quest-item:|quest-probe:|party-count:|party:)" "--no-audio"

golden_run_all
[ "$MODE" = "record" ] && exit 0
if [ "$GOLDEN_FAIL" = 0 ]; then echo "quest e2e: ALL PASS"; else echo "quest e2e: FAIL"; exit 1; fi
```

Check whether this script has a `dotnet build` line above `run()`; if it does, keep it where it is. If it does not, add `dotnet build src/Hexwaste.Viewer -c Debug >/dev/null || { echo "build failed"; exit 2; }` immediately before the `source` line — the library invokes the built binary and will fail confusingly if nothing built it.

- [ ] **Step 2: Verify byte-identical output**

```bash
cd /home/eko/dev/FPOC
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
./scripts/quest-golden.sh check > /tmp/quest-new.txt 2>&1; echo "exit=$?"
sed -n '/^=== quest ===/,/^=== /p' .superpowers/sdd/golden-baseline.log \
  | grep -vE '^=== |^WALL ' > /tmp/quest-base.txt
diff -u /tmp/quest-base.txt /tmp/quest-new.txt && echo "BYTE-IDENTICAL"
```

Expected: `exit=0`, `BYTE-IDENTICAL`. This suite has 39 fixtures and previously dominated by wall time after encounter — report the before/after.

- [ ] **Step 3: Commit**

```bash
cd /home/eko/dev/FPOC
git add scripts/quest-golden.sh
git commit -m "test(golden): run the quest suite through the shared runner

Output byte-identical against the baseline."
```

---

### Task 7: Migrate `encounter-golden.sh` and give the four stateful scenarios private scratch

The 188-fixture suite — the bulk of the wall time — and the one containing every scenario that writes to a hardcoded path. Left last so the library is already proven on five suites.

**Files:**
- Modify: `scripts/encounter-golden.sh`

**Interfaces:**
- Consumes: everything from Task 2, plus the `@SCRATCH@` token `_golden_job` substitutes.

**The hazard, restated because it is the whole point of this task:** four scenarios write to fixed absolute paths. Serially they are harmless. Under the job model the *two passes of the same fixture* run concurrently against the same file or directory, producing intermittent, unreproducible failures that look like flaky parallelism rather than a fixture bug.

- [ ] **Step 1: Replace the four hardcoded paths with the scratch token**

In `scripts/encounter-golden.sh`, in the `SCENARIOS` array only, make these four substitutions:

| scenario | from | to |
|---|---|---|
| `automap-persist` | `--save-path /tmp/hexwaste-automap-persist.json` | `--save-path @SCRATCH@/automap-persist.json` |
| `save-slot-roundtrip` | `--save-dir /tmp/hexwaste-p48-rt` | `--save-dir @SCRATCH@/p48-rt` |
| `save-slots-probe` | `--save-dir /tmp/hexwaste-p48-sp` | `--save-dir @SCRATCH@/p48-sp` |
| `vic-save-roundtrip` | `--save-path /tmp/hexwaste-m3golden.json` | `--save-path @SCRATCH@/m3golden.json` |

Then confirm none is left:

```bash
cd /home/eko/dev/FPOC
grep -n '/tmp/hexwaste' scripts/encounter-golden.sh || echo "no hardcoded paths remain"
```

Note the token expands to a directory the library has already created, but the paths above add a
**subdirectory** under it (`@SCRATCH@/p48-rt`). The original paths (`/tmp/hexwaste-p48-rt`) did not
pre-exist either and the viewer created them, so this should behave the same — Step 3's diff is what
confirms it. If those two scenarios fail with a missing-directory error, create the subdirectory in
`_golden_job` rather than reverting to a shared path.

- [ ] **Step 2: Migrate the runner**

Replace everything from the `run()` definition to the end of the file with:

```bash
source "$(dirname "$0")/golden-lib.sh"
golden_runner viewer 90 src/Hexwaste.Viewer/bin/Debug/net10.0/Hexwaste.Viewer "$FILTER" "--no-audio"
MISMATCH_LABEL=REGRESSION
DIFF_TRUNC=30

golden_run_all
[ "$GOLDEN_FAIL" -eq 0 ] && echo "golden encounter: ALL PASS" || echo "golden encounter: FAILURES"
exit "$GOLDEN_FAIL"
```

Leave the `FILTER=` assignment where it is — the runner registration reads it. Keep any `dotnet build` line above; if there is none, add `dotnet build src/Hexwaste.Viewer -c Debug >/dev/null || { echo "build failed"; exit 2; }` before the `source` line.

- [ ] **Step 3: Verify byte-identical output**

```bash
cd /home/eko/dev/FPOC
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
./scripts/encounter-golden.sh check > /tmp/encounter-new.txt 2>&1; echo "exit=$?"
sed -n '/^=== encounter ===/,/^=== /p' .superpowers/sdd/golden-baseline.log \
  | grep -vE '^=== |^WALL ' > /tmp/encounter-base.txt
diff -u /tmp/encounter-base.txt /tmp/encounter-new.txt && echo "BYTE-IDENTICAL"
```

Expected: `exit=0`, `BYTE-IDENTICAL`. Pay particular attention to the four scenarios you edited — if any of them diffs, the scratch substitution changed behaviour and that is a stop condition, not something to record around.

- [ ] **Step 4: Prove the four stateful scenarios are actually isolated**

Run the suite three times and require the same result each time. Concurrency bugs of this shape are intermittent, so one green run proves little.

```bash
cd /home/eko/dev/FPOC
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
for i in 1 2 3; do
  ./scripts/encounter-golden.sh check > /tmp/enc-run-$i.txt 2>&1
  echo "run $i exit=$?"
done
md5sum /tmp/enc-run-1.txt /tmp/enc-run-2.txt /tmp/enc-run-3.txt
```

Expected: three `exit=0` lines and three identical hashes. **Report the hashes.** If they differ, the isolation is incomplete — find the remaining shared path rather than retrying.

- [ ] **Step 5: Commit**

```bash
cd /home/eko/dev/FPOC
git add scripts/encounter-golden.sh
git commit -m "test(golden): parallelise the encounter suite, isolating its stateful scenarios

The 188-fixture suite, and the only one whose scenarios write to disk.
Four of them used hardcoded /tmp paths, which is harmless serially but
collides once the two passes of a fixture run concurrently — an
intermittent failure that would read as 'parallelism is flaky here'.
They now take a private per-job scratch directory via @SCRATCH@.

Verified byte-identical against the baseline, and stable across three
consecutive runs."
```

---

### Task 8: Measure the whole thing and record the result

**Files:**
- Modify: `docs/BACKLOG.md`

**Interfaces:** none.

- [ ] **Step 1: Run all six suites end to end and time them**

```bash
cd /home/eko/dev/FPOC
export DISPLAY=:0 FALLOUT2_DIR="$(pwd)/game-data"
{
  for s in combat quest endgame opening encounter; do
    echo "=== $s ==="
    /usr/bin/time -f "WALL $s %e s" ./scripts/$s-golden.sh check 2>&1
  done
  echo "=== census ==="
  /usr/bin/time -f "WALL census %e s" ./scripts/census-sweep.sh check 2>&1
  echo "AFTER-DONE"
} > /tmp/golden-after.log 2>&1
grep -E '^WALL |ALL PASS|FAIL' /tmp/golden-after.log
grep -cE '^ok ' /tmp/golden-after.log
```

Expected: six `ALL PASS` verdicts and **279** `ok` lines, matching the baseline exactly.

- [ ] **Step 2: Diff the whole run against the baseline**

```bash
cd /home/eko/dev/FPOC
grep -vE '^WALL |^# golden baseline|^BASELINE-DONE|^AFTER-DONE' .superpowers/sdd/golden-baseline.log > /tmp/base-cmp.txt
grep -vE '^WALL |^# golden baseline|^BASELINE-DONE|^AFTER-DONE' /tmp/golden-after.log > /tmp/after-cmp.txt
diff -u /tmp/base-cmp.txt /tmp/after-cmp.txt && echo "WHOLE-SUITE BYTE-IDENTICAL"
```

Expected: `WHOLE-SUITE BYTE-IDENTICAL`.

- [ ] **Step 3: Record the before/after in the backlog**

Add an entry to `docs/BACKLOG.md` alongside the other shipped entries. It must carry the real measured numbers from Steps 1 and 2 — per-suite before and after, the total, and the byte-identical result. State the three costs that were removed (`dotnet run` overhead, serial execution, and the fact that the double run now costs a core rather than wall time), and state plainly that **no assertion was weakened and no fixture was touched**.

Re-derive any line citation you write from the tree; do not copy numbers out of this plan.

- [ ] **Step 4: Commit**

```bash
cd /home/eko/dev/FPOC
git add docs/BACKLOG.md
git commit -m "docs: record the golden-harness speedup with measured numbers"
```

---

## Verification Summary

| Task | How it is proven |
|---|---|
| 1 | Baseline exists, 279 `ok` lines, six `WALL` figures, captured from unmodified `scripts/` |
| 2 | endgame byte-identical; not slower; `record` reproduces its fixtures exactly; a timed-out job fails loudly |
| 3 | combat byte-identical; wall time vs the measured 112.3 s |
| 4 | census byte-identical; single-pass confirmed; no `LOAD-FAIL` in the baseline |
| 5 | opening byte-identical; every scenario kind maps to a registered runner |
| 6 | quest byte-identical |
| 7 | encounter byte-identical **and stable across three consecutive runs** |
| 8 | All six suites, 279 `ok` lines, whole-run diff empty, before/after recorded |

## What this plan deliberately does not do

- **No fixture is re-recorded.** A diff is a stop condition, always.
- **No assertion is weakened** — in particular the determinism double-run stays for the five suites that have it, and is *not* added to census, which never had one.
- **No scenario's arguments change**, except the four `@SCRATCH@` substitutions in Task 7.
- **No visual/pixel coverage.** That is the separate gap the brainstorm identified and it gets its own spec.
