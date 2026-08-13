# Explosion Ring-Spiral Ordering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port `_compute_explosion_on_extras`'s ring-spiral victim ordering, and use it in a damage-free counting mode to unblock the explosive `×(extras+1)` best-weapon factor.

**Architecture:** A new pure `ExplosionSpiral` enumerator in `Hexwaste.Formats` reproduces the reference's ring-and-rotation tile walk. `CombatEngine.Explode` consumes it for victim ordering (keeping the centre critter as primary), and `AiSwitchWeapon` consumes it in counting mode.

**Tech Stack:** C# / .NET (net10.0), xUnit, MonoGame DesktopGL (viewer only).

**Spec:** `docs/superpowers/specs/2026-08-13-explosion-spiral-design.md`

## Global Constraints

- **Port, never guess.** Every behavioural change carries a comment naming the reference source (e.g. `// ported from fallout2-ce src/combat.cc _compute_explosion_on_extras (:4022)`). If a detail cannot be confirmed in `reference/fallout2-ce`, stop and ask.
- **NO fixture is expected to move.** This is the inverse of the previous sub-project's contract. If any golden fixture moves, **STOP and report** — it means the victim set or ordering changed somewhere the design did not predict. Do not re-record anything.
- **Hermetic tests carry the entire proof**, because no committed fixture exercises the divergence (it needs two or more non-centre victims; no fixture has that). A test that cannot fail against the pre-change code proves nothing.
- No game assets may enter the repository.
- Golden scripts need a real display and game data (`FALLOUT2_DIR`, default `./game-data`). **Never run two golden scripts concurrently, never background one.** `quest-golden.sh` and `encounter-golden.sh` take many minutes each; run in the foreground, one at a time, and wait.
- Conventional commits ending with `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`.

## Reference facts established during planning (do not re-derive)

- Rotations are `NE=0, E=1, SE=2, SW=3, W=4, NW=5`, `ROTATION_COUNT=6` (`obj_types.h:8-14`).
- `HexGrid.TileInDirection(tile, rotation, distance = 1)` is Hexwaste's port of `tileGetTileInDirection` and is a **trusted primitive** — these tests pin the traversal rule, not that primitive.
- Explosion radii are engine globals, not proto fields: `gGrenadeExplosionRadius = 2`, `gRocketExplosionRadius = 3` (`item.cc:3376-3377`).
- `weaponIsGrenade` = damage type is EXPLOSION, PLASMA or EMP (`item.cc:1968-1972`).
- `weaponGetDamageRadius` (`item.cc:1975-1995`): ranged + `ANIM_FIRE_SINGLE` + explosion damage → rocket radius; throw + grenade → grenade radius; otherwise 0.
- Max targets is 6 (`explosionGetMaxTargets`, `item.cc:3574`).

## File Structure

| File | Responsibility |
|---|---|
| `src/Hexwaste.Formats/Combat/ExplosionSpiral.cs` | **Create.** Pure ring-and-rotation tile enumerator. |
| `src/Hexwaste.Formats/Combat/CombatEngine.cs` | Modify `Explode` (centre-first + spiral ordering) and `AiSwitchWeapon` (counting mode). |
| `src/Hexwaste.Formats/Combat/AiBestWeapon.cs` | Modify `AvgDamage` to take the explosive extras count. |
| `tests/Hexwaste.Formats.Tests/ExplosionSpiralTests.cs` | **Create.** Traversal-rule tests. |
| `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs` | Add the multi-victim ordering test. |
| `tests/Hexwaste.Formats.Tests/AiBestWeaponTests.cs` | Add the explosive-factor tests. |
| `docs/BACKLOG.md` | Record the five documented divergences and move the item out of the re-record tier. |

---

### Task 1: The `ExplosionSpiral` enumerator

**Files:**
- Create: `src/Hexwaste.Formats/Combat/ExplosionSpiral.cs`
- Test: `tests/Hexwaste.Formats.Tests/ExplosionSpiralTests.cs`

