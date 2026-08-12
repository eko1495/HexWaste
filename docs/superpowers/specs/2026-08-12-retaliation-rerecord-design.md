# Re-record tier, sub-project 1: rating-gated retaliation — design spec (2026-08-12)

Restore `_combatai_check_retaliation`'s rating gate, which was implemented during the
2026-08-11 fidelity batch and then deliberately deferred because it moves a committed golden
fixture. This is the first of four remaining re-record-tier items and deliberately the
smallest: its blast radius is already measured, so it establishes the re-record discipline on
the cheapest possible case.

## Context

The 2026-08-11 batch shipped under a byte-identical contract: any change that moved a fixture
was escalated rather than accommodated. The rating gate was written (commit `d808238`), found
to move `brawl-watch`, and reverted (commit `0bc86da`), leaving `RegisterHit` at
last-hitter-wins with a comment recording the deferral.

That contract is now lifted for this tier. **The consequence is that the golden suite no longer
provides evidence.** A re-recorded fixture is green by construction, so proof of correctness has
to come from somewhere else — see "What carries the proof".

## Scope — one item

Restore in `src/Hexwaste.Formats/Combat/CombatEngine.cs`:

- `RegisterHit` regains the two-line gate ported from `_combatai_check_retaliation`
  (`combat_ai.cc:3484`): a null `WhoHitMe` is set unconditionally; an existing one is replaced
  **only when `Rating(new) > Rating(existing)`** — strictly greater, so an equal-rated attacker
  does not steal aggro. Hexwaste's existing team gate and dead-target gate are retained
  unchanged (the reference's equivalents live in its callers).
- The method's doc comment stops describing a deferral and describes the port.
- `RegisterHit` returns to an instance method, since it calls `Rating`. This reverses final-review
  Minor 6 (`static`) from the previous batch, which existed only because the gate was absent.

`AiRating.Score` and `CombatEngine.Rating` already exist and are unchanged — they shipped in the
previous batch and are already unit-tested.

Explicitly **not** in this sub-project: ring-spiral explosion damage, the explosive
`×(extras+1)` best-weapon factor, `_combat_safety_invalidate_weapon` +
`_cai_retargetTileFromFriendlyFire`, and `_ai_danger_source` + perception-based
`PruneEscapedHostiles`. Each is its own spec→plan→build cycle, sequenced after this one.

## What carries the proof

The fixture is a record of a consequence, never the evidence itself. Three hermetic tests, none
depending on any transcript, are the actual proof. All live in
`tests/Hexwaste.Formats.Tests/CombatEngineTests.cs` and run through the existing
`FakeCombatHost`:

1. **The rule.** Null `WhoHitMe` → set. Strictly-greater rating → replace. **Equal rating → keep**
   (the boundary the reference's `>` comparison defines). Lower rating → keep.
2. **The surviving gates.** A same-team hit and a hit on an already-dead target still never
   register, so restoring the gate did not disturb them.
3. **Engine-level retargeting.** A critter hit by a low-rated attacker and then by a high-rated
   one ends up targeting the high-rated one; driven in the reverse order, it does not switch.

Every one of these must be confirmed to **fail against the pre-change code** (temporarily
reverting `CombatEngine.cs` is sufficient), and that confirmation reported. A test that passes
both before and after proves nothing — the previous batch had two bugs survive precisely because
the only net was a suite that never reached the branch.

## The fixture step

Order matters, because the guard depends on measuring before recording.

1. Run all four suites in `check` mode: `dotnet test`, `combat-golden.sh check`,
   `quest-golden.sh check`, `encounter-golden.sh check`. Record exactly which fixtures fail.
   **Expected: exactly one — `brawl-watch`.**
2. If any fixture other than `brawl-watch` fails, **stop and report**. The blast radius was
   measured as exactly one fixture; a second means the change does more than believed and the
   design is wrong, not the fixture.
3. Only then run `scripts/encounter-golden.sh record`. That script rewrites all 188 fixtures, so
   `git status --short tests/golden-encounter/` must afterwards show **exactly one modified
   file**. More than one is a stop condition, not something to accept.
4. Re-run every suite in `check` mode to confirm green.

## The justification

The commit body must trace each of the three changed values in `brawl-watch` —
`rounds 11→9`, `survivors 1→2`, `winTeam [2]→[1]` — to the retargeting decisions that cause
them: which critter changed target on which round because of the rating comparison, and why that
is what the reference would do.

**If that trace cannot actually be constructed from the transcript, say so plainly and do not
re-record.** The item then returns to deferred. An unexplained fixture delta accepted because
"the port looks faithful" is exactly how a bug gets laundered into the baseline, and this
sub-project exists to set the opposite precedent.

## Docs

`docs/BACKLOG.md`: move rating-gated retaliation out of the re-record tier into the shipped list,
leaving four items in the tier. Note that its `brawl-watch` fixture was deliberately re-recorded,
with the commit SHA, so a future reader knows the baseline changed by intent rather than by drift.

## Definition of done

The gate restored; three hermetic tests green and each confirmed failing pre-change; exactly one
fixture re-recorded with its delta traced in the commit body; all four suites green afterwards;
`docs/BACKLOG.md` reconciled. Delivered as a single task — the change is two lines plus its
verification, and does not warrant multi-task machinery.
