using System.Text;
using Hexwaste.Formats;
using Hexwaste.Formats.Int;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// vault13.gam GAME_GLOBAL_VARS seeding (P32), ported from game.cc globalVarsRead. Positional indexing,
/// value after '=', '//' comments + blank lines skipped, ';' inline-comment stripped.
/// </summary>
public class GameGlobalVarsTests
{
    [Fact]
    public void ParsesPositionalValuesAndSkipsCommentsAndBlanks()
    {
        const string txt = "// preamble\n"
            + "MAP_GLOBAL_VARS:\nGVAR_IGNORED_IN_WRONG_SECTION :=99;\n"
            + "GAME_GLOBAL_VARS:\n"
            + "//GLOBAL   NUMBER\n"
            + "\n"
            + "GVAR_A :=0;   // (0)\n"
            + "GVAR_B :=50;  // (1)\n"
            + "GVAR_C :=-1;  // (2)\n"
            + "GVAR_D;       // (3) no '=' -> 0\n";
        IReadOnlyList<int> v = GameGlobalVars.Parse(txt);
        Assert.Equal(4, v.Count);   // the MAP_GLOBAL_VARS line is before the GAME section → excluded
        Assert.Equal(0, v[0]);
        Assert.Equal(50, v[1]);     // value after '='
        Assert.Equal(-1, v[2]);     // negative
        Assert.Equal(0, v[3]);      // no '=' → 0
    }

    [GameDataFact]
    public void RealVault13GamSeedsTheKnownNonZeroGlobals()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        IReadOnlyList<int> v = GameGlobalVars.Parse(
            Encoding.Latin1.GetString(vfs.ReadAllBytes(@"data\vault13.gam")));
        Assert.True(v.Count >= 690, $"expected ~696 globals, got {v.Count}");
        // The positional indices the // (N) markers assert:
        Assert.Equal(0, v[0]);     // GVAR_PLAYER_REPUTATION
        Assert.Equal(50, v[47]);   // GVAR_TOWN_REP_ARROYO (Arroyo starts Idolized)
        Assert.Equal(1, v[619]);   // GVAR_FIND_VIC
        // The base game seeds almost everything to 0 — only a handful are non-zero.
        Assert.InRange(v.Count(x => x != 0), 8, 20);
    }
}