**Interfaces:**
- Consumes: `HexGrid.TileInDirection(int tile, int rotation, int distance = 1)`.
- Produces: `ExplosionSpiral.Tiles(int centerTile, int maxRadius) -> IEnumerable<int>` — tiles in reference order, excluding the centre, stopping after `maxRadius` rings.

- [ ] **Step 1: Write the failing traversal tests**

Create `tests/Hexwaste.Formats.Tests/ExplosionSpiralTests.cs`:

```csharp
using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Hex;

namespace Hexwaste.Formats.Tests;

/// <summary>The ring-and-rotation tile walk of _compute_explosion_on_extras (combat.cc:4022-4045).
/// Expectations are built from the REFERENCE'S RULES (open each ring at the NE neighbour with
/// rotation SE; rotate one step whenever ringTileIdx % radius == 0), not from the implementation's
/// own output. HexGrid.TileInDirection is a trusted pre-existing primitive.</summary>
public class ExplosionSpiralTests
{
    private const int NE = 0, E = 1, SE = 2, SW = 3, W = 4, NW = 5;
    private const int Center = 20100; // mid-grid, far from any edge

    [Fact]
    public void RadiusOneVisitsTheSixNeighboursStartingNorthEast()
    {
        // radius 1: ringTileIdx % 1 == 0 every step, so the rotation advances every step:
        // open at the NE neighbour, then step SE, SW, W, NW, NE (the sixth step, E, closes the ring).
        int t0 = HexGrid.TileInDirection(Center, NE);
        int t1 = HexGrid.TileInDirection(t0, SE);
        int t2 = HexGrid.TileInDirection(t1, SW);
        int t3 = HexGrid.TileInDirection(t2, W);
        int t4 = HexGrid.TileInDirection(t3, NW);
        int t5 = HexGrid.TileInDirection(t4, NE);

        Assert.Equal(new[] { t0, t1, t2, t3, t4, t5 }, ExplosionSpiral.Tiles(Center, 1).ToArray());
    }

    [Fact]
    public void RadiusOneRingClosesBackOnItsFirstTile()
    {
        // The sixth step (rotation E) must return to the ring's first tile, which is what ends the ring.
        int[] ring = ExplosionSpiral.Tiles(Center, 1).ToArray();
        Assert.Equal(6, ring.Length);
        Assert.Equal(ring[0], HexGrid.TileInDirection(ring[5], E));
    }

    [Fact]
    public void RadiusTwoRotatesEveryTwoStepsAndHasTwelveTiles()
    {
        // radius 2: rotate only when ringTileIdx % 2 == 0, i.e. two steps per direction —
        // SE,SE, SW,SW, W,W, NW,NW, NE,NE, E,E — 12 tiles, closing on the first.
        int[] all = ExplosionSpiral.Tiles(Center, 2).ToArray();
        int[] ring2 = all.Skip(6).ToArray();
        Assert.Equal(12, ring2.Length);

        int start = HexGrid.TileInDirection(HexGrid.TileInDirection(Center, NE), NE);
        Assert.Equal(start, ring2[0]);

        int[] dirs = [SE, SE, SW, SW, W, W, NW, NW, NE, NE, E];
        int tile = start;
        for (int i = 0; i < dirs.Length; i++)
        {
            tile = HexGrid.TileInDirection(tile, dirs[i]);
            Assert.Equal(tile, ring2[i + 1]);
        }
    }

    [Fact]
    public void RingsAreEmittedOutwardAndStopAtMaxRadius()
    {
        // 6 tiles at radius 1, 12 at radius 2, 18 at radius 3 (6*r per ring).
        Assert.Equal(6, ExplosionSpiral.Tiles(Center, 1).Count());
        Assert.Equal(18, ExplosionSpiral.Tiles(Center, 2).Count());
        Assert.Equal(36, ExplosionSpiral.Tiles(Center, 3).Count());
        Assert.Empty(ExplosionSpiral.Tiles(Center, 0));
    }

    [Fact]
    public void TheCentreTileIsNeverEnumerated()
    {
        // combat.cc:4033 opens at radius 1 — the blast tile itself is the primary defender's,
        // handled by the main attack path, never an "extra".
        Assert.DoesNotContain(Center, ExplosionSpiral.Tiles(Center, 3));
    }
}
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter FullyQualifiedName~ExplosionSpiralTests`
Expected: build error — `ExplosionSpiral` does not exist.

