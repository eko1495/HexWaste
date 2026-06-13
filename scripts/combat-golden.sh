#!/usr/bin/env bash
# Golden combat-transcript regression net — phase-9 M0 (CombatEngine extraction).
#
# Re-runs a set of deterministic headless fights (--rng-seed) and (a) asserts each
# is reproducible run-to-run and (b) diffs full stdout against the committed
# fixtures in tests/golden-combat/. This is the behaviour-preservation net to run
# after every extraction step: the transcript MUST stay byte-identical.
#
# Usage:  scripts/combat-golden.sh [check|record]   (default: check)
#   record  — (re)capture the fixtures from current behaviour (the baseline)
#   check   — fail if any run is nondeterministic or differs from its fixture
#
# Requires a real display (the MonoGame app needs a GraphicsDevice — it cannot run
# on a headless CI runner) and game data (FALLOUT2_DIR, default ./game-data).
set -uo pipefail
cd "$(dirname "$0")/.."

MODE="${1:-check}"
GAME="${FALLOUT2_DIR:-$(pwd)/game-data}"
FIX="tests/golden-combat"
mkdir -p "$FIX"

# name | harness args (each is a deterministic, self-exiting --fight/--attack run)
SCENARIOS=(
  "arcaves-fight-42-loss|--map arcaves.map --fight 20529 --rng-seed 42"
  "arcaves-fight-7-win|--map arcaves.map --fight 20529 --rng-seed 7"
  "arcaves-fight-combat-1|--character combat --map arcaves.map --fight 20529 --rng-seed 1"
  "arcaves-attack-42|--map arcaves.map --attack 20529 --rng-seed 42"
  "denbus2-fight-flee|--character combat --map denbus2.map --fight 11670 --rng-seed 3"
  "arcaves-crit-day2|--character combat --advance-days 1 --map arcaves.map --fight 20529 --rng-seed 2"
  "arcaves-aim-eyes-day2|--character combat --advance-days 1 --aim eyes --map arcaves.map --fight 20529 --rng-seed 2"
  "arcaves-knockdown-day2|--character combat --advance-days 1 --aim right_leg --map arcaves.map --fight 20529 --rng-seed 4"
  "arcaves-explode|--map arcaves.map --explode 20529 --rng-seed 1"
  "arcaves-throw-spear|--map arcaves.map --give 7 --use-item 7 --throw 20529 --rng-seed 1"
  "arcaves-throw-grenade|--map arcaves.map --give 25 --use-item 25 --throw 20529 --rng-seed 1"
)

echo "Building viewer..."
dotnet build src/Hexwaste.Viewer -c Debug >/dev/null || { echo "build failed"; exit 2; }

run() {
  timeout 90 env DISPLAY="${DISPLAY:-:0}" FALLOUT2_DIR="$GAME" \
    dotnet run --project src/Hexwaste.Viewer -c Debug --no-build -- \
    --game-dir "$GAME" --no-audio $1 2>/dev/null
}

fail=0
for entry in "${SCENARIOS[@]}"; do
  name="${entry%%|*}"; args="${entry#*|}"
  out="$(run "$args")"
  if [ "$MODE" = "record" ]; then
    printf '%s\n' "$out" > "$FIX/$name.txt"
    echo "recorded $name ($(printf '%s\n' "$out" | wc -l | tr -d ' ') lines)"
    continue
  fi
  out2="$(run "$args")"            # determinism: second run must match the first
  if [ "$out" != "$out2" ]; then
    echo "NONDETERMINISTIC: $name"; fail=1
  fi
  if [ ! -f "$FIX/$name.txt" ]; then
    echo "MISSING FIXTURE: $name (run 'record' first)"; fail=1; continue
  fi
  if diff -u "$FIX/$name.txt" <(printf '%s\n' "$out") >/dev/null; then
    echo "ok  $name"
  else
    echo "REGRESSION: $name"; diff -u "$FIX/$name.txt" <(printf '%s\n' "$out") | head -30; fail=1
  fi
done

[ "$fail" -eq 0 ] && echo "golden combat: ALL PASS" || echo "golden combat: FAILURES"
exit $fail
