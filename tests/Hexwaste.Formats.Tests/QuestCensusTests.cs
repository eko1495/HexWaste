using Hexwaste.Formats.Int;

namespace Hexwaste.Formats.Tests;

/// <summary>P124 quest-QA sweep: the set_global_var bytecode scanner, hermetic — a
/// hand-assembled minimal .int (42-byte header, procCount 0, empty identifier block,
/// no static strings, then code; the same container trick as VmGlobalsPersistenceTests).</summary>
public class GlobalWriteScanTests
{
    private static void Op(List<byte> code, ushort opcode)
    {
        code.Add((byte)(opcode >> 8));
        code.Add((byte)opcode);
    }

    private static void PushInt(List<byte> code, int value)
    {
        Op(code, 0xC001);
        code.Add((byte)(value >> 24));
        code.Add((byte)(value >> 16));
        code.Add((byte)(value >> 8));
        code.Add((byte)value);
    }

    private static byte[] Assemble(Action<List<byte>> body)
    {
        var bytes = new List<byte>(new byte[42]);        // header
        bytes.AddRange([0, 0, 0, 0]);                    // procedureCount = 0
        bytes.AddRange([0, 0, 0, 0]);                    // identifiers size = 0
        bytes.AddRange([0xFF, 0xFF, 0xFF, 0xFF]);        // identifiers terminator
        bytes.AddRange([0xFF, 0xFF, 0xFF, 0xFF]);        // no static strings
        body(bytes);
        return [.. bytes];
    }

    [Fact]
    public void ScanFindsConstWritesAndCountsDynamicOnes()
    {
        byte[] script = Assemble(code =>
        {
            // set_global_var(700, 5) — the exact const-const triple.
            PushInt(code, 700);
            PushInt(code, 5);
            Op(code, 0x80C6);
            // set_global_var(701, global_var(701) + 1) — a computed value: no const write,
            // but the set still counts and 701 lands in the pushed-int upper bound.
            PushInt(code, 701);
            PushInt(code, 701);
            Op(code, 0x80C5); // get_global_var
            PushInt(code, 1);
            Op(code, 0x8039); // add
            Op(code, 0x80C6);
            // A read-only reference (push 650, get) — a "toucher" false positive by design.
            PushInt(code, 650);
            Op(code, 0x80C5);
        });

        GlobalWriteScan.Result r = GlobalWriteScan.Scan(script);

        Assert.Equal(2, r.SetGlobalCount);
        Assert.Equal([700], r.ConstWrites.Keys);
        Assert.Equal([5], r.ConstWrites[700]);
        Assert.Superset(new HashSet<int> { 700, 5, 701, 1, 650 }, r.PushedInts.ToHashSet());
    }

    [Fact]
    public void ScanSurvivesGarbage()
    {
        GlobalWriteScan.Result r = GlobalWriteScan.Scan([1, 2, 3]);
        Assert.Empty(r.ConstWrites);
        Assert.Equal(0, r.SetGlobalCount);
    }
}

/// <summary>P124: the whole-game quest-completion census — every quests.txt row's gvar
/// must be writable to its completion threshold by some shipped script, except the three
/// VANILLA content gaps this sweep confirmed (they are the game's bugs, not Hexwaste's):
///  - gvar 108 GVAR_MODOC_BRAHMIN_SEED — listed in quests.txt, NO script ever writes it
///    (cut content; every "toucher" is a message-list id or comparison false positive).
///  - gvar 396 GVAR_QUEST_REPAIR_POWER_PLANT — an orphan; Gecko's real repair quest runs
///    on a different (verified) gvar. No writes anywhere.
///  - gvar 370 GVAR_NEW_RENO_JET_SOURCE — Myron's dialog writes 1/2/3 but the completion
///    threshold is 4: the Jet-source quest never reads completed in vanilla.</summary>
public class QuestCensusRealGameDataTests
{
    [GameDataFact]
    public void EveryQuestCompletionIsReachableExceptTheVanillaGaps()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        IReadOnlyList<Quest> quests;
        using (Stream qs = vfs.OpenRead(@"data\quests.txt"))
            quests = QuestLog.Parse(qs);
        Assert.Equal(110, quests.Count);

        ScriptList scripts = ScriptList.Load(vfs);
        var maxConstWrite = new Dictionary<int, int>();
        int scanned = 0;
        for (int i = 0; i < scripts.Count; i++)
        {
            if (scripts.GetScriptPath(i) is not { } path || !vfs.Exists(path))
                continue;
            scanned++;
            foreach ((int gvar, SortedSet<int> values) in GlobalWriteScan.Scan(vfs.ReadAllBytes(path)).ConstWrites)
                maxConstWrite[gvar] = Math.Max(maxConstWrite.GetValueOrDefault(gvar, int.MinValue), values.Max);
        }
        Assert.True(scanned > 1200, $"only {scanned} scripts scanned — scripts.lst/VFS wiring broke");

        int[] vanillaGaps = [108, 370, 396];
        foreach (Quest q in quests)
        {
            int max = maxConstWrite.GetValueOrDefault(q.Gvar, int.MinValue);
            if (vanillaGaps.Contains(q.Gvar))
                continue; // asserted precisely below
            Assert.True(max >= q.CompletedThreshold,
                $"quest gvar {q.Gvar}: max const write {max} < completion threshold {q.CompletedThreshold}"
                + " — a shipped script stopped reaching it (or the scanner regressed)");
        }

        // The pinned vanilla gaps — if a future data patch fixes one, this fails and the
        // exception list shrinks.
        Assert.False(maxConstWrite.ContainsKey(108)); // Modoc brahmin seed: never written
        Assert.False(maxConstWrite.ContainsKey(396)); // power-plant orphan: never written
        Assert.Equal(3, maxConstWrite[370]);          // Jet source: 1/2/3 only, threshold 4
    }
}