- [ ] **Step 3: Implement the enumerator**

Create `src/Hexwaste.Formats/Combat/ExplosionSpiral.cs`:

```csharp
using Hexwaste.Formats.Hex;

namespace Hexwaste.Formats.Combat;

/// <summary>
/// ported from fallout2-ce src/combat.cc _compute_explosion_on_extras (:4022-4045): the ring-by-ring
/// tile walk an explosion uses to find its victims. Each ring opens at the NE neighbour of the
/// previous ring's first tile with rotation SE, advances one tile per step, and rotates one step
/// further whenever <c>ringTileIdx % radius == 0</c> ("the larger the radius, the slower we rotate",
/// :4026); a ring ends when the walk returns to its first tile. The BLAST TILE ITSELF IS NEVER
/// ENUMERATED — the reference starts at radius 1, because the critter standing there is the primary
/// defender handled by the main attack path.
///
/// PURE: tile arithmetic only. The caller applies radius limits, line-of-sight, damage and caps.
/// </summary>
public static class ExplosionSpiral
{
    private const int RotationNe = 0, RotationSe = 2, RotationCount = 6;

    /// <summary>Tiles in reference order, outward from (but excluding) <paramref name="centerTile"/>,
    /// for rings 1..<paramref name="maxRadius"/>.</summary>
    public static IEnumerable<int> Tiles(int centerTile, int maxRadius)
    {
        if (maxRadius < 1)
            yield break;

        int ringFirstTile = centerTile;
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            // Each ring opens NE of the previous ring's first tile (combat.cc:4040).
            int tile = HexGrid.TileInDirection(ringFirstTile, RotationNe);
            if (tile == ringFirstTile)
                yield break; // walked off the grid edge — TileInDirection clamps, so stop rather than spin
            ringFirstTile = tile;
            int rotation = RotationSe;
            int ringTileIdx = 0;
            yield return tile;

            // 6*radius steps close a hex ring; the guard is a backstop for edge-clamped tiles.
            for (int step = 0; step < 6 * radius; step++)
            {
                int next = HexGrid.TileInDirection(tile, rotation);
                if (next == ringFirstTile || next == tile)
                    break; // ring closed (or clamped at the grid edge)
                tile = next;
                yield return tile;

                ringTileIdx++;
                if (ringTileIdx % radius == 0)
                {
                    rotation++;
                    if (rotation == RotationCount)
                        rotation = RotationNe;
                }
            }
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter FullyQualifiedName~ExplosionSpiralTests`
Expected: PASS, 5 tests.

If a ring count is off by one, re-derive the expectation from the reference's rules before touching the implementation — the tests encode the rule, and the rule is the thing being ported.

- [ ] **Step 5: Run the full unit suite**

Run: `dotnet build && dotnet test tests/Hexwaste.Formats.Tests`
Expected: build clean, all tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Hexwaste.Formats/Combat/ExplosionSpiral.cs tests/Hexwaste.Formats.Tests/ExplosionSpiralTests.cs
git commit -m "feat: port the explosion ring-spiral tile walk (_compute_explosion_on_extras)

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Order `Explode`'s victims by the spiral

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` (`Explode`, the victim-selection loop)
- Test: `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`

**Interfaces:**
- Consumes: `ExplosionSpiral.Tiles(int centerTile, int maxRadius)` from Task 1.
- Produces: no new public API; `Explode`'s signature is unchanged.

**Current behaviour to replace.** `Explode` currently selects victims with:

```csharp
        foreach (MapObject victim in victims
            .Where(c => HexGrid.Distance(c.HexTile, centerTile) <= radius)
            .OrderBy(c => HexGrid.Distance(c.HexTile, centerTile)))
