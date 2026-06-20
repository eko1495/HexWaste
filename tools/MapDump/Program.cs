using Hexwaste.Formats;
using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

// MapDump — parses a MAP file and prints a summary.
// usage: MapDump --game-dir <dir> [--map artemple.map]

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
    Console.Error.WriteLine("usage: MapDump --game-dir <dir> [--map artemple.map]");
    return 1;
}

using GameFileSystem vfs = GameFileSystem.Open(gameDir);
var protos = new ProtoDatabase(vfs);
ScriptList scriptList = ScriptList.Load(vfs);

MapFile map;
using (Stream stream = vfs.OpenRead($@"maps\{mapName}"))
    map = MapFile.Load(stream, protos);

MapHeader h = map.Header;
Console.WriteLine($"map '{h.Name}' (version {h.Version}, index {h.Index})");
Console.WriteLine($"  entering: tile {h.EnteringTile}, elevation {h.EnteringElevation}, rotation {h.EnteringRotation}");
Console.WriteLine($"  vars: {map.GlobalVariables.Length} global, {map.LocalVariables.Length} local");
Console.WriteLine($"  flags: 0x{h.Flags:X}, darkness {h.Darkness}");

// map_update_p_proc census (M0 diagnostic): does this map's MAP script or any object/
// spatial script DEFINE map_update_p_proc (SCRIPT_PROC_MAP_UPDATE = 23)? The engine runs
// it on the map script + every object/spatial script that defines it, once on load then
// every 600 game ticks (scripts.cc scriptsExecMapUpdateScripts / mapUpdateEventProcess).
// A purely STATIC check of the .int procedure table (IntProgram.FindProcedure) — no
// bytecode execution — so it cannot perturb anything; it just answers "is it live here?".
{
    Console.WriteLine("  map_update_p_proc census (SCRIPT_PROC 23):");
    int mapIdx = h.ScriptIndex - 1;
    (bool Update, bool Enter, bool UpdateImported)? mp = h.ScriptIndex > 0 ? ScriptProcs(mapIdx) : null;
    string mapVerdict = mp is null
        ? "(no map script)"
        : $"map_update={(mp.Value.Update ? (mp.Value.UpdateImported ? "IMPORTED" : "DEFINED") : "no")}"
          + $" map_enter={(mp.Value.Enter ? "yes" : "no")}";
    Console.WriteLine($"    map script: index {mapIdx} '{scriptList.GetName(mapIdx) ?? "-"}' {mapVerdict}");

    List<int> objIdxs = map.ScriptsBySid.Values.Select(r => r.ScriptListIndex)
        .Concat(map.SpatialScripts.Select(s => s.ScriptListIndex))
        .Where(i => i >= 0)
        .Distinct().OrderBy(i => i).ToList();
    var defining = new List<string>();
    foreach (int idx in objIdxs)
    {
        (bool Update, bool Enter, bool UpdateImported)? p = ScriptProcs(idx);
        if (p is { Update: true })
            defining.Add($"{idx}:{scriptList.GetName(idx) ?? "?"}{(p.Value.UpdateImported ? "(imp)" : "")}");
    }
    Console.WriteLine($"    object/spatial scripts: {objIdxs.Count} distinct, {defining.Count} define map_update"
        + (defining.Count > 0 ? $" [{string.Join(", ", defining)}]" : ""));
    bool anyDefined = (mp?.Update ?? false) || defining.Count > 0;
    Console.WriteLine($"    => map_update_p_proc {(anyDefined ? "IS LIVE on this map" : "is ABSENT (dead code) on this map")}");
}

