using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Oracle F6 follow-up: the critter heartbeat now runs procs for critters on every
/// elevation, so the cross-floor gates of obj_can_see_obj / obj_can_hear_obj /
/// tile_distance_objs matter. Hermetic: the StaleHandleGuardTests .int + temp-dir scaffold;
/// critter_p_proc stores the three results into GVARs 1..3.
/// </summary>
public class ElevationGateTests : IDisposable
{
    private readonly string _tempDir;

    public ElevationGateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hexwaste-elevgate-" + Guid.NewGuid().ToString("N"));
        string scriptsDir = Path.Combine(_tempDir, "data", "scripts");
        Directory.CreateDirectory(scriptsDir);
        File.WriteAllBytes(Path.Combine(_tempDir, "master.dat"),
            [0, 0, 0, 0, 4, 0, 0, 0, 12, 0, 0, 0]); // the 12-byte empty DAT2
        File.WriteAllText(Path.Combine(scriptsDir, "scripts.lst"), "testhndl.int ; # local_vars=0\n");
        File.WriteAllBytes(Path.Combine(scriptsDir, "testhndl.int"), BuildScript());
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

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

    private static void Epilogue(List<byte> code)
    {
        PushInt(code, 0);
        Op(code, 0x800D); // d_to_a
        Op(code, 0x8019); // swapa
        Op(code, 0x802A); // pop_to_base
        Op(code, 0x8029); // pop_base
        Op(code, 0x800C); // a_to_d
        Op(code, 0x801C); // pop_return
    }

    private static byte[] BuildScript()
    {
        string[] names = ["critter_p_proc"];
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

        var prologue = new List<byte>();
        Op(prologue, 0x802C); // set_global (no module globals)
        Op(prologue, 0x8003); // leave_critical_section
        Op(prologue, 0x801C); // pop_return -> 18

        var proc = new List<byte>();
        Op(proc, 0x802B);     // push_base
        PushInt(proc, 1);
        Op(proc, 0x80BC); Op(proc, 0x80BF); Op(proc, 0x80DC); // obj_can_see_obj(self_obj, dude_obj)
        Op(proc, 0x80C6);
        PushInt(proc, 2);
        Op(proc, 0x80BC); Op(proc, 0x80BF); Op(proc, 0x80F5); // obj_can_hear_obj(self_obj, dude_obj)
        Op(proc, 0x80C6);
        PushInt(proc, 3);
        Op(proc, 0x80BC); Op(proc, 0x80BF); Op(proc, 0x80D3); // tile_distance_objs(self_obj, dude_obj)
        Op(proc, 0x80C6);
        Epilogue(proc);

        int prologueAt = codeStart;
        int procAt = prologueAt + prologue.Count;

        var file = new List<byte>();
        Op(file, 0x8002);
        PushInt(file, 18);
        Op(file, 0x800D);
        PushInt(file, prologueAt);
        Op(file, 0x8004);
        Op(file, 0x8010);
        Op(file, 0x801A);
        Op(file, 0x8020);
        Op(file, 0x801A);
        Op(file, 0x8021);
        Op(file, 0x801A);
        Op(file, 0x8022);
        Op(file, 0x801A);
        Op(file, 0x8023);
        Op(file, 0x8024);
        Op(file, 0x8025);
        Op(file, 0x8026);

        Int32(file, names.Length);
        Int32(file, nameOffsets[0]);
        Int32(file, 0);
        Int32(file, 0);
        Int32(file, 0);
        Int32(file, procAt);
        Int32(file, 0);

        Int32(file, idBytes.Count);
        file.AddRange(idBytes);
        Int32(file, unchecked((int)0xFFFFFFFF));

        file.AddRange(prologue);
        file.AddRange(proc);
        return file.ToArray();
    }

    private static MapFile MakeMap(int sid)
    {
        var map = new MapFile
        {
            Header = new MapHeader(20, "testmap.map", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            GlobalVariables = [],
            LocalVariables = [],
            Elevations = new MapElevation?[MapFile.ElevationCount],
        };
        map.ScriptsBySid[sid] = new MapScriptRecord(0, -1, 0);
        return map;
    }

    private static MapObject MakeObject(int id, int sid, int tile) => new()
    {
        Id = id, HexTile = tile, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = Fid.Build(ObjectType.Critter, 0), Flags = 0, Pid = 0x01000000, Sid = sid,
    };

    // fo2ce opObjectCanSeeObject / opObjectCanHearObject / opTileDistanceBetweenObjects
    // (interpreter_extra.cc): false / false / 9999 unless both objects share an elevation.
    [Theory]
    [InlineData(0, 0, 1, 1, 3)]
    [InlineData(0, 1, 0, 0, 9999)]
    [InlineData(2, 1, 0, 0, 9999)]
    public void PerceptionAndDistanceRequireTheSameElevation(int selfElev, int dudeElev,
        int see, int hear, int distance)
    {
        using GameFileSystem vfs = GameFileSystem.Open(_tempDir);
        var host = new ScriptHost(vfs, ScriptList.Load(vfs), new ProtoDatabase(vfs));
        MapFile map = MakeMap(5);
        MapObject self = MakeObject(1, 5, 20100);
        MapObject dude = MakeObject(2, -1, 20103);
        host.ElevationProvider = o => o == self ? selfElev : dudeElev;

        Assert.NotNull(host.RunObjectProc(self, map, dude, "critter_p_proc"));

        Assert.Equal(see, host.GlobalVars.GetValueOrDefault(1, -1));
        Assert.Equal(hear, host.GlobalVars.GetValueOrDefault(2, -1));
        Assert.Equal(distance, host.GlobalVars.GetValueOrDefault(3, -1));
    }
}