```

Everything inside that loop — the LoS trace, the `raw − DT − DR%` damage, the difficulty modifier, knockback, the `hits >= maxTargets` cap, the kill handling — stays exactly as it is. Only the sequence of victims changes.

- [ ] **Step 1: Write the failing ordering test**

Add to `tests/Hexwaste.Formats.Tests/CombatEngineTests.cs`:

```csharp
    [Fact]
    public void ExplosionDamagesNonCentreVictimsInSpiralOrderNotDistanceOrder()
    {
        // ported from fallout2-ce src/combat.cc _compute_explosion_on_extras (:4022): victims are
        // collected ring-by-ring in rotation order, NOT nearest-first. Both victims here sit at
        // distance 1, so a distance sort keeps list order (west, then north-east) while the spiral
        // opens at the NE neighbour — so the order flips, and with it which victim draws first.
        const int center = 20100;
        const int NE = 0, W = 4;
        var host = new FakeCombatHost();
        host.SetDude(NewCritter(tile: 20900, hp: 30, ap: 10)); // far away, not a victim

        MapObject west = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(center, W), hp: 100));
        MapObject northEast = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(center, NE), hp: 100));

        var engine = new CombatEngine(host, new MinRng());
        engine.Explode(center, killer: null, minDamage: 10, maxDamage: 10, radius: 2);

        // The transcript records victims in the order they were damaged.
        var hitOrder = host.Transcripts
            .Where(t => t.StartsWith("explosion-hit:"))
            .ToList();
        Assert.Equal(2, hitOrder.Count);
        Assert.Contains($"@{northEast.HexTile}", hitOrder[0]); // spiral opens NE
        Assert.Contains($"@{west.HexTile}", hitOrder[1]);
    }
```

The critters are added west-first so that the pre-change distance sort — which is stable and therefore preserves insertion order for equal distances — yields west, then north-east. If `FakeCombatHost.CombatCritters` does not preserve insertion order, adjust the setup so the pre-change order genuinely differs from the spiral order, and say so in your report; a test that passes before the change proves nothing.

Add a second test pinning the centre-first rule, which is the substitution that keeps `arcaves-explode` correct:

```csharp
    [Fact]
    public void ACritterOnTheBlastTileIsDamagedBeforeAnySpiralVictim()
    {
        // DOCUMENTED DIVERGENCE (combat.cc:4033): the reference never enumerates the blast tile — its
        // occupant is the primary defender, damaged by the main attack path. Hexwaste's Explode has no
        // separate primary path, so the centre critter is damaged FIRST and the spiral orders the rest.
        // Without this, a strict spiral port would leave a critter standing on the blast tile unharmed.
        const int center = 20100;
        const int NE = 0;
        var host = new FakeCombatHost();
        host.SetDude(NewCritter(tile: 20900, hp: 30, ap: 10)); // far away, not a victim

        MapObject neighbour = host.AddCritter(NewCritter(tile: HexGrid.TileInDirection(center, NE), hp: 100));
        MapObject atCenter = host.AddCritter(NewCritter(tile: center, hp: 100));

        var engine = new CombatEngine(host, new MinRng());
        engine.Explode(center, killer: null, minDamage: 10, maxDamage: 10, radius: 2);

        var hitOrder = host.Transcripts.Where(t => t.StartsWith("explosion-hit:")).ToList();
        Assert.Equal(2, hitOrder.Count);
        Assert.Contains($"@{atCenter.HexTile}", hitOrder[0]); // centre victim first...
        Assert.Contains($"@{neighbour.HexTile}", hitOrder[1]); // ...then the spiral
    }
```

- [ ] **Step 2: Run it and confirm it fails**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter FullyQualifiedName~ExplosionDamagesNonCentreVictims`
Expected: **FAIL** — the west critter is damaged first under the distance sort.

- [ ] **Step 3: Replace the victim ordering**

In `src/Hexwaste.Formats/Combat/CombatEngine.cs`, inside `Explode`, replace the `foreach` header shown above with a centre-first-then-spiral sequence built before the loop:

