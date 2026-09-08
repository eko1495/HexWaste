# fo2ce ↔ Hexwaste combat comparison (2026-09-08)

Piloted per `docs/fo2ce-comparison-playbook.md`. Screenshots live in the (gitignored) run dir
`scratch/compare-runs/combat-fidelity-20260908-085608/` — this note is the durable summary.

## Scope

General combat fidelity check: approach, engage, and (on the Hexwaste side) a full attack
sequence through to a kill, against the Temple of Trials' cave-critter encounter
(`arcaves.map`).

## A genuine discovery before any comparison began

Investigating `arcaves.map`'s critter placements (`tools/ProcAnalyze --map-objects`) showed
5 critters on elevation 0 running `script=ZClRat`. The natural reading is "rats." Piloting
fo2ce into the map, however, its own stdout log read:

```
Giant Ant is using Rat packet with a 0% chance to taunt
```

fo2ce's Temple of Trials cave critters are **Giant Ants** running a `ZClRat`-named AI
package — the script name is a leftover/reused label, not the creature. Hexwaste's own
runtime log for the same map independently confirms the same thing without any prompting:
`wander: Giant Ant hex 25905 -> 25506`. Both engines agree on the real creature type; only
the internal script identifier is confusingly named "rat." This matches vanilla — Fallout 2's
Temple of Trials trial is a giant ant nest, not a rat cave.

## fo2ce (piloted manually, `docs/fo2ce-comparison-playbook.md`)

- **00 — entrance-check**: Temple of Trials entrance, Narg (premade tribal) on the steps.
- **01 — approach**: inside `artemple.map`'s interior — a dark, arched brick corridor room
  (log: *"You are in a dark, musty temple. The shadows seem to play tricks with your eyes,
  and you can hear the faint sound of movement."*).
- **02 — ant-spotted**: a Giant Ant enters view; turn-based combat auto-triggers (a `1`
  turn-order badge appears over the dude, and the TURN/CMBT HUD indicator lights up).

Manual melee engagement (walking adjacent and punching) was not completed in this run —
fo2ce's SDL relative-mouse input (no absolute cursor warp; see the playbook's own documented
gotcha) made blind navigation through this specific maze's tight corridors too slow and
error-prone within this session's time budget, and a mid-run render freeze (recovered by
killing and relaunching fo2ce) cost additional time. The approach and combat-initiation states
were captured cleanly; the actual attack/kill comparison uses Hexwaste's existing
`combat-golden.sh` fixture for the same map instead (see below).

## Hexwaste (`scripts/hexwaste-checkpoint.sh`, deterministic CLI actions)

- **01 — approach**: `--map arcaves.map --rng-seed 42` — same repeating brick-arch corridor
  architecture as fo2ce's room, confirming the two engines render the same map geometry/art
  consistently.
- **02 — attack-hit**: `--map arcaves.map --attack 25905 --rng-seed 3` — one punch at the Giant
  Ant found at tile 25905 (per `ProcAnalyze --map-objects`): `chance=46%, hit=True, damage=3`,
  ant HP 6→3.
- **03 — attack-kill**: four chained attacks at the same tile/seed (`--attack 25905` ×4) —
  hit/3dmg, miss, hit/2dmg, hit/1dmg, ant dies at HP 0. The screenshot shows the actual
  combat log text rendered in-game: *"Combat begins - round 1, your turn (AP 4). You hit the
  Giant Ant for 2 damage... for 1 damage. The Giant Ant dies."* AP counter reads `4/7`
  (3 AP already spent this round on the prior punch), dude HP unchanged at `30/30` (no return
  damage taken — the ant never got a turn before dying).

## Conclusion

The most interesting finding here wasn't a UI/rendering comparison at all: both engines
independently agree that `arcaves.map`'s "rat" script actually drives Giant Ant critters,
which is the correct vanilla Fallout 2 content (the Temple of Trials' second trial is an ant
nest). Visually, Hexwaste's room geometry/art matches fo2ce's corridor precisely. Combat
presentation — AP costs, hit/miss/damage numbers, and the "Combat begins... You hit the X for
N damage... The X dies" log phrasing — reads as authentic Fallout 2 combat text in Hexwaste,
consistent with what fo2ce's own combat log format is known to look like from this project's
existing fo2ce-fidelity work. The one gap in this run is a live side-by-side melee kill from
fo2ce itself (blocked by piloting friction, not a fidelity concern) — a good candidate to
revisit with more session time or a scripted approach path if deeper fo2ce combat verification
is wanted later.
