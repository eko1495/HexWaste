# Melee/unarmed weapon charges (F34) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Spend one weapon charge per attack for any weapon with an ammo capacity — not only guns — and refuse the attack when such a weapon is drained, matching the reference on both halves.

**Architecture:** Two one-condition changes in `CombatEngine`, each replacing an `isGun` test with a capacity test at the exact site the reference gates on `ammoGetCapacity(weapon) > 0`. No new host seam, no new types; `WeaponAmmo` is already weapon-class-agnostic.

**Tech Stack:** C# / .NET 10, xUnit. `src/Hexwaste.Formats` only — no MonoGame, no viewer change.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-08-22-melee-charges-design.md`. Read it; it carries the grounding and the proto census.
- **Verify every line number as the code stands now**, not as this plan or any earlier document records it. Citations in this repo have drifted before; a stale citation shipped into a source comment once already.
- Every ported line carries `// ported from fallout2-ce src/<file> <fn>()` per CLAUDE.md.
- Every new test must be **confirmed failing against the pre-change code, and for the right reason** — run it before the change and paste the failure.
- The reference is `reference/fallout2-ce` at `alexbatalov e97087b`. Do not port `// CE:` changes.
- **Expected outcome is byte-identical fixtures.** Do not re-record anything. If a golden moves, stop and report — that is a stop condition on this item, not a re-record.
- `isGun` stays in use at every touched site for the things it legitimately decides (range, knockback, animation, transcript text). Only the ammo condition changes.

---

### Task 1: Spending is gated on capacity, not weapon class

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` (three decrement sites, currently `:381-382`, `:3775`, `:3912`)
- Test: `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`

**Interfaces:**
- Consumes: `WeaponProtoStats.AmmoCapacity` (`ProtoDatabase.cs:73`), `ICombatHost.WeaponAmmo`.
- Produces: a private predicate on `CombatEngine` — `private static bool UsesCharges(ProtoInfo? weaponProto)` — used by Task 2 as well.

- [ ] **Step 1: Extend the melee-weapon test helper with a capacity**

In `CombatEngineTests.cs`, `MakeMeleeWeapon` currently hardcodes `AmmoCapacity` (the 14th positional argument) to 0. Add an optional parameter, leaving every existing call site unchanged:

```csharp
    private static (ProtoInfo Proto, MapObject Item) MakeMeleeWeapon(int ext, int minDmg = 1, int maxDmg = 6, int ap = 3, int dmgType = 0, int ammoCapacity = 0)
    {
        var w = new WeaponProtoStats(1, minDmg, maxDmg, dmgType, 1, 0, 0, 1, ap, 0, 0, 0, -1, ammoCapacity, 0);
        var proto = new ProtoInfo(8, 0, 0x01000000, 0, ext, 3, Weapon: w);
        var item = new MapObject { Id = 8, HexTile = 0, X = 0, Y = 0, Frame = 0, Rotation = 0, Fid = 0, Flags = 0, Pid = 8, Sid = -1 };
        return (proto, item);
    }
```

Note the item's `AmmoQuantity` default: check what `MapObject.AmmoQuantity` initialises to. If it is not `-1`, set `AmmoQuantity = -1` in the tests that need a full magazine rather than changing the helper's default, so no existing test's item changes shape.

- [ ] **Step 2: Write the failing tests**

Add to `CombatEngineTests.cs`. The cattle prod is the real case: `ext 0x01` (SWING, low nibble 1 → not a gun), capacity 20.

```csharp
    // F34: the reference spends one charge per attack for ANY weapon with an ammo capacity
    // (combat.cc:3900-3902 sets ammoQuantity = 1 for the non-ranged branch; combat.cc:5347-5350
    // deducts it), not only for guns. The five non-gun capacity weapons in the game are the
    // Ripper, Cattle Prod, Power Fist, Super Cattle Prod and Mega Power Fist.
    [Fact]
    public void MeleeWeaponWithAmmoCapacitySpendsOneChargePerAttack()
    {
        (ProtoInfo proto, MapObject item) = MakeMeleeWeapon(0x01, ammoCapacity: 20);
        item.AmmoQuantity = 20;
        var host = new FakeCombatHost { Equipped = (proto, item) };
        host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 100));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));

        Assert.True(new CombatEngine(host, new MinRng()).TryAttack(enemy));

        Assert.Equal(19, item.AmmoQuantity);
    }

    [Fact]
    public void MeleeWeaponWithoutAmmoCapacitySpendsNothing()
    {
        (ProtoInfo proto, MapObject item) = MakeMeleeWeapon(0x01);   // capacity 0 — a knife
        item.AmmoQuantity = 0;
        var host = new FakeCombatHost { Equipped = (proto, item) };
        host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 100));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));

        Assert.True(new CombatEngine(host, new MinRng()).TryAttack(enemy));

        Assert.Equal(0, item.AmmoQuantity);   // must NOT go to -1
    }

    [Fact]
    public void GunChargeSpendingIsUnchanged()
    {
        (ProtoInfo proto, MapObject item) = MakeGun();
        var host = new FakeCombatHost { Equipped = (proto, item) };
        host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 100));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));

        Assert.True(new CombatEngine(host, new MinRng()).TryAttack(enemy));

        Assert.Equal(11, item.AmmoQuantity);   // MakeGun's capacity is 12, item starts at -1 = full
    }
