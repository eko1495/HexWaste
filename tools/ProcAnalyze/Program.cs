using Hexwaste.Formats;
using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

string? gameDir = null;
string mapName = "artemple.map";

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--game-dir" && i + 1 < args.Length)
        gameDir = args[++i];
    else if (args[i] == "--map" && i + 1 < args.Length)
        mapName = args[++i];
}

if (gameDir is null)
{
    Console.Error.WriteLine("usage: ProcAnalyze --game-dir <dir> --map <mapname>");
    return 1;
}

using GameFileSystem vfs = GameFileSystem.Open(gameDir);
var protos = new ProtoDatabase(vfs);
ScriptList scriptList = ScriptList.Load(vfs);

MapFile map;
using (Stream stream = vfs.OpenRead($@"maps\{mapName}"))
    map = MapFile.Load(stream, protos);

var procInfo = new Dictionary<string, int>
{
    { "start_p_proc", 1 },
    { "spatial_p_proc", 2 },
    { "description_p_proc", 3 },
    { "pickup_p_proc", 4 },
    { "drop_p_proc", 5 },
    { "use_p_proc", 6 },
    { "use_obj_on_p_proc", 7 },
    { "use_skill_on_p_proc", 8 },
    { "talk_p_proc", 11 },
    { "critter_p_proc", 12 },
    { "combat_p_proc", 13 },
    { "damage_p_proc", 14 },
    { "map_enter_p_proc", 15 },
    { "map_exit_p_proc", 16 },
    { "look_at_p_proc", 21 },
    { "timed_event_p_proc", 22 },
    { "map_update_p_proc", 23 },
    { "push_p_proc", 24 },
    { "combat_is_starting_p_proc", 26 },
    { "combat_is_over_p_proc", 27 },
};

var allScriptIndices = new SortedSet<int>();
if (map.Header.ScriptIndex > 0)
    allScriptIndices.Add(map.Header.ScriptIndex - 1);

foreach (var record in map.ScriptsBySid.Values)
    if (record.ScriptListIndex >= 0)
        allScriptIndices.Add(record.ScriptListIndex);

foreach (var spatial in map.SpatialScripts)
    if (spatial.ScriptListIndex >= 0)
        allScriptIndices.Add(spatial.ScriptListIndex);

var procUsage = new Dictionary<string, List<string>>();
foreach (var procName in procInfo.Keys)
    procUsage[procName] = new List<string>();

foreach (int idx in allScriptIndices)
{
    string? path = scriptList.GetScriptPath(idx);
    if (path is null) continue;
    
    string scriptName = scriptList.GetName(idx) ?? $"script_{idx}";
    
    try
    {
        using Stream s = vfs.OpenRead(path);
        IntProgram prog = IntProgram.Load(s);
        
        foreach (var (procName, _) in procInfo)
        {
            int procPos = prog.FindProcedure(procName);
            if (procPos >= 0)
                procUsage[procName].Add($"{idx}:{scriptName}");
        }
    }
    catch { }
}

Console.WriteLine($"Map: {mapName}\n");
Console.WriteLine("Script Procedure Usage:");
foreach (var (procName, scripts) in procUsage.OrderBy(x => x.Key))
{
    if (scripts.Count > 0)
    {
        Console.WriteLine($"  {procName}:");
        foreach (var script in scripts)
            Console.WriteLine($"    {script}");
    }
}

var usedProcs = procUsage.Where(x => x.Value.Count > 0).ToList();
Console.WriteLine($"\nSummary: {usedProcs.Count} proc types used");
return 0;
