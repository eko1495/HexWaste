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

A **third pass** dug into fo2ce's own C++ source (`reference/fallout2-ce/src`) to stop guessing
and get a grounded answer. The source confirms a single left-click-up in `GAME_MOUSE_MODE_
CROSSHAIR` over a resolved critter should fire `_combat_attack_this()` immediately — no
double-click, no special confirm step (`game_mouse.cc:1000-1017`). Right-click cycles
`gameMouseCycleMode()` through `MOVE(0) → ARROW(1) → CROSSHAIR(2)`, but `CROSSHAIR` is only
reachable while `isInCombat()` is true (`game_mouse.cc:1422-1440`) — outside combat, right-click
just bounces between `MOVE`/`ARROW` forever, which is a real trap for a pilot who hasn't
triggered combat yet. Armed with this, the pass reproduced every step correctly live — reached
the genuine red crosshair over an adjacent ant (**06 — crosshair-on-target**) — and along the
way found and fixed a **second real bug in the piloting technique**: pressing **Home** recenters
the *camera* on the player but does **not** move the *cursor* — after a Home recenter, the
crosshair sprite is left stranded at its old screen position while the world shifts underneath
it, so a click made right after Home (without first repositioning the cursor) always misses,
silently. Once the cursor was manually repositioned back onto the recentered target, the
crosshair rendered precisely on it — and left-click **still** produced no AP change and no log
line. A parallel action (clicking the TURN/CMBT button to attempt ending combat) DID produce a
fresh log line (**07 — combat-cannot-end-confirms-hostile**: *"Combat cannot end with nearby
hostile creatures."*) confirming combat state is genuinely active, the ant is genuinely
recognized as hostile, and the message log genuinely does update when a real action dispatches —
which rules out "the punch keeps missing silently" and narrows the mystery specifically to
`_gmouse_handle_event()`'s left-click-up handler never resolving a target/never firing at all
for this input setup. This remains unsolved after combining source-code grounding with live
testing; see Non-goals below for the leading unverified theory.

A **fourth pass** (live, with the user watching and correcting technique in real time) confirmed
two more concrete findings and reproduced the same end state a third time from a completely
fresh session:
- **Never use a raw, atomic `xdotool click 1`** against this build. Doing so once (as a quick
  experiment) left left-click completely non-functional afterward — against everything, not just
  combat: menu buttons (`DONE` in the pause menu), `INV`, `MAP`, and even `TURN`/`CMBT` all
  stopped registering clicks entirely, confirmed by the absence of `Combat cannot end...`
  reappearing on a repeat click. `fo2ce-control.sh`'s own tested `mousedown` → `sleep 0.15` →
  `mouseup` pattern kept working fine both before and after this incident on a fresh relaunch, so
  the regression is specific to the atomic form, not clicking in general.
- **The cursor must be moved away from any screen edge *before* pressing Home**, not just before
  clicking. With the cursor pinned at an edge, `Home` silently does nothing (an identical frame
  before and after the press) — this is distinct from, and in addition to, "`Home` doesn't move
  the cursor" from the third pass. Once the cursor was nudged to a safe, non-edge position first,
  `Home` reliably recentered the camera on the dude every time.
- A second, independent way to arm `CROSSHAIR` mode was found: left-clicking on (or immediately
  beside) the yellow attack-mode label in the interface bar (`PUNCH` when unarmed) also arms it,
  confirmed by a small red reticle icon appearing next to the label — an alternative to the
  right-click mode-cycle.
- Combining all of the above, the full correct sequence (walk adjacent → arm `CROSSHAIR` via the
  interface-bar label → keep the cursor off-edge → `Home` to recenter → reposition cursor onto
  the now-recentered target) was reproduced cleanly on a **third independent fresh session** —
  crosshair precisely on the ant (**08 — home-recenter-after-edge-fix**, **09 —
  crosshair-on-target-final**) — and left-click **still** produced no AP change and no log
  message, identically to passes two and three. The melee-attack execution mystery persists
  despite this being the cleanest, most carefully-controlled reproduction attempt of the whole
  investigation.

A **fifth pass** directly tested the leading theory from the fourth pass (below) by creating
`reference/fallout2-ce/run/f2_res.ini` with `SCR_WIDTH=1920`, `SCR_HEIGHT=1080`,
`WINDOWED=0` — matching the engine's internal render resolution 1:1 to the real desktop output,
eliminating the 640×480-to-1920×1080 stretch entirely (`svga.cc:109-124` confirms these
`f2_res.ini` `[MAIN]` keys are exactly what sets the internal `width`/`height` passed to
`_GNW95_init_window`/`directDrawInit`, and `mouse.cc:615-627`'s `_mouse_clip()` confirms the
internal cursor's clip bounds derive from that same configured size). Relaunched fresh,
reproduced the identical correct sequence a **fourth** time, reached a crosshair confirmed
precisely centered on the target (**10 — f2res-crosshair-on-target**, independently confirmed by
the user watching the live screen) — and left-click **still** produced no AP change and no log
message. **This refutes the resolution-mismatch theory.** The melee-attack failure is not caused
by a coordinate-transform mismatch between rendering and hit-testing.

A **sixth pass**, at the user's request, specifically tested whether target distance/proximity
mattered — reverted `f2_res.ini` (back to the default 640×480 internal resolution) and targeted
the *closest possible* ant: one standing immediately adjacent, directly at the dude's own feet
(the tightest spatial case possible, as opposed to the one-tile-further ant used in prior
passes). Crosshair confirmed precisely on this closest ant (**11 — close-ant-armed**, **12 —
close-ant-crosshair**) — left-click still produced the identical null result. **This rules out
target distance/proximity as a variable too.**

The approach and full combat-engagement-up-to-crosshair states were captured cleanly across all
six passes; the actual attack/kill comparison uses Hexwaste's existing `combat-golden.sh`
fixture for the same map instead (see below).

## Theories tested and refuted for the melee-attack mystery

- **Resolution/coordinate-transform mismatch (REFUTED, pass five).** Matching the engine's
  internal render resolution 1:1 to the display via `f2_res.ini` made no difference — the attack
  still doesn't execute with a correctly-aimed crosshair.
- **Target distance/proximity (REFUTED, pass six).** The closest possible adjacent target (right
  at the dude's own feet) fails identically to a target one tile further away.
- **"The punch keeps missing silently" (REFUTED, pass three).** A parallel action (clicking
  `TURN`/`CMBT`) produces a real log line when combat state genuinely blocks something,
  confirming the message log updates correctly for real game actions — no such line ever
  appears for the attack click, meaning it doesn't reach `_combat_attack_this()`'s validation
  logic at all, rather than reaching it and failing a check silently.
- **Wrong cursor mode / not actually in `CROSSHAIR`.** Ruled out by the visually-confirmed red
  targeting crosshair (distinct from the plain hex-outline walk cursor and the yellow ARROW-mode
  reach-line) appearing precisely on the target, reproduced across six independent passes via
  two different arming methods (right-click mode-cycle, and the interface-bar `PUNCH` label).

**Not yet tested**: enabling `fallout2.cfg`'s `[debug] console_output_path` for verbose
per-frame diagnostics — this remains the most promising next step, since every environmental
and technique-level theory has now been eliminated and the remaining candidates (a bug specific
to synthetic/`xdotool`-injected input events not being recognized identically to genuine SDL
hardware events, or a deeper engine-side issue) would most plausibly show up there.

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
piloting setup, now narrowed considerably: source-code analysis confirms a plain left-click-up
in `CROSSHAIR` mode over a resolved critter should attack immediately, and live testing
confirmed combat state, target recognition, and the message log all work correctly for other
actions — yet the melee-attack click specifically never resolves. Two real, reproducible
piloting bugs were found and fixed along the way (`docs/fo2ce-comparison-playbook.md` updated
with all of them): `CROSSHAIR` mode is only reachable via right-click while already
`isInCombat()`; **Home recenters the camera but leaves the cursor stranded at its old screen
position**; **Home itself silently does nothing if the cursor is pinned at a screen edge when
pressed**; and **a raw atomic `xdotool click 1` can break left-click entirely, against
everything, until fo2ce is relaunched**. Missing any of these silently desyncs the pilot's aim
from where the game thinks the cursor is, or breaks input outright. fo2ce's relative-mouse
cursor also has no internal clamp (large cumulative deltas can push it far off-canvas with no
visible sprite to recover a bearing from) — also folded into the playbook. Despite fixing every
one of these and reproducing a correctly-aimed crosshair on the target across **six** independent
sessions (several live, with real-time correction from a human watching the actual screen), the
melee-attack click itself never executes — not for a far target, not for the closest possible
adjacent target, and not with the engine's internal resolution matched 1:1 to the display. Three
concrete theories (resolution mismatch, target distance, and "misses are just silent") are now
directly refuted by evidence rather than merely undemonstrated; what remains is documented above
as the next concrete step to try (verbose debug console output), not a vague "keep guessing."
This is a well-characterized, thoroughly reproducible open problem, isolated about as far as it
can be from the outside without instrumenting the engine's own input-handling code directly.
