using Hexwaste.Formats.Item;

namespace Hexwaste.Formats.Tests;

public class SkillBooksTests
{
    [Theory]
    [InlineData(73, 12, 802)]  // Big Book of Science → Science
    [InlineData(76, 13, 803)]  // Dean's Electronics  → Repair
    [InlineData(80, 6, 804)]   // First Aid Book      → First Aid
    [InlineData(86, 17, 806)]  // Scout Handbook      → Outdoorsman
    [InlineData(102, 0, 805)]  // Guns and Bullets    → Small Guns
    public void TryGetMapsTheFiveVanillaBooks(int pid, int skill, int msgId)
    {
        Assert.True(SkillBooks.TryGet(pid, out int s, out int m));
        Assert.Equal(skill, s);
        Assert.Equal(msgId, m);
    }

    [Fact]
    public void TryGetIsFalseForANonBook()
    {
        Assert.False(SkillBooks.TryGet(7, out _, out _));   // spear
        Assert.False(SkillBooks.TryGet(87, out _, out _));  // Buffout (a drug, not a book)
    }

    [Theory]
    [InlineData(0, 10)]   // skill 0 → +10 (the max)
    [InlineData(35, 6)]   // (100-35)/10 = 6
    [InlineData(50, 5)]
    [InlineData(80, 2)]
    [InlineData(90, 1)]
    [InlineData(91, 0)]   // 9/10 → 0, the diminishing returns floor
    [InlineData(99, 0)]
    [InlineData(100, 0)]  // the cap — a maxed skill learns nothing
    [InlineData(120, 0)]  // over-cap stays 0 (no negative)
    public void IncreaseFollowsTheDiminishingCurveAndCaps(int effective, int expected)
    {
        Assert.Equal(expected, SkillBooks.Increase(effective, hasComprehension: false));
    }

    [Fact]
    public void ComprehensionAddsFiftyPercentFloored()
    {
        // 6 → 150*6/100 = 9; 5 → 7 (7.5 floored); 10 → 15.
        Assert.Equal(9, SkillBooks.Increase(35, hasComprehension: true));
        Assert.Equal(7, SkillBooks.Increase(50, hasComprehension: true));
        Assert.Equal(15, SkillBooks.Increase(0, hasComprehension: true));
        // Past the cap, Comprehension still grants nothing (the <=0 gate is checked first).
        Assert.Equal(0, SkillBooks.Increase(100, hasComprehension: true));
    }

    [Theory]
    [InlineData(10, 3600)]    // INT 10 → 1 hour
    [InlineData(5, 21600)]    // INT 5  → 6 hours
    [InlineData(1, 36000)]    // INT 1  → 10 hours
    public void ReadSecondsShrinksWithIntelligence(int intelligence, int seconds)
    {
        Assert.Equal(seconds, SkillBooks.ReadSeconds(intelligence));
    }
}
