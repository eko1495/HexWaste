using Hexwaste.Formats;
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

MapFile map;
using (Stream stream = vfs.OpenRead($@"maps\{mapName}"))
    map = MapFile.Load(stream, protos);

MapHeader h = map.Header;
Console.WriteLine($"map '{h.Name}' (version {h.Version}, index {h.Index})");
Console.WriteLine($"  entering: tile {h.EnteringTile}, elevation {h.EnteringElevation}, rotation {h.EnteringRotation}");
Console.WriteLine($"  vars: {map.GlobalVariables.Length} global, {map.LocalVariables.Length} local");
Console.WriteLine($"  flags: 0x{h.Flags:X}, darkness {h.Darkness}");

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

    var critters = elev.Objects.Where(o => Fid.Type(o.Fid) == ObjectType.Critter).ToList();
    if (critters.Count > 0)
    {
        Console.WriteLine($"    critters x{critters.Count}:");
        foreach (var c in critters.OrderBy(c => c.HexTile))
        {
            int pkt = c.AiPacket;
            if (pkt == 0)
                try { pkt = protos.Get(c.Pid).Critter?.AiPacket ?? 0; } catch { /* unknown proto */ }
            Console.WriteLine($"      hex {c.HexTile} pid 0x{c.Pid:X} aiPacket {pkt} hp {c.CurrentHp}");
        }
    }

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
