using Hexwaste.Formats;
using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Module-globals lifecycle (the "fix E" cross-proc persistence), tested HERMETICALLY:
/// a hand-assembled two-proc .int (no FALLOUT2_DIR needed) served through a temp game
/// dir (empty master.dat + loose data/scripts/), driven through the real
/// ScriptHost VM cache. This is the ONLY regression guard for the pattern (heartbeat
/// counters, dialog→lifecycle quest flags, the New Reno prizefight coordinator) — no
/// golden fixture exercises cross-proc module globals.
///
/// The synthesized script mirrors the compiler's output exactly (stub layout and proc
/// epilogue copied from a disassembled kcratgod.int; the 42-byte stub hosts the fixed
/// return addresses 18=exit_program and 24=pop+pop_flags_exit used by the call
/// convention — see IntProgram's doc comment):
///   module global[0] init 0
///   critter_p_proc:     global[0] := 42
///   timed_event_p_proc: GVAR[777] := global[0]   (observable via host.GlobalVars)
/// </summary>
public class VmGlobalsPersistenceTests : IDisposable
{
    private readonly string _tempDir;

    public VmGlobalsPersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hexwaste-vmglobals-" + Guid.NewGuid().ToString("N"));
        string scriptsDir = Path.Combine(_tempDir, "data", "scripts");
        Directory.CreateDirectory(scriptsDir);

        // Minimal empty DAT2 (dfile.cc dbaseOpen footer): [int32 entriesLength=0]
        // [int32 entriesDataSize=4][int32 dbaseDataSize=12] — GameFileSystem.Open
        // requires at least one archive; all real content is served loose.
        File.WriteAllBytes(Path.Combine(_tempDir, "master.dat"),
            [0, 0, 0, 0, 4, 0, 0, 0, 12, 0, 0, 0]);

        File.WriteAllText(Path.Combine(scriptsDir, "scripts.lst"), "testglob.int ; # local_vars=0\n");
        File.WriteAllBytes(Path.Combine(scriptsDir, "testglob.int"), BuildTwoProcScript());
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ---- bytecode assembler ------------------------------------------------

    private static void Op(List<byte> code, ushort opcode)
    {
        code.Add((byte)(opcode >> 8));
        code.Add((byte)opcode);
    }

