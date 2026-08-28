# Re-record tier, sub-project 4: the `max_dist` flee gate and the engine's maneuver flags (F1) — design spec (2026-08-15)

> **As-of note:** the `ScriptHost.cs:1805`/`:2113`/`:2282` citations below described the tree as
> of this spec's own writing (2026-08-15) and have since drifted (the `feat/tier-f-small-batch`
> branch's `ScriptHost.cs` edits shifted everything past line ~1616 by +33); they are deliberately
> not maintained past that date. For current locations, see `docs/BACKLOG.md`.

Close **F1** from `docs/BACKLOG.md`: `_ai_run_away`'s `max_dist` predicate and the
`CRITTER_MANEUVER_DISENGAGING` semantics that terminate a flight. This is the first item in this tier
that is **expected to move a committed fixture**, and the first genuine deliberate re-record since
`brawl-watch`.

## Grounding — verified against `e97087b` on 2026-08-15

Re-read in the reference before writing. The previous three sub-projects shipped a combined twelve
wrong assertions of mine that reviewers caught by doing exactly this, so treat everything here as
falsifiable rather than settled.

### The reference

`_ai_run_away(a1, a2)` (`combat_ai.cc:1173`), where `a2` defaults to `gDude` when null:

```c
int distance = objectGetDistanceBetween(a1, a2);
if (distance < ai->max_dist) {
    combatData->maneuver |= CRITTER_MANUEVER_FLEEING;
    …run away, up to full AP…
} else {
    combatData->maneuver |= CRITTER_MANEUVER_DISENGAGING;
}
```

- The comparison is **`<`** (`:1183`). **This is the trap in this item.** The maintained fork's
  PR #675 flips it to `<=`; we classified that hunk as ungrounded and rejected it (no disassembly, no
  issue, no symptom). Porting the gate with the fork's comparison would import the very change we
  declined. Use `<`.
