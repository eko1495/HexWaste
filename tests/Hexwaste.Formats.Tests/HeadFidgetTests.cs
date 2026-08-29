using Hexwaste.Formats.Art;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// F46: the talking-head fidget roll, ported from fallout2-ce
/// src/game_dialog.cc _gdSetupFidget() (:2505-2529). Hexwaste previously hardcoded
/// the fidget nibble to 1, which is why F5's sway arithmetic could never be observed:
/// every head carrying a nonzero frame X offset is a fidget 2 or 3.
/// </summary>
public class HeadFidgetTests
{
    [Fact]
    public void OneVariantAlwaysPicksTheOnlyOne()
    {
        for (int chance = 1; chance <= 150; chance++)
            Assert.Equal(1, HeadFidget.Roll(1, chance));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(67, 1)]   // last value below the engine's 68 split
    [InlineData(68, 2)]   // the split itself belongs to fidget 2
    [InlineData(100, 2)]
    public void TwoVariantsSplitAt68(int chance, int expected) =>
        Assert.Equal(expected, HeadFidget.Roll(2, chance));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(51, 1)]
    [InlineData(52, 2)]   // first value at or past the 52 split
    [InlineData(76, 2)]
    [InlineData(77, 3)]   // first value at or past the 77 split
    [InlineData(100, 3)]
    public void ThreeVariantsSplitAt52And77(int chance, int expected) =>
        Assert.Equal(expected, HeadFidget.Roll(3, chance));

    [Fact]
    public void AnUnsupportedCountFallsThroughToTheReferencesInitialiser()
    {
        // _gdSetupFidget starts `int fidget = fidgetCount;` and the switch has no default,
        // so 0 (head 0 "reser", whose heads.lst line carries no counts) stays 0.
        Assert.Equal(0, HeadFidget.Roll(0, 50));
        Assert.Equal(4, HeadFidget.Roll(4, 50));
    }

    [Fact]
    public void OnlyTheThreeVariantCaseZeroesTheIdleAccumulator()
    {
        // game_dialog.cc:2520 puts the reset inside `case 3:` alone.
        Assert.False(HeadFidget.ResetsIdleAccumulator(1));
        Assert.False(HeadFidget.ResetsIdleAccumulator(2));
        Assert.True(HeadFidget.ResetsIdleAccumulator(3));
    }

    [Fact]
    public void TheIdleTermIsIntegerHalvedSoItOnlyBitesAfterTwoSeconds()
    {
        Assert.Equal(50, HeadFidget.Chance(50, 0));
        Assert.Equal(50, HeadFidget.Chance(50, 1)); // 1/2 == 0
        Assert.Equal(51, HeadFidget.Chance(50, 2));
        Assert.Equal(55, HeadFidget.Chance(50, 11));
    }

    [Fact]
    public void ALongIdlePushesAThreeVariantHeadTowardTheShowierFidgets()
    {
        // The point of the idle term: the same base roll lands differently after a pause.
        Assert.Equal(1, HeadFidget.Roll(3, HeadFidget.Chance(50, 0)));
        Assert.Equal(3, HeadFidget.Roll(3, HeadFidget.Chance(50, 60)));
    }
}

/// <summary>
/// F46 on real data: the shipped heads.lst, and the claim that the reference's
/// one-number-into-all-three parse cannot be observed on it.
/// </summary>
public class HeadFidgetCountTests
{
    private static int HeadFid(int index, int anim) =>
        ((int)Hexwaste.Formats.ObjectType.Head << 24) | (anim << 16) | index;

    [GameDataFact]
    public void HeadFidgetCountsMatchTheShippedList()
    {
        using var vfs = Hexwaste.Formats.GameFileSystem.Open(GameData.RequiredDir);
        var art = new Hexwaste.Formats.Art.ArtIndex(vfs);

        // Head 0 is "reser" — no comma on its line, so every count is 0, exactly as the
        // reference's atoi("eser") gives.
        Assert.Equal(0, art.HeadFidgetCount(HeadFid(0, HeadFidget.Neutral)));

        // Every other shipped head has 2 or 3 variants, and the three emotions agree —
        // which is precisely why the reference's collapsed parse is unobservable.
        int checkedHeads = 0;
        for (int index = 1; index < 13; index++)
        {
            int good = art.HeadFidgetCount(HeadFid(index, HeadFidget.Good));
            int neutral = art.HeadFidgetCount(HeadFid(index, HeadFidget.Neutral));
            int bad = art.HeadFidgetCount(HeadFid(index, HeadFidget.Bad));
            Assert.True(neutral is 2 or 3, $"head {index}: unexpected fidget count {neutral}");
            Assert.Equal(neutral, good);
            Assert.Equal(neutral, bad);
            checkedHeads++;
        }
        Assert.Equal(12, checkedHeads);
    }

    [GameDataFact]
    public void ANonFidgetAnimHasNoFidgetCount()
    {
        using var vfs = Hexwaste.Formats.GameFileSystem.Open(GameData.RequiredDir);
        var art = new Hexwaste.Formats.Art.ArtIndex(vfs);
        // anim 9/10/11 are the phoneme sets, not fidget families — artGetFidgetCount's
        // switch has no case for them and returns 0.
        Assert.Equal(0, art.HeadFidgetCount(HeadFid(1, 10)));
    }
}
