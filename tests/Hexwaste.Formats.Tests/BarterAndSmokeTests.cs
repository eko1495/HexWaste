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

public class StartProcGlobalInitTests
{
    [GameDataFact]
    public void MapEnterStartPassPublishesCombatOnlyScriptExports()
    {
        // P1-M1: the Den gang war's dcLara/dcTyler are combat-only (no map_enter_p_proc), so before the
        // start pass their global-init prologue never ran at map enter and their exported gang_2_member_*
        // resolved to undefined->0 for same-map importers (dcG2Grd). RunStartProcs runs every scripted
        // object's global-init (ported from map.cc:1006 scriptsExecStartProc), publishing those exports.
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        var host = new ScriptHost(vfs, ScriptList.Load(vfs), protos);

        using Stream stream = vfs.OpenRead(@"maps\denbus2.map");
        MapFile map = MapFile.Load(stream, protos);
        IEnumerable<MapObject> scripted = map.Elevations
            .Where(e => e is not null)
            .SelectMany(e => e!.Objects)
            .Where(o => o.Sid != -1);

        // Nothing has executed yet: the export is undefined.
        Assert.False(host.ExternalVars.IsDefined("gang_2_member_2"));

        host.RunStartProcs(map, scripted, dude: null);

        // The combat-only exporters' prologues ran → their cross-script variables are published.
        Assert.True(host.ExternalVars.IsDefined("gang_2_member_2"));
        Assert.True(host.ExternalVars.IsDefined("gang_2_member_5"));
    }
}

public class SpatialScriptTests
{
    [GameDataFact]
    public void ArcavesCarriesTheEighteenSpearTraps()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        using Stream stream = vfs.OpenRead(@"maps\arcaves.map");
        MapFile map = MapFile.Load(stream, protos);

        Assert.Equal(18, map.SpatialScripts.Count);
        Assert.All(map.SpatialScripts, sp =>
        {
            Assert.Equal(1, sp.Sid >> 24); // spatial sid type
            Assert.InRange(sp.Radius, 0, 10);
            Assert.True(Hexwaste.Formats.Hex.HexGrid.IsValid(sp.Tile));
        });

        // Spatial triggers run through the host without throwing, and the
        // _scr_SpatialsEnabled gate suppresses them when disabled.
        var host = new ScriptHost(vfs, ScriptList.Load(vfs), protos);
        var mover = new MapObject
        {
            Id = 1,
            HexTile = map.SpatialScripts[0].Tile,
            X = 0,
            Y = 0,
            Frame = 0,
            Rotation = 0,
            Fid = 0x01000000,
            Flags = 0,
            Pid = 0x01000001,
            Sid = -1,
        };
        host.SpatialsEnabled = false;
        host.RunSpatialsAt(map, mover.HexTile, map.SpatialScripts[0].Elevation, mover);
        host.SpatialsEnabled = true;
        host.RunSpatialsAt(map, mover.HexTile, map.SpatialScripts[0].Elevation, mover);
    }
}
