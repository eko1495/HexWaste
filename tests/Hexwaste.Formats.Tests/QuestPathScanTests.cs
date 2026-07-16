using Hexwaste.Formats.Int;

namespace Hexwaste.Formats.Tests;

/// <summary>P128 quest-path finder: the per-procedure write attribution + the static
/// dialog graph (option/call edges) + the BFS, hermetic — a hand-assembled .int with
/// talk_p_proc =call=&gt; NodeA =opt1=&gt; NodeB, where NodeB writes the quest gvar.</summary>
public class QuestPathScanTests
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

    private static void Int32(List<byte> bytes, int value)
    {
        bytes.Add((byte)(value >> 24));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }

    private static byte[] Assemble()
    {
        string[] names = ["talk_p_proc", "NodeA", "NodeB"];
        var idBytes = new List<byte>();
        var nameOffsets = new int[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            idBytes.Add((byte)((names[i].Length + 1) >> 8));
            idBytes.Add((byte)(names[i].Length + 1));
            nameOffsets[i] = 4 + idBytes.Count;
            idBytes.AddRange(System.Text.Encoding.Latin1.GetBytes(names[i]));
            idBytes.Add(0);
        }

        int codeStart = 42 + 4 + names.Length * 24 + 4 + idBytes.Count + 4;

        // talk_p_proc: call NodeA (proc index 1).
        var talk = new List<byte>();
        PushInt(talk, 1);
        Op(talk, 0x8005); // call

        // NodeA: two options — opt0 -> talk_p_proc (a back edge), opt1 -> NodeB.
        var nodeA = new List<byte>();
        PushInt(nodeA, 970); PushInt(nodeA, 100); PushInt(nodeA, 0); PushInt(nodeA, 50);
        Op(nodeA, 0x811F); // gsay_option -> proc 0
        PushInt(nodeA, 4); PushInt(nodeA, 970); PushInt(nodeA, 101); PushInt(nodeA, 2); PushInt(nodeA, 50);
        Op(nodeA, 0x8121); // giq_option -> proc 2

        // NodeB: set_global_var(500, 2).
        var nodeB = new List<byte>();
        PushInt(nodeB, 500);
        PushInt(nodeB, 2);
        Op(nodeB, 0x80C6);

        int talkAt = codeStart;
        int nodeAAt = talkAt + talk.Count;
        int nodeBAt = nodeAAt + nodeA.Count;

        var file = new List<byte>(new byte[42]); // stub irrelevant to the static scan
        Int32(file, names.Length);
        int[] bodies = [talkAt, nodeAAt, nodeBAt];
        for (int i = 0; i < names.Length; i++)
        {
            Int32(file, nameOffsets[i]);
            Int32(file, 0);
            Int32(file, 0);
            Int32(file, 0);
            Int32(file, bodies[i]);
            Int32(file, 0);
        }
        Int32(file, idBytes.Count);
        file.AddRange(idBytes);
        Int32(file, unchecked((int)0xFFFFFFFF));
        file.AddRange(talk);
        file.AddRange(nodeA);
        file.AddRange(nodeB);
        return [.. file];
    }

    [Fact]
    public void ScanAttributesWritesAndBuildsTheDialogGraph()
    {
        QuestPathScan.Result scan = QuestPathScan.Scan(Assemble());

        QuestPathScan.ConstWrite write = Assert.Single(scan.Writes);
        Assert.Equal((2, 500, 2), (write.Proc, write.Gvar, write.Value)); // NodeB writes gvar 500 := 2

        Assert.Contains((0, 1), scan.Calls); // talk_p_proc =call=> NodeA
        Assert.Contains(scan.Options, e => e is { FromProc: 1, Ordinal: 0, ToProc: 0 }); // back edge
        Assert.Contains(scan.Options, e => e is { FromProc: 1, Ordinal: 1, ToProc: 2 }); // -> NodeB
    }

    [Fact]
    public void FindPathWalksCallThenOptionEdges()
    {
        QuestPathScan.Result scan = QuestPathScan.Scan(Assemble());
        List<string>? path = QuestPathScan.FindPath(scan, fromProc: 0, toProc: 2);

        Assert.NotNull(path);
        Assert.Equal(["=call=> NodeA", "=opt1=> NodeB"], path);
        Assert.Null(QuestPathScan.FindPath(scan, fromProc: 2, toProc: 1)); // NodeB has no out edges
    }

    /// <summary>P137: a single proc that both TESTS a task bit (<c>global(446) &amp; 0x100</c>) and
    /// SETS one (<c>global(446) |= 0x8000</c> — the read-modify-write). The bit-level scan must
    /// capture each with its exact mask; the mask is what disambiguates a shared task bitfield.</summary>
    [Fact]
    public void ScanCapturesBitChecksAndBitSetsWithExactMask()
    {
        var body = new List<byte>();
        // BitCheck: push 446, get_global, push 0x100, bitwise_and
        PushInt(body, 446); Op(body, 0x80C5); PushInt(body, 0x100); Op(body, 0x8040);
        // BitSet (RMW): push 446, push 446, get_global, push 0x8000, bitwise_or, set_global
        PushInt(body, 446); PushInt(body, 446); Op(body, 0x80C5);
        PushInt(body, 0x8000); Op(body, 0x8041); Op(body, 0x80C6);

        QuestPathScan.Result scan = QuestPathScan.Scan(AssembleOneProc(body));

        QuestPathScan.BitCheck check = Assert.Single(scan.BitChecks);
        Assert.Equal((0, 446, 0x100), (check.Proc, check.Gvar, check.Mask));
        QuestPathScan.BitSet set = Assert.Single(scan.BitSets);
        Assert.Equal((0, 446, 0x8000), (set.Proc, set.Gvar, set.Mask));
    }

    /// <summary>Assemble a single procedure "start" whose body is <paramref name="body"/>.</summary>
    private static byte[] AssembleOneProc(List<byte> body)
    {
        string[] names = ["start"];
        var idBytes = new List<byte>();
        var nameOffsets = new int[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            idBytes.Add((byte)((names[i].Length + 1) >> 8));
            idBytes.Add((byte)(names[i].Length + 1));
            nameOffsets[i] = 4 + idBytes.Count;
            idBytes.AddRange(System.Text.Encoding.Latin1.GetBytes(names[i]));
            idBytes.Add(0);
        }
        int codeStart = 42 + 4 + names.Length * 24 + 4 + idBytes.Count + 4;
        var file = new List<byte>(new byte[42]);
        Int32(file, names.Length);
        Int32(file, nameOffsets[0]); Int32(file, 0); Int32(file, 0); Int32(file, 0);
        Int32(file, codeStart); Int32(file, 0);
        Int32(file, idBytes.Count);
        file.AddRange(idBytes);
        Int32(file, unchecked((int)0xFFFFFFFF));
        file.AddRange(body);
        return [.. file];
    }
}

