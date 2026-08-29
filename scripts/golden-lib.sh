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
