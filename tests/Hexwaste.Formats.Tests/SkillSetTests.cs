using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

public class SkillSetTests
{
    [Theory]
    [InlineData(0, 1)] [InlineData(100, 1)] [InlineData(101, 2)] [InlineData(125, 2)]
    [InlineData(126, 3)] [InlineData(150, 3)] [InlineData(151, 4)] [InlineData(175, 4)]
    [InlineData(176, 5)] [InlineData(200, 5)] [InlineData(201, 6)] [InlineData(290, 6)]
    public void CostRampMatchesEngine(int effectiveValue, int cost) =>
        Assert.Equal(cost, SkillSet.Cost(effectiveValue));

    [Theory]
    [InlineData(1, 7)] [InlineData(5, 15)] [InlineData(10, 25)]
    public void PointsPerLevelIsFivePlusTwiceInt(int intelligence, int points) =>
        Assert.Equal(points, SkillSet.PointsPerLevel(intelligence));

    [Fact]
    public void PointsPerLevelAddsEducatedSkilledAndGiftedPenalty()
    {
        // P29-M2 (character_editor.cc:5686): 5 + 2·IN + 2·Educated + 5·Skilled − (Gifted ? 5).
        Assert.Equal(15, SkillSet.PointsPerLevel(5));                                      // base: 5 + 10
        Assert.Equal(21, SkillSet.PointsPerLevel(5, educatedRank: 3));                     // +2·3 Educated
        Assert.Equal(20, SkillSet.PointsPerLevel(5, skilled: true));                       // +5 Skilled
        Assert.Equal(10, SkillSet.PointsPerLevel(5, gifted: true));                        // −5 Gifted
        // Gifted IN 6 (base 5 + the +1 the caller folds in): 5 + 12 − 5 = 12.
        Assert.Equal(12, SkillSet.PointsPerLevel(6, gifted: true));
        // Everything together: IN 6, Educated 2, Skilled, Gifted = 5 + 12 + 4 + 5 − 5 = 21.
        Assert.Equal(21, SkillSet.PointsPerLevel(6, educatedRank: 2, skilled: true, gifted: true));
    }

    [Fact]
    public void ValueAppliesTagBonusAndClamps()
    {
        int[] baseStats = new int[35];
        int[] bonus = new int[35];
        int[] skills = new int[18];
        baseStats[5] = 7; // AGILITY → Small Guns = 5 + 4*7 = 33

        Assert.Equal(33, SkillSet.Value(baseStats, bonus, skills, null, 0));          // NPC
        Assert.Equal(33, SkillSet.Value(baseStats, bonus, skills, [4, 5], 0));        // dude, not tagged
        Assert.Equal(53, SkillSet.Value(baseStats, bonus, skills, [0, 4], 0));        // dude, tagged: +20

        // Spent points on a tagged skill count double; clamp at 300.
        skills[0] = 100;
        Assert.Equal(Math.Min(33 + 100 + 100 + 20, 300), SkillSet.Value(baseStats, bonus, skills, [0], 0));
        skills[0] = 300;
        Assert.Equal(300, SkillSet.Value(baseStats, bonus, skills, [0], 0));
    }
}