/// <summary>P128 against the shipped data: the finder must keep resolving the quest-golden
/// ground truths — the Rat God quest (gvar 390) completes via a NON-dialog kill trigger
/// (the golden drives it with --kill), and the Torr quest (182) has dialog-reachable
/// writers. Numeric structure only.</summary>
public class QuestPathRealGameDataTests
{
    [GameDataFact]
    public void ResolvesTheKnownGoldenQuestMechanisms()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        ScriptList scripts = ScriptList.Load(vfs);

        QuestPathScan.Result ratGod = ScanByName(vfs, scripts, "kcratgod");
        QuestPathScan.ConstWrite kill = ratGod.Writes.Single(w => w.Gvar == 390 && w.Value == 2);
        Assert.Equal("destroy_p_proc", ratGod.Program.Procedures[kill.Proc].Name);

        QuestPathScan.Result torr = ScanByName(vfs, scripts, "kctorr");
        QuestPathScan.ConstWrite advance = torr.Writes.First(w => w.Gvar == 182);
        int talk = torr.Program.FindProcedure("talk_p_proc");
        Assert.True(talk >= 0);
        Assert.NotNull(QuestPathScan.FindPath(torr, talk, advance.Proc)); // dialog-reachable
    }

    /// <summary>P137 bit-level prerequisite ground truth: Rebecca's quest 371 completion gates on
    /// Fred's demand-full task bit — Fred SETS <c>446 |= 0x8000</c> and Rebecca CHECKS
    /// <c>446 &amp; 0x8000</c>. The exact-mask match is a clean cross-NPC prerequisite the
    /// gvar-level analysis (plan §10) drowned in the shared-bitfield noise.</summary>
    [GameDataFact]
    public void ResolvesTheFredRebeccaBitPrerequisite()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        ScriptList scripts = ScriptList.Load(vfs);

        QuestPathScan.Result fred = ScanByName(vfs, scripts, "dcfred");
        Assert.Contains(fred.BitSets, b => b.Gvar == 446 && b.Mask == 0x8000);

        QuestPathScan.Result rebecca = ScanByName(vfs, scripts, "dcrebecc");
        Assert.Contains(rebecca.BitChecks, b => b.Gvar == 446 && b.Mask == 0x8000);
    }

    private static QuestPathScan.Result ScanByName(GameFileSystem vfs, ScriptList scripts, string name)
    {
        for (int i = 0; i < scripts.Count; i++)
            if (string.Equals(scripts.GetName(i), name, StringComparison.OrdinalIgnoreCase)
                && scripts.GetScriptPath(i) is { } path && vfs.Exists(path))
                return QuestPathScan.Scan(vfs.ReadAllBytes(path));
        throw new InvalidOperationException($"script '{name}' not found");
    }
}
