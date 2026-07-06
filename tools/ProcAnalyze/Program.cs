using Hexwaste.Formats;
using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

string? gameDir = null;
string mapName = "artemple.map";
bool questCensus = false;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--game-dir" && i + 1 < args.Length)
        gameDir = args[++i];
    else if (args[i] == "--map" && i + 1 < args.Length)
        mapName = args[++i];
    else if (args[i] == "--quest-census")
        questCensus = true;
}

if (gameDir is null)
{
    Console.Error.WriteLine("usage: ProcAnalyze --game-dir <dir> (--map <mapname> | --quest-census)");
    return 1;
}

using GameFileSystem vfs = GameFileSystem.Open(gameDir);
var protos = new ProtoDatabase(vfs);
ScriptList scriptList = ScriptList.Load(vfs);

// P124 quest-QA sweep: the whole-game quest-completion census. Every scripts.lst entry's
// bytecode is scanned for set_global_var writes (GlobalWriteScan); each quests.txt row is
// then classified by whether its completion threshold is reachable. STATE-only output —
// gvar numbers, script FILE names and counts, never game text.
if (questCensus)
{
    IReadOnlyList<Quest> quests;
    using (Stream qs = vfs.OpenRead(@"data\quests.txt"))
        quests = QuestLog.Parse(qs);

    var constWriters = new Dictionary<int, List<(string Script, int Value)>>(); // gvar -> writes
    var touchers = new Dictionary<int, SortedSet<string>>();                    // gvar -> scripts (upper bound)
    int scanned = 0, missing = 0;
    for (int idx = 0; idx < scriptList.Count; idx++)
    {
        if (scriptList.GetName(idx) is not { } name || scriptList.GetScriptPath(idx) is not { } path)
            continue;
        if (!vfs.Exists(path)) { missing++; continue; }
        GlobalWriteScan.Result r = GlobalWriteScan.Scan(vfs.ReadAllBytes(path));
        scanned++;
        foreach ((int gvar, SortedSet<int> values) in r.ConstWrites)
            foreach (int v in values)
                (constWriters.TryGetValue(gvar, out var l) ? l : constWriters[gvar] = []).Add((name, v));
        if (r.SetGlobalCount > 0)
            foreach (int g in r.PushedInts)
                if (g is >= 0 and < 2048) // plausible gvar range only
                    (touchers.TryGetValue(g, out var t) ? t : touchers[g] = []).Add(name);
    }
    Console.WriteLine($"quest-census: scripts={scanned} missing={missing} quests={quests.Count}");

    int verified = 0, dynamic_ = 0, noWriter = 0, unreachable = 0;
    foreach (Quest q in quests)
    {
        List<(string Script, int Value)> writes = constWriters.GetValueOrDefault(q.Gvar, []);
        SortedSet<string> touch = touchers.GetValueOrDefault(q.Gvar, []);
        int maxConst = writes.Count > 0 ? writes.Max(w => w.Value) : int.MinValue;
        string state;
        if (writes.Count > 0 && maxConst >= q.CompletedThreshold) { state = "VERIFIED"; verified++; }
        else if (touch.Count > 0) { state = "DYNAMIC"; dynamic_++; }
        else if (writes.Count > 0) { state = "UNREACHABLE"; unreachable++; }
        else { state = "NO-WRITER"; noWriter++; }
        Console.WriteLine($"quest gvar={q.Gvar} completed>={q.CompletedThreshold} display>={q.DisplayThreshold}"
            + $" constWrites={writes.Count} maxConst={(writes.Count > 0 ? maxConst : 0)}"
            + $" touchers={touch.Count} -> {state}"
            + (state is "NO-WRITER" or "UNREACHABLE"
                ? $" [{string.Join(",", touch.Take(4))}{string.Join(",", writes.Select(w => w.Script).Distinct().Take(4))}]"
                : ""));
    }
    Console.WriteLine($"quest-census summary: verified={verified} dynamic={dynamic_}"
        + $" unreachable={unreachable} noWriter={noWriter} of {quests.Count}");

    // The two GVARs flagged by the P100 census for a spot check.
    foreach (int g in (int[])[108, 396])
    {
        List<(string Script, int Value)> w = constWriters.GetValueOrDefault(g, []);
        Console.WriteLine($"spot-check gvar={g}: constWrites={w.Count}"
            + $" values=[{string.Join(",", w.Select(x => x.Value).Distinct().OrderBy(v => v))}]"
            + $" scripts=[{string.Join(",", w.Select(x => x.Script).Distinct().OrderBy(s => s))}]"
            + $" touchers={touchers.GetValueOrDefault(g, []).Count}");
    }
    return 0;
}

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

// P100 (Point 2) — the silent quest-gap detector: the FULL external surface the map's scripts
// reference (statically, beyond what map_enter/map_update actually fire), split wired vs stubbed.
var referencedExternals = new SortedSet<int>();
var definedProcs = new SortedSet<string>();

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

        foreach (var proc in prog.Procedures)
            definedProcs.Add(proc.Name);
        foreach (int ext in prog.ReferencedExternals())
            referencedExternals.Add(ext);
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

// Machine-readable census line (stable, sorted) — the gap detector's headline output.
var wiredRefs = referencedExternals.Where(IntVm.WiredExternals.Contains).ToList();
var stubbedRefs = referencedExternals.Where(e => !IntVm.WiredExternals.Contains(e)).ToList();
string mapId = Path.GetFileNameWithoutExtension(mapName);
Console.WriteLine($"\nprocanalyze: map={mapId} scripts={allScriptIndices.Count} defined-procs={definedProcs.Count}"
    + $" externals={referencedExternals.Count} wired={wiredRefs.Count} stubbed={stubbedRefs.Count}");
if (stubbedRefs.Count > 0)
    Console.WriteLine("  stubbed: " + string.Join(" ",
        stubbedRefs.Select(e => $"{ExternalArity.Table[e].Name}(0x{e:X4})")));
return 0;
