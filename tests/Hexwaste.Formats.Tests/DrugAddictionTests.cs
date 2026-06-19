using Hexwaste.Formats.Item;
using Hexwaste.Formats.Perks;

namespace Hexwaste.Formats.Tests;

public class DrugAddictionTests
{
    [Theory]
    [InlineData(106, 21)] // Nuka-Cola
    [InlineData(87, 22)]  // Buffout
    [InlineData(53, 23)]  // Mentats
    [InlineData(110, 24)] // Psycho
    [InlineData(48, 25)]  // RadAway
    [InlineData(124, 26)] // Beer  → alcohol
    [InlineData(125, 26)] // Booze → alcohol
    [InlineData(259, 294)] // Jet
    [InlineData(304, 293)] // Deck of Tragic Cards
    public void GvarForPidMapsTheNineAddictiveDrugs(int pid, int gvar)
    {
        Assert.Equal(gvar, DrugAddiction.GvarForPid(pid));
        Assert.True(DrugAddiction.IsAddictive(pid));
    }

    [Fact]
    public void GvarForPidIsMinusOneForANonDrug()
    {
        Assert.Equal(-1, DrugAddiction.GvarForPid(7));   // spear
        Assert.False(DrugAddiction.IsAddictive(7));
        Assert.Equal(-1, DrugAddiction.GvarForPid(40));  // stimpak heals but is NOT addictive
    }

    [Fact]
    public void RollIsInclusiveAndAppliesTraitPerkModifiers()
    {
        // Base chance 25: a 25 roll hits (inclusive), 26 misses.
        Assert.True(DrugAddiction.Roll(25, false, false, false, 25));
        Assert.False(DrugAddiction.Roll(25, false, false, false, 26));

        // Chem Reliant doubles (25→50): a 50 roll now hits.
        Assert.True(DrugAddiction.Roll(25, chemReliant: true, false, false, 50));
        // Chem Resistant halves (25→12): a 13 roll misses.
        Assert.False(DrugAddiction.Roll(25, false, chemResistant: true, false, 13));
        Assert.True(DrugAddiction.Roll(25, false, chemResistant: true, false, 12));
        // Flower Child halves (25→12) like Chem Resistant.
        Assert.True(DrugAddiction.Roll(25, false, false, flowerChild: true, 12));

        // The engine's order: ×2 then ÷2 then ÷2 (integer). Reliant+Resistant → 25*2/2 = 25.
        Assert.True(DrugAddiction.Roll(25, true, true, false, 25));
        Assert.False(DrugAddiction.Roll(25, true, true, false, 26));
        // Reliant+FlowerChild → 25*2/2 = 25.
        Assert.True(DrugAddiction.Roll(25, true, false, true, 25));
    }

    // The withdrawal STAT penalties, decoded from the checksum-guarded PerkTable.g.cs (the verbatim
    // perk.cc gPerkDescriptions port) — NOT from the grounding synthesis, which mis-decoded them.
    [Fact]
    public void WithdrawalPerkEffectsMatchTheVerifiedTableDecode()
    {
        // Nuka-Cola (53): no effect.
        Assert.Empty(PerkRules.MaxRankPerkEffect(53));

        // Buffout (54): ST-2, EN-2, AG-3.
        AssertEffect(54, (0, -2), (2, -2), (5, -3));
        // Mentats (55): IN-3, AG-2.
        AssertEffect(55, (4, -3), (5, -2));
        // Psycho (56): IN-2.
        AssertEffect(56, (4, -2));
        // RadAway (57): RadResist (stat 31) -20.
        AssertEffect(57, (31, -20));
        // Jet (70): MaxAP (stat 8) -1, plus ST-1, PE-1.
        AssertEffect(70, (8, -1), (0, -1), (1, -1));
        // Deck of Tragic Cards (71): PE-2, IN-1, LK-1.
        AssertEffect(71, (1, -2), (4, -1), (6, -1));
    }

    [Fact]
    public void MaxRankPerkEffectIsEmptyForARankBasedPerk()
    {
        // Toughness etc. are rank-based (MaxRank > 0) — applied via PerkRules.StatModifier, not this fold.
        Assert.Empty(PerkRules.MaxRankPerkEffect(PerkId.Educated));
        Assert.Empty(PerkRules.MaxRankPerkEffect(-1));
        Assert.Empty(PerkRules.MaxRankPerkEffect(9999));
    }

    private static void AssertEffect(int perkIndex, params (int Stat, int Delta)[] expected)
    {
        var actual = PerkRules.MaxRankPerkEffect(perkIndex).OrderBy(e => e.Stat).ToArray();
        var exp = expected.OrderBy(e => e.Stat).ToArray();
        Assert.Equal(exp, actual);
    }
}
