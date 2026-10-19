using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;
using Hexwaste.Formats.Trace;

namespace Hexwaste.Formats.Tests;

/// <summary>Oracle F12: object ids from fo2ce's scriptsNewObjectId counter. Hermetic: a 12-byte empty DAT2,
/// hand-assembled idtest.int (map_enter/map_update empty; critter_p_proc creates an unscripted then a
/// scripted misc object), and a 28-byte misc proto.</summary>
[Collection(nameof(OracleTraceTests))] // uses the static OracleTrace
public class ObjectIdsTests : IDisposable
{
    private const int MiscPid = 0x05000001;
    private readonly string _tempDir;

    public ObjectIdsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hexwaste-objectids-" + Guid.NewGuid().ToString("N"));
        string scriptsDir = Path.Combine(_tempDir, "data", "scripts");
        string miscDir = Path.Combine(_tempDir, "data", "proto", "misc");
        Directory.CreateDirectory(scriptsDir);
        Directory.CreateDirectory(miscDir);
        File.WriteAllBytes(Path.Combine(_tempDir, "master.dat"),
            [0, 0, 0, 0, 4, 0, 0, 0, 12, 0, 0, 0]); // the 12-byte empty DAT2
        File.WriteAllText(Path.Combine(scriptsDir, "scripts.lst"), "idtest.int ; # local_vars=0\n");
        File.WriteAllBytes(Path.Combine(scriptsDir, "idtest.int"), TestIntScript.Build(
            ("map_enter_p_proc", _ => { }),
            ("map_update_p_proc", _ => { }),
            ("critter_p_proc", c =>
            {
                foreach (int sid in new[] { -1, 1 }) // unscripted, then scripted (sid is 1-based)
                {
                    TestIntScript.PushInt(c, MiscPid);
                    TestIntScript.PushInt(c, 12345); // tile
                    TestIntScript.PushInt(c, 0);     // elevation
                    TestIntScript.PushInt(c, sid);
                    TestIntScript.Op(c, 0x80B7);     // create_object_sid
                    TestIntScript.Op(c, 0x801A);     // pop the handle
                }
            })));
        File.WriteAllText(Path.Combine(miscDir, "misc.lst"), "test.pro\n");
        var pro = new List<byte>();
        foreach (int v in new[] { MiscPid, 0, Fid.Build(ObjectType.Misc, 1), 0, 0, 0, 0 })
            pro.AddRange([(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v]); // big-endian
        File.WriteAllBytes(Path.Combine(miscDir, "test.pro"), [.. pro]);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private ScriptHost MakeHost()
    {
        GameFileSystem vfs = GameFileSystem.Open(_tempDir);
        return new ScriptHost(vfs, ScriptList.Load(vfs), new ProtoDatabase(vfs));
    }

    private static MapFile ScriptedMap() => new()
    {
        Header = new MapHeader(20, "idtest.map", 0, 0, 0, 0, 1, 0, 0, 0, 0, 0), // ScriptIndex 1 = idtest.int
        GlobalVariables = [],
        LocalVariables = [],
        Elevations = new MapElevation?[MapFile.ElevationCount],
    };

    private static string Traced(Action run)
    {
        var sw = new StringWriter();
        try
        {
            OracleTrace.Open(sw, "hdr");
            run();
        }
        finally
        {
            OracleTrace.Close();
        }
        return sw.ToString();
    }

    [Fact]
    public void NextReturnsEvenIdsFromTwelve()
    {
        var ids = new ObjectIds();
        Assert.Equal(14, ids.Next(_ => false));
        Assert.Equal(16, ids.Next(_ => false));
    }

    [Fact]
    public void OnlyTheFirstIncrementIsChecked() => // 13 in use: 14 is checked and free, 15 is returned
        Assert.Equal(15, new ObjectIds().Next(id => id == 13));

    [Fact]
    public void MapScriptOwnerIsObj16AcrossTheLoadPasses()
    {
        ScriptHost host = MakeHost();
        MapFile map = ScriptedMap();
        string trace = Traced(() =>
        {
            host.RunStartProcs(map, [], null);
            host.RunMapEnter(map, [], null);
            host.RunMapUpdate(map, [], null);
        });
        Assert.Contains("PROC idtest.int map_enter_p_proc self=obj#16", trace);
        Assert.Contains("PROC idtest.int map_update_p_proc self=obj#16", trace);
        Assert.DoesNotContain("obj#-2", trace);
    }

    [Fact]
    public void CreateObjectTakesASecondIdOnlyWhenScripted()
    {
        ScriptHost host = MakeHost();
        MapFile map = ScriptedMap();
        map.Elevations[0] = new MapElevation { Squares = new int[MapElevation.SquareGridSize] };
        const int OwnerSid = 0x03000001;
        map.ScriptsBySid[OwnerSid] = new MapScriptRecord(0, -1, 0);
        var owner = new MapObject
        {
            Id = 100, HexTile = 200, X = 0, Y = 0, Frame = 0, Rotation = 0,
            Fid = Fid.Build(ObjectType.Scenery, 1), Flags = 0, Pid = 0x02000001, Sid = OwnerSid,
        };
        string trace = Traced(() =>
        {
            host.RunStartProcs(map, [], null);                                        // owner: 14, 16
            host.RunScriptProc(OwnerSid, owner, map, null, 0, -1, "critter_p_proc"); // 18; 20 then 22
        });
        Assert.Contains("SPAWN obj#18 pid=0x05000001", trace);
        Assert.Contains("SPAWN obj#20 pid=0x05000001", trace);
        List<MapObject> created = map.Elevations[0]!.Objects.Where(o => o.Pid == MiscPid).ToList();
        Assert.Equal([18, 22], created.Select(o => o.Id));
        Assert.All(created, o => Assert.True(o.Spawned));
    }
}
