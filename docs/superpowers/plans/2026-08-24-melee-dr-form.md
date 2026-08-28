# Melee damage-resistance form (F42) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply damage resistance on the melee/unarmed path in the reference's subtract-form, so melee damage matches the engine and Hexwaste's own ranged path.

**Architecture:** One expression in `CombatMath.ReduceByArmor`, plus tests. The change moves fixtures; the controller measures and re-records.

**Tech Stack:** C# / .NET 10, xUnit. `src/Hexwaste.Formats` only.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-24-melee-dr-form-design.md`. Read the derivation — it is what the fixture review is checked against.
- **The fix can only ever increase melee damage, and only ever by exactly 1**, on attacks against a defender whose clamped effective resistance (`Math.Clamp(dr + ammoDrModifier, 0, 100)`) is non-zero. A value that decreases, changes by more than 1, or changes when that clamped effective resistance is zero means the implementation is wrong — but a defender with a zero DR *stat* can still move if Finesse or a non-neutral ammo DR modifier raises the effective resistance above zero. (Corrected after `ea956b9`, which fixed the same overstatement in the spec; the two documents previously disagreed.)
- **Verify every line number and reference function name as things stand now.** This project shipped five wrong citations in the last week, every one from trusting a remembered number.
- Ported lines carry `// ported from fallout2-ce src/<file> <fn>()`.
- Reference is `reference/fallout2-ce` at `alexbatalov e97087b`.
- Every new test confirmed failing pre-change **and for the right reason** — the old value must be exactly one lower, not merely different. Paste the real output.
- **Do not re-record or modify anything under `tests/golden-*/`.** The controller measures and records. If you notice a fixture would move, that is expected — say so, do not act on it.
- Do not unify `ReduceByArmor` with `RangedMath.RollDamage`'s equivalent block. It is deliberately out of scope and separately filed.

---

### Task 1: The subtract-form

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatMath.cs` (`ReduceByArmor`, currently ending around `:90-93`)
- Test: `tests/Hexwaste.Formats.Tests/CombatMathTests.cs`

- [ ] **Step 1: Read both paths and the reference**

Read `attackComputeDamage`'s threshold-and-resistance tail, `CombatMath.ReduceByArmor`, and `RangedMath.RollDamage`'s equivalent block. **The ranged block is your model — it already has the correct form.** Confirm the two Hexwaste forms differ as the spec states before changing anything.

- [ ] **Step 2: Write the failing tests**

Five, per the spec's proof list. Model the setup on the existing tests in `CombatMathTests.cs`.

```csharp
    // F42: the reference reduces post-threshold damage as `damage -= damage * dr / 100`
    // (combat.cc:4606-4610), not `damage * (100 - dr) / 100`. The forms differ under integer
    // truncation by exactly 1 whenever damage*dr is not a multiple of 100.
    [Fact]
    public void MeleeDamageResistanceUsesTheSubtractForm() { … }   // d=7, r=33 → 5, not 4

    [Fact]
    public void ADamageTimesResistanceMultipleOf100IsUnchanged() { … }   // d=10, r=50 → 5 either way

    [Fact]
    public void ZeroResistanceIsUntouched() { … }

    [Fact]
    public void MeleeAndRangedAgreeOnTheSameDamageAndResistance() { … }

    [Fact]
    public void TheAmmoDrModifierStillApplies() { … }
```

Fill in the bodies from the existing tests' idiom. Choose DR values that survive `CritterState`'s stat bounds — an earlier review found DR is capped at 90, so a test using a higher value would be asserting against a clamp rather than the arithmetic.

Assert exact values computed by hand from the reference's order.

- [ ] **Step 3: Run them to verify they fail**

```bash
dotnet test tests/Hexwaste.Formats.Tests --filter "FullyQualifiedName~CombatMathTests"
```

Expected: the worked-example test fails with the value exactly one lower (`Expected: 5, Actual: 4`) — **confirm it is one lower, not merely different**, since that is the signature of this specific bug. Report which of the five pass pre-change; those are guards, not mutation-verified.

- [ ] **Step 4: Implement**

Rewrite `ReduceByArmor`'s tail to the reference's form, citing `combat.cc:4606-4610`. The ammo DR modifier stays exactly where it is, inside the existing `Math.Clamp(dr + ammoDrModifier, 0, 100)` — this task changes only how the clamped resistance is applied.

- [ ] **Step 5: Run the full Formats suite**

```bash
dotnet test tests/Hexwaste.Formats.Tests
```

**Expect some pre-existing tests to fail** — unlike every recent task, this one changes real damage values, and any hermetic test that asserts a melee damage number against a DR-bearing target will move. For each failure: confirm the new value is exactly 1 higher than the old, and update it. **A failure that is not +1 is a stop condition** — report it rather than updating the test.

List every updated test and its old→new value in your report.

- [ ] **Step 6: Commit**

```bash
git add src/Hexwaste.Formats/Combat/CombatMath.cs tests/Hexwaste.Formats.Tests/CombatMathTests.cs
git commit -m "fix(combat): apply melee damage resistance in the reference's subtract-form"
```

---

### Task 2: Reconcile the backlog

**Files:**
- Modify: `docs/BACKLOG.md`

Wait for the controller's golden-suite result and re-record before writing the fixture outcome.

- [ ] **Step 1: F42 → shipped**, in the format its neighbours use, with the commit SHAs, the measured suite results, the count of re-recorded fixtures, and at least one traced example (which attack, which damage value, the defender's DR, and the arithmetic showing +1).

- [ ] **Step 2: Record the derivation** — the two forms differ by exactly `ceil(d·r/100) − floor(d·r/100)`, i.e. +1 iff `d·r` is not a multiple of 100, and 0 otherwise; the fix can only increase damage, never decrease it; attacks where the clamped effective resistance is zero cannot move — but that is `r == 0`, not a zero DR *stat*, since Finesse and a non-neutral ammo DR modifier fold into `r` before the clamp and can make it non-zero against a zero-DR defender. (Corrected after `ea956b9`.)

- [ ] **Step 3: Record the provenance.** This was found by a reviewer reading the melee and ranged paths side by side during F36 — not by any fixture failing, because both forms were self-consistently wrong in the baseline. That is the useful lesson: a golden suite cannot catch an error that was present when it was recorded.

- [ ] **Step 4: File the unification** of `ReduceByArmor` and `RangedMath.RollDamage`'s now-identical reduction block as its own entry, noting it was deliberately rejected here so the fixture delta stayed attributable.

- [ ] **Step 5: Verify every citation** in the file as it now stands, then commit:

```bash
git add docs/BACKLOG.md
git commit -m "docs: F42 shipped, with the +1 derivation and the re-recorded set"
```