- The flags are `CRITTER_MANEUVER_ENGAGING = 0x01`, `DISENGAGING = 0x02`, `MANUEVER_FLEEING = 0x04`
  (`obj_types.h:120-123`; note the reference's own spelling of "MANUEVER").
- What `DISENGAGING` *means* is the half that matters. `_combatai_want_to_fight` returns **false** on
  it (`:3195`) and `_combatai_want_to_stop` returns **true** on it (`:3215`). It is the signal that
  lets a fight end. `FLEEING` has the same effect at `:3199` / `:3223`.

### The Hexwaste side — narrower and worse than the backlog says

The backlog's F1 entry says Hexwaste "has no distance predicate at all". True, but it understates and
slightly misdescribes the gap. What is actually there:

- The maneuver flags and **all four consumers already exist** and are correctly ported:
  the want-to-join check (`CombatEngine.cs:2044-2046`), the turn-order filter that skips disengaging
  critters (`:2134`), `WantsToStopFighting` (`:2266`), and the flee-continuation at `:2889`.
- The flags are also **already settable from scripts** — `critter_set_flee_state` and the
  engaging/disengaging externals write them (`ScriptHost.cs:1805`, `:2113`, `:2282`).
- What is missing is that **the engine's own AI never sets any of them.** `TryFlee`
  (`CombatEngine.cs:3098`) runs the retreat and returns, without marking `FLEEING` on the way in or
  `DISENGAGING` on the way out — and without a distance gate to decide between them. The only
  engine-side write to `Maneuver` anywhere is `critter.Maneuver = 0` at `:2065`.

So the consumers are live but permanently starved on an engine-initiated flight: a critter that the
engine sends running is never marked, so nothing can ever conclude that it has disengaged, and it
re-flees every turn forever.

`AiPacket.MaxDist` is parsed (`AiPackets.cs:18`) and otherwise **dead** — its only reference outside
the ledger is `AiPacketTests.cs:40`. `CombatEngine` already reaches the packet via
`_host.GetAiPacket(critter)` at three sites, so no new host seam is needed.

### The symptom, visible in a committed fixture

`tests/golden-combat/denbus2-fight-flee.txt` records `flee: Cute Slave@11272 -> 10480` at lines
**25, 39, 57 and 75** — the same critter, the same origin tile, the same destination, four times over.
`Handsome Slave@12670 -> 14270` repeats identically alongside it. That is the bug written into the
baseline: flight that never terminates.

## Scope

### In — `TryFlee` becomes `_ai_run_away`

`TryFlee(critter, threatTile, ref actorAp)` gains the gate. Distance is
`HexGrid.Distance(critter.HexTile, threatTile)` — the same threat the caller already resolves, so no
new plumbing:

- `distance < ai.MaxDist` → set `ManeuverFleeing` on the critter, then run exactly as today.
- otherwise → set `ManeuverDisengaging` and **take no movement and no AP**, returning the value that
  means "no turn taken", matching the reference's empty `else`.

The AI packet is nullable at this call site (`GetAiPacket` returns `AiPacket?`). The reference always
has a packet, so a null one is a Hexwaste-only state: **keep today's behaviour (always flee) when it
is null**, and say so in a comment. Do not invent a default `max_dist`.

### Out — the second `DISENGAGING` setter, deliberately

The reference sets `DISENGAGING` in a second place: `_combat_ai`'s tail (`combat_ai.cc:3098-3112`),
when the target is alive, AP remains, and `distance > max_dist`, *and* there is no friendly corpse to
back away from and `_ai_find_friend` fails.

**Hexwaste has neither `aiInfoGetFriendlyDead`/`aiInfoSetFriendlyDead` nor `_ai_find_friend`** — a
repo-wide search finds no equivalent of either. Porting that branch means building friendly-corpse
tracking and a friend search first, which is its own item. It is therefore out of scope here and must
be **recorded as a new backlog entry**, not left as prose in this spec — that is precisely how F13
went unnoticed for a full release cycle.

Also out: `_ai_move_away` (`:1221`), which is a different routine with a different gate (`<=` against
its own `a3` argument, not `max_dist`).

## What carries the proof

The fixture will move, so by this tier's contract **the fixture is a record of a consequence, never
the evidence**. Hermetic tests through `FakeCombatHost` are the proof, and every one must be confirmed
to **fail against the pre-change code**.

1. **Below `max_dist` → flees and is marked.** A critter inside the threshold runs as it does today
   *and* carries `ManeuverFleeing` afterwards.
2. **At exactly `max_dist` → disengages.** The boundary the `<` defines: distance == `MaxDist` takes
   the `else`. This is the test that would catch someone "fixing" the comparison to the fork's `<=`,
   so assert the exact boundary, both at `MaxDist` and at `MaxDist - 1`.
3. **Disengaging costs nothing.** The disengaged critter does not move and its AP is untouched.
4. **Termination actually happens.** The end-to-end point of the item: a critter marked
   `ManeuverDisengaging` makes `WantsToStopFighting` return true, so a fight whose only hostile has
   disengaged can end. Drive this through the engine rather than asserting the flag alone — the flag
   is a means, and a test that only checks the flag would pass even if nothing consumed it.
5. **Null packet → unchanged.** A critter with no AI packet still flees, so the fake host and any
   packet-less fixture critter stay inert.

## Fixture expectations — stated before the run

- **`denbus2-fight-flee` is expected to move**, and to move in a specific, checkable direction:
  fewer repeated `flee:` lines, and the fight ending earlier. If it moves in some *other* direction —
  more flee lines, or a changed winner with no disengagement in the transcript — stop and
  investigate.
- Other combat fixtures reach `TryFlee` only if their critters flee at all; `brawl-watch` in the
  encounter suite involves fleeing critters and is a plausible second mover.
- **Nothing in the quest suite should move.** If it does, stop.

Measure first, enumerate the failures, confirm they are the predicted set, and only then record. A
fixture moving that this spec did not predict is a stop condition, not a re-record.

## The justification

The commit body must trace `denbus2-fight-flee`'s delta to the gate: which critter disengaged, at
what distance, on which round, and why that is what `distance < max_dist` produces. **If the trace
cannot be constructed from the transcript, do not record** — the item returns to deferred. This is
the discipline that has already caught one false prediction (F11) and prevented one unjustified
re-record.

## Docs

`docs/BACKLOG.md`: F1 moves to shipped with its commit SHA, noting the deliberate re-record and the
fixtures affected. Add a new entry for the out-of-scope second `DISENGAGING` setter, naming the two
missing pieces (`friendly dead` tracking and `_ai_find_friend`) so it is tracked rather than lost.
Correct F1's claim that Hexwaste "has no distance predicate at all" to the sharper finding: the
consumers and the script setters exist, and it is the engine-side setters that are missing.

## Definition of done

The gate ported with `<`; `FLEEING`/`DISENGAGING` set by the engine; five hermetic tests green and
each confirmed failing pre-change; the fixture delta traced in the commit body before recording;
exactly the predicted fixtures re-recorded; all four suites green afterwards; `docs/BACKLOG.md`
reconciled including the new out-of-scope entry.

**Or:** a fixture moved that this spec did not predict, and the work stopped for investigation.
