namespace FalloutPoc.Formats.Map;

/// <summary>
/// Map index → file name registry from <c>data\maps.txt</c>
/// (<c>[Map NNN]</c> sections with <c>map_name=</c> keys) — the same data the
/// engine's worldmap module uses to resolve MapTransition.map indices.
/// </summary>
public sealed class MapList
{
    private readonly Dictionary<int, string> _names = [];

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
                list._names[currentIndex] = line["map_name=".Length..].Trim();
            }
        }

        return list;
    }

    /// <summary>Returns e.g. "artemple.map", or null for unknown indices.</summary>
    public string? GetMapFileName(int index) =>
        _names.TryGetValue(index, out string? name) ? $"{name}.map" : null;
}
