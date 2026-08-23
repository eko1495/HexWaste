# Melee-branch ammo modifiers (F36) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the melee/unarmed damage and to-hit paths consult the loaded ammo's modifiers, as the reference does with no attack-type gate.

**Architecture:** Optional parameters on two `CombatMath` helpers plus the melee to-hit expression, defaulting to neutral values so every existing call site is unchanged by construction; the `CombatEngine` melee branch passes `LoadedAmmo`'s values the way the gun branch already does.

**Tech Stack:** C# / .NET 10, xUnit. `src/Hexwaste.Formats` only.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-23-melee-ammo-mods-design.md`. Read it, especially the RNG constraint and the census that proves this is inert on shipped data.
- **The single `rng.Next` draw in each melee damage helper must not move, multiply or disappear.** All new work is arithmetic *after* the draw. If a combat fixture moves, the draw order changed — that is a regression and a stop condition, not a re-record.
- **Verify every line number and reference function name as things stand now.** This project shipped four wrong citations in the last week, every one from trusting a remembered number.
- Ported lines carry `// ported from fallout2-ce src/<file> <fn>()`.
- Reference is `reference/fallout2-ce` at `alexbatalov e97087b`.
- Every new test confirmed failing pre-change **and for the right reason** — paste the real output.
- **Do not touch anything under `tests/golden-*/`.** Expected outcome is byte-identical; the controller measures it.
- Do not change the gun branch. It already does all four reads correctly.

---

### Task 1: Thread the ammo modifiers through the melee paths

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatMath.cs` (`RollDamage`, `RollWeaponDamage`, `ReduceByArmor`, and the melee to-hit expression near the top of the class)
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` (the `else` branch of the `if (isGun)` damage split, and the melee to-hit call site)
- Test: `tests/Hexwaste.Formats.Tests/CombatMathTests.cs` (and `CombatEngineTests.cs` if an engine-level test is warranted)

- [ ] **Step 1: Read the reference and both Hexwaste paths**

Read `attackComputeDamage`'s ammo block and `attackDetermineToHit`'s armor-class block, and confirm the operation order stated in the spec. Then read `CombatMath.RollDamage`, `RollWeaponDamage`, `ReduceByArmor`, the melee to-hit expression, and the gun branch in `CombatEngine` that already passes these values — **the gun branch is your model; match its shape rather than inventing one.**

- [ ] **Step 2: Write the failing tests**

Five, per the spec's proof list: multiplier, divisor (including the divisor-of-0 guard), DR modifier with clamping at both ends, AC modifier with its `>= 0` clamp, and a neutral-values inertness test. Use synthetic ammo values — shipped data is all-neutral, so a test using real values would assert nothing.

Assert **exact** expected damage computed by hand from the reference's operation order, not inequalities. A test asserting "more than before" would pass against an implementation that applies the multiplier in the wrong place.

Model the setup on the existing tests in `CombatMathTests.cs`; use a deterministic RNG the way they do.

- [ ] **Step 3: Run them to verify they fail**

```bash
dotnet test tests/Hexwaste.Formats.Tests --filter "FullyQualifiedName~CombatMathTests"
```

Expected: the four behavioural tests fail (the parameters do not exist yet — if they fail to compile, add the parameters first with neutral defaults, then re-run so you see a real assertion failure rather than a build error). The inertness test passes throughout and must be reported as a guard, not as mutation-verified.

- [ ] **Step 4: Implement**

Add the parameters with neutral defaults (`ammoDrModifier = 0`, `ammoDamageMultiplier = 1`, `ammoDamageDivisor = 1`; `ammoAcModifier = 0` for to-hit), apply them in the reference's order, and pass them from the `CombatEngine` melee branch via `_host.LoadedAmmo(...)`. Cite `combat.cc:4579-4587` for the damage half and `combat.cc:4428-4432` for the to-hit half.

Guard the divisor exactly as the reference does — divide only when it is non-zero.

- [ ] **Step 5: Run the full Formats suite**

```bash
dotnet test tests/Hexwaste.Formats.Tests
```

Expected: all pass. **Any pre-existing test that changes value is a stop condition** — it would mean an existing call site was not neutral after all. Report it rather than updating the test.

- [ ] **Step 6: Commit**

```bash
git add src/Hexwaste.Formats tests/Hexwaste.Formats.Tests
git commit -m "fix(combat): consult the loaded ammo's modifiers on the melee path too"
```

---

### Task 2: Reconcile the backlog

**Files:**
- Modify: `docs/BACKLOG.md`

Wait for the controller's golden-suite result before writing the fixture outcome.

- [ ] **Step 1: F36 → shipped**, in the format its neighbours use, with the commit SHA and the measured suite results.

- [ ] **Step 2: Correct the entry's framing.** It was filed as "damage-affecting" and "re-record tier". The census shows otherwise: exactly five non-gun weapons reference ammo at all, all five load Small Energy Cell, and its AC/DR/multiplier/divisor are all neutral — so the change is provably inert on shipped data and the value is structural. Say that plainly, including that the item was filed on an assumption that measurement did not support.

- [ ] **Step 3: Record that the mechanism is real.** 17 ammo protos carry non-neutral modifiers (e.g. 10mm JHP: DR +25, multiplier 2; 5mm AP: DR −35, divisor 2; 2mm EC: AC −30, DR −20, multiplier 3, divisor 2). They are all gun ammo today, which is why this is inert — but the wiring now matches the reference for any future non-gun weapon.

- [ ] **Step 4: Verify every citation** in the file as it now stands, then commit:

```bash
git add docs/BACKLOG.md
git commit -m "docs: F36 shipped, with its damage-affecting framing corrected"
```
