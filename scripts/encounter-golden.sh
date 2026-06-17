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
  # P16-M4: lock the per-member If()/Distance fidelity on real data — ARRO_Spore_Plants'
  # Dead Primitive Female is gated behind lowercase "if (Rand(5%))" (a case-sensitivity bug
  # made it spawn 100%); at seed 13 the roll passes so the corpse appears (the flat line),
  # while its Distance-pinned plant-item siblings stay gated out.
  "encounter-spore-plants|--encounter desert1.map ARRO_Spore_Plants 4 --rng-seed 13"
  # P16-M1: travelling the Arroyo->Den leg now DETECTS the ARRO_Rats encounter ahead
  # (Outdoorsman), grants the avoid XP, and (headless default) engages it. The avoid
  # variant declines -> travels on -> walks into the next (undetected) ambush.
  "travel-arroyo-den|--travel-from 184 133 1 --rng-seed 2"
  "travel-arroyo-avoid|--encounter-answer no --travel-from 184 133 1 --rng-seed 2"
  # P16-M2: leaving an encounter map mid-leg auto-resumes travel toward the destination
  # (the engine's isWalking) — no worldmap re-click; here the resumed leg rolls again.
  "travel-resume|--travel-resume 204 143 1 --rng-seed 2"
  # P17-M2: the ANIMATED travel path (the moving dot) drains the SAME leg as the sync
  # resolve — same encounter + worldPos as travel-arroyo-den — while terrain cadence makes
  # cadence-ticks (26) exceed pixel-steps (20): mountains hold the dot some ticks.
  "travel-step|--travel-step 184 133 1 --rng-seed 2"
  # P17-M4: saving MID-travel round-trips the dot worldPos + the in-flight destination
  # (load resumes toward it) — a documented divergence from the engine's drop-stopped reload.
  "travel-save-mid|--character combat --map artemple.map --travel-save-mid 184 133 1 5 --rng-seed 2"
  # P28-M4: the perk picker. At level 3 the dude has 1 pick over 14 eligible perks; picking row 0
  # takes it and closes the picker. At level 6 (2 picks) it stays open after one pick. Level 1 = 0.
  "perk-pick|--character combat --map arcaves.map --perk-pick 3 0 --perk-pick 6 0 --perk-pick 1 0 --rng-seed 1"
  # P28-M2: perk infrastructure + selection (perk.cc perkCanAdd + the table-driven stat perks).
  # Bonus HtH Damage (idx 2, melee +2/rank, needs ST6/AG6/level3) is level-GATED at lvl 2, eligible
  # at 3 (melee 8->10, atop Narg's Heavy Handed +4), STACKS to rank 2 (->12); More Criticals (idx 8)
  # stays stat-GATED (Narg LK4 < req6) even at lvl99. picks = level/3.
  "perk-gates|--character combat --map arcaves.map --perk-probe 2 2 --perk-probe 2 3 --perk-probe 2 3 --perk-probe 8 99 --rng-seed 1"
  # P28-M1: optional-trait effects (trait.cc traitGetStatModifier/SkillModifier), applied live.
  # No traits is inert (baseline); Gifted +1 all SPECIAL & -10 all skills; Bruiser+Kamikaze stack
  # (STR+2/AP-2/AC->0/SEQ+5); Good Natured shifts combat (-10) vs social (+15) skills.
  "trait-none|--character combat --map arcaves.map --trait-probe -1 -1 --rng-seed 1"
  "trait-gifted|--character combat --map arcaves.map --trait-probe 15 -1 --rng-seed 1"
  "trait-bruiser-kamikaze|--character combat --map arcaves.map --trait-probe 1 5 --rng-seed 1"
  "trait-goodnatured|--character combat --map arcaves.map --trait-probe 10 -1 --rng-seed 1"
  # P26: gory death animations (actions.cc _pick_death). A solid burst/laser/explosion kill
  # gives the corpse a gore variant by damage type — DancingAutofire(26)/SlicedInHalf(28)/
  # BigHole(23) — when the critter ships that art. A denbus2 human (pid 0x1000004) does (gore=True);
  # an arcaves scorpion does NOT, so it faithfully falls back to FALL_BACK (gore=False).
  "gore-human|--map denbus2.map --death-probe 8667 --rng-seed 1"
  "gore-scorpion|--map arcaves.map --death-probe 20529 --rng-seed 1"
  # P25: dialogue IQ-gating. The dude's real INT now gates giq_option dumb/smart options
  # (interpreter_extra.cc _op_giq_option) instead of a hardcoded 5. Vic's greeting offers 1
  # option to a dim dude (IN 2 — smart options gated out) vs 4 to a bright one (IN 9). The
  # probe reports only the option COUNT, never the copyrighted option text.
  "iq-gate-dumb|--map denbus2.map --iq-probe 17070 2 --rng-seed 1"
  "iq-gate-smart|--map denbus2.map --iq-probe 17070 9 --rng-seed 1"
  # P24: carry weight + encumbrance. A light load (1 SMG = 7 lbs vs the combat char's
  # 250 lb capacity) is unencumbered with no AP penalty; 60 SMGs (420 lbs) is over -> the
  # stat.cc:198 max-AP penalty = (420-250)/40+1 = 5. Proves the weight field parses + the
  # InventoryWeight stack runs on real protos. (--give bypasses the pickup gate by design.)
  "weight-light|--character combat --map arcaves.map --give 9 --weight-probe --rng-seed 1"
  "weight-heavy|--character combat --map arcaves.map --give 9:60 --weight-probe --rng-seed 1"
  # P22: worldmap subtile fog-of-war. Travelling the arroyo->den corridor reveals subtiles
  # along the Bresenham path — the start + destination flip to VISITED (clear), the trail's
  # radius-1 neighbourhood to KNOWN (fogged). The reveal draws no RNG, so every other travel
  # golden stayed byte-identical (silent reveal + this dedicated probe, the P21 pattern).
  "worldmap-fog|--fog-probe 184 133 1 --rng-seed 2"
  # P16-M3: an X-FIGHTING-Y encounter spawns its two groups on DISTINCT teams (1 & 2) and
  # opens a brawl — the factions fight each other (cross-team targeting), not just the dude.
  "encounter-fight|--encounter-fight desert1.map ARRO_Spore_Plants 3 ARRO_Silver_Geckos 2 --rng-seed 3"
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
  # P11 M4 + P12 M0/M1/M2 — the HUD bar buttons fire their panel actions (INV/MAP/CHA
  # wired in P11; SKILLDEX in P12-M0; PIP in P12-M1; OPT in P12-M2).
  "hud-buttons|--character combat --map denbus2.map --hud-click INV --hud-click MAP --hud-click CHA --hud-click SKILLDEX --hud-click PIP --hud-click OPT --rng-seed 1"
  # P12 M0 — the Skilldex use-skill picker: lockpick a scripted door (use_skill_on_p_proc
  # honours the script — the door stays locked, not blindly unlocked), First Aid self at
  # full HP (healthy already, no roll), and the Sneak stance toggle. All deterministic.
  "skilldex-skills|--map denbus2.map --use-skill 9 9510 --use-skill 6 -1 --use-skill 8 -1 --rng-seed 1"
  # P15 M1 — the HUD weapon slot cycles the attack mode (single->burst) for a burst gun.
  "weapon-mode-cycle|--map arcaves.map --give 9 --use-item 9 --hud-click WEAPON --rng-seed 1"
  # P18 M0/M1 — in-combat movement costs AP per hex. AP 8 reaches the 4-hex target (4 left);
  # AP 2 TRUNCATES at 2 hexes (gate halts the walk); a crippled leg costs 4 AP/hex so 8 AP
  # only covers 2 hexes — the P14-M3 MovePointCost now bites the player (the SCOPE asymmetry).
  "combat-walk-full|--map arcaves.map --combat-walk 20529 20534 8 --rng-seed 1"
  "combat-walk-truncated|--map arcaves.map --combat-walk 20529 20534 2 --rng-seed 1"
  "combat-walk-crippled|--map arcaves.map --combat-walk 20529 20534 8 cripple --rng-seed 1"
  # P15 M2 — item-panel row CLICK == its number key: open the inventory (HUD INV), click an
  # empty row (out of bounds -> no-op, consumed=false) then row 0 (equips, same as pressing 1).
  "panel-click-equip|--character combat --map denbus2.map --give 9 --hud-click INV --panel-click 0 5 --panel-click 0 0 --rng-seed 1"
  # P15 M3 — the Options/Pip-Boy menu rows are clickable (Skilldex parity). Each row's
  # centre must hit-test back to its own index (hit==row), then dispatch: Options row 4 =
  # Resume (closes); Pip-Boy row 0 = Rest (opens rest menu), rest-menu row 9 = Back, status
  # row 1 = Automap. All side-effect-free rows so the state line is map-independent.
  "menu-click-options|--character combat --map denbus2.map --menu-click options 4 --rng-seed 1"
  "menu-click-pipboy|--character combat --map denbus2.map --menu-click pipboy 0 --menu-click pipboy-rest 9 --menu-click pipboy 1 --rng-seed 1"
  # P15 M0 — the Pip-Boy automap object census (the dots it plots): deterministic
  # per-type object counts + the dude tile for a fixed map (no RNG).
  "automap-arcaves|--map arcaves.map --automap --rng-seed 1"
  # P21 — script-driven lighting + reg_anim: artemple's map_enter calls set_light_level(100)
  # (now pins the ambient) and reg_anim_animate_forever on its two firepits (now reaches the
  # animator; redundant with FRM auto-loop on the slice, faithful for the critter case). Both
  # were arity-stubbed no-ops before. The probes report the results.
  "script-light|--map artemple.map --light-probe --reg-anim-probe --rng-seed 1"
  # P12 M1 — the Pip-Boy rest options: a timed rest (6h heals proportionally) then an
  # until-healed rest from near-death to full. --hurt sets up the wound; deterministic
  # clock math + heal amounts (artemple has no enemy near the entry, so rest is allowed).
  "pipboy-rest|--map artemple.map --hurt 20 --rest-for 360 --hurt 20 --rest-for -1 --rng-seed 1"
  # #10 M2 — a legitimately-recruited Vic levels up his proto as the dude gains levels
  # (PartyLevelUp wired into AwardXp; party.txt member 13, level_minimum 5).
  "vic-levelup|--map denbus2.map --give 41:2000 --give 266:1 --talk-seq 17070 1,1,1 --talk-seq 15278 2,2,1,1 --talk-seq 17070 2,1 --grant-xp 60000 --party-count --rng-seed 1"
  # #10 M3 — the scripted recruit + its proto level-up survive a save/load round-trip:
  # the party-count line is identical before and after (members=2, no duplication; Vic
  # keeps his levelled stage HP). Saves to /tmp so nothing lands in the repo.
  "vic-save-roundtrip|--map denbus2.map --give 41:2000 --give 266:1 --talk-seq 17070 1,1,1 --talk-seq 15278 2,2,1,1 --talk-seq 17070 2,1 --grant-xp 60000 --save-path /tmp/hexwaste-m3golden.json --party-count --save-now --load-now --party-count --rng-seed 1"
  # P30 A-M0/M1/M2 — the sneak state probes (deterministic): the periodic SKILL_SNEAK roll under a
  # fixed seed (the isolated _sneakRng), the two-layer flag/working state, and the Silent Death facing
  # test (behind hex 0/0 → mult 4; front 0/3 → mult 2).
  "sneak-state|--map artemple.map --sneak-roll 1 --sneak-probe 1 --backstab-probe 0 0 --backstab-probe 0 3 --rng-seed 1"
)

# Keep only the deterministic transcript lines (drop map-load / animate / stub /
# dialog-text noise — NEVER capture REPLY/OPTION game-asset strings).
FILTER='^(encounter|travel-from|companion|dismiss-persist|trade:|party:|party-count:|set-global:|hud-click:|use-skill:|hurt:|rest:|automap:|weapon-mode:|panel-click:|menu-click:|travel-resume:|travel-step:|travel-save-mid:|worldmap-fog:|weight:|iq-probe:|death-probe:|trait-probe:|perk-probe:|perk-pick:|combat-walk:|light:|reg-anim:|encounter-fight:|brawl:|sneak-probe:|sneak-roll:|backstab-probe:|  spawn|  flat|  wait:|  follow:|  dismiss:|  rejoin:)'

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
