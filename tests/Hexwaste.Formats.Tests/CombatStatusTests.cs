using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

/// <summary>P14-M3: the crippled-leg move cost + blind to-hit/PE penalties (pure math).</summary>
public class CombatStatusTests
{
    private static MapObject Obj(int combatResults) => new()
    {
        Id = 1, HexTile = 100, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = 0x01000000, Flags = 0, Pid = 0x01000001, Sid = -1, CombatResults = combatResults,
    };

    [Theory]
    [InlineData(0, 1)]                                                    // intact
    [InlineData(CriticalTables.DamCripLegLeft, 4)]                       // one leg
    [InlineData(CriticalTables.DamCripLegRight, 4)]
    [InlineData(CriticalTables.DamCripLegLeft | CriticalTables.DamCripLegRight, 8)] // both
    [InlineData(CriticalTables.DamCripArmLeft, 1)]                       // arms don't slow legs
    public void MovePointCostMatchesCrippledLegs(int results, int cost) =>
        Assert.Equal(cost, CritterState.MovePointCost(results));

    [Fact]
    public void BlindLowersPerceptionByFive()
    {
        int[] b = new int[35]; b[CritterStat.Perception] = 7;
        var proto = new CritterProtoStats(0, 0, 0, b, new int[35], new int[18], 0, 0, 0, 0);
        Assert.Equal(7, new CritterState(Obj(0), proto).Perception);
        Assert.Equal(2, new CritterState(Obj(CriticalTables.DamBlind), proto).Perception);
        Assert.True(new CritterState(Obj(CriticalTables.DamBlind), proto).Blind);
    }

    [Fact]
    public void BlindTriplesTheRangedDistancePenalty()
    {
        // At range (positive distance modifier = dist > 2*PE), a blind shooter
        // takes ×12 vs ×4.
        const int skill = 100, dist = 20, pe = 5, ac = 0;
        int sighted = RangedMath.ToHitChance(skill, dist, pe, attackerIsDude: false, ac, 0, 0, 5, 0, attackerBlind: false);
        int blind = RangedMath.ToHitChance(skill, dist, pe, attackerIsDude: false, ac, 0, 0, 5, 0, attackerBlind: true);
        Assert.True(blind < sighted, $"blind {blind} should be far below sighted {sighted}");
    }

    [Fact]
    public void DoctorHealsAllCrippledLimbsAtHighSkill()
    {
        int crippled = CriticalTables.DamBlind | CriticalTables.DamCripArmLeft
            | CriticalTables.DamCripLegRight | CriticalTables.DamKnockedOut; // KO is NOT healable
        int after = SkillHealing.HealLimbs(crippled, 100, new AlwaysHits(), out List<string> healed);

        Assert.Equal(["eyes", "left arm", "right leg"], healed); // gHealableDamageFlags order
        Assert.False(SkillHealing.IsCrippled(after));
        Assert.True((after & CriticalTables.DamKnockedOut) != 0); // knockout is left untouched
    }

    [Fact]
    public void DoctorFailingTheRollLeavesTheLimbCrippled()
    {
        int crippled = CriticalTables.DamCripLegLeft;
        int after = SkillHealing.HealLimbs(crippled, 0, new AlwaysHits(), out List<string> healed);
        // skill 0 → d10s of 1 are never <= 0 → nothing mended.
        Assert.Empty(healed);
        Assert.Equal(crippled, after);
    }

    [Fact]
    public void HealLimbsOnlyRollsForPresentInjuries()
    {
        // No crippled flags → no rolls drawn, no heals.
        int after = SkillHealing.HealLimbs(0, 100, new AlwaysHits(), out List<string> healed);
        Assert.Empty(healed);
        Assert.Equal(0, after);
    }

    private sealed class AlwaysHits : ICombatRng
    {
        public int Next(int minInclusive, int maxExclusive) => minInclusive; // d100 -> 1
    }

    [Fact]
    public void BlindDoesNotPenalizeTheCloseRangeBonus()
    {
        // Point-blank (negative distance modifier = a bonus): blind keeps the ×4, so
        // the chance is unchanged by blindness at distance 1 (only the flat -25 and
        // PE-5, applied by the engine, bite there).
        int sighted = RangedMath.ToHitChance(100, 1, 8, attackerIsDude: false, 0, 0, 0, 5, 0, attackerBlind: false);
        int blind = RangedMath.ToHitChance(100, 1, 8, attackerIsDude: false, 0, 0, 0, 5, 0, attackerBlind: true);
        Assert.Equal(sighted, blind);
    }
}
