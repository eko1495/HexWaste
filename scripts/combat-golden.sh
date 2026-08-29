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
  # P41: critical FAILURE on a miss (_cf_table). The trigger fires from day 2 but the DUDE's EFFECT is
  # gated to day 6 (combat.cc:4190) — so at day 6 a missed unarmed swing fumbles: seed 7 lands a
  # LOSE_TURN (crit-fail flags=0x8000). The day-2 fixtures above were re-recorded for the trigger's
  # extra d100-on-miss (RNG shift, the P14-M4 precedent); the day-1 fixtures stay byte-identical.
  "arcaves-crit-fail-day6|--character combat --advance-days 6 --map arcaves.map --fight 20529 --rng-seed 7"
  "arcaves-explode|--map arcaves.map --explode 20529 --rng-seed 1"
  "arcaves-throw-spear|--map arcaves.map --give 7 --use-item 7 --throw 20529 --rng-seed 1"
  "arcaves-throw-grenade|--map arcaves.map --give 25 --use-item 25 --throw 20529 --rng-seed 1"
  "arcaves-projectile-spear|--map arcaves.map --projectile 20529 --rng-seed 1"
  "arcaves-burst-smg|--map arcaves.map --give 9 --use-item 9 --burst 20529 --rng-seed 1"
  "arcaves-burst-shotgun|--map arcaves.map --give 242 --use-item 242 --burst 20529 --rng-seed 1"
  # P20-M4: the P13-M2 collateral CONE on real data — bursting at a Den slave from across
  # the cluster sweeps two real bystanders (Handsome + Cute Slave) on the left/right lines.
  "denbus2-burst-collateral|--character combat --map denbus2.map --give 9 --use-item 9 --burst-at 13270 11670 --rng-seed 1"
  # F21: pins the walker-restart-probe's discriminating value (started2) so a regression reverting
  # StartNpcWalk's guard back to ContainsKey is caught by an automated diff, not only by hand.
  "walker-restart|--map denbus2.map --walker-restart-probe 14716 14718 14716"
  # F32: pins ShouldRunDamageProc's party pair gate (combat.cc:4849, F27). The proc's own output never
  # reaches stdout (RunDamageProc routes through Log, not Transcript), so the probe reports the gate's
  # own outcome for both quadrants — see the doc comment on StartupAction.PartyProcProbe.
  "party-proc|--map arcaves.map --party-proc-probe 20529 21729"
)

echo "Building viewer..."
dotnet build src/Hexwaste.Viewer -c Debug >/dev/null || { echo "build failed"; exit 2; }

source "scripts/golden-lib.sh" || exit 2
golden_runner viewer 90 src/Hexwaste.Viewer/bin/Debug/net10.0/Hexwaste.Viewer "" "--no-audio"
MISMATCH_LABEL=REGRESSION
DIFF_TRUNC=30

# Coverage assertion: a suite that quietly lost a scenario still reports ALL PASS
# over the hole. Update this deliberately when adding or removing a fixture.
GOLDEN_EXPECT_SCENARIOS=18

golden_run_all || exit 2
[ "$GOLDEN_FAIL" -eq 0 ] && echo "golden combat: ALL PASS" || echo "golden combat: FAILURES"
exit "$GOLDEN_FAIL"
