using System.Text;
using Hexwaste.Formats;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Generic-reputation titles (P31 B-M1), ported from character_editor.cc genericReputationInit + the
/// descending threshold lookup. The title is the highest-threshold row the reputation value meets.
/// </summary>
public class GenericReputationTests
{
    private const string Sample = """
        # generic reputation thresholds (threshold message_id)
        -20, 2000
        0,   2002
        25,  2003
        50,  2004
        100, 2006
        """;

    [Fact]
    public void ParsesAndSortsDescending()
    {
        IReadOnlyList<ReputationEntry> e = GenericReputation.Parse(Sample);
        Assert.Equal(5, e.Count);
        Assert.Equal(100, e[0].Threshold);   // sorted descending
        Assert.Equal(-20, e[^1].Threshold);
        Assert.Equal(2006, e[0].MessageId);
    }

    [Fact]
    public void TitleIsTheHighestThresholdMet()
    {
        IReadOnlyList<ReputationEntry> e = GenericReputation.Parse(Sample);
        Assert.Equal(2002, GenericReputation.TitleFor(0, e));    // exactly at a threshold
        Assert.Equal(2002, GenericReputation.TitleFor(24, e));   // between 0 and 25 → the 0 row
        Assert.Equal(2003, GenericReputation.TitleFor(25, e));
        Assert.Equal(2006, GenericReputation.TitleFor(9999, e)); // above the top
        Assert.Equal(2000, GenericReputation.TitleFor(-20, e));  // the floor
        Assert.Equal(-1, GenericReputation.TitleFor(-21, e));    // below every threshold → no title
    }

    [Fact]
    public void SkipsCommentsAndBlankAndMalformedLines()
    {
        IReadOnlyList<ReputationEntry> e = GenericReputation.Parse("# header\n\n  \nfoo bar\n10 500\n");
        Assert.Equal(new ReputationEntry(10, 500), Assert.Single(e));
    }

    [GameDataFact]
    public void RealGenrepTxtParses()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        IReadOnlyList<ReputationEntry> e = GenericReputation.Parse(
            Encoding.Latin1.GetString(vfs.ReadAllBytes(@"data\genrep.txt")));
        Assert.NotEmpty(e);
        // descending + every row maps to a real message id.
        for (int i = 1; i < e.Count; i++)
            Assert.True(e[i - 1].Threshold >= e[i].Threshold);
        Assert.All(e, row => Assert.True(row.MessageId > 0));
    }
}