    private static void PushInt(List<byte> code, int value)
    {
        Op(code, 0xC001); // push, int type bits
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

    /// <summary>The compiler's void-proc epilogue (disassembled from kcratgod.int
    /// destroy_p_proc): return 0, unwind locals, restore frame, land on stub offset 24
    /// (pop + pop_flags_exit).</summary>
    private static void Epilogue(List<byte> code)
    {
        PushInt(code, 0);   // return value
        Op(code, 0x800D);   // d_to_a
        Op(code, 0x8019);   // swapa
        Op(code, 0x802A);   // pop_to_base
        Op(code, 0x8029);   // pop_base
        Op(code, 0x800C);   // a_to_d
        Op(code, 0x801C);   // pop_return
    }

    private static byte[] BuildTwoProcScript()
    {
        // Identifiers block: [int32 size] then per entry [u16 len][chars][NUL];
        // a proc record's nameOffset points at the chars (block-relative).
        string[] names = ["critter_p_proc", "timed_event_p_proc"];
        var idBytes = new List<byte>();
        var nameOffsets = new int[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            idBytes.Add((byte)((names[i].Length + 1) >> 8));
            idBytes.Add((byte)(names[i].Length + 1));
            nameOffsets[i] = 4 + idBytes.Count; // +4: past the block's size prefix
            idBytes.AddRange(System.Text.Encoding.Latin1.GetBytes(names[i]));
            idBytes.Add(0);
        }

        int codeStart = 42                       // stub
            + 4 + names.Length * 24              // proc count + records
            + 4 + idBytes.Count                  // identifiers block
            + 4;                                 // static strings size (absent)

        // Global-init prologue: set_global (basePointer := stack top), push global[0]
        // init value, then pop_return -> stub offset 18 (exit_program; the stub's
        // d_to_a parked 18 on the return stack).
        var prologue = new List<byte>();
        Op(prologue, 0x802C);      // set_global
        PushInt(prologue, 0);      // global[0] := 0
        Op(prologue, 0x8003);      // leave_critical_section
        Op(prologue, 0x801C);      // pop_return -> 18

        // critter_p_proc: global[0] := 42
        var procA = new List<byte>();
        Op(procA, 0x802B);         // push_base (pops SetupCall's arg-count 0)
        PushInt(procA, 42);        // value
        PushInt(procA, 0);         // global addr
        Op(procA, 0x8013);         // store_global
        Epilogue(procA);

        // timed_event_p_proc: GVAR[777] := global[0]
        var procB = new List<byte>();
        Op(procB, 0x802B);         // push_base
        PushInt(procB, 777);       // gvar index
        PushInt(procB, 0);         // global addr
        Op(procB, 0x8012);         // fetch_global
        Op(procB, 0x80C6);         // set_global_var
        Epilogue(procB);

        int prologueAt = codeStart;
        int procAAt = prologueAt + prologue.Count;
        int procBAt = procAAt + procA.Count;

        // 42-byte stub, byte-for-byte the compiler's (kcratgod.int offsets 0..41):
        // enter_crit; push 18; d_to_a; push <prologue>; jump; then the fixed return
        // addresses 18/20/24/28/32 + trailing handler opcodes.
        var file = new List<byte>();
        Op(file, 0x8002);          //  0: enter_critical_section
        PushInt(file, 18);         //  2: push 18 (init's return address)
        Op(file, 0x800D);          //  8: d_to_a
        PushInt(file, prologueAt); // 10: push prologue address
        Op(file, 0x8004);          // 16: jump
        Op(file, 0x8010);          // 18: exit_program
        Op(file, 0x801A);          // 20: pop
        Op(file, 0x8020);          // 22: pop_flags_return
        Op(file, 0x801A);          // 24: pop            <- procedure return lands here
        Op(file, 0x8021);          // 26: pop_flags_exit
        Op(file, 0x801A);          // 28: pop
        Op(file, 0x8022);          // 30
        Op(file, 0x801A);          // 32: pop
        Op(file, 0x8023);          // 34
        Op(file, 0x8024);          // 36
        Op(file, 0x8025);          // 38: pop_flags_return_val_exit
        Op(file, 0x8026);          // 40

        Int32(file, names.Length); // procedure count
        int[] bodies = [procAAt, procBAt];
        for (int i = 0; i < names.Length; i++)
        {
            Int32(file, nameOffsets[i]); // nameOffset
            Int32(file, 0);              // flags
            Int32(file, 0);              // time
            Int32(file, 0);              // conditionOffset
            Int32(file, bodies[i]);      // bodyOffset
            Int32(file, 0);              // argumentCount
        }

        Int32(file, idBytes.Count);
        file.AddRange(idBytes);
        Int32(file, unchecked((int)0xFFFFFFFF)); // static strings absent

        file.AddRange(prologue);
        file.AddRange(procA);
        file.AddRange(procB);
        return file.ToArray();
    }

    // ---- harness -----------------------------------------------------------

    private static MapObject MakeObject(int id, int sid) => new()
    {
        Id = id,
        HexTile = 1,
        X = 0,
        Y = 0,
        Frame = 0,
        Rotation = 0,
        Fid = Fid.Build(ObjectType.Critter, 0),
        Flags = 0,
        Pid = 0x01000000,
        Sid = sid,
    };

    private static MapFile MakeMap(params int[] sids)
    {
        var map = new MapFile
        {
            Header = new MapHeader(20, "testmap.map", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            GlobalVariables = [],
            LocalVariables = [],
            Elevations = new MapElevation?[MapFile.ElevationCount],
        };
        foreach (int sid in sids)
            map.ScriptsBySid[sid] = new MapScriptRecord(0, -1, 0);
        return map;
    }

    private ScriptHost MakeHost(out GameFileSystem vfs)
    {
        vfs = GameFileSystem.Open(_tempDir);
        return new ScriptHost(vfs, ScriptList.Load(vfs), new ProtoDatabase(vfs));
    }

    // ---- the invariants ----------------------------------------------------

    [Fact]
    public void ModuleGlobalsPersistAcrossProcRuns()
    {
        ScriptHost host = MakeHost(out GameFileSystem vfs);
        using (vfs)
        {
            MapFile map = MakeMap(5);
            MapObject obj = MakeObject(1, 5);

            Assert.NotNull(host.RunObjectProc(obj, map, null, "critter_p_proc"));     // global[0] := 42
            Assert.NotNull(host.RunObjectProc(obj, map, null, "timed_event_p_proc")); // GVAR[777] := global[0]

            // Pre-fix each proc got a fresh VM, re-zeroing the global -> 777 read 0.
            Assert.Equal(42, host.GlobalVars.GetValueOrDefault(777, -1));
        }
    }

    [Fact]
    public void ModuleGlobalsAreIsolatedPerSid()
    {
        // Two critters sharing one .int have INDEPENDENT globals — fo2ce allocates
        // a fresh Program per Script (scripts.cc:661-671), keyed by sid, not path.
        ScriptHost host = MakeHost(out GameFileSystem vfs);
        using (vfs)
        {
            MapFile map = MakeMap(5, 6);

            Assert.NotNull(host.RunObjectProc(MakeObject(1, 5), map, null, "critter_p_proc"));
            Assert.NotNull(host.RunObjectProc(MakeObject(2, 6), map, null, "timed_event_p_proc"));

            Assert.Equal(0, host.GlobalVars.GetValueOrDefault(777, -1));
        }
    }

    [Fact]
    public void ClearScriptVmsResetsGlobalsToInitValues()
    {
        // Map unload frees each Program's stackValues (scripts.cc:2405 programListFree):
        // module globals reset per visit, unlike LVARs.
        ScriptHost host = MakeHost(out GameFileSystem vfs);
        using (vfs)
        {
            MapFile map = MakeMap(5);
            MapObject obj = MakeObject(1, 5);

            Assert.NotNull(host.RunObjectProc(obj, map, null, "critter_p_proc"));
            host.ClearScriptVms();
            Assert.NotNull(host.RunObjectProc(obj, map, null, "timed_event_p_proc"));

            Assert.Equal(0, host.GlobalVars.GetValueOrDefault(777, -1));
        }
    }
}
