using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

public class ProgressionTests
{
    [Theory]
    [InlineData(2, 1000)]
    [InlineData(3, 3000)]
    [InlineData(4, 6000)]
    [InlineData(5, 10000)]
    [InlineData(10, 45000)]
    public void XpTableMatchesEngine(int level, int xp) =>
        Assert.Equal(xp, Progression.XpForLevel(level));

    [Fact]
    public void LevelForXpWalksThresholds()
    {
        Assert.Equal(1, Progression.LevelForXp(0));
        Assert.Equal(1, Progression.LevelForXp(999));
        Assert.Equal(2, Progression.LevelForXp(1000));
        Assert.Equal(3, Progression.LevelForXp(3000));
        Assert.Equal(3, Progression.LevelForXp(5999));
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    [InlineData(6, 5)]
    [InlineData(10, 7)]
    public void HpPerLevelIsHalfEndurancePlusTwo(int endurance, int gain) =>
        Assert.Equal(gain, Progression.HpPerLevel(endurance));
}

public class CombatRulesTests
{
    private static MapObject Critter(int tile, int team, bool dead = false)
    {
        var obj = new MapObject
        {
            Id = 1,
            HexTile = tile,
            X = 0,
            Y = 0,
            Frame = 0,
            Rotation = 0,
            Fid = 0x01000000,
            Flags = 0,
            Pid = 0x01000001,
            Sid = -1,
        };
        obj.Team = team;
        if (dead)
            obj.CombatResults = 0x80;
        return obj;
    }

    [Fact]
    public void SameTeamWithinSightJoins()
    {
        int dudeTile = 100 * Hex.HexGrid.Width + 100;
        MapObject hostile = Critter(dudeTile + 2, team: 4);

        Assert.True(CombatRules.ShouldJoin(Critter(dudeTile + 5, 4), [hostile], dudeTile));
        Assert.False(CombatRules.ShouldJoin(Critter(dudeTile + 5, 9), [hostile], dudeTile)); // other team
        Assert.False(CombatRules.ShouldJoin(Critter(dudeTile + 5, 4, dead: true), [hostile], dudeTile));
        // 30 hexes away — out of the 20-hex sight range
        Assert.False(CombatRules.ShouldJoin(
            Critter(Hex.HexGrid.TileInDirection(dudeTile, 1, 30), 4), [hostile], dudeTile));
    }
}

public class BarterMathTests
{
    [Fact]
    public void BuyPriceMatchesWorkedExample()
    {
        // Track-A worked example: stimpak (cost 175) vs an Average Merchant
        // (barter 80), dude barter 20, modifier 0:
        // 175 × 2 × (160+80)/(160+20) = 466.67 → 466.
        Assert.Equal(466, Combat.BarterMath.BuyPrice(175, 0, 80, 20));
        // With dude barter 35 (the report's table): 430.
        Assert.Equal(430, Combat.BarterMath.BuyPrice(175, 0, 80, 35));
        // Player goods always credit at face value.
        Assert.Equal(175, Combat.BarterMath.SellPrice(175));
        // A hostile modifier raises the demand.
        Assert.True(Combat.BarterMath.BuyPrice(100, 25, 80, 20) > Combat.BarterMath.BuyPrice(100, 0, 80, 20));
    }
}
