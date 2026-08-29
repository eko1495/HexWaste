#!/usr/bin/env bash
# Self-test for scripts/golden-lib.sh — does the harness FAIL when it should?
#
# The golden suites are the project's main correctness oracle, and a green run is
# the evidence every change is merged on. That makes one question load-bearing and
# otherwise unasked: when something IS broken, does the harness notice? A harness
# that silently reports success is worse than no harness, because it converts an
# absence of checking into positive evidence of correctness.
#
# So each case below deliberately breaks something and REQUIRES the harness to
# fail. A case that "passes" by not failing is itself a failure.
#
# Hermetic on purpose: a fake runner script stands in for the viewer, so this
# needs no game data, no DISPLAY, and no build. It runs in CI, where the golden
# suites themselves cannot.
#
# NOT covered: the "JOB FAILED (no output)" path, which fires only when a
# background job dies between running the binary and writing its output file.
# Provoking that requires killing a process mid-function; every way of faking it
# was less faithful than not testing it. It is also a loud failure path, not a
# silent-success one, so it is the least dangerous gap here.
set -uo pipefail
cd "$(dirname "$0")/.."
LIB="$PWD/scripts/golden-lib.sh"

WORK="$(mktemp -d)" || { echo "selftest: mktemp failed" >&2; exit 2; }
trap 'rm -rf "$WORK"' EXIT

# A stand-in for the viewer. Called as: BIN --game-dir GAME [EXTRA] ARGS...
# Echoes one line per scenario argument, so output is a pure function of input.
cat > "$WORK/fake-bin" <<'EOF'
#!/usr/bin/env bash
shift 2                      # drop --game-dir GAME
for a in "$@"; do echo "line: $a"; done
EOF
# Same, but its output changes every run — stands in for a real nondeterminism bug.
cat > "$WORK/flaky-bin" <<'EOF'
#!/usr/bin/env bash
shift 2
echo "line: $RANDOM"
EOF
# Same, but hangs — stands in for a scenario that outgrows its timeout.
cat > "$WORK/slow-bin" <<'EOF'
#!/usr/bin/env bash
sleep 30
echo "line: too late"
EOF
chmod +x "$WORK"/*-bin

PASSED=0; FAILED=0

# case_run NAME EXPECT_RC EXPECT_TEXT PRELUDE BODY
#   EXPECT_RC:   2 if golden_run_all must abort, else the required GOLDEN_FAIL.
#   EXPECT_TEXT: a string the combined output must contain ("" to skip).
#   PRELUDE:     evaluated BEFORE the library is sourced — this is where an
#                environment override belongs. golden-lib.sh defends against a
#                hostile environment by assigning its knobs at SOURCE time, so a
#                prelude that runs afterwards would test nothing: it would just
#                be a suite setting its own knob, which is supported. Ordering
#                is the whole point of these two cases.
#   BODY:        evaluated after sourcing — the suite's own setup.
# Each case runs in a subshell that sources the library FRESH, so knob defaults
# reset between cases and no case can contaminate the next.
case_run() {
  local name="$1" want_rc="$2" want_text="$3" prelude="$4" body="$5"
  local out rc
  out="$( {
    set +e
    cd "$WORK"
    eval "$prelude"
    # shellcheck disable=SC1090
    source "$LIB" || exit 99
    eval "$body"
    golden_run_all; rc=$?
    [ "$rc" = 2 ] && exit 2
    exit "$GOLDEN_FAIL"
  } 2>&1 )" ; rc=$?

  local bad=""
  [ "$rc" != "$want_rc" ] && bad="exit $rc, wanted $want_rc"
  if [ -n "$want_text" ] && [[ "$out" != *"$want_text"* ]]; then
    bad="${bad:+$bad; }output lacks '$want_text'"
  fi
  if [ -z "$bad" ]; then
    echo "ok    $name"; PASSED=$((PASSED + 1))
  else
    echo "FAIL  $name — $bad"
    printf '%s\n' "$out" | sed 's/^/        | /'
    FAILED=$((FAILED + 1))
  fi
}

# Builds a fixture dir holding N scenarios that the fake runner reproduces exactly.
mkfix() {
  local dir="$WORK/$1" n="$2" i
  rm -rf "$dir"; mkdir -p "$dir"
  for ((i = 1; i <= n; i++)); do echo "line: arg$i" > "$dir/s$i.txt"; done
}
# The matching SCENARIOS array + suite globals, as a string to eval.
setup() {
  local dir="$1" n="$2" bin="${3:-fake-bin}" timeout="${4:-30}" i
  local entries=""
  for ((i = 1; i <= n; i++)); do entries+="\"s$i|arg$i\" "; done
  echo "MODE=check; FIX=\"$WORK/$dir\"; GAME=\"$WORK/game\";
        SCENARIOS=($entries);
        golden_runner viewer $timeout \"$WORK/$bin\" '' ''"
}

echo "harness self-test — every case below must FAIL the harness on purpose"
echo

# --- positive control: without this, a harness that fails everything scores 100% ---
mkfix fix-ok 3
case_run "clean run passes" 0 "ok  s3" "" "$(setup fix-ok 3)"

# --- a fixture no longer matches what the code produces ---
mkfix fix-diff 3; echo "line: WRONG" > "$WORK/fix-diff/s2.txt"
case_run "corrupted fixture is caught" 1 "s2" "" "$(setup fix-diff 3)"

# --- a fixture file vanished ---
mkfix fix-gone 3; rm "$WORK/fix-gone/s2.txt"
case_run "missing fixture is caught" 1 "MISSING FIXTURE: s2" "" "$(setup fix-gone 3)"

# --- output differs between two runs of the same scenario ---
mkfix fix-flaky 1
case_run "nondeterminism is caught" 1 "NONDETERMINISTIC: s1" "" "$(setup fix-flaky 1 flaky-bin)"

# --- the determinism check must not be switchable off from the environment.
#     golden-lib.sh assigns its knobs unconditionally at source time for exactly
#     this reason; this case is what proves that assignment still happens. ---
mkfix fix-env 1
case_run "exported DOUBLE_RUN=0 cannot disable the determinism check" 1 "NONDETERMINISTIC" \
  "export DOUBLE_RUN=0" "$(setup fix-env 1 flaky-bin)"

# --- a scenario outgrew its timeout: truncated output must not read as success ---
mkfix fix-slow 1
case_run "timeout is caught, not silently passed" 1 "s1" "" "$(setup fix-slow 1 slow-bin 1)"

# --- a garbage throttle must clamp to 1, not fork every job at once or error out ---
mkfix fix-jobs 3
case_run "garbage GOLDEN_JOBS clamps instead of breaking" 0 "ok  s3" \
  "export GOLDEN_JOBS=nonsense" "$(setup fix-jobs 3)"

# --- the coverage assertion: a scenario silently dropped from the array ---
mkfix fix-count 3
case_run "a dropped scenario is caught by the count assertion" 2 "SCENARIO COUNT" \
  "" "$(setup fix-count 2); GOLDEN_EXPECT_SCENARIOS=3"

# --- the two scratch-directory guards, which protect every later path ---
mkfix fix-mk 1
case_run "failing mktemp -d aborts the run" 2 "mktemp -d failed" \
  "" "$(setup fix-mk 1); mktemp() { return 1; }"
case_run "mktemp -d returning nothing aborts the run" 2 "returned no directory" \
  "" "$(setup fix-mk 1); mktemp() { echo ''; }"

echo
echo "harness self-test: $PASSED passed, $FAILED failed"
[ "$FAILED" -eq 0 ] || exit 1
