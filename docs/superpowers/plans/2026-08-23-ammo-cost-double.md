# Two-PID ammo-cost doubling (F31) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Spend two charges per attack for the Super Cattle Prod (399) and Mega Power Fist (407), matching `_item_w_compute_ammo_cost`.

**Architecture:** One small helper beside F34's `UsesCharges`, applied at the four sites that spend charges. No new host seam, no new types.

**Tech Stack:** C# / .NET 10, xUnit. `src/Hexwaste.Formats` only.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-23-ammo-cost-double-design.md`. Read it — in particular the quirk section, which is a deliberate porting decision, not an oversight.
- **Verify every line number and reference function name as the code stands now.** F34 shifted `CombatEngine.cs` substantially. This project shipped three citation errors in the last week; every one came from trusting a remembered number.
- Ported lines carry `// ported from fallout2-ce src/<file> <fn>()`.
- Reference is `reference/fallout2-ce` at `alexbatalov e97087b`.
- Every new test confirmed failing pre-change **and for the right reason** — paste the real output.
- **Do not touch anything under `tests/golden-*/`.** Expected outcome is byte-identical; the controller measures it.
- **Do not add a floor to the charge count and do not change the refusal.** Vanilla has neither. The odd-count drift to −1 is genuine vanilla behaviour and is being ported deliberately.

---

### Task 1: The cost helper, applied at every spend site

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs`
- Test: `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`

**Interfaces:**
- Consumes: `UsesCharges(ProtoInfo?)`, already on `CombatEngine` from F34.
- Produces: `private static int AmmoCost(ProtoInfo? weaponProto, int quantity)`.

- [ ] **Step 1: Locate the four spend sites**

Three are of the shape `weaponItem.AmmoQuantity = _host.WeaponAmmo(weaponProto, weaponItem) - 1` (dude, ally, enemy attack paths); the fourth is the burst path, `Math.Max(0, b.AmmoBefore - b.RoundsFired)`. Find them by their code — `grep -n "AmmoQuantity = " src/Hexwaste.Formats/Combat/CombatEngine.cs` — not by remembered line numbers. Confirm you have exactly four before changing any.

- [ ] **Step 2: Write the failing tests**

Model them on F34's charge tests in the same file (search for `MeleeWeaponWithAmmoCapacitySpendsOneChargePerAttack` and reuse its setup and helpers verbatim — do not invent new ones). The proto PID is what selects the behaviour, so the weapon protos in these tests must carry the real PIDs.

```csharp
    // F31: _item_w_compute_ammo_cost (item.cc:1947-1965) doubles the ammo cost for exactly two
    // hardcoded PIDs — 399 Super Cattle Prod and 407 Mega Power Fist (proto_types.h:177-178).
    [Theory]
    [InlineData(399)]   // Super Cattle Prod
    [InlineData(407)]   // Mega Power Fist
    public void TheTwoSpecialPidsSpendTwoChargesPerAttack(int pid) { … }

    [Fact]
    public void AnOrdinaryCapacityWeaponStillSpendsOne() { … }   // Cattle Prod, PID 160

    [Fact]
    public void AGunStillSpendsOne() { … }
```

Fill in the bodies from the F34 sibling tests. The Cattle Prod case matters: it is the weapon a wrong implementation (matching on name, capacity, or "is a prod") would sweep up by accident.

If `MakeMeleeWeapon` does not let you set the proto PID, add an optional parameter for it the way F34 added `ammoCapacity`, leaving existing call sites unchanged.

- [ ] **Step 3: Run them to verify they fail**

```bash
dotnet test tests/Hexwaste.Formats.Tests --filter "FullyQualifiedName~TheTwoSpecialPidsSpendTwoCharges|FullyQualifiedName~AnOrdinaryCapacityWeaponStillSpendsOne|FullyQualifiedName~AGunStillSpendsOne"
```

Expected: the `[Theory]` fails on both rows (`Expected 18, Actual 19` shape — one charge spent where two are due); the other two already pass and are inertness guards, which you must report as such rather than as mutation-verified.

- [ ] **Step 4: Add the helper and apply it**

```csharp
    // proto_types.h:177-178
    private const int PidSuperCattleProd = 399, PidMegaPowerFist = 407;

    /// <summary>The two hardcoded PIDs whose attacks cost double ammo. The reference applies this
    /// AFTER both the ranged and non-ranged branches (attackCompute, combat.cc:3905), so it would
    /// double a burst too — inert here, since neither PID is burst-capable (SWING / PUNCH).
    /// ported from fallout2-ce src/item.cc _item_w_compute_ammo_cost() (:1947-1965)</summary>
    private static int AmmoCost(ProtoInfo? weaponProto, int quantity) =>
        weaponProto?.Pid is PidSuperCattleProd or PidMegaPowerFist ? quantity * 2 : quantity;
```

Apply at all four sites: the three single-shot sites subtract `AmmoCost(weaponProto, 1)`; the burst site subtracts `AmmoCost(weaponProto, b.RoundsFired)`. Check the property name for the proto's PID before writing `weaponProto?.Pid` — read `ProtoInfo` and use whatever it is actually called.

**Do not** add a `Math.Max` floor at the three single-shot sites, and do not remove the existing one at the burst site.

- [ ] **Step 5: Run the full class**

```bash
dotnet test tests/Hexwaste.Formats.Tests --filter "FullyQualifiedName~CombatEngineTests"
```

Expected: all pass, including every pre-existing test.

- [ ] **Step 6: Commit**

```bash
git add src/Hexwaste.Formats/Combat/CombatEngine.cs tests/Hexwaste.Formats.Tests/CombatEngineTests.cs
git commit -m "fix(combat): double the ammo cost for the Super Cattle Prod and Mega Power Fist"
```

---

### Task 2: Reconcile the backlog

**Files:**
- Modify: `docs/BACKLOG.md`

Wait for the controller's golden-suite result before writing the fixture outcome.

- [ ] **Step 1: F31 → shipped**, in the format its neighbours use, with the commit SHA and the measured suite results. State plainly that the two PIDs are hardcoded in the reference and are not proto-driven.

- [ ] **Step 2: Record the odd-count quirk as ported-deliberately.** For these two weapons at an odd charge count, vanilla spends 2 from 1 and lands on −1; the refusal tests `== 0` (`combat.cc:5679-5683`) and `ammoSetQuantity` clamps only at the top (`item.cc:1421-1426`), so the weapon keeps attacking and drifts −1, −3, −5… Reloading resets it and both capacities are even, so it needs an odd starting count that is never reloaded. **Say explicitly that map data was not surveyed for such instances**, so the entry does not imply it cannot happen.

- [ ] **Step 3: Fix the duplicated clause in F34's header.** Its measurement clause appears twice — the suite list, then again with the parenthetical about `census`, `endgame` and `opening` not being run. Keep one coherent sentence carrying both facts.

- [ ] **Step 4: Verify every citation** in the file as it now stands, then commit:

```bash
git add docs/BACKLOG.md
git commit -m "docs: F31 shipped, with the odd-count drift recorded as deliberate"
```
