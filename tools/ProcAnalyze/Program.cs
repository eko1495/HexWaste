using Hexwaste.Formats;
using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

string? gameDir = null;
string mapName = "artemple.map";
bool questCensus = false;
bool mapObjects = false;
int questPathsGvar = -2; // -2 = off, -1 = all quests, >= 0 = one gvar
int bitScanGvar = -2;    // -2 = off, -1 = all task gvars, >= 0 = one gvar

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--game-dir" && i + 1 < args.Length)
        gameDir = args[++i];
    else if (args[i] == "--map" && i + 1 < args.Length)
        mapName = args[++i];
    else if (args[i] == "--quest-census")
        questCensus = true;
    else if (args[i] == "--map-objects")
        mapObjects = true;
    else if (args[i] == "--quest-paths")
        questPathsGvar = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? int.Parse(args[++i]) : -1;
    else if (args[i] == "--bit-scan")
        bitScanGvar = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? int.Parse(args[++i]) : -1;
}

if (gameDir is null)
{
    Console.Error.WriteLine("usage: ProcAnalyze --game-dir <dir> (--map <mapname> [--map-objects] | --quest-census | --quest-paths [gvar])");
    return 1;
}

using GameFileSystem vfs = GameFileSystem.Open(gameDir);
var protos = new ProtoDatabase(vfs);
ScriptList scriptList = ScriptList.Load(vfs);

// P137 bit-scan: dump every single-bit task-flag CHECK (global(G) & mask) and SET (global(G) |= mask)
// across all scripts, grouped by (gvar,mask). The ground-truth for the bit-level prerequisite
// resolver — a CHECK in one script's completer + a SET of the SAME (gvar,mask) in another script's
// node is a cross-NPC prerequisite the gvar-level analysis (plan §10) couldn't distinguish.
if (bitScanGvar != -2)
{
    var checks = new List<(string Script, int Gvar, int Mask)>();
    var sets = new List<(string Script, int Gvar, int Mask)>();
    for (int idx = 0; idx < scriptList.Count; idx++)
    {
        if (scriptList.GetName(idx) is not { } name || scriptList.GetScriptPath(idx) is not { } path
            || !vfs.Exists(path))
            continue;
        QuestPathScan.Result scan;
        try { scan = QuestPathScan.Scan(vfs.ReadAllBytes(path)); }
        catch { continue; }
        foreach (QuestPathScan.BitCheck c in scan.BitChecks)
            if (bitScanGvar < 0 || c.Gvar == bitScanGvar) checks.Add((name, c.Gvar, c.Mask));
        foreach (QuestPathScan.BitSet s in scan.BitSets)
            if (bitScanGvar < 0 || s.Gvar == bitScanGvar) sets.Add((name, s.Gvar, s.Mask));
    }
    // Only (gvar,mask) pairs with BOTH a check and a set are cross-NPC prerequisites of interest.
    var setKeys = sets.Select(s => (s.Gvar, s.Mask)).ToHashSet();
    var checkKeys = checks.Select(c => (c.Gvar, c.Mask)).ToHashSet();
    foreach ((int Gvar, int Mask) key in setKeys.Intersect(checkKeys)
                 .OrderBy(k => k.Gvar).ThenBy(k => k.Mask))
    {
        string setters = string.Join(",", sets.Where(s => (s.Gvar, s.Mask) == key)
            .Select(s => s.Script).Distinct());
        string checkers = string.Join(",", checks.Where(c => (c.Gvar, c.Mask) == key)
            .Select(c => c.Script).Distinct());
        Console.WriteLine($"bit-scan: gvar={key.Gvar} mask=0x{key.Mask:X} " +
            $"set-by=[{setters}] checked-by=[{checkers}]");
    }
    return 0;
}

// P128 quest-path finder: for each quest gvar, find the writer scripts, attribute each
// completing write to its PROCEDURE, and — when the writer proc is reachable from
// talk_p_proc over the static dialog graph (giq/gsay_option + call edges) — print the
// option-pick chain that reaches it. The fixture-authoring guide for the campaign-QA
// arc. STATE-only output: script file names, proc identifiers, gvar numbers, ordinals.
if (questPathsGvar != -2)
{
    IReadOnlyList<Quest> quests;
    using (Stream qs = vfs.OpenRead(@"data\quests.txt"))
        quests = QuestLog.Parse(qs);
    if (questPathsGvar >= 0)
        quests = [.. quests.Where(q => q.Gvar == questPathsGvar)];

    // Scan every script once; keep per-script results for the wanted gvars.
    var wanted = quests.Select(q => q.Gvar).ToHashSet();
    var findings = new List<(string Script, QuestPathScan.Result Scan, QuestPathScan.ConstWrite Write)>();
    for (int idx = 0; idx < scriptList.Count; idx++)
    {
        if (scriptList.GetName(idx) is not { } name || scriptList.GetScriptPath(idx) is not { } path
            || !vfs.Exists(path))
            continue;
        QuestPathScan.Result scan;
        try { scan = QuestPathScan.Scan(vfs.ReadAllBytes(path)); }
        catch (InvalidDataException) { continue; }
        foreach (QuestPathScan.ConstWrite w in scan.Writes)
            if (wanted.Contains(w.Gvar))
                findings.Add((name, scan, w));
    }

    foreach (Quest q in quests)
    {
        var mine = findings.Where(f => f.Write.Gvar == q.Gvar).ToList();
        Console.WriteLine($"quest gvar={q.Gvar} display>={q.DisplayThreshold} completed>={q.CompletedThreshold}"
            + $" writes={mine.Count}");
        foreach ((string script, QuestPathScan.Result scan, QuestPathScan.ConstWrite w) in mine
                     .OrderByDescending(f => f.Write.Value))
        {
            string procName = scan.Program.Procedures[w.Proc].Name;
            string reach;
            int talk = scan.Program.FindProcedure("talk_p_proc");
            if (talk >= 0 && QuestPathScan.FindPath(scan, talk, w.Proc) is { } path)
                reach = path.Count == 0 ? "IS talk_p_proc" : "talk_p_proc " + string.Join(" ", path);
            else
                reach = $"non-dialog (trigger = {procName})";
            string marker = w.Value >= q.CompletedThreshold ? "COMPLETES" : "advances ";
            Console.WriteLine($"  {marker} {script}: {procName} := {w.Value}  [{reach}]");
        }
    }
    return 0;
}

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

// --map-objects: the tile-discovery aid for quest-fixture authoring. Lists every scripted
// object (critter/scenery) as elev/tile/pid/script so a golden can target it by tile via
// --talk-seq / --kill / --use-on. STATE-only (no game text).
if (mapObjects)
{
    Console.WriteLine($"map-objects: {mapName}");
    for (int e = 0; e < map.Elevations.Length; e++)
    {
        MapElevation? elev = map.Elevations[e];
        if (elev is null)
            continue;
        foreach (MapObject o in elev.Objects.OrderBy(o => o.HexTile))
        {
            if (o.Sid < 0 || !map.ScriptsBySid.TryGetValue(o.Sid, out MapScriptRecord? rec)
                || rec.ScriptListIndex < 0)
                continue;
            string script = scriptList.GetName(rec.ScriptListIndex) ?? $"script_{rec.ScriptListIndex}";
            Console.WriteLine($"  elev={e} tile={o.HexTile} pid={o.Pid} script={script}");
        }
    }
    return 0;
}

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
