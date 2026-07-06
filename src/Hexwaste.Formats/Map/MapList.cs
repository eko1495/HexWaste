namespace Hexwaste.Formats.Map;

/// <summary>A random-encounter spawn point (maps.txt random_start_point_N).</summary>
public readonly record struct StartPoint(int Elevation, int Tile);

/// <summary>
/// Map index → file name registry from <c>data\maps.txt</c>
/// (<c>[Map NNN]</c> sections with <c>map_name=</c> keys) — the same data the
/// engine's worldmap module uses to resolve MapTransition.map indices.
/// </summary>
public sealed class MapList
{
    private readonly Dictionary<int, string> _names = [];
    private readonly Dictionary<string, int> _byLookupName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> _musicByIndex = [];
    private readonly Dictionary<int, IReadOnlyList<(string Name, int Chance)>> _ambientByIndex = [];
    private readonly Dictionary<string, int> _indexByMapName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _unsaved = [];           // saved=No → transient encounter maps
    private readonly Dictionary<int, List<StartPoint>> _startPoints = [];
    // can_rest_here=Yes/No[,Yes/No,Yes/No] per elevation (worldmap.cc:2683 → the
    // MAP_CAN_REST_ELEVATION_0/1/2 flags 0x08/0x10/0x20). Absent = the engine default
    // (flags init to all-can-rest, wmMapInit).
    private readonly Dictionary<int, bool[]> _canRestByIndex = [];

