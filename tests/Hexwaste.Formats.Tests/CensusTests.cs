using Hexwaste.Formats;
using Hexwaste.Formats.Int;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Point-2 QA tooling: the static "silent quest-gap detector" backbone — IntVm.WiredExternals (the set of
/// externals with an explicit case in ExecuteExternal) and IntProgram.ReferencedExternals (a linear opcode
/// scan). The census reports referenced \ wired as a map's unwired-external demand.
/// </summary>
public class CensusTests
{
    [Fact]
    public void WiredExternalsAreAllRealExternals()
    {
        // Guard against drift: every hand-listed wired opcode must be a known external (⊆ ExternalArity.Table).
        Assert.NotEmpty(IntVm.WiredExternals);
        foreach (int op in IntVm.WiredExternals)
            Assert.True(ExternalArity.Table.ContainsKey(op), $"0x{op:X4} is in WiredExternals but not ExternalArity.Table");
    }

    [Fact]
    public void EndgameExternalsAreWired()
    {
        // Point-1's endgame_slideshow/movie must be counted as wired by the census.
        Assert.Contains(0x8146, IntVm.WiredExternals);
        Assert.Contains(0x8148, IntVm.WiredExternals);
    }

    [GameDataFact]
    public void ReferencedExternalsOnRealScriptsAreValidAndFindStubs()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var scriptList = ScriptList.Load(vfs);
        int scanned = 0, totalRefs = 0;
        // Scan a spread of real scripts; every opcode the scanner reports must be a valid external key
        // (a misaligned scan would surface garbage keys not in the table).
        for (int idx = 0; idx < 400 && scanned < 40; idx++)
        {
            string? path = scriptList.GetScriptPath(idx);
            if (path is null || !vfs.Exists(path))
                continue;
            IntProgram prog;
            try { using Stream s = vfs.OpenRead(path); prog = IntProgram.Load(s); }
            catch { continue; }
            var refs = prog.ReferencedExternals();
            foreach (int op in refs)
                Assert.True(ExternalArity.Table.ContainsKey(op), $"scanner produced non-external 0x{op:X4} in {path}");
            totalRefs += refs.Count;
            scanned++;
        }
        Assert.True(scanned > 0, "no real scripts were scanned");
        Assert.True(totalRefs > 0, "real scripts referenced no externals — scanner likely broken");
    }
}
