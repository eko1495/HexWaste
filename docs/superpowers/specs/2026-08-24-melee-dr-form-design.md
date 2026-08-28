# Sub-project: the melee damage-resistance form (F42) — design spec (2026-08-24)

Close **F42**: the melee/unarmed path reduces post-threshold damage by DR in a different algebraic
form from the reference and from Hexwaste's own ranged path. The forms are equal over the reals and
**not** equal under integer truncation.

This is the first item in this arc that is **expected to move fixtures**, and it is a genuine
arithmetic bug affecting every melee and unarmed attack in the game.

## Grounding — verified against `e97087b` on 2026-08-24

The reference (`attackComputeDamage`, `combat.cc:4606-4610`):

```c
damage -= damageThreshold;

if (damage > 0) {
    damage -= damage * damageResistance / 100;
}
```

Hexwaste has two implementations of this reduction, and they disagree:

- **Ranged** (`CombatMath.cs:173-174`, in `RangedMath.RollDamage`) — `damage - damage * resistance / 100`.
  **Matches the reference.**
- **Melee/unarmed** (`CombatMath.cs:93`, in `ReduceByArmor`) — `afterThreshold * (100 - dr) / 100`.
  **Does not.**

## The exact size and direction of the change — derived, not guessed

Let `d` be post-threshold damage and `r` the clamped resistance.

- Ours: `floor(d·(100−r)/100)` = `d − ceil(d·r/100)`
- Reference: `d − floor(d·r/100)`

Their difference is therefore `ceil(d·r/100) − floor(d·r/100)`, which is:

- **`1` whenever `d·r` is not a multiple of 100** — the reference deals **one more point** of damage
- **`0` otherwise** — identical

So this fix can only ever **increase** melee damage, and only ever **by exactly 1**. Corollaries worth
stating because they bound the blast radius:

- `r == 0` → `d·r = 0` → **no change**. But note carefully: **`r` is the *clamped effective*
  resistance, not the defender's DR stat.** `ReduceByArmor` folds Finesse's `extraDr` (+30, dude
  attacker, non-bypass) and F36's `ammoDrModifier` into `r` before the clamp, so a Finesse dude
  hitting a DR-0 defender has `r = 30` and **can** legitimately move. Applying this corollary to the
  raw defender stat would flag a correct delta as a stop condition.
- `d == 0` → no change.
- The worked example from the entry: `d = 7`, `r = 33` → ours `floor(469/100) = 4`, reference
  `7 − floor(231/100) = 5`.

**This is the prediction the fixture review is checked against**, with one further precision: the
invariant is **per damage computation, not per printed number.** `CritFailDamage`
(`CombatEngine.cs:1340-1347`) loops `roundCount` times through these same melee helpers — including
for a gun, since F15 passes a burst's rounds-spent count — so one self-damage figure can legitimately
move by up to `+N`. Likewise a moved damage value cascades into hp lines, deaths and XP.

So the rule is: **every individual post-armor damage computation moves 0 or +1**, re-derived
arithmetically. A damage computation that *decreases*, or moves by more than 1, means the
implementation is wrong — a stop condition, not a re-record.

## Scope

### In

`ReduceByArmor`'s final expression becomes the reference's subtract-form, matching the shape
`RangedMath.RollDamage` already uses (`CombatMath.cs:170-174`): subtract the threshold, return 0 if
nothing is left, then subtract `damage * resistance / 100`.

The ammo DR modifier added by F36 stays exactly where it is — an addend inside the same
`Math.Clamp(dr + ammoDrModifier, 0, 100)`. F42 changes only how the clamped resistance is *applied*.

### Out

- **Unifying the two near-duplicate reduction blocks.** `ReduceByArmor` and its counterpart in
  `RangedMath.RollDamage` will be doing the same arithmetic in the same shape after this change, and
  merging them is tempting. It is a refactor with its own risk on a change that already moves
  fixtures, and it would make the fixture delta impossible to attribute. **Record it as a rejected
  alternative and file it**, rather than leaving the observation in this spec — that is how F13 got
  lost for a release cycle.
- **F43** (the gun path's `Math.Max(mult, 1)` clamp). Separately filed, unrelated arithmetic.
- **The `Math.Max(raw - dt, 0)` vs `damage -= dt; if (damage <= 0) return 0;` difference.** These are
  equivalent for the value returned; adopting the reference's shape is fine if it falls out of the
  rewrite, but it is not itself the fix and must not be presented as one.

## What carries the proof

Hermetic tests, each confirmed failing pre-change **and for the right reason** — the pre-change value
must be exactly one lower, not merely different:

1. **The worked example.** `d = 7`, `r = 33` → 5, not 4. The case the entry is built on.
2. **A no-change case where `d·r` is a multiple of 100** — e.g. `d = 10`, `r = 50` → 5 either way.
   This pins that the fix does not shift values it should not touch.
3. **Zero DR is untouched** — the guarantee that most fixture attacks cannot move.
4. **The two paths now agree.** Same damage and same DR through the melee helper and through
   `RangedMath.RollDamage` produce the same number. This is the invariant the bug violated, and it is
   the test that would catch a future divergence in either direction.
5. **The F36 ammo DR modifier still lands correctly** through the new form — a non-neutral modifier
   changes the result in the expected direction, so the fix does not undo the wiring beneath it.

## Fixture expectations — stated before the run

**Fixtures are expected to move**, and only in one direction: melee and unarmed damage values increase
by exactly 1, on attacks against DR-bearing defenders. Downstream consequences of a +1 (a critter
dying a round earlier, a changed hp line, a knockdown that now triggers) are legitimate and expected;
a *damage* value that moves by anything other than +1 is not.

Every one of the six suites may move — combat and encounter certainly, since both contain melee
fights; the quest suite only if a scripted fight's outcome shifts.

Measure first, enumerate every failing fixture, confirm each delta fits the rule, and only then
record. The commit body must state the count of re-recorded fixtures and give at least one traced
example: which attack, which damage value, which defender's DR, and the arithmetic showing why +1 is
correct.

## Docs

`docs/BACKLOG.md`: F42 → shipped, with the derivation (`+1` iff `d·r % 100 != 0`), the measured
fixture count, and a traced example. File the rejected `ReduceByArmor`/`RangedMath` unification as its
own entry. Note the provenance: this was found by a reviewer reading the melee and ranged paths side
by side during F36 — not by any fixture failing, because both forms were self-consistently wrong in
the baseline.

## Definition of done

The subtract-form ported with its citation; five hermetic tests green and mutation-verified; every
re-recorded fixture's delta conforming to the `+1` rule; the traced example in the commit body; all
suites green afterwards; `docs/BACKLOG.md` reconciled with F42 shipped and the unification filed.

**Or:** a delta appeared that the `+1` rule cannot explain, and the work stopped for investigation.