```

The `MinRng` / hp / skill values above are copied from the neighbouring tests in this file; if the attack does not land or the assert count is off, read a nearby passing test (e.g. the crippled-arms `CanAttack` helper around `:2795`) and match its setup rather than inventing new values. **Do not weaken an assertion to make a test pass** — if `TryAttack` returns false, find out why.

The NPC-side coverage (spec proof 5) is the fifth test. Both NPC decrement sites are inside NPC turn handling; find the entry point the existing NPC-attack tests in this file use (search for tests that drive an enemy turn) and write the equivalent assertion: an NPC swinging a capacity melee weapon spends a charge. If, after reading, there is **no existing seam that reaches those two sites hermetically**, do not invent one and do not skip the coverage silently — report it in your task report as a gap with what you found, and it will be decided before the task is accepted.

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test tests/Hexwaste.Formats.Tests --filter "FullyQualifiedName~MeleeWeaponWithAmmoCapacity|FullyQualifiedName~MeleeWeaponWithoutAmmoCapacity|FullyQualifiedName~GunChargeSpendingIsUnchanged"
```

Expected: `MeleeWeaponWithAmmoCapacitySpendsOneChargePerAttack` FAILS (`Assert.Equal() Failure: Expected 19, Actual 20`) — that is the bug. `MeleeWeaponWithoutAmmoCapacitySpendsNothing` and `GunChargeSpendingIsUnchanged` PASS already; they are inertness guards, not mutation-verified, and must be reported as such. Paste the actual output in your report.

- [ ] **Step 4: Add the predicate and use it at all three sites**

```csharp
    /// <summary>The reference gates ammo spending on the weapon's ammo capacity, never on its
    /// attack animation — a Cattle Prod or Power Fist drains Small Energy Cells exactly like a
    /// gun drains its magazine.
    /// ported from fallout2-ce src/combat.cc _compute_attack() (:3900-3902) and
    /// _combat_anim_finished() (:5347-5350), both gated on ammoGetCapacity(weapon) > 0.</summary>
    private static bool UsesCharges(ProtoInfo? weaponProto) => (weaponProto?.Weapon?.AmmoCapacity ?? 0) > 0;
```

Then at each of the three decrement sites replace the `if (isGun)` that guards **only** the `AmmoQuantity` assignment with `if (UsesCharges(weaponProto))`. At `:381-382` the `if (isGun)` guards exactly that one statement. At `:3775` and `:3912` **read the surrounding block first** — if the `isGun` there also guards other statements, narrow only the ammo assignment and leave the rest of the condition intact.

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test tests/Hexwaste.Formats.Tests --filter "FullyQualifiedName~CombatEngineTests"
```

Expected: all pass, including every pre-existing test in the class.

- [ ] **Step 6: Commit**

```bash
git add src/Hexwaste.Formats/Combat/CombatEngine.cs tests/Hexwaste.Formats.Tests/CombatEngineTests.cs
git commit -m "fix(combat): spend a charge per attack for any weapon with ammo capacity"
```

---

### Task 2: The dude's empty-weapon refusal is gated on capacity

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` (the `if (isGun)` block at `:318-333`; the stale comment at `:2295`)
- Test: `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`

**Interfaces:**
- Consumes: `UsesCharges` from Task 1.

- [ ] **Step 1: Write the failing test**

```csharp
    // F34: a drained weapon cannot attack — _combat_check_bad_shot returns COMBAT_BAD_SHOT_NO_AMMO
    // on `ammoGetCapacity(weapon) > 0 && ammoGetQuantity(weapon) == 0` (combat.cc:5678-5683),
    // with no weapon-class condition. Without this, spending charges would merely relocate the
    // infinite weapon rather than remove it.
    [Fact]
    public void DrainedMeleeWeaponCannotAttack()
    {
        (ProtoInfo proto, MapObject item) = MakeMeleeWeapon(0x01, ammoCapacity: 20);
        item.AmmoQuantity = 0;
        var host = new FakeCombatHost { Equipped = (proto, item) };
        host.SetDude(NewCritter(20100, hp: 30, ap: 10, skill: 100));
        MapObject enemy = host.AddCritter(NewCritter(HexGrid.TileInDirection(20100, 0), hp: 100));

        Assert.False(new CombatEngine(host, new MinRng()).TryAttack(enemy));
        Assert.Equal(0, item.AmmoQuantity);       // and no charge was spent on the refusal
    }
```

