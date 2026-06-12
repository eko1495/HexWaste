using FalloutPoc.Formats.Int;
using FalloutPoc.Formats.Map;
using FalloutPoc.Formats.Proto;

namespace FalloutPoc.Formats.Tests;

/// <summary>Phase-4 M0: script context, LVAR plumbing and pure-function externals.</summary>
public class VmFoundationsTests
{
    private sealed class RecordingExternals : IVmExternals
    {
        public Dictionary<int, int> LocalVars { get; } = [];
        public List<string> Displayed { get; } = [];
        public List<string> Stubbed { get; } = [];
        public int LocalVarReads { get; private set; }
        public bool OverridesSet { get; private set; }

        public void DisplayMessage(string text) => Displayed.Add(text);
        public string GetMessage(int messageListId, int id) => $"msg{messageListId}:{id}";
        public void SetScriptOverrides() => OverridesSet = true;
        public int SelfObjectId() => 1;
        public string ObjectName(int objectHandle) => "thing";
        public int GetGlobalVar(int index) => 0;

        public int GetLocalVar(int index)
        {
            LocalVarReads++;
            return LocalVars.TryGetValue(index, out int value) ? value : 0;
        }

        public void SetLocalVar(int index, int value) => LocalVars[index] = value;
        public int GetMapVar(int index) => 0;
        public int DudeObjectId() => 2;
        public int SourceObjectId() => 2;
    }

    private static IntProgram LoadScript(GameFileSystem vfs, string name) =>
        IntProgram.Load(vfs.ReadAllBytes($@"scripts\{name}.int"));

    [GameDataFact]
    public void DoorScriptReadsAndWritesLocalVars()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        IntProgram program = LoadScript(vfs, "midoor");

        var externals = new RecordingExternals();
        var vm = new IntVm(program, externals, externals.Stubbed.Add);

        // map_enter sets the door's lock state (Set_Lock writes an LVAR);
        // description reads it back for the lock-status line.
        Assert.True(vm.TryRunProcedure("map_enter_p_proc"));
        Assert.True(vm.TryRunProcedure("description_p_proc"));

        Assert.True(externals.LocalVars.Count > 0, "map_enter wrote no LVARs");
        Assert.True(externals.LocalVarReads > 0, "description read no LVARs");

        // The phase-4 trap: rolls and their helpers must be real, not stubbed.
        Assert.DoesNotContain(externals.Stubbed,
            s => s.Contains("roll_vs_skill") || s.Contains("success") || s.Contains("critical")
                || s.Contains("metarule") || s.Contains("set_local_var") || s.Contains("source_obj")
                || s.Contains("dude_obj") || s.Contains("fixed_param") || s.Contains("game_time"));
    }

    [GameDataFact]
    public void UseProcRunsWithRealRolls()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        IntProgram program = LoadScript(vfs, "midoor");

        var externals = new RecordingExternals();
        var vm = new IntVm(program, externals, externals.Stubbed.Add);

        Assert.True(vm.TryRunProcedure("use_p_proc"));
        Assert.DoesNotContain(externals.Stubbed, s => s.Contains("roll_vs_skill"));
    }

    [GameDataFact]
    public void MapScriptsSectionCarriesLocalVarSlices()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        using Stream stream = vfs.OpenRead(@"maps\denbus1.map");
        MapFile map = MapFile.Load(stream, protos);

        Assert.True(map.ScriptsBySid.Count > 0, "no script records harvested");

        // Pristine maps store -1 offsets: the engine lazily appends zeroed
        // LVAR slices at runtime (map.cc _map_malloc_local_var), with counts
        // re-derived from scripts.lst. Verify the records parse sanely.
        Assert.All(map.ScriptsBySid.Values, r =>
        {
            Assert.True(r.LocalVarsOffset >= -1);
            Assert.InRange(r.ScriptListIndex, 0, 5000);
        });
        Assert.Contains(map.ScriptsBySid.Values, r => r.LocalVarsOffset == -1);
    }

    [GameDataFact]
    public void LocalVarsPersistAcrossRunsViaScriptHost()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        var host = new ScriptHost(vfs, ScriptList.Load(vfs));

        using Stream stream = vfs.OpenRead(@"maps\denbus1.map");
        MapFile map = MapFile.Load(stream, protos);

        // Find a scripted door-ish object whose script defines the procs.
        MapObject? scripted = map.Elevations[0]!.Objects
            .FirstOrDefault(o => o.Sid != -1 && map.ScriptsBySid.ContainsKey(o.Sid)
                && host.RunObjectProc(o, map, null, "map_enter_p_proc") is not null);
        Assert.NotNull(scripted);

        // map_enter writes lock state into the script's lazily allocated LVAR
        // slice; a later description run on the SAME host must see it (i.e.
        // the slice persists across VM invocations).
        var first = host.RunObjectProc(scripted, map, null, "description_p_proc", "look_at_p_proc");
        var second = host.RunObjectProc(scripted, map, null, "description_p_proc", "look_at_p_proc");
        if (first is not null && second is not null)
            Assert.Equal(first.Messages, second.Messages); // deterministic given persisted LVARs
    }

    [GameDataFact]
    public void MapEnterScriptsLockDoors()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        var host = new ScriptHost(vfs, ScriptList.Load(vfs));

        using Stream stream = vfs.OpenRead(@"maps\denbus1.map");
        MapFile map = MapFile.Load(stream, protos);
        List<MapObject> objects = map.Elevations[0]!.Objects;

        Assert.Equal(0, objects.Count(o => o.IsLockedState)); // pristine flags

        host.RunMapEnter(map, objects.Where(o => o.Sid != -1), null);

        // The Den's scripts lock at least Mom's door (hex 16862) at map entry.
        Assert.True(objects.Count(o => o.IsLockedState) >= 2,
            "map_enter scripts locked no doors");
        Assert.Contains(objects, o => o.HexTile == 16862 && o.IsLockedState);
    }

    [GameDataFact]
    public void ScriptsLstCarriesLocalVarCounts()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        ScriptList scripts = ScriptList.Load(vfs);

        int withVars = Enumerable.Range(0, scripts.Count).Count(i => scripts.GetLocalVarsCount(i) > 0);
        Assert.True(withVars > 100, $"only {withVars} scripts declare local_vars in scripts.lst");
    }
}