```csharp
        // ported from fallout2-ce src/combat.cc _compute_explosion_on_extras (:4022): victims are
        // found ring-by-ring in rotation order, not nearest-first — the order decides which victim
        // draws its damage first. DOCUMENTED DIVERGENCE: the reference never examines the blast tile
        // (its occupant is the primary defender, damaged by the main attack path); Hexwaste's Explode
        // has no separate primary path, so the centre critter is damaged FIRST and the spiral orders
        // the rest.
        var byTile = new Dictionary<int, MapObject>();
        foreach (MapObject c in victims)
            byTile.TryAdd(c.HexTile, c);

        var ordered = new List<MapObject>();
        if (byTile.TryGetValue(centerTile, out MapObject? atCenter))
            ordered.Add(atCenter);
        foreach (int tile in ExplosionSpiral.Tiles(centerTile, radius))
            if (byTile.TryGetValue(tile, out MapObject? victimAtTile))
                ordered.Add(victimAtTile);

        foreach (MapObject victim in ordered)
```

Leave the loop body untouched.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter FullyQualifiedName~ExplosionDamagesNonCentreVictims`
Expected: PASS.

- [ ] **Step 5: Run the full unit suite**

Run: `dotnet build && dotnet test tests/Hexwaste.Formats.Tests`
Expected: build clean, all tests PASS.

- [ ] **Step 6: Verify no golden fixture moved**

Run each in the FOREGROUND, one at a time, waiting for each:

```bash
scripts/combat-golden.sh check
scripts/quest-golden.sh check
scripts/encounter-golden.sh check
```

Expected: **all PASS, nothing moved** — 16/16, 39/39, 188/188. The two explosion fixtures (`arcaves-explode`, `arcaves-throw-grenade`) are both centred on their only-or-first victim, so centre-first-then-spiral reproduces today's order.

**If any fixture moves, STOP and report.** Do not re-record. It means the victim set or ordering changed in a way the design did not predict, and that must be understood first.

- [ ] **Step 7: Commit**

```bash
git add src/Hexwaste.Formats/Combat/CombatEngine.cs tests/Hexwaste.Formats.Tests/CombatEngineTests.cs
git commit -m "feat: order explosion victims by the ring spiral (centre critter stays primary)

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Counting mode and the explosive best-weapon factor

**Files:**
- Modify: `src/Hexwaste.Formats/Combat/AiBestWeapon.cs` (`AvgDamage`)
- Modify: `src/Hexwaste.Formats/Combat/CombatEngine.cs` (`AiSwitchWeapon` candidate loop)
- Test: `tests/Hexwaste.Formats.Tests/AiBestWeaponTests.cs`
- Modify: `docs/BACKLOG.md`

**Interfaces:**
- Consumes: `ExplosionSpiral.Tiles` (Task 1); the existing `AiBestWeapon.AvgDamage(int minDamage, int maxDamage, int weaponPerk)`.
- Produces: `AiBestWeapon.AvgDamage(int minDamage, int maxDamage, int weaponPerk, int explosionExtras = 0)` — the optional fourth parameter keeps every existing call site compiling unchanged.

**Ordering matters.** The reference multiplies by `extrasLength + 1` **before** doubling for the weapon perk (`combat_ai.cc:1857-1870`). Preserve that order.

- [ ] **Step 1: Write the failing explosive-factor tests**

Add to `tests/Hexwaste.Formats.Tests/AiBestWeaponTests.cs`:

```csharp
    [Fact]
    public void AvgDamageMultipliesByTheExplosionExtrasCount()
    {
        // combat_ai.cc:1861 — avgDamage *= attack.extrasLength + 1, applied BEFORE the perk doubling.
        // (4+10)/2 = 7, two extra victims -> 7 * 3 = 21.
        Assert.Equal(21, AiBestWeapon.AvgDamage(minDamage: 4, maxDamage: 10, weaponPerk: -1, explosionExtras: 2));
    }

    [Fact]
    public void ExplosionExtrasApplyBeforeThePerkDoubling()
    {
        // 7 * (1+1) extras = 14, then *2 for the perk = 28. PerkAccurate == 59.
        Assert.Equal(28, AiBestWeapon.AvgDamage(minDamage: 4, maxDamage: 10, weaponPerk: 59, explosionExtras: 1));
    }

    [Fact]
    public void ZeroExtrasLeavesTheScoreUnchanged()
    {
        // The default keeps every pre-existing call site byte-identical.
        Assert.Equal(7, AiBestWeapon.AvgDamage(minDamage: 4, maxDamage: 10, weaponPerk: -1, explosionExtras: 0));
        Assert.Equal(7, AiBestWeapon.AvgDamage(minDamage: 4, maxDamage: 10, weaponPerk: -1));
    }
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter FullyQualifiedName~AiBestWeaponTests`
Expected: build error — `AvgDamage` has no `explosionExtras` parameter.

