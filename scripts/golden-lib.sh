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
#
# Suite-tunable knobs (set before calling golden_run_all; all optional):
#   SCENARIO_FIELDS     "2" (name|args, default runner) or "3" (name|runner|args)
#   DOUBLE_RUN           1 (default) to double-run each fixture for a determinism
#                         check, 0 to run once
#   MISMATCH_LABEL       label printed before a diff (default "DIFF")
#   DIFF_TRUNC            if set, pipe the diff through `head -N` instead of full
#   GOLDEN_RESULT_HOOK   name of a function called as `hook NAME OUTPUT ARGS`; a
#                         non-zero return marks the fixture failed and skips it.
#                         ARGS is the scenario's own raw argument string (the
#                         SCENARIOS entry's args field), for suites that need
#                         it in their own failure wording (e.g. census-sweep.sh's
#                         "LOAD-FAIL: NAME (ARGS emitted no census line)").
#   GOLDEN_RECORD_COUNT   1 (default) to suffix a recorded line with its line
#                         count ("recorded NAME (N lines)"); 0 for the bare
#                         "recorded NAME" wording (census-sweep.sh)
#   GOLDEN_MISSING_HINT   text appended to "MISSING FIXTURE: NAME" (default
#                         " (run 'record' first)"); set to "" for the bare
#                         wording (census-sweep.sh)
#
# GOLDEN_FAIL is initialised to 0 at source time so a suite (or an early-return
# path) can read it under `set -u` before golden_run_all has run.
GOLDEN_FAIL=0

# The tuning knobs are given their defaults here, unconditionally, at source
# time — NOT via `${X:-default}` inside golden_run_all. A suite sets these
# (if at all) *after* sourcing this file, so an unconditional assignment here
# is always overwritten by a suite's later assignment while still shadowing
# anything the same-named variable held in the environment. golden_run_all
# below reads them plainly. Without this, `export DOUBLE_RUN=0` (or any of
# the others) silently changes suite behaviour with zero output difference —
# exactly the kind of "reports success when it shouldn't" bug a golden
# harness must not have. GOLDEN_JOBS is deliberately exempt: its env override
# is documented and intentional (see below).
SCENARIO_FIELDS=2
DOUBLE_RUN=1
MISMATCH_LABEL=DIFF
DIFF_TRUNC=
GOLDEN_RESULT_HOOK=
GOLDEN_RECORD_COUNT=1
GOLDEN_MISSING_HINT=" (run 'record' first)"

GOLDEN_JOBS="${GOLDEN_JOBS:-$(nproc)}"
# Guard against an empty/non-numeric throttle (unset nproc, a blank export) —
# otherwise the `-ge` comparison below errors out and every job forks at once.
case "$GOLDEN_JOBS" in
  ''|*[!0-9]*) GOLDEN_JOBS=1 ;;
  0) GOLDEN_JOBS=1 ;;
esac

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
#
# TMPDIR is set to the per-job SCRATCH directory below, and that is load-bearing, not
# defensive: three encounter scenarios (travel-save-mid, companion-persist, and
# companion-dismiss-persist) drive harness actions that write to FIXED filenames —
# hexwaste-travelmid-test.json, hexwaste-persist-test.json, hexwaste-dismiss-test.json —
# under Path.GetTempPath() (src/Hexwaste.Viewer/ViewerGame.Harness.cs), which .NET
# resolves via TMPDIR on Unix. Both passes of the same fixture run concurrently under
# this runner (see the file header); without a distinct TMPDIR per pass, the two passes
# would race on the same fixed path. Do not remove this override.
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
  local jobdir
  jobdir="$(mktemp -d)" || { echo "golden: mktemp -d failed" >&2; GOLDEN_FAIL=1; return 2; }
  [ -n "$jobdir" ] && [ -d "$jobdir" ] || { echo "golden: mktemp -d returned no directory" >&2; GOLDEN_FAIL=1; return 2; }
  # Interrupting a long run (the normal way to abandon a big suite) must not
  # leave scratch subdirectories and savegames behind. Plain EXIT just cleans
  # up; INT/TERM must ALSO exit here — otherwise execution falls through into
  # the emit phase below against a jobdir that was just rm -rf'd, printing a
  # failure line per scenario instead of stopping. 128+signum is the
  # conventional shell exit status for "killed by signal".
  trap 'rm -rf "$jobdir"' EXIT
  trap 'rm -rf "$jobdir"; exit 130' INT
  trap 'rm -rf "$jobdir"; exit 143' TERM
  local fields="$SCENARIO_FIELDS"
  local double="$DOUBLE_RUN"
  local label="$MISMATCH_LABEL"
  local trunc="$DIFF_TRUNC"
  local hook="$GOLDEN_RESULT_HOOK"
  local record_count="$GOLDEN_RECORD_COUNT"
  local missing_hint="$GOLDEN_MISSING_HINT"

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

    if [ ! -f "$jobdir/$i.1.out" ]; then
      echo "JOB FAILED: $name (no output — job died before writing it)"
      GOLDEN_FAIL=1
      continue
    fi
    out="$(cat "$jobdir/$i.1.out")"

    if [ -n "$hook" ]; then
      if ! "$hook" "$name" "$out" "${argses[$i]}"; then GOLDEN_FAIL=1; continue; fi
    fi

    if [ "$MODE" = "record" ]; then
      printf '%s\n' "$out" > "$FIX/$name.txt"
      if [ "$record_count" = 1 ]; then
        echo "recorded $name ($(printf '%s\n' "$out" | wc -l | tr -d ' ') lines)"
      else
        echo "recorded $name"
      fi
      continue
    fi

    if [ "$double" = 1 ]; then
      if [ ! -f "$jobdir/$i.2.out" ]; then
        echo "JOB FAILED: $name (no output — job died before writing it)"
        GOLDEN_FAIL=1
        continue
      fi
      out2="$(cat "$jobdir/$i.2.out")"
      if [ "$out" != "$out2" ]; then echo "NONDETERMINISTIC: $name"; GOLDEN_FAIL=1; fi
    fi

    if [ ! -f "$FIX/$name.txt" ]; then
      echo "MISSING FIXTURE: $name${missing_hint}"; GOLDEN_FAIL=1; continue
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
  trap - EXIT INT TERM
  return 0
}
