using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

public class CritterScriptOrderTests : IDisposable
{
    private readonly string _tempDir;

    public CritterScriptOrderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hexwaste-critterorder-" + Guid.NewGuid().ToString("N"));
        string scriptsDir = Path.Combine(_tempDir, "data", "scripts");
        Directory.CreateDirectory(scriptsDir);
        File.WriteAllBytes(Path.Combine(_tempDir, "master.dat"),
            [0, 0, 0, 0, 4, 0, 0, 0, 12, 0, 0, 0]); // the 12-byte empty DAT2
        File.WriteAllText(Path.Combine(scriptsDir, "scripts.lst"), "obj_dude.int ; # local_vars=0\n");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private ScriptHost MakeHost()
    {
        GameFileSystem vfs = GameFileSystem.Open(_tempDir);
        return new ScriptHost(vfs, ScriptList.Load(vfs), new ProtoDatabase(vfs));
    }

    private static MapObject Critter(int id, int sid) => new()
    {
        Id = id, HexTile = 100 + id, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = Fid.Build(ObjectType.Critter, 1), Flags = 0, Pid = 0x01000002, Sid = sid,
    };

    private static MapFile MapWith(string name, params MapObject[] critters)
    {
        var map = new MapFile
        {
            Header = new MapHeader(20, name, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            GlobalVariables = [],
            LocalVariables = [],
            Elevations = new MapElevation?[MapFile.ElevationCount],
        };
        map.Elevations[0] = new MapElevation { Squares = new int[MapElevation.SquareGridSize] };
        foreach (MapObject c in critters)
        {
            map.Elevations[0]!.Objects.Add(c);
            if (c.Sid != -1)
            {
                map.ScriptsBySid[c.Sid] = new MapScriptRecord(0, -1, 0);
                map.CritterSids.Add(c.Sid);
            }
        }
        return map;
    }

    [Fact]
    public void MapChangeOrderIsMapCrittersThenDudeThenParty()
    {
        ScriptHost host = MakeHost();
        MapObject dude = Critter(18000, -1);
        MapObject companion = Critter(50, -1);
        MapFile map = MapWith("b.map", Critter(1, 0x04000001), Critter(2, 0x04000002));

        host.RebuildCritterScripts(map);
        host.BindDudeScript(map, dude);
        companion.Sid = host.AllocateSid(map, 0, companion); // party re-bind after map_enter

        Assert.Equal([0x04000001, 0x04000002, ScriptHost.DudeSid, companion.Sid], host.CritterScripts.Sids);
        Assert.Equal(4, companion.Sid >> 24);
        Assert.NotEqual(ScriptHost.DudeSid, companion.Sid);
        Assert.True(host.TryGetScriptOwner(companion.Sid, out MapObject owner));
        Assert.Same(companion, owner);
    }

    [Fact]
    public void RemoveScriptKeepsListAndIndexInSync()
    {
        ScriptHost host = MakeHost();
        MapObject a = Critter(1, 0x04000001), b = Critter(2, 0x04000002), c = Critter(3, 0x04000003);
        MapFile map = MapWith("a.map", a, b, c);
        host.RebuildCritterScripts(map);

        host.RemoveScript(0x04000001);

        Assert.Equal([0x04000003, 0x04000002], host.CritterScripts.Sids);
        Assert.False(host.TryGetScriptOwner(0x04000001, out _));
        Assert.True(host.TryGetScriptOwner(0x04000003, out MapObject owner));
        Assert.Same(c, owner);
    }

    [Fact]
    public void RebuildSkipsSidsNoObjectHolds()
    {
        ScriptHost host = MakeHost();
        MapObject killed = Critter(1, 0x04000001);
        MapFile map = MapWith("c.map", killed, Critter(2, 0x04000002));
        killed.Sid = -1; // the revisit delta replay clears a dead critter's sid (ViewerGame.SaveLoad.cs:164)

        host.RebuildCritterScripts(map);

        Assert.Equal([0x04000002], host.CritterScripts.Sids);
    }

    [Fact]
    public void ExtraOwnerKeepsAnUnheldSidAtItsSectionPosition()
    {
        ScriptHost host = MakeHost();
        MapObject dude = Critter(18000, -1);
        MapFile map = MapWith("e.map", Critter(24, 0x04000001));
        map.CritterSids.Add(0x04004650); // the .SAV's stale obj_dude record: no map object holds it

        host.RebuildCritterScripts(map, 0x04004650, dude);
        host.BindDudeScript(map, dude);

        Assert.Equal([0x04000001, 0x04004650, ScriptHost.DudeSid], host.CritterScripts.Sids);
        Assert.True(host.TryGetScriptOwner(0x04004650, out MapObject owner));
        Assert.Same(dude, owner);
    }

    [Fact]
    public void ItemsGetItemTypeSidsAndStayOffTheList()
    {
        ScriptHost host = MakeHost();
        MapFile map = MapWith("d.map");
        host.RebuildCritterScripts(map);
        var item = new MapObject
        {
            Id = 7, HexTile = 7, X = 0, Y = 0, Frame = 0, Rotation = 0,
            Fid = Fid.Build(ObjectType.Item, 1), Flags = 0, Pid = 0x00000001, Sid = -1,
        };

        int sid = host.AllocateSid(map, 0, item);

        Assert.Equal(3, sid >> 24);
        Assert.Empty(host.CritterScripts.Sids);
    }

    [GameDataFact]
    public void MapCritterSidsAreTheCritterSectionInFileOrder()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        MapFile map;
        using (Stream stream = vfs.OpenRead(@"maps\kladwtwn.map"))
            map = MapFile.Load(stream, protos);

        Assert.NotEmpty(map.CritterSids);
        Assert.All(map.CritterSids, sid => Assert.Equal(4, sid >> 24));
        Assert.Equal(map.ScriptsBySid.Keys.Where(sid => sid >> 24 == 4).Order(), map.CritterSids.Order());
    }
}
