using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

/// <summary>Oracle F16: a sid runs its own script state even when another sid shares its owner (the
/// vanilla import's stale obj_dude slot). Hermetic: hand-assembled lvartest.int, 1 LVAR:
///   critter_p_proc:    LVAR[0] := 99
///   map_update_p_proc: GVAR[777] := LVAR[0]
///   map_enter_p_proc:  (empty)</summary>
[Collection(nameof(OracleTraceTests))] // Task 2's order test uses the static OracleTrace
public class StaleDudeScriptTests : IDisposable
{
    private const int StaleSid = 0x04004650;
    private readonly string _tempDir;

    public StaleDudeScriptTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hexwaste-staledude-" + Guid.NewGuid().ToString("N"));
        string scriptsDir = Path.Combine(_tempDir, "data", "scripts");
        Directory.CreateDirectory(scriptsDir);
        File.WriteAllBytes(Path.Combine(_tempDir, "master.dat"),
            [0, 0, 0, 0, 4, 0, 0, 0, 12, 0, 0, 0]); // the 12-byte empty DAT2
        File.WriteAllText(Path.Combine(scriptsDir, "scripts.lst"), "lvartest.int ; # local_vars=1\n");
        File.WriteAllBytes(Path.Combine(scriptsDir, "lvartest.int"), TestIntScript.Build(
            ("critter_p_proc", c =>
            {
                TestIntScript.PushInt(c, 0);           // LVAR index
                TestIntScript.PushInt(c, 99);          // value
                TestIntScript.Op(c, 0x80C2);           // set_local_var
            }),
            ("map_update_p_proc", c =>
            {
                TestIntScript.PushInt(c, 777);         // GVAR index
                TestIntScript.PushInt(c, 0);           // LVAR index
                TestIntScript.Op(c, 0x80C1);           // get_local_var
                TestIntScript.Op(c, 0x80C6);           // set_global_var
            }),
            ("map_enter_p_proc", _ => { })));
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private ScriptHost MakeHost()
    {
        GameFileSystem vfs = GameFileSystem.Open(_tempDir);
        return new ScriptHost(vfs, ScriptList.Load(vfs), new ProtoDatabase(vfs));
    }

    private static MapObject Obj(int id, int sid, ObjectType type) => new()
    {
        Id = id, HexTile = 100 + id, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = Fid.Build(type, 1), Flags = 0, Pid = 0x01000002, Sid = sid,
    };

    private static MapFile EmptyMap(int[] localVars) => new()
    {
        Header = new MapHeader(20, "stale.map", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        GlobalVariables = [],
        LocalVariables = localVars,
        Elevations = new MapElevation?[MapFile.ElevationCount],
    };

    [Fact]
    public void TwoSidsSharingAnOwnerKeepSeparateLocalVars()
    {
        ScriptHost host = MakeHost();
        const int OwnSid = 0x04000001;
        MapFile map = EmptyMap([10, 20]);
        map.ScriptsBySid[OwnSid] = new MapScriptRecord(0, 0, 1);  // LVAR seeded from slot 0 (10)
        map.ScriptsBySid[StaleSid] = new MapScriptRecord(0, 1, 1); // LVAR seeded from slot 1 (20)
        MapObject owner = Obj(18000, OwnSid, ObjectType.Critter);

        host.RunScriptProc(OwnSid, owner, map, owner, 0, -1, "critter_p_proc");
        host.RunScriptProc(StaleSid, owner, map, owner, 0, -1, "map_update_p_proc");

        Assert.Equal(20, host.GlobalVars[777]); // the stale sid read its own seeded LVAR, not 99
        Dictionary<int, int[]> lvars = host.ExportLocalVars("stale.map");
        Assert.Equal([99], lvars[OwnSid]);
        Assert.Equal([20], lvars[StaleSid]);
    }
}