for (int elevation = 0; elevation < MapFile.ElevationCount; elevation++)
{
    MapElevation? elev = map.Elevations[elevation];
    if (elev is null)
    {
        Console.WriteLine($"  elevation {elevation}: absent");
        continue;
    }

    int floorTiles = 0;
    int roofTiles = 0;
    for (int square = 0; square < MapElevation.SquareGridSize; square++)
    {
        if (elev.FloorTileId(square) != 1)
            floorTiles++;
        if (elev.RoofTileId(square) != 1)
            roofTiles++;
    }

    var byType = elev.Objects
        .GroupBy(o => Fid.Type(o.Fid))
        .OrderBy(g => g.Key)
        .Select(g => $"{g.Count()} {g.Key}")
        .ToList();

    Console.WriteLine($"  elevation {elevation}: {floorTiles} floor tiles, {roofTiles} roof tiles, "
        + $"{elev.Objects.Count} objects ({string.Join(", ", byType)})");

    // Translucency census (P23): objects the engine alpha-blends (glass/steam/energy/red/wall).
    // TRANS_NONE (0x8000) is OPAQUE ("never fade near the dude") so it's excluded from the count.
    var trans = new Dictionary<string, List<(int Pid, int Hex)>>();
    foreach (var o in elev.Objects)
    {
        Hexwaste.Formats.Proto.TransType t;
        try { t = protos.Get(o.Pid).Translucency; } catch { continue; }
        if (t == Hexwaste.Formats.Proto.TransType.None)
            continue;
        string kind = t.ToString();
        (trans.TryGetValue(kind, out var l) ? l : trans[kind] = []).Add((o.Pid, o.HexTile));
    }
    if (trans.Count > 0)
        Console.WriteLine($"    translucent: {string.Join(", ", trans.OrderBy(k => k.Key).Select(k => $"{k.Key} x{k.Value.Count} (e.g. pid 0x{k.Value[0].Pid:X} hex {k.Value[0].Hex})"))}");

    var critters = elev.Objects.Where(o => Fid.Type(o.Fid) == ObjectType.Critter).ToList();
    if (critters.Count > 0)
    {
        Console.WriteLine($"    critters x{critters.Count}:");
        foreach (var c in critters.OrderBy(c => c.HexTile))
        {
            int pkt = c.AiPacket;
            if (pkt == 0)
                try { pkt = protos.Get(c.Pid).Critter?.AiPacket ?? 0; } catch { /* unknown proto */ }
            int scriptIndex = c.Sid != -1 && map.ScriptsBySid.TryGetValue(c.Sid, out MapScriptRecord? rec)
                ? rec.ScriptListIndex : -1;
            Console.WriteLine($"      hex {c.HexTile} pid 0x{c.Pid:X} aiPacket {pkt} hp {c.CurrentHp} script {scriptIndex}");

            // Inventory weapon census (AI best_weapon driver check): pids of carried weapons,
            // marking the wielded one (in-hand flag). A critter with >1 weapon is a best_weapon driver.
            var weps = c.Inventory
                .Where(it => Fid.Type(it.Fid) == ObjectType.Item && IsWeapon(it.Pid))
                .Select(it => $"0x{it.Pid:X}{(it.IsInHand ? "*" : "")}")
                .ToList();
            if (weps.Count > 0)
                Console.WriteLine($"        weapons: {string.Join(", ", weps)} (*=wielded)");
        }
    }

    var containers = elev.Objects.Where(o =>
        Fid.Type(o.Fid) == ObjectType.Item && TryGetSubType(o.Pid) == 1).ToList(); // ITEM_TYPE_CONTAINER
    if (containers.Count > 0)
        Console.WriteLine($"    containers x{containers.Count} (e.g. {string.Join(", ", containers.Take(6).Select(c => $"hex {c.HexTile}"))})");

    var doors = elev.Objects.Where(o =>
        Fid.Type(o.Fid) == ObjectType.Scenery
        && Fid.PidType(o.Pid) == (int)ObjectType.Scenery
        && TryGetSubType(o.Pid) == 0).ToList();
    if (doors.Count > 0)
        Console.WriteLine($"    doors x{doors.Count} (e.g. {string.Join(", ", doors.Take(6).Select(d => $"hex {d.HexTile} flags 0x{d.Flags:X}"))})");

    foreach (var group in elev.Objects
        .Where(o => o.Destination is not null)
        .GroupBy(o => (o.Destination!.Map, o.Destination.Tile, o.Destination.Elevation,
            IsExit: Fid.IsExitGridPid(o.Pid))))
    {
        ((int destMap, int destTile, int destElev, bool isExit), int count) = (group.Key, group.Count());
        string kind = isExit ? "exit grid" : "stairs/ladder";
        string sample = $"hex {group.First().HexTile}";
        Console.WriteLine($"    {kind} x{count} -> map {destMap}, tile {destTile}, elev {destElev} (e.g. {sample})");
    }
}

return 0;

// Static .int procedure-table check for the M0 diagnostic: does scripts.lst[scriptListIndex]
// DEFINE map_update_p_proc / map_enter_p_proc? Returns null if the script can't be loaded.
// An IMPORTED procedure (a forward declaration, never the local handler) is flagged separately.
(bool Update, bool Enter, bool UpdateImported)? ScriptProcs(int scriptListIndex)
{
    string? path = scriptList.GetScriptPath(scriptListIndex);
    if (path is null)
        return null;
    try
    {
        using Stream s = vfs.OpenRead(path);
        IntProgram prog = IntProgram.Load(s);
        int u = prog.FindProcedure("map_update_p_proc");
        int e = prog.FindProcedure("map_enter_p_proc");
        return (u >= 0, e >= 0, u >= 0 && prog.Procedures[u].IsImported);
    }
    catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or EndOfStreamException)
    {
        return null;
    }
}

int TryGetSubType(int pid)
{
    try
    {
        return protos.Get(pid).SubType;
    }
    catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
    {
        return -1;
    }
}

bool IsWeapon(int pid)
{
    try { return protos.Get(pid).Weapon is not null; }
    catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException) { return false; }
}
