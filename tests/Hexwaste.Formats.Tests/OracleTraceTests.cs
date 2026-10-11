using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;
using Hexwaste.Formats.Trace;

namespace Hexwaste.Formats.Tests;

// All OracleTrace-touching tests live in this one class: OracleTrace is static, and xUnit runs
// a class's tests sequentially, so they cannot race on the shared sink. The collection is also
// non-parallel: the engine hooks (MapObject.HexTile, ScriptHost, CombatEngine) report to the same
// static sink, so another test class moving a critter while a sink is open here would leak lines in.
[CollectionDefinition(nameof(OracleTraceTests), DisableParallelization = true)]
public sealed class OracleTraceCollection;

[Collection(nameof(OracleTraceTests))]
public class OracleTraceTests
{
    [Fact]
    public void WritesExactLineFormats()
    {
        var sw = new StringWriter();
        try
        {
            OracleTrace.Open(sw, "hdr");
            OracleTrace.Clock = () => 42u;
            OracleTrace.Gvar(7, 0, 1);
            OracleTrace.Lvar(-1, 3, 5, 6);
            OracleTrace.Attack(1, 2, 60, true, 9, 0x10);
            OracleTrace.Spawn(5, -2, 100);
            OracleTrace.Move(4, 9, 9);
        }
        finally
        {
            OracleTrace.Clock = null;
            OracleTrace.Close();
        }

        Assert.Equal(
            "# hdr\n" +
            "t=42 GVAR 7 0->1\n" +
            "t=42 LVAR sid=0xFFFFFFFF 3 5->6\n" +
            "t=42 ATTACK obj#1->obj#2 chance=60 hit=1 dmg=9 dflags=0x10\n" +
            "t=42 SPAWN obj#5 pid=0xFFFFFFFE tile=100\n",
            sw.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public void InactiveAfterCloseAndWritesNothing()
    {
        var sw = new StringWriter();
        OracleTrace.Open(sw, "h");
        OracleTrace.Close();
        Assert.False(OracleTrace.Active);
        OracleTrace.Gvar(1, 0, 1);
        Assert.Equal("# h\n", sw.ToString().Replace("\r\n", "\n"));
    }

    private static Map.MapObject Obj(int id, int pid, int tile) => new()
    {
        Id = id, HexTile = tile, X = 0, Y = 0, Frame = 0, Rotation = 0, Fid = 0, Flags = 0, Pid = pid, Sid = -1,
    };

    [Fact]
    public void HexTileSetterTracesCritterMovesOnly()
    {
        var sw = new StringWriter();
        try
        {
            OracleTrace.Open(sw, "h");
            Map.MapObject critter = Obj(7, 0x01000010, 100); // initializer store: construction, not a move
            Map.MapObject item = Obj(8, 0x00000029, 100);
            critter.HexTile = 101;
            critter.HexTile = 101; // from == to: nothing
            critter.HexTile = -1;  // invalid tile (fo2ce objectSetLocation refuses it): nothing
            item.HexTile = 300;    // not a critter: nothing
            OracleTrace.SpawnPlaced(9, 0x01000010, 500); // SPAWN at the pre-placement -1, then the MOVE
            OracleTrace.SpawnPlaced(10, 0x00000029, 500);
        }
        finally
        {
            OracleTrace.Close();
        }

        Assert.Equal(
            "# h\n" +
            "t=0 MOVE obj#7 100->101\n" +
            "t=0 SPAWN obj#9 pid=0x01000010 tile=-1\n" +
            "t=0 MOVE obj#9 -1->500\n" +
            "t=0 SPAWN obj#10 pid=0x00000029 tile=-1\n",
            sw.ToString().Replace("\r\n", "\n"));
    }

    // fallout2-ce scriptsExecMapUpdateScripts() calls only scripts with procs[proc] > 0: kcsulik.int
    // defines map_update_p_proc, ksbboard.int (the Klamath sign board) does not.
    [GameDataFact]
    public void MapUpdateSkipsScriptsWithoutTheProc()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        var host = new ScriptHost(vfs, ScriptList.Load(vfs), protos);
        MapFile map;
        using (Stream stream = vfs.OpenRead(@"maps\kladwtwn.map"))
            map = MapFile.Load(stream, protos);
        var sw = new StringWriter();
        try
        {
            OracleTrace.Open(sw, "hdr");
            host.RunMapUpdate(map, map.Elevations.Where(e => e is not null).SelectMany(e => e!.Objects)
                .Where(o => o.Sid != -1), dude: null);
        }
        finally
        {
            OracleTrace.Close();
        }

        string trace = sw.ToString();
        Assert.Contains("kcsulik.int map_update_p_proc", trace);
        Assert.DoesNotContain("ksbboard.int", trace);
    }
}
