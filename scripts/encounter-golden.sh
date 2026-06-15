#!/usr/bin/env bash
# Golden transcript regression net for phase-10 — random encounters + companions.
#
# Re-runs deterministic headless encounter/companion scenarios (--rng-seed) and
# (a) asserts each is reproducible run-to-run and (b) diffs the transcript lines
# against the committed fixtures in tests/golden-encounter/. Mirrors
# combat-golden.sh; run it after touching the worldmap/encounter/companion code.
#
# Usage:  scripts/encounter-golden.sh [check|record]   (default: check)
#   record  — (re)capture the fixtures from current behaviour (the baseline)
#   check   — fail if any run is nondeterministic or differs from its fixture
#
# Requires a real display (the MonoGame app needs a GraphicsDevice — it cannot run
# on a headless CI runner) and game data (FALLOUT2_DIR, default ./game-data).
set -uo pipefail
cd "$(dirname "$0")/.."

MODE="${1:-check}"
GAME="${FALLOUT2_DIR:-$(pwd)/game-data}"
FIX="tests/golden-encounter"
mkdir -p "$FIX"

# name | harness args (each is a deterministic, self-exiting startup-action run)
SCENARIOS=(
  "encounter-arro-rats|--encounter desert1.map ARRO_Rats 5 --rng-seed 1"
  "encounter-war-party|--encounter desert1.map ARRO_War_Party 4 --rng-seed 7"
  "encounter-scorpions|--encounter desert1.map ARRO_Sm_Scorpions 4 --rng-seed 2"
  "travel-arroyo-den|--travel-from 184 133 1 --rng-seed 2"
  "companion-lifecycle|--map arcaves.map --companion 20529 --rng-seed 1"
  "trade-roundtrip|--map arcaves.map --trade 20529 7 --rng-seed 1"
  "companion-persist|--map arcaves.map --companion-persist 20529 --rng-seed 1"
  "companion-dismiss-persist|--map arcaves.map --dismiss-persist 20529 --rng-seed 1"
  # Legitimate Vic recruit (#10 M1 + M-radio) — denbus2, fully VM-driven, no GVAR
  # cheat: give Vic the radio (pid 266), his dialog runs the real inventory externals
  # (obj_is_carrying_obj / obj_carrying_pid_obj / rm_obj_from_inven) to set the
  # radio-fixed bit GVAR446|0x400000; that unlocks Metzger's $1000 buy (free-bit
  # GVAR445 handshake); then Vic's talk_p_proc party_add recruits him. The radio
  # ITEM (--give 266) is the one documented content gap — it has no in-slice source.
  "vic-recruit|--map denbus2.map --give 41:2000 --give 266:1 --talk-seq 17070 1,1,1 --talk-seq 15278 2,2,1,1 --talk-seq 17070 2,1 --party-count --rng-seed 1"
  # P11 M4 — the HUD bar buttons fire their panel actions (INV/MAP/CHA wired).
  "hud-buttons|--character combat --map denbus2.map --hud-click INV --hud-click MAP --hud-click CHA --rng-seed 1"
  # #10 M2 — a legitimately-recruited Vic levels up his proto as the dude gains levels
  # (PartyLevelUp wired into AwardXp; party.txt member 13, level_minimum 5).
  "vic-levelup|--map denbus2.map --give 41:2000 --give 266:1 --talk-seq 17070 1,1,1 --talk-seq 15278 2,2,1,1 --talk-seq 17070 2,1 --grant-xp 60000 --party-count --rng-seed 1"
)

# Keep only the deterministic transcript lines (drop map-load / animate / stub /
# dialog-text noise — NEVER capture REPLY/OPTION game-asset strings).
FILTER='^(encounter|travel-from|companion|dismiss-persist|trade:|party:|party-count:|set-global:|hud-click:|  spawn|  wait:|  follow:|  dismiss:|  rejoin:)'

echo "Building viewer..."
dotnet build src/Hexwaste.Viewer -c Debug >/dev/null || { echo "build failed"; exit 2; }

run() {
  timeout 90 env DISPLAY="${DISPLAY:-:0}" FALLOUT2_DIR="$GAME" \
    dotnet run --project src/Hexwaste.Viewer -c Debug --no-build -- \
    --game-dir "$GAME" --no-audio $1 2>/dev/null | grep -E "$FILTER"
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

[ "$fail" -eq 0 ] && echo "golden encounter: ALL PASS" || echo "golden encounter: FAILURES"
exit $fail
