namespace Hexwaste.Formats.Map;

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
    private readonly Dictionary<string, int> _indexByMapName = new(StringComparer.OrdinalIgnoreCase);

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
        }

        return list;
    }

    /// <summary>Returns e.g. "artemple.map", or null for unknown indices.</summary>
    public string? GetMapFileName(int index) =>
        _names.TryGetValue(index, out string? name) ? $"{name}.map" : null;

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
