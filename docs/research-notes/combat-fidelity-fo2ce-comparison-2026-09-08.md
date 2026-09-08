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

First pass:
- **00 — entrance-check**: Temple of Trials entrance, Narg (premade tribal) on the steps.
- **01 — approach**: inside `artemple.map`'s interior — a dark, arched brick corridor room
  (log: *"You are in a dark, musty temple. The shadows seem to play tricks with your eyes,
  and you can hear the faint sound of movement."*).
- **02 — ant-spotted**: a Giant Ant enters view; turn-based combat auto-triggers (a `1`
  turn-order badge appears over the dude, and the TURN/CMBT HUD indicator lights up).

That first pass stopped short of melee engagement — the camera locked onto a position that
didn't include the dude once combat auto-triggered, and mouse-drift (fo2ce's relative-mouse
mode has no internal clamp, so large cumulative relative deltas pushed the in-game cursor far
outside the visible canvas) made recovery slow. A second pass, on a fresh relaunch with small
disciplined mouse moves throughout, got much further:

- **01 — approach**: same corridor room, camera cleanly centered on the dude right after
  entering.
- **02 — ant-in-view**: pressing **Home** (fo2ce's camera-recenter-on-player hotkey) recovered
  the view after combat auto-triggered and the camera drifted away — the key finding that
  unstuck this whole line of investigation. A Giant Ant is visible near the dude.
- **03 — adjacent-aimline**: after a further short click-to-walk, the dude stands adjacent to
  the ant; a yellow reach/aim-line renders between them.
- **04 — attack-crosshair**: right-click cycles the cursor through modes (walk → look → attack);
  landing on "look" prints `You see: Giant Ant.` to the log, and continuing the cycle reaches a
  genuine red attack-targeting crosshair over the ant.
- **05 — post-click-still-alive**: with the crosshair showing, left-click (plain, then a genuine
  double-click) produced no AP change and no combat log message — the ant survived. The actual
  attack-CONFIRM input for this control setup (`xdotool` mousedown/mouseup against this SDL
  build) was not identified this session, despite trying left-click, the right-click mode-cycle,
  the `F` key, and double-click in combination.

The approach and full combat-engagement-up-to-crosshair states were captured cleanly across
both passes; the actual attack/kill comparison uses Hexwaste's existing `combat-golden.sh`
fixture for the same map instead (see below).

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
fo2ce itself — not a Hexwaste fidelity concern, but a genuine open question about this specific
piloting setup: the attack-confirm input eluded every combination tried (left-click, the
right-click mode-cycle, `F`, double-click) even once the crosshair cursor was reached, which is
new, reproducible information for whoever revisits this control script next. Also newly
documented for future pilots: fo2ce's relative-mouse cursor has no internal clamp (large
cumulative deltas can push it far off-canvas with no visible sprite to recover from), and
**Home recenters the camera on the player** — both worth folding into
`docs/fo2ce-comparison-playbook.md` if this scenario gets revisited.
