using FalloutPoc.Formats.Int;
using FalloutPoc.Formats.Map;
using FalloutPoc.Formats.Proto;

namespace FalloutPoc.Formats.Tests;

public class SaveStateRoundTripTests
{
    [Fact]
    public void MultiMapModelRoundTrips()
    {
        var state = new SaveState
        {
            Map = "denbus1.map",
            DudeTile = 16321,
            Elevation = 0,
            ClockTicks = 302400,
            GlobalVars = { [5] = 2 },
            DudeInventory = { new SaveState.SavedItem(41, 120) },
            LocalVars = { ["denbus1.map"] = new Dictionary<int, int[]> { [3] = [1, 0, 7] } },
        };
        state.VisitedMaps["denbus2.map"] = new SaveState.MapDelta
        {
            Doors = { new SaveState.SavedDoor(15000, 0x02000021, Open: true, Locked: false) },
            TakenOrdinals = { 17, 304 },
            Created = { new SaveState.CreatedObject(0x0700004A, 16000, 0, 1) },
            ContainerInventories = { [55] = [new SaveState.SavedItem(41, 50)] },
            MapVars = [9, 8, 7],
        };

        SaveState? loaded = SaveState.FromJson(state.ToJson());

        Assert.NotNull(loaded);
        Assert.Equal(state.Map, loaded.Map);
        Assert.Equal(2, loaded.GlobalVars[5]);
        Assert.Equal(new SaveState.SavedItem(41, 120), Assert.Single(loaded.DudeInventory));
        Assert.Equal([1, 0, 7], loaded.LocalVars["denbus1.map"][3]);

        SaveState.MapDelta delta = loaded.VisitedMaps["denbus2.map"];
        Assert.Equal(new SaveState.SavedDoor(15000, 0x02000021, true, false), Assert.Single(delta.Doors));
        Assert.Equal([17, 304], delta.TakenOrdinals);
        Assert.Equal(new SaveState.CreatedObject(0x0700004A, 16000, 0, 1), Assert.Single(delta.Created));
        Assert.Equal(new SaveState.SavedItem(41, 50), Assert.Single(delta.ContainerInventories[55]));
        Assert.Equal([9, 8, 7], delta.MapVars);
    }
}

public class ScriptHostTransitionTests
{
    [GameDataFact]
    public void LocalVarsSurvivePristineReloadAndHandlesReset()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        var host = new ScriptHost(vfs, ScriptList.Load(vfs), protos);

        MapFile LoadDen()
        {
            using Stream stream = vfs.OpenRead(@"maps\denbus1.map");
            return MapFile.Load(stream, protos);
        }

        MapFile first = LoadDen();
        host.RunMapEnter(first, first.Elevations[0]!.Objects.Where(o => o.Sid != -1), dude: null);
        string mapName = first.Header.Name;
        Assert.NotEmpty(host.ExportLocalVars(mapName)); // map_enter allocated slices

        // Handles must not outlive a map transition (the phase-5 leak).
        MapObject probe = first.Elevations[0]!.Objects[0];
        int handle = host.HandleOf(probe);
        Assert.True(handle >= 1);
        host.ResetHandles();
        Assert.Null(host.ObjectOf(handle));

        // Name-keyed slices: a sentinel (fake sid no script touches) survives
        // a pristine reload + revisit map_enter on a DIFFERENT MapFile
        // instance — the old instance-keyed dictionary forgot everything and
        // leaked ~590 KB per transition.
        host.ImportLocalVars(mapName, new Dictionary<int, int[]> { [99999] = [42] });
        MapFile second = LoadDen();
        host.RunMapEnter(second, second.Elevations[0]!.Objects.Where(o => o.Sid != -1), dude: null,
            firstRunOverride: false);
        Assert.Equal([42], host.ExportLocalVars(mapName)[99999]);

        Assert.True(host.ExportAllLocalVars().ContainsKey(mapName));
        host.ClearAllLocalVars();
        Assert.Empty(host.ExportAllLocalVars());
    }
}