- [ ] **Step 3: Add the factor to `AvgDamage`**

In `src/Hexwaste.Formats/Combat/AiBestWeapon.cs`, replace `AvgDamage` with:

```csharp
    /// <summary>ported from fallout2-ce src/combat_ai.cc _ai_best_weapon (:1857-1870): the candidate's
    /// damage score — the (min+max)/2 midpoint (the SFALL avg-damage fix), multiplied by
    /// <paramref name="explosionExtras"/> + 1 for a blast weapon's extra victims (:1861), THEN doubled
    /// when the weapon carries a weapon perk (:1866). The order matters: extras first, perk second.</summary>
    public static int AvgDamage(int minDamage, int maxDamage, int weaponPerk, int explosionExtras = 0)
    {
        int avg = (minDamage + maxDamage) / 2;
        avg *= explosionExtras + 1;
        return weaponPerk != -1 ? avg * 2 : avg;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Hexwaste.Formats.Tests --filter FullyQualifiedName~AiBestWeaponTests`
Expected: PASS.

- [ ] **Step 5: Wire counting mode into the candidate loop**

In `src/Hexwaste.Formats/Combat/CombatEngine.cs`, add this private helper next to `AiSwitchWeapon`:

```csharp
    /// <summary>ported from fallout2-ce src/item.cc weaponGetDamageRadius (:1975): a ranged
    /// single-shot explosion weapon uses the rocket radius (3), a thrown grenade the grenade radius
    /// (2), everything else 0 (item.cc:3376-3377 — engine globals, not proto fields). weaponIsGrenade
    /// is damage type EXPLOSION / PLASMA / EMP (item.cc:1968).</summary>
    private static int WeaponDamageRadius(ProtoInfo proto, int attackType)
    {
        if (proto.Weapon is not { } w)
            return 0;
        bool blastDamage = w.DamageType is 6 /* EXPLOSION */ or 7 /* PLASMA */ or 8 /* EMP */;
        if (attackType == WeaponClass.AttackRanged && w.AnimationCode == 1 /* ANIM_FIRE_SINGLE */
            && w.DamageType == 6)
            return 3; // gRocketExplosionRadius
        if (attackType == WeaponClass.AttackThrow && blastDamage)
            return 2; // gGrenadeExplosionRadius
        return 0;
    }

    /// <summary>ported from fallout2-ce src/combat_ai.cc _ai_best_weapon (:1859-1862): how many EXTRA
    /// victims a blast at the defender's tile would catch — the engine calls
    /// _compute_explosion_on_extras with noDamage = 1 purely to read extrasLength. Counting only; no
    /// damage, no RNG. Returns 0 for a non-blast weapon or a null defender.</summary>
    private int ExplosionExtrasAt(ProtoInfo proto, int attackType, MapObject? defender)
    {
        int radius = WeaponDamageRadius(proto, attackType);
        if (radius <= 0 || defender is null)
            return 0;
        var occupied = new HashSet<int>();
        foreach (MapObject c in _host.CombatCritters)
            if (!c.IsDead)
                occupied.Add(c.HexTile);
        int extras = 0;
        foreach (int tile in ExplosionSpiral.Tiles(defender.HexTile, radius))
            if (occupied.Contains(tile) && ++extras == 6) // explosionGetMaxTargets (item.cc:3574)
                break;
        return extras;
    }
```

Then, in the `AiSwitchWeapon` candidate loop, pass the count into the score. Replace the `cand` construction:

