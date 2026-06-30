namespace Hexwaste.Formats.Map;

/// <summary>One way into a world area: a target map + position.</summary>
public sealed record AreaEntrance(
    bool StartsOn,
    /// <summary>maps.txt lookup_name (NOT the file name).</summary>
    string MapLookupName,
    int Elevation,
    /// <summary>-1 means use the map's own entering position.</summary>
    int Tile,
    int Rotation);

/// <summary>A location on the worldmap.</summary>
public sealed class WorldArea
{
    public required int Index { get; init; }
    public required string Name { get; init; }

    /// <summary>Position in worldmap pixels (the 1400x1500 tile canvas). Settable so the
    /// wm_area_set_pos (0x80E5) script external can relocate a town marker at runtime.</summary>
    public required int WorldX { get; set; }
    public required int WorldY { get; set; }

    public required string Size { get; init; }
    public required bool StartsOn { get; init; }
    public List<AreaEntrance> Entrances { get; } = [];
}

/// <summary>
/// Worldmap areas from <c>data\city.txt</c>: <c>[Area NN]</c> sections with
/// <c>area_name</c>, <c>world_pos=x,y</c>, <c>size</c> and
/// <c>entrance_N=State,tmX,tmY,MapLookupName,elevation,tile,rotation</c>
/// lines — the same data fallout2-ce's wmAreaInit() consumes
/// (src/worldmap.cc). Encounter/terrain config is intentionally ignored.
/// </summary>
public sealed class CityList
{
    public IReadOnlyList<WorldArea> Areas => _areas;
    private readonly List<WorldArea> _areas = [];

    public static CityList Load(GameFileSystem vfs)
    {
        var list = new CityList();
        using Stream stream = vfs.OpenRead(@"data\city.txt");
        using var reader = new StreamReader(stream);

        int index = -1;
        string name = "";
        int worldX = 0;
        int worldY = 0;
        string size = "Medium";
        bool startsOn = false;
        List<AreaEntrance> entrances = [];

        void Flush()
        {
            if (index < 0)
                return;
            var area = new WorldArea
            {
                Index = index,
                Name = name,
                WorldX = worldX,
                WorldY = worldY,
                Size = size,
                StartsOn = startsOn,
            };
            area.Entrances.AddRange(entrances);
            list._areas.Add(area);
        }

        while (reader.ReadLine() is { } rawLine)
        {
            string line = rawLine.Split(';')[0].Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith("[Area ", StringComparison.OrdinalIgnoreCase) && line.EndsWith(']'))
            {
                Flush();
                index = int.TryParse(line[6..^1].Trim(), out int parsed) ? parsed : -1;
                name = "";
                worldX = 0;
                worldY = 0;
                size = "Medium";
                startsOn = false;
                entrances = [];
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq < 0)
                continue;
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();

            if (key.Equals("area_name", StringComparison.OrdinalIgnoreCase))
            {
                name = value;
            }
            else if (key.Equals("world_pos", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = value.Split(',');
                if (parts.Length == 2)
                {
                    _ = int.TryParse(parts[0].Trim(), out worldX);
                    _ = int.TryParse(parts[1].Trim(), out worldY);
                }
            }
            else if (key.Equals("size", StringComparison.OrdinalIgnoreCase))
            {
                size = value;
            }
            else if (key.Equals("start_state", StringComparison.OrdinalIgnoreCase))
            {
                startsOn = value.Equals("On", StringComparison.OrdinalIgnoreCase);
            }
            else if (key.StartsWith("entrance_", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = value.Split(',');
                if (parts.Length >= 7)
                {
                    entrances.Add(new AreaEntrance(
                        parts[0].Trim().Equals("On", StringComparison.OrdinalIgnoreCase),
                        parts[3].Trim(),
                        ParseOr(parts[4], -1),
                        ParseOr(parts[5], -1),
                        ParseOr(parts[6], 0)));
                }
            }
        }

        Flush();
        return list;
    }

    private static int ParseOr(string text, int fallback) =>
        int.TryParse(text.Trim(), out int value) ? value : fallback;
}