    public static MapList Load(GameFileSystem vfs)
    {
        var list = new MapList();
        using Stream stream = vfs.OpenRead(@"data\maps.txt");
        using var reader = new StreamReader(stream);

        int currentIndex = -1;
        while (reader.ReadLine() is { } rawLine)
        {
            string line = rawLine.Trim();
            if (line.StartsWith(';') || line.Length == 0)
                continue;

            if (line.StartsWith("[Map ", StringComparison.OrdinalIgnoreCase) && line.EndsWith(']'))
            {
                currentIndex = int.TryParse(line[5..^1].Trim(), out int index) ? index : -1;
            }
            else if (currentIndex >= 0 && line.StartsWith("map_name=", StringComparison.OrdinalIgnoreCase))
            {
                string mapName = line["map_name=".Length..].Trim();
                list._names[currentIndex] = mapName;
                list._indexByMapName.TryAdd(mapName, currentIndex);
            }
            else if (currentIndex >= 0 && line.StartsWith("lookup_name=", StringComparison.OrdinalIgnoreCase))
            {
                string lookup = line["lookup_name=".Length..].Split(';')[0].Trim();
                list._byLookupName.TryAdd(lookup, currentIndex);
            }
            else if (currentIndex >= 0 && line.StartsWith("music=", StringComparison.OrdinalIgnoreCase))
            {
                list._musicByIndex[currentIndex] = line["music=".Length..].Split(';')[0].Trim();
            }
            else if (currentIndex >= 0 && line.StartsWith("ambient_sfx=", StringComparison.OrdinalIgnoreCase))
            {
                list._ambientByIndex[currentIndex] = ParseAmbient(line["ambient_sfx=".Length..].Split(';')[0]);
            }
            else if (currentIndex >= 0 && line.StartsWith("saved=", StringComparison.OrdinalIgnoreCase))
            {
                // saved=No marks a random-encounter map that is regenerated each
                // visit (no save slot); anything else (Yes/absent) is saved.
                if (line["saved=".Length..].Split(';')[0].Trim().StartsWith("No", StringComparison.OrdinalIgnoreCase))
                    list._unsaved.Add(currentIndex);
            }
            else if (currentIndex >= 0 && line.StartsWith("can_rest_here=", StringComparison.OrdinalIgnoreCase))
            {
                // "Yes,No,Yes" per elevation; a short list leaves the tail at the engine
                // default (Yes) — wmSetFlags only runs for the values present.
                string[] parts = line["can_rest_here=".Length..].Split(';')[0].Split(',');
                bool[] canRest = [true, true, true];
                for (int e = 0; e < canRest.Length && e < parts.Length; e++)
                    canRest[e] = !parts[e].Trim().StartsWith("No", StringComparison.OrdinalIgnoreCase);
                list._canRestByIndex[currentIndex] = canRest;
            }
            else if (currentIndex >= 0 && line.StartsWith("random_start_point_", StringComparison.OrdinalIgnoreCase))
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && ParseStartPoint(line[(eq + 1)..]) is { } sp)
                    (list._startPoints.TryGetValue(currentIndex, out List<StartPoint>? pts)
                        ? pts : list._startPoints[currentIndex] = []).Add(sp);
            }
        }

        return list;
    }

    /// <summary>Parse "elev:0, tile_num:19086" → StartPoint; null if no tile.</summary>
    private static StartPoint? ParseStartPoint(string value)
    {
        int elev = 0, tile = -1;
        foreach (string part in value.Split(';')[0].Split(','))
        {
            string[] kv = part.Split(':');
            if (kv.Length != 2 || !int.TryParse(kv[1].Trim(), out int n))
                continue;
            if (kv[0].Trim().Equals("elev", StringComparison.OrdinalIgnoreCase))
                elev = n;
            else if (kv[0].Trim().Equals("tile_num", StringComparison.OrdinalIgnoreCase))
                tile = n;
        }
        return tile >= 0 ? new StartPoint(elev, tile) : null;
    }

    /// <summary>Parse "name:chance, name:chance, ..." → the weighted ambient list. Splits each
    /// comma entry on its FIRST ':' (name, chance); a malformed/unparseable entry is skipped
    /// gracefully (the one "animal:15 animal:10" quirk in the real maps.txt drops cleanly).</summary>
    private static IReadOnlyList<(string Name, int Chance)> ParseAmbient(string value)
    {
        var entries = new List<(string, int)>();
        foreach (string part in value.Split(','))
        {
            int colon = part.IndexOf(':');
            if (colon <= 0)
                continue;
            string name = part[..colon].Trim();
            if (name.Length > 0 && int.TryParse(part[(colon + 1)..].Trim(), out int chance) && chance > 0)
                entries.Add((name, chance));
        }
        return entries;
    }

    /// <summary>The map's weighted ambient sound-effect list (maps.txt ambient_sfx=), or empty.</summary>
    public IReadOnlyList<(string Name, int Chance)> GetAmbientSfx(string mapFileName) =>
        _ambientByIndex.TryGetValue(GetIndexByFileName(mapFileName), out IReadOnlyList<(string, int)>? list)
            ? list : [];

    /// <summary>Returns e.g. "artemple.map", or null for unknown indices.</summary>
    public string? GetMapFileName(int index) =>
        _names.TryGetValue(index, out string? name) ? $"{name}.map" : null;

    /// <summary>True if the map is saved=No — a transient random-encounter map that
    /// regenerates each visit (no save slot). Default true (saved) for real maps.</summary>
    public bool IsTransient(string mapFileName) =>
        _unsaved.Contains(GetIndexByFileName(mapFileName));

    /// <summary>The map's random-encounter spawn points (maps.txt
    /// random_start_point_N), or empty.</summary>
    public IReadOnlyList<StartPoint> GetRandomStartPoints(string mapFileName) =>
        _startPoints.TryGetValue(GetIndexByFileName(mapFileName), out List<StartPoint>? pts)
            ? pts : [];

    /// <summary>wmMapCanRestHere (worldmap.cc:2840): may the dude rest on this map's
    /// elevation? Absent key / unknown map = yes (the engine's default flags). (P118 WATCH.)</summary>
    public bool CanRestHere(string mapFileName, int elevation) =>
        !_canRestByIndex.TryGetValue(GetIndexByFileName(mapFileName), out bool[]? canRest)
        || elevation is < 0 or > 2 || canRest[elevation];

    /// <summary>Resolves a maps.txt lookup_name (used by city.txt entrances) to a map index, or -1.</summary>
    public int FindByLookupName(string lookupName) =>
        _byLookupName.TryGetValue(lookupName.Trim(), out int index) ? index : -1;

    /// <summary>maps.txt index for a map file name (cur_map_index), or -1.</summary>
    public int GetIndexByFileName(string mapFileName) =>
        _indexByMapName.TryGetValue(System.IO.Path.GetFileNameWithoutExtension(mapFileName), out int index)
            ? index : -1;

    /// <summary>Music track name for a map (maps.txt music= key), e.g. "07desert"; null if none.</summary>
    public string? GetMusic(string mapFileName)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(mapFileName);
        return _indexByMapName.TryGetValue(name, out int index)
            && _musicByIndex.TryGetValue(index, out string? music)
            && music.Length > 0 ? music : null;
    }
}
