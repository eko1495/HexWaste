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
    public void AdrenalineRushAddsStrengthBelowHalfHp()
    {
        // P70: Adrenaline Rush — +1 ST while current HP < max/2 (stat.cc:256), gated on the perk.
        int[] b = new int[35];
        b[CritterStat.Strength] = 5;
        b[CritterStat.MaximumHitPoints] = 30;
        var proto = new CritterProtoStats(0, 0, 0, b, new int[35], new int[18], 0, 0, 0, 0);
        var ranks = new int[Hexwaste.Formats.Perks.PerkTable.Count];
        ranks[Hexwaste.Formats.Perks.PerkId.AdrenalineRush] = 1;

        MapObject Hurt(int hp) => new()
        {
            Id = 1, HexTile = 100, X = 0, Y = 0, Frame = 0, Rotation = 0,
            Fid = 0x01000000, Flags = 0, Pid = 0x01000001, Sid = -1, CurrentHp = hp,
        };

        // hurt (HP < 15) → +1 ST; at/above half → no bonus.
        Assert.Equal(6, new CritterState(Hurt(14), proto, perkRanks: ranks).Stat(CritterStat.Strength));
        Assert.Equal(5, new CritterState(Hurt(15), proto, perkRanks: ranks).Stat(CritterStat.Strength));
        // No perk → never any bonus, even when hurt (inert by default).
        Assert.Equal(5, new CritterState(Hurt(14), proto).Stat(CritterStat.Strength));
    }

    private static MapObject Critter(int hp = 30) => new()
    {
        Id = 1, HexTile = 100, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = 0x01000000, Flags = 0, Pid = 0x01000001, Sid = -1, CurrentHp = hp,
    };

    [Fact]
    public void GainSpecialPerksAddOneToTheRightPrimary()
    {
        // P74-M1: Gain STR/PER/.../LCK (perks 84..90) add +1 to that primary (stat.cc:252-309).
        int[] b = new int[35];
        for (int s = CritterStat.Strength; s <= CritterStat.Luck; s++) b[s] = 5;
        var proto = new CritterProtoStats(0, 0, 0, b, new int[35], new int[18], 0, 0, 0, 0);
        var ranks = new int[Hexwaste.Formats.Perks.PerkTable.Count];
        ranks[Hexwaste.Formats.Perks.PerkId.GainPerception] = 1;

        var cs = new CritterState(Critter(), proto, perkRanks: ranks);
        Assert.Equal(6, cs.Stat(CritterStat.Perception));   // +1
        Assert.Equal(5, cs.Stat(CritterStat.Strength));     // a different primary is unaffected
        Assert.Equal(5, new CritterState(Critter(), proto).Stat(CritterStat.Perception)); // no perk → no bonus
    }

    [Fact]
    public void EffectiveStatClampsToTheEngineBounds()
    {
        // P74-M1: critterGetStat clamps to gStatDescriptions (stat.cc:369). A maxed primary + Gain-X
        // can't exceed 10, and a 0 primary clamps UP to the min 1.
        int[] b = new int[35]; b[CritterStat.Strength] = 10; // already at max
        var maxed = new CritterProtoStats(0, 0, 0, b, new int[35], new int[18], 0, 0, 0, 0);
        var ranks = new int[Hexwaste.Formats.Perks.PerkTable.Count];
        ranks[Hexwaste.Formats.Perks.PerkId.GainStrength] = 1;       // would push ST to 11
        Assert.Equal(10, new CritterState(Critter(), maxed, perkRanks: ranks).Stat(CritterStat.Strength));

        var zero = new CritterProtoStats(0, 0, 0, new int[35], new int[35], new int[18], 0, 0, 0, 0);
        Assert.Equal(1, new CritterState(Critter(), zero).Stat(CritterStat.Perception)); // 0 → min 1
    }

    [Theory]
    [InlineData(CritterStat.Strength, 1, 10)]
    [InlineData(CritterStat.MaximumActionPoints, 1, 99)]
    [InlineData(CritterStat.DamageResistance, 0, 90)]
    [InlineData(CritterStat.BetterCriticals, -60, 100)]
    public void StatBoundsMatchTheEngineTable(int stat, int min, int max) =>
        Assert.Equal((min, max), StatBounds.For(stat));

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
