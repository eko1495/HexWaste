using System.Text;
using Hexwaste.Formats;
using Hexwaste.Formats.Endgame;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Tests for the endgame.txt / enddeath.txt parsers + the death-ending selector (Point-1 endgame slideshow),
/// ported from fallout2-ce src/endgame.cc. Behavior is covered by inline fixtures (no copyrighted content);
/// the real data\endgame.txt / data\enddeath.txt are asserted only under a GameDataFact so CI passes assetless.
/// </summary>
public class EndgameEndingFileTests
{
    private static byte[] Bytes(string s) => Encoding.ASCII.GetBytes(s);

    // ---- endgame.txt (victory) parser -----------------------------------

    [Fact]
    public void SkipsCommentsAndBlankLinesAndParsesFields()
    {
        const string txt =
            "# a comment\n" +
            "\n" +
            "#408, 1, 445, nar_x1\n" +      // commented-out record → skipped
            "408, 1, 440, nar_ar1\n" +      // 4 fields → direction defaults to 1
            "  409, 2, 445, nar_mo2  \n";   // leading/trailing space tolerated
        var r = EndgameEndingFile.Parse(Bytes(txt));
        Assert.Equal(2, r.Count);
        Assert.Equal(new EndgameEnding(408, 1, 440, "nar_ar1", 1), r[0]);
        Assert.Equal(new EndgameEnding(409, 2, 445, "nar_mo2", 1), r[1]);
    }

    [Fact]
    public void AbsentDirectionDefaultsToOneExplicitDirectionParsed()
    {
        var r = EndgameEndingFile.Parse(Bytes("40, 1, 327, nar_10, 1\n50, 1, 327, nar_11, -1\n"));
        Assert.Equal(1, r[0].Direction);
        Assert.Equal(-1, r[1].Direction);
    }

    [Fact]
    public void InlineHashCommentAfterNameYieldsDirectionZero()
    {
        // fo2ce does NOT strip inline comments: the 5th strtok token is "#", atoi("#") == 0 → direction 0.
        var r = EndgameEndingFile.Parse(Bytes("410, 3, 442, nar_de3         # Den Pic 2: 454\n"));
        Assert.Single(r);
        Assert.Equal(new EndgameEnding(410, 3, 442, "nar_de3", 0), r[0]);
    }

    [Fact]
    public void RowNeedsFourFields()
    {
        Assert.Empty(EndgameEndingFile.Parse(Bytes("408, 1, 440\n")));
    }

    [GameDataFact]
    public void RealEndgameTxtHasFiftyTwoActiveVictoryRows()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var r = EndgameEndingFile.Parse(vfs.ReadAllBytes(@"data\endgame.txt"));
        Assert.Equal(52, r.Count);
        Assert.Equal(new EndgameEnding(408, 1, 440, "nar_ar1", 1), r[0]);
        // The Den row with a trailing inline comment parses direction 0.
        Assert.Contains(r, e => e.Gvar == 410 && e.Value == 3 && e.VoiceOverBaseName == "nar_de3" && e.Direction == 0);
    }

    // ---- enddeath.txt (death) parser + selector -------------------------

    // A trimmed enddeath.txt fixture mirroring the real file's structure (indices 0-12).
    private const string DeathFixture =
        "# comment\n" +
        "-1, 0, -1, -1, 0, 20, nar_4\n" +      // 0  generic (always eligible)
        "-1, 0, -1, -1, 0, 20, nar_5\n" +      // 1
        "-1, 0, -1, -1, 0, 20, nar_6\n" +      // 2
        "-1, 0, -1, -1, 0, 20, nar_dth1\n" +   // 3
        "-1, 0, -1, -1, 0, 20, nar_dth2\n" +   // 4
        "-1, 0, -1, 16, 0, 40, nar_dth3\n" +   // 5  requires area 16 NOT known
        "-1, 0, -1, 16, 0, 40, nar_dth4\n" +   // 6
        "-1, 0, -1, 16, 0, 40, nar_dth5\n" +   // 7
        "-1, 0, 22, -1, 0, 40, nar_dth6\n" +   // 8  requires area 22 known
        "-1, 0, 16, -1, 0, 40, nar_dth7\n" +   // 9  requires area 16 known
        "-1, 0, 16, -1, 0, 40, nar_dth8\n" +   // 10
        "-1, 0, 16, -1, 0, 40, nar_dth9\n" +   // 11
        "491, 1, 3, -1, 150, 0, nar_mo1\n";    // 12 Modoc special

    [Fact]
    public void DeathParserReadsSevenFields()
    {
        var r = EndgameDeathEndingFile.Parse(Bytes(DeathFixture));
        Assert.Equal(13, r.Count);
        Assert.Equal(new EndgameDeathEnding(491, 1, 3, -1, 150, 0, "nar_mo1"), r[12]);
    }

    [Fact]
    public void ModocShittyDeathForcesRecordTwelveWithNoRandom()
    {
        var r = EndgameDeathEndingFile.Parse(Bytes(DeathFixture));
        string pick = EndgameDeathEndingFile.Select(
            r, EndgameDeathReason.Death,
            getGlobalVar: g => g == EndgameDeathEndingFile.GvarModocShittyDeath ? 1 : 0,
            areaKnown: _ => false, pcLevel: 200,
            randomBetween: (_, _) => throw new Exception("random must not be consulted for the Modoc special"));
        Assert.Equal(@"narrator\nar_mo1", pick);
    }

    [Fact]
    public void AreaGatingDisablesSpecificDeathsThatNeedAKnownArea()
    {
        var r = EndgameDeathEndingFile.Parse(Bytes(DeathFixture));
        // No area known: nar_dth6..9 (require an area known) are disabled; the "area 16 not known" ones stay in.
        // Force chance past the generic block so the walk lands on the first "not-known-16" record (index 5).
        string pick = EndgameDeathEndingFile.Select(
            r, EndgameDeathReason.Death, getGlobalVar: _ => 0, areaKnown: _ => false, pcLevel: 1,
            randomBetween: (_, _) => 101); // > sum of generic (100) → skip to first specific enabled
        Assert.Equal(@"narrator\nar_dth3", pick);
    }

    [Fact]
    public void EmptyTableReturnsDefaultNarration()
    {
        Assert.Equal(EndgameDeathEndingFile.DefaultFileName,
            EndgameDeathEndingFile.Select([], EndgameDeathReason.Death, _ => 0, _ => false, 1, (_, _) => 0));
    }

    [GameDataFact]
    public void RealEndDeathTxtRecordTwelveIsTheModocSpecial()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var r = EndgameDeathEndingFile.Parse(vfs.ReadAllBytes(@"data\enddeath.txt"));
        Assert.Equal(new EndgameDeathEnding(491, 1, 3, -1, 150, 0, "nar_mo1"), r[EndgameDeathEndingFile.ModocDeathIndex]);
    }
}
