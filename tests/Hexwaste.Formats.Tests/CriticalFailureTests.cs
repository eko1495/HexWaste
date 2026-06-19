using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

public class CriticalFailureTests
{
    [Theory]
    [InlineData(-50, 0)]  // very high Luck pushes chance negative → mildest
    [InlineData(1, 0)]
    [InlineData(20, 0)]
    [InlineData(21, 1)]
    [InlineData(50, 1)]
    [InlineData(51, 2)]
    [InlineData(75, 2)]
    [InlineData(76, 3)]
    [InlineData(95, 3)]
    [InlineData(96, 4)]
    [InlineData(100, 4)]
    [InlineData(150, 4)]
    public void SeverityBucketsMatchTheEngine(int chance, int expected)
    {
        Assert.Equal(expected, CriticalFailure.Severity(chance));
    }

    [Fact]
    public void CritFailFlagsMatchTheVerifiedTable()
    {
        // Row 0 (unarmed/default): {0, LoseTurn, LoseTurn, HurtSelf|KnockedDown, CripRandom}.
        Assert.Equal(0, CriticalTables.CritFailFlags(0, 0));
        Assert.Equal(CriticalTables.DamLoseTurn, CriticalTables.CritFailFlags(0, 1));
        Assert.Equal(CriticalTables.DamHurtSelf | CriticalTables.DamKnockedDown, CriticalTables.CritFailFlags(0, 3));
        Assert.Equal(CriticalTables.DamCripRandom, CriticalTables.CritFailFlags(0, 4));

        // Row 2 (a ranged class): {0, LoseAmmo, Drop, RandomHit, Destroy}.
        Assert.Equal(CriticalTables.DamLoseAmmo, CriticalTables.CritFailFlags(2, 1));
        Assert.Equal(CriticalTables.DamDrop, CriticalTables.CritFailFlags(2, 2));
        Assert.Equal(CriticalTables.DamRandomHit, CriticalTables.CritFailFlags(2, 3));
        Assert.Equal(CriticalTables.DamDestroy, CriticalTables.CritFailFlags(2, 4));

        // Row 6 (the worst): col 4 = Explode|LoseTurn|OnFire.
        Assert.Equal(CriticalTables.DamExplode | CriticalTables.DamLoseTurn | CriticalTables.DamOnFire,
            CriticalTables.CritFailFlags(6, 4));
    }

    [Fact]
    public void CritFailFlagsClampOutOfRange()
    {
        // failureType -1 / >=7 → row 0; effect clamps to 0..4.
        Assert.Equal(CriticalTables.CritFailFlags(0, 1), CriticalTables.CritFailFlags(-1, 1));
        Assert.Equal(CriticalTables.CritFailFlags(0, 1), CriticalTables.CritFailFlags(99, 1));
        Assert.Equal(CriticalTables.CritFailFlags(3, 4), CriticalTables.CritFailFlags(3, 9));
        Assert.Equal(CriticalTables.CritFailFlags(3, 0), CriticalTables.CritFailFlags(3, -3));
    }

    [Fact]
    public void ResolveUsesTheSeverityDrawAndLuck()
    {
        // A fixed d100 of 60 with Luck 5 → chance 60 → severity 2. Row 2 effect 2 = Drop.
        Assert.Equal(CriticalTables.DamDrop, CriticalFailure.Resolve(2, luck: 5, new FixedRng(60)));
        // Luck 10 lowers chance by 25 → 35 → severity 1 → row 2 effect 1 = LoseAmmo.
        Assert.Equal(CriticalTables.DamLoseAmmo, CriticalFailure.Resolve(2, luck: 10, new FixedRng(60)));
        // Luck 1 raises chance by 20 → 80 → severity 3 → row 2 effect 3 = RandomHit.
        Assert.Equal(CriticalTables.DamRandomHit, CriticalFailure.Resolve(2, luck: 1, new FixedRng(60)));
    }

    [Fact]
    public void TableChecksumIsIntact()
    {
        // The generated _cf_table is now folded into the FNV-1a guard.
        Assert.Equal(CriticalTables.DataChecksum, CriticalTables.ComputeChecksum());
    }

    private sealed class FixedRng(int value) : ICombatRng
    {
        public int Next(int minInclusive, int maxExclusive) => Math.Clamp(value, minInclusive, maxExclusive - 1);
    }
}