```csharp
                var cand = new AiBestWeapon.Choice(attackType,
                    AiBestWeapon.AvgDamage(weapon.MinDamage, weapon.MaxDamage, weapon.WeaponPerk,
                        ExplosionExtrasAt(proto, attackType, defender)),
                    proto.Cost, IsFlare: proto.Pid == 79);
```

**`AiSwitchWeapon` currently has no `defender` in scope** — its overloads take `(enemy, ai/bestWeapon, minToHit, distance, currentItem)`. Thread one through: add a trailing `MapObject? defender = null` parameter to both overloads and pass the real target from the call sites that have one (`TryEnemyAction`'s chosen `defenderObj`, and `ProbeAiWeaponSwitch`'s `target`). The optional parameter keeps any call site without a target compiling and inert, matching the reference's own `defender != nullptr` guard (`combat_ai.cc:1859`).

**The factor must be demonstrably live, not merely wired.** This project has shipped an inert feature behind green suites before, so add a test proving the extras count actually changes the AI's choice: give a critter two candidate weapons whose base scores would pick weapon A, place two extra critters around the intended target so a blast weapon B catches them, and assert B is chosen — then confirm it fails when `ExplosionExtrasAt` is stubbed to return 0. Report that pre-stub/post-stub comparison. If the liveness test cannot be made to pass because no reachable call site supplies a defender, **stop and report that** rather than shipping the factor inert.

- [ ] **Step 6: Run the full unit suite**

Run: `dotnet build && dotnet test tests/Hexwaste.Formats.Tests`
Expected: build clean, all tests PASS.

- [ ] **Step 7: Verify no golden fixture moved**

Foreground, one at a time:

```bash
scripts/combat-golden.sh check
scripts/quest-golden.sh check
scripts/encounter-golden.sh check
```

Expected: all PASS, nothing moved. **If any fixture moves, STOP and report** — do not re-record.

- [ ] **Step 8: Record the divergences in the backlog**

In `docs/BACKLOG.md`, move the ring-spiral item out of the re-record tier into the shipped list, noting it landed **byte-identical** (no fixture moved) rather than as a re-record, and that the explosive `×(extras+1)` factor shipped with it. Record these five documented divergences:

- **Attacker backwash** (`combat.cc:4056-4060`) — not ported; note explicitly that `arcaves-throw-grenade` exercises it (the dude is caught in his own grenade blast and takes ordinary blast damage where the reference computes backwash).
- **Centre critter as primary** — the reference damages the blast-tile critter through the main attack path; Hexwaste's `Explode` hits it first inside the same loop.
- **Victim discovery** — the reference finds victims per-tile via `_obj_blocking_at`; Hexwaste maps critters by tile, which differs for multihex critters occupying several tiles.
- **Radius accessors** — grenade (2) vs rocket (3) radii are applied in `WeaponDamageRadius` for the AI count, but `Explode` still takes a single caller-supplied radius.
- **Damage computation** — the reference calls `attackComputeDamage`; Hexwaste keeps its simplified explosion formula (pre-existing).

The re-record tier should then list three remaining items: `_combat_safety_invalidate_weapon` + `_cai_retargetTileFromFriendlyFire`, `_ai_danger_source` + perception-based `PruneEscapedHostiles`, and the rating-gated retaliation if PR #16 has not yet merged.

- [ ] **Step 9: Commit**

```bash
git add src/Hexwaste.Formats/Combat/AiBestWeapon.cs src/Hexwaste.Formats/Combat/CombatEngine.cs \
        tests/Hexwaste.Formats.Tests/AiBestWeaponTests.cs docs/BACKLOG.md
git commit -m "feat: explosive x(extras+1) best-weapon factor via spiral counting mode

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
```

---

## Done when

`ExplosionSpiral` landed with traversal tests derived from the reference's rules; `Explode` ordering ported with the centre-first rule intact and proven by a test that fails against the distance sort; the explosive `×(extras+1)` factor wired via counting mode with the extras-before-perk order preserved; the five divergences recorded in `docs/BACKLOG.md`; all four suites green **with no fixture moved**.

**Or:** a fixture moved and the work stopped for investigation — a legitimate outcome, since it would mean the spec's analysis was wrong in a way worth understanding before anything is re-recorded.
