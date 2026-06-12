using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

public class BarterSessionTests
{
    [GameDataFact]
    public void TubbyOpensBarterAgainstHisStockBox()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        var host = new ScriptHost(vfs, ScriptList.Load(vfs), protos);

        using Stream stream = vfs.OpenRead(@"maps\denbus1.map");
        MapFile map = MapFile.Load(stream, protos);

        // A real dude with the premade sheet — without stats every dialog
        // serves the low-INT branch and the trade option never appears.
        var dude = new MapObject
        {
            Id = -1,
            HexTile = 16908,
            X = 0,
            Y = 0,
            Frame = 0,
            Rotation = 0,
            Fid = 0x01000000,
            Flags = 0,
            Pid = 0x01000001,
            Sid = -1,
        };
        using (Stream gcdStream = vfs.OpenRead(@"premade\player.gcd"))
        {
            Combat.GcdFile gcd = Combat.GcdFile.Load(gcdStream);
            host.StatsResolver = obj => obj == dude ? gcd.Stats : null;
        }

        IEnumerable<MapObject> scripted = map.Elevations
            .Where(e => e is not null)
            .SelectMany(e => e!.Objects)
            .Where(o => o.Sid != -1);
        host.RunMapEnter(map, scripted, dude);

        MapObject tubby = map.Elevations[0]!.Objects.First(o => o.Sid == 0x04000002);
        ScriptHost.DialogSession? session = host.StartDialog(tubby, map, dude, out _);
        Assert.NotNull(session);
        Assert.Contains(session.Options, o => o.Contains("trade", StringComparison.OrdinalIgnoreCase));

        // Option 1 = "Yes, lets trade." → dcTubby Node996 calls gdialog_barter.
        session.Choose(0);
        Assert.True(session.TakeBarterRequest(out int modifier));
        Assert.Equal(0, modifier); // gdialog_barter(0) OVERWRITES the -30 set_barter_mod

        // The talk epilogue already returned stock to the box — the session
        // tracks it; the box holds goods AND the restock caps.
        Assert.NotNull(session.StockBox);
        Assert.NotEmpty(session.StockBox.Inventory);
        Assert.True(host.CapsTotal(session.StockBox) > 0);

        // The flag is one-shot.
        Assert.False(session.TakeBarterRequest(out _));
    }
}

public class OpeningMapsSmokeTests
{
    [GameDataTheory]
    [InlineData("artemple.map")]
    [InlineData("arcaves.map")]
    [InlineData("arvillag.map")]
    [InlineData("klamall.map")]
    [InlineData("denbus1.map")]
    [InlineData("denbus2.map")]
    public void MapEnterRunsCleanly(string mapName)
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        var host = new ScriptHost(vfs, ScriptList.Load(vfs), protos);

        using Stream stream = vfs.OpenRead($@"maps\{mapName}");
        MapFile map = MapFile.Load(stream, protos);

        IEnumerable<MapObject> scripted = map.Elevations
            .Where(e => e is not null)
            .SelectMany(e => e!.Objects)
            .Where(o => o.Sid != -1);

        // ScriptHost swallows script-level errors by design; this asserts the
        // host itself never throws across the whole opening set.
        host.RunMapEnter(map, scripted, dude: null);
    }
}