`FakeCombatHost.TryReload` decides whether the refusal is reached at all — the block attempts a reload before refusing. Read the fake's `TryReload` implementation and, if it succeeds by default, make it fail for this test (the fake has settable fields for this kind of thing; follow the existing pattern rather than adding a new one). Say in your report which way it behaves.

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test tests/Hexwaste.Formats.Tests --filter "FullyQualifiedName~DrainedMeleeWeaponCannotAttack"
```

Expected: FAIL — `Assert.False() Failure` (the attack goes through today), and with Task 1 in place `item.AmmoQuantity` will have gone to `-1`, which is the same bug seen from the other side.

- [ ] **Step 3: Move the gate**

Change the block's condition from `if (isGun)` to `if (UsesCharges(weaponProto))`, keeping its body — the reload attempts, the "Out of ammo." log and `OnWeaponOutOfAmmo` — as it is. **The line-of-fire trace that follows inside the same block must stay `isGun`-gated**: the reference gates it on `RANGED || THROW || weaponGetRange(hitMode) > 1` (`combat.cc:5685-5687`), a different condition. If the trace is inside the same `if (isGun)` braces, split the block so the ammo half takes the new condition and the trace half keeps `isGun`, and make sure `crittersInPath` is still assigned on every path.

Add the citation:

```csharp
        // ported from fallout2-ce src/combat.cc _combat_check_bad_shot() (:5678-5683): the empty-weapon
        // refusal is gated on ammoGetCapacity(weapon) > 0, NOT on weapon class — the same gate
        // CheckBadShot already uses on the NPC side. Hexwaste's dude-side auto-reload here is a
        // pre-existing deviation from _combat_attack_this (:5738-5747) and is left as-is.
```

- [ ] **Step 4: Correct the stale comment at `:2295`**

`CheckBadShot`'s comment claims "Hexwaste has no non-gun ammo-capacity weapon in practice, so this is a fidelity fix with no observed behavior change." The proto census in the spec disproves it: five ship. Replace that clause with the census — name the five (Ripper 116, Cattle Prod 160, Power Fist 235, Super Cattle Prod 399, Mega Power Fist 407, all drawing Small Energy Cell) — and keep the rest of the comment.

- [ ] **Step 5: Run the full Formats suite**

```bash
dotnet test tests/Hexwaste.Formats.Tests
```

Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/Hexwaste.Formats/Combat/CombatEngine.cs tests/Hexwaste.Formats.Tests/CombatEngineTests.cs
git commit -m "fix(combat): refuse the attack when a drained non-gun weapon has no charges"
```

---

### Task 3: Reconcile the backlog

**Files:**
- Modify: `docs/BACKLOG.md`

Do not start this task until the controller has run the golden suites and told you the result — the F34 entry states the fixture outcome, and stating it before it is measured is exactly the failure mode this project's re-record tier exists to prevent.

- [ ] **Step 1: F34 → shipped**

Move F34 to the shipped section in whatever form that section uses (read neighbouring shipped entries and match). It must record: the two commit SHAs; the census of five weapons with their PIDs and capacities; that both halves shipped (spending and refusal); and the measured fixture outcome the controller gives you.

- [ ] **Step 2: Unblock F31**

F31's entry says "Blocked because the layer beneath it is missing — see F34." Charges are now spent, so it is actionable. Rewrite that paragraph to say so, and record that the census confirms both hardcoded PIDs (399 Super Cattle Prod, 407 Mega Power Fist) ship and are in the five.

- [ ] **Step 3: File the dude-side auto-reload deviation**

A new entry, in the same format as its neighbours: Hexwaste auto-reloads the dude's empty weapon inside the attack path (`CombatEngine.cs`, the block Task 2 touched), attempting a reload and consuming AP for it; vanilla does not — `_combat_attack_this` prints "Out of ammo.", plays the out-of-ammo sfx and returns (`combat.cc:5738-5747`). Only the AI reloads (`combat_ai.cc:2732-2740`). It predates the CombatEngine extraction (`53c1df4`). Note that closing it would move fixtures wherever a gun runs dry, so it belongs to the re-record tier.

- [ ] **Step 4: File the ammo-damage-modifier gap**

A new entry: `attackComputeDamage` applies the loaded ammo's DR modifier, damage multiplier and divisor unconditionally (`combat.cc:4579-4586`); Hexwaste applies them only inside `if (isGun)` (`CombatEngine.cs:1109-1123`), so the melee branch passes none. All five non-gun capacity weapons load Small Energy Cells, so their damage is currently computed as if unloaded. Name it as F34's natural successor alongside F31.

- [ ] **Step 5: Verify every citation you just wrote**

Re-read each line number you cite **in the file as it now stands** — Tasks 1 and 2 shifted `CombatEngine.cs`. A citation that was right when this plan was written may be wrong now.

- [ ] **Step 6: Commit**

```bash
git add docs/BACKLOG.md
git commit -m "docs: F34 shipped, F31 unblocked, two successor gaps filed"
```
