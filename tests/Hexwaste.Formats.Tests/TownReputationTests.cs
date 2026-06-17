using System.Text;
using Hexwaste.Formats;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Town reputation bands + karma titles (P31 B-M2), ported from character_editor.cc:5574 (the level
/// thresholds) + karmaInit (the karmavar.txt parse) + the non-zero-GVAR active-title scan.
/// </summary>
public class TownReputationTests
{
    [Theory]
    [InlineData(-31, TownRepLevel.Vilified)]
    [InlineData(-30, TownRepLevel.Hated)]      // < -30 is false at -30 → Hated
    [InlineData(-16, TownRepLevel.Hated)]
    [InlineData(-15, TownRepLevel.Antipathy)]
    [InlineData(-1, TownRepLevel.Antipathy)]
    [InlineData(0, TownRepLevel.Neutral)]
    [InlineData(1, TownRepLevel.Accepted)]
    [InlineData(14, TownRepLevel.Accepted)]
    [InlineData(15, TownRepLevel.Liked)]
    [InlineData(29, TownRepLevel.Liked)]
    [InlineData(30, TownRepLevel.Idolized)]
    [InlineData(999, TownRepLevel.Idolized)]
    public void LevelForMatchesTheEngineThresholds(int value, TownRepLevel expected) =>
        Assert.Equal(expected, TownReputation.LevelFor(value));

    [Fact]
    public void EachLevelMapsToItsMessageId()
    {
        Assert.Equal(2006, TownReputation.MessageId(TownRepLevel.Vilified));
        Assert.Equal(2003, TownReputation.MessageId(TownRepLevel.Neutral));
        Assert.Equal(2000, TownReputation.MessageId(TownRepLevel.Idolized));
    }

    [Fact]
    public void KarmaTitlesParseAndActiveScan()
    {
        // gvar art name desc; gvar 0 is the generic-reputation row (excluded from titles).
        const string txt = """
            # karma titles
            0  47 125 126
            37 48 2000 2100
            45 49 2008 2108
            """;
        IReadOnlyList<KarmaEntry> e = KarmaTitles.Parse(txt);
        Assert.Equal(3, e.Count);
        Assert.Equal(new KarmaEntry(37, 48, 2000, 2100), e[1]);

        // Only gvar 37 is non-zero → exactly that title is earned (gvar 0 excluded even if set).
        var globals = new Dictionary<int, int> { [0] = 99, [37] = 1, [45] = 0 };
        KarmaEntry[] active = [.. KarmaTitles.Active(e, g => globals.GetValueOrDefault(g))];
        Assert.Equal(2000, Assert.Single(active).NameMessageId);
    }

    [GameDataFact]
    public void RealKarmavarTxtParses()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        IReadOnlyList<KarmaEntry> e = KarmaTitles.Parse(
            Encoding.Latin1.GetString(vfs.ReadAllBytes(@"data\karmavar.txt")));
        Assert.NotEmpty(e);
        Assert.All(e, row => Assert.True(row.NameMessageId > 0));
    }
}
