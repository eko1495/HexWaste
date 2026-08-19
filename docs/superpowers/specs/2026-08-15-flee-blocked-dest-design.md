# Sub-project 5: the phantom flee — `pathfinderFindPath`'s unmodelled `a5` destination check (F18) — design spec (2026-08-15)

Close **F18** from `docs/BACKLOG.md`: a critter logs a *successful* flee and never moves, because
Hexwaste's pathfinder and its walker disagree about whether the retreat tile is legal.

This is the first item in this arc that is **a live bug rather than a fidelity gap**, and — unlike the
last four — it is genuinely expected to move a committed fixture.

## The bug, and why it is one

`tests/golden-combat/denbus2-fight-flee.txt` records

```
flee: Cute Slave@11272 -> 10480
```

at lines **25, 39, 57 and 75**: the same critter, the same origin, the same destination, four times.
It never moves. In the same fight `Healthy Slave` retreats normally (`11670 → 10270 → 8870`), so this
is not a general failure of fleeing.

The engine writes that transcript line and then fails to act on it. `TryFlee`
(`CombatEngine.cs:3049`) picks a destination with `Pathfinder.FindPath`, logs `flee:`, and calls
`_host.StartWalk` — but `StartNpcWalk` (`ViewerGame.cs:3326`) refuses the move in its early-return
guard, because `_blockedTiles.Contains(10480)` is true and the tile is not a door. The walk never
starts.

**The two disagree by construction.** `Pathfinder.FindPath` exempts the goal tile from its blocked
test (`Pathfinder.cs:48`):

```csharp
if (neighbor != to && isBlocked(neighbor) && !(isPassableDoor?.Invoke(neighbor) ?? false))
    continue;
```

That exemption is deliberate and correct for its main use — you must be able to path *to* an occupied
tile to melee its occupant. It is wrong for choosing a *retreat* tile, where a blocked destination is
worthless.

So the transcript has been recording flights that never happen, and the golden suite has been
faithfully preserving that as correct behaviour.

## The reference already distinguishes these two cases — we just never modelled it

`_ai_run_away` retreats via `_make_path(a1, a1->tile, destination, nullptr, **1**)`
(`combat_ai.cc:1192`), and `_make_path` is `pathfinderFindPath(object, from, to, rotations, a5, _obj_blocking_at)`
(`animation.cc:1711`). That function opens with (`animation.cc:1716-1722`):

```c
int pathfinderFindPath(Object* object, int from, int to, unsigned char* rotations, int a5, PathBuilderCallback* callback)
{
    if (a5) {
        if (callback(object, to, object->elevation) != nullptr) {
            return 0;
        }
    }
```

**With `a5` set, a blocked destination yields no path at all.** That is exactly the check Hexwaste is
missing: vanilla's `_ai_run_away` loop shrinks its retreat distance until it finds a genuinely free
tile, because a blocked candidate simply fails to produce a path.

Hexwaste's `FindPath` has no equivalent of `a5` — it behaves unconditionally like `a5 = 0`. This is
therefore **a port gap with a live symptom**, not a Hexwaste invention needing a designed fix.

## Scope

### 1. `Pathfinder.FindPath` gains the `a5` behaviour

A new optional parameter — default `false`, reproducing today's behaviour exactly, so **all eight
existing call sites stay inert by construction**. When set, a blocked destination returns `null`
immediately, before any search, matching the reference's pre-search early return rather than being
folded into the loop condition.

The door predicate applies to the destination test the same way it applies to intermediate tiles: a
closed door the critter can open is not a blocker. Vanilla's `_obj_blocking_at` has its own door
semantics; the existing `isPassableDoor` callback is Hexwaste's stand-in for that and must not be
bypassed by the new check.

### 2. `TryFlee` opts in

The one call site that corresponds to `_make_path(..., 1)`, cited as such. Nothing else changes: the
retreat-distance loop, the ±1 rotation fan, the AP handling, the `flee:` line and the `StartWalk` call
all stay as they are. With a truthful path test the loop naturally shrinks to a free tile, or exhausts
its candidates and returns `false` — the existing "hemmed in" path, which takes no turn and logs
nothing.

### 3. Explicitly NOT in scope

- **Reordering the `flee:` transcript line after a successful `StartWalk`.** It is a tempting second
  belt, but it treats the symptom, and with the destination check the line is only written when a
  real retreat tile was found. Adding both would make it impossible to tell which one fixed the
  fixture. Record it as a rejected alternative, with the reasoning.
- **The other seven `FindPath` call sites.** The reference passes `a5 = 1` at other sites too — most
  visibly `_ai_move_away` (`combat_ai.cc:1239`) — so Hexwaste's unmodelled `a5` is probably wrong in
  more places than this one. Auditing each against its reference counterpart is real work and would
  move fixtures unpredictably. **It gets its own backlog entry**, naming `_ai_move_away` as the known
  next case. Leaving it as prose in this spec is how F13 got lost.
- **The `Pathfinder` / `_blockedTiles` disagreement in general.** This spec makes the flee path stop
  proposing blocked destinations; it does not unify the two occupancy views. If they can disagree for
  reasons beyond the goal exemption, that is a separate investigation.

## What carries the proof

The fixture will move, so the fixture is a record of a consequence, never the evidence. Hermetic tests
carry the proof, and each must be **confirmed failing against the pre-change code**.

1. **The unit-level rule.** `FindPath` with the flag set returns `null` for a blocked destination, and
   without it still returns a path — the same call, both ways, in one test. This is the whole
   behavioural delta and it is pure tile arithmetic, so it needs no combat harness.
2. **A passable closed door is still not a blocker** at the destination when the door predicate says
   the critter can open it, so the new check does not regress `P113`'s flee-through-doors behaviour.
3. **Inertness.** A blocked destination with the flag unset still yields a path — the guarantee that
   the other seven call sites are unchanged.
4. **The engine-level bug itself.** A critter whose only full-distance retreat tile is occupied must
   either retreat to a nearer free tile or take no turn — and in **neither** case may a `flee:` line
   be written without the critter's tile changing. Assert on the pairing of the transcript line and
   the actual position, because that pairing is precisely what is broken today.

## Fixture expectations — stated before the run

- **`denbus2-fight-flee` is expected to move**, and in a specific direction: the four identical
  `Cute Slave@11272 -> 10480` lines must be gone, replaced by either a retreat to a nearer free tile
  or no flee line at all for that critter. A delta in some other direction — different critters
  moving, a changed winner with no change to those four lines — is a stop condition.
- Other fixtures reach this path only if a critter flees toward a blocked tile. `brawl-watch` in the
  encounter suite is the plausible second mover.
- **Nothing in the quest suite should move.**

Measure first, enumerate, confirm the failing set matches, construct the trace, then record. This
protocol has now caught two false predictions of mine in two sub-projects; it is not ceremony.

## The justification

The commit body must trace the delta: which critter, which candidate tile was rejected as blocked,
what it retreated to instead (or why it took no turn), and why that is what `_make_path(..., 1)`
produces. **If the trace cannot be constructed, do not record** — the item returns to deferred.

## Definition of done

The `a5` check ported with its citation; `TryFlee` opting in; four hermetic tests green and the
mutation-verified ones confirmed failing pre-change; the fixture delta traced before recording;
exactly the predicted fixtures re-recorded; all four suites green; `docs/BACKLOG.md` reconciled with
F18 shipped and a new entry for the unaudited `a5` call sites.

**Or:** the fixture moved in an unpredicted way and the work stopped for investigation.
