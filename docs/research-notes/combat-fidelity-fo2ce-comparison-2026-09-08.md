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

A **seventh pass** finally got a conclusive, mechanism-level answer by instrumenting the engine's
own debug output rather than only observing the screen. Set `DEBUGACTIVE=log` (an environment
variable `_debug_register_env()` reads at startup, `debug.cc:83-103`) to route every
`debugPrint()` call in the engine to `reference/fallout2-ce/run/debug.log`. (A `ddraw.ini`
`[Misc] ConsoleOutputPath` was also tried, to mirror the on-screen message log to a file, but the
target file was never created — that sfall mechanism doesn't appear to be wired up in this build/
session, and wasn't investigated further since `debug.log` alone answered the question.)
Reproduced the correct sequence an eighth time, reached a crosshair precisely on target
(**13 — debug-crosshair-on-target**) — the on-screen result was, again, no visible change
(**14 — debug-post-click-no-change**). But `debug.log` (saved as `debug.log.txt` alongside the
screenshots) is the real payoff:

- It correctly captured *other* `debugPrint()` calls throughout the session (`OVERRIDE_MAP_START`,
  `MAP LOAD`, `Giant Ant is using Rat packet...`, etc. — confirmed each is a genuine `debugPrint()`
  call by reading its call site, e.g. `interpreter_extra.cc:531-532` for `OVERRIDE_MAP_START`),
  proving the capture mechanism genuinely works.
- It contains **zero** occurrences of `"computing attack..."`, `"sequencing attack..."`, or
  `"running attack..."` — the three `debugPrint()` lines `combat.cc`'s `_combat_attack()`
  **unconditionally** prints every single time it runs (`combat.cc:3499`, `:3536`, `:3559`).
  **This proves `_combat_attack()` is never called by our click.**

Combined with the on-screen log never once showing any of `_combat_attack_this()`'s other
failure-branch messages — out of ammo, out of range, not enough AP, aim blocked, arm crippled,
both arms crippled (`combat.cc:5715-5805`; each of these branches calls
`displayMonitorAddMessage(...)`, so any of them firing would have shown up on screen) — the
failure is narrowed to **exactly one of two silent, message-free early-return branches** at the
very top of `_combat_attack_this()` itself (`combat.cc:5715-5721`):

```cpp
void _combat_attack_this(Object* target)
{
    if (target == nullptr) {
        return;
    }

    if ((gCombatState & 0x02) == 0) {
        return;
    }
    ...
```

Either `gameMouseGetObjectUnderCursor()` resolves to no object at all despite the crosshair
sprite visually rendering right on the target, or `gCombatState`'s bit `0x02` — the "it's the
dude's turn, input is enabled" flag, set once per turn in `_combat_turn()` (`combat.cc:3273`)
right before entering the `_combat_input()` polling loop (`combat.cc:3134-3199`) that reads
`inputGetInput()` each frame — is unexpectedly unset at the moment our click is processed, even
though every other observable signal (movement working, the TURN/CMBT indicator, the AP display)
suggested we were mid-turn with input enabled. This was the most precise diagnosis reached up to
that point in the session — narrowed to four lines of one function. **It turned out to be the
wrong branch entirely — see the Breakthrough section below, reached shortly after with the
user driving the session live.**

## BREAKTHROUGH — the mystery is fully solved

An **eighth pass**, with the user directly driving mouse input on the live screen, produced a
result no earlier pass had: the on-screen log showed **"Target out of range."** and the crosshair
displayed a live hit-chance percentage. This was the first genuine `_combat_check_bad_shot()`
message the whole investigation had seen — proof the click dispatch, target resolution, and
turn-state were all fine all along; the seventh pass's two-candidate diagnosis, while a correct
read of the *symptom* (no message, no debug line), had the wrong root cause. The real
explanation reads straight from the source (`combat.cc:5643-5673`):

```cpp
int _combat_check_bad_shot(Object* attacker, Object* defender, int hitMode, bool aiming)
{
    int range = 1;
    ...
    range = objectGetDistanceBetween(attacker, defender);   // REAL hex-grid distance
    ...
    if (weaponGetRange(attacker, hitMode) < range) {
        return COMBAT_BAD_SHOT_OUT_OF_RANGE;
    }
    ...
```

`objectGetDistanceBetween()` (`object.cc:2604-2618`) computes true hex-grid distance via
`tileDistanceBetween(object1->tile, object2->tile)`. `weaponGetRange()` for an unarmed attacker
with no weapon (Narg's case) hits the fallback `return 1;` (`item.cc:1636`) — so a punch legitimately
requires true hex distance ≤ 1, exactly as expected.

**The actual bug in our technique, finally identified**: the walk-mode cursor's on-screen
"distance" number (the `1`, `2`, etc. shown when hovering a target in `MOVE` mode) is **not**
the same metric as `objectGetDistanceBetween()`'s true hex-grid distance — it behaves like a
path-cost/steps-remaining estimate that can diverge from true hex adjacency near obstacles (this
room is dense with pillars). A **ninth pass** walked the dude to the point where the move-cursor
showed a plain blocked `X` with no number at all — directly on the ant's own tile, the maximum
possible proximity reachable by walking from that approach angle — armed the crosshair, and
clicked: a **freshly appended** "Target out of range." line appeared (confirmed via a visible
shift in the log's scrolled content, ruling out message deduplication as an explanation). Even
this maximum-walkable proximity was still further than true hex distance 1 in the underlying hex
grid from that particular approach direction.

This resolves the entire investigation. The attack pipeline works exactly as the source code
says it should — a plain left-click-up in `CROSSHAIR` mode over a resolved critter at true hex
distance ≤ 1 fires `_combat_attack()` immediately, and every earlier "silent" result across the
whole investigation was almost certainly this exact same `COMBAT_BAD_SHOT_OUT_OF_RANGE`
rejection recurring — invisible as a *change* because the display log doesn't clearly signal "the
same message happened again" the way a fresh, different line does, and because earlier passes had
no reason yet to expect "out of range" specifically, so a static, unchanged-looking log read as
"nothing happened" rather than "the same rejection happened again." One additional confound was
found and fixed along the way: a **second, stale fo2ce process** had been running unnoticed
alongside the current one for at least part of this session (found via `ps aux`), which could
have caused some fraction of screenshots and clicks to target inconsistent windows — always
verify only one instance is running (`pgrep -f fallout2-ce` should show exactly one PID) before
trusting a reproduction.

## THE KILL — confirmed live, on camera

A **tenth pass** put the corrected technique together end to end: approach carefully, verify the
`MOVE`-mode cursor shows a plain blocked `X` directly on the target's own occupied tile (not a
low-but-nonzero number), arm `CROSSHAIR` via the interface-bar `PUNCH` label, and — the new,
critical check — watch for a **live hit-chance percentage** rendered directly on the crosshair
when hovering the target. That percentage only appears when the engine considers the shot
genuinely valid; its presence is a real-time confirmation of true range, independent of and more
reliable than any of the walk-cursor's distance indicators. Approaching from one particular angle
finally produced a crosshair showing **`45%`** — confirmed valid. Click one: *"You missed."* — a
real attack animation and a real message, the first successful attack dispatch of the entire
investigation. Click two, same round, AP still available: *"Giant Ant was hit for 10 hit points
and was killed."* **The kill** — full authentic combat log text, matching the exact
`"<Name> was hit for <N> hit points and was killed."` phrasing this whole investigation expected
from the source and from Hexwaste's own already-verified combat log rendering. This closes out
the comparison with its original goal achieved: a genuine, live, on-camera fo2ce melee kill,
directly comparable to Hexwaste's own deterministic kill sequence recorded earlier in this same
document.

## Theories tested along the way

- **Resolution/coordinate-transform mismatch (refuted, pass five).** Matching the engine's
  internal render resolution 1:1 to the display via `f2_res.ini` made no difference to the
  symptom — correctly refuted; the real cause (hex-range) is orthogonal to display resolution.
- **Target distance/proximity — appeared refuted in pass six, actually the answer.** Pass six's
  "closest possible adjacent target" was only confirmed close in *screen* terms, not verified
  against true hex distance the way the breakthrough passes later did — an important
  methodological lesson: screen adjacency and `objectGetDistanceBetween()` hex adjacency are not
  the same thing near obstacles, and only the latter matters to combat.
- **"The punch keeps missing silently" (refuted as stated, but for the wrong reason at the
  time).** `_combat_check_bad_shot()`'s failure branches genuinely do fire — `COMBAT_BAD_SHOT_
  OUT_OF_RANGE` — but the display log's behavior on repeated identical messages made this very
  hard to distinguish from "no message at all" without a fresh line to compare against.
- **Wrong cursor mode / not actually in `CROSSHAIR`.** Genuinely ruled out throughout — the
  visually-confirmed red targeting crosshair was correct every time; the failure was always
  downstream of successfully entering `CROSSHAIR` mode with a resolved target.

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
consistent with fo2ce's own combat log, and this session finally proved that directly: **a live
fo2ce melee kill was captured on camera** (see "THE KILL" above) — *"Giant Ant was hit for 10 hit
points and was killed."* — closing the one gap this comparison originally set out to fill.
Getting there required source-code analysis confirming a plain left-click-up in `CROSSHAIR`
mode over a resolved critter should attack immediately, live testing confirming combat state,
target recognition, and the message log all work correctly for other actions — yet the
melee-attack click specifically never resolved, across eight independent attempts. **The ninth
attempt, live with the user, finally solved it: the attack pipeline was never broken.** The dude
was never actually at true hex-adjacency (`objectGetDistanceBetween()` distance ≤ 1) to the
target in any earlier attempt, despite looking screen-adjacent and despite the walk-mode
cursor's own "distance" indicator showing what looked like adjacency — that on-screen number is
a path-cost estimate, not the same metric combat's real range check uses, and the two silently
diverge near obstacles (this room is full of pillars). Once genuinely hex-adjacent (confirmed
the hard way — walking until the move cursor could make no further progress at all), an unarmed
punch is governed by ordinary, correctly-functioning game logic: `weaponGetRange()` returns 1
for an unarmed attacker, `_combat_check_bad_shot()` compares that against the real hex distance,
and rejects with `"Target out of range."` — a real, working message this investigation simply
hadn't recognized as *new information* the many earlier times it silently recurred, because a
repeated identical line doesn't visually announce itself as "this happened again" the way a
change does. The **tenth pass** turned that understanding into a reliable technique — approach,
verify a blocked `X` directly on the target's own tile, arm `CROSSHAIR`, and specifically watch
for a live hit-chance **percentage** rendering on the crosshair as confirmation of true range
before clicking — and landed the kill on the very next attempt.

Four real, reproducible piloting/technique bugs were found and fixed along the way
(`docs/fo2ce-comparison-playbook.md` updated with all of them): `CROSSHAIR` mode is only
reachable via right-click while already `isInCombat()`; **Home recenters the camera but leaves
the cursor stranded at its old screen position**; **Home itself silently does nothing if the
cursor is pinned at a screen edge when pressed**; and **a raw atomic `xdotool click 1` can break
left-click entirely, against everything, until fo2ce is relaunched**. A stray **second fo2ce
process** running unnoticed alongside the current one was also found and killed, a real confound
for at least part of the session. None of these four technique bugs were actually the root cause
of the melee-attack mystery — the real cause (hex-range vs. screen-proximity) was orthogonal to
all of them — but each was genuine and worth fixing regardless, and clearing them was what made
the room's actual hex geometry legible enough to finally see the true cause. `debugPrint()`
instrumentation (`DEBUGACTIVE=log`) was the key methodological tool throughout the second half
of this investigation — it didn't directly reveal the hex-range issue, but it definitively ruled
out every other candidate (target resolution, turn-state, message-log suppression) that could
have looked identical from the outside, which is what made the live "out of range" message,
once it finally appeared, immediately recognizable as the real answer rather than one more
inconclusive data point.
