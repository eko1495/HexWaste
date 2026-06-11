using FalloutPoc.Formats.Map;
using FalloutPoc.Formats.Proto;

namespace FalloutPoc.Formats.Tests;

public class MapRealGameDataTests
{
    [GameDataFact]
    public void ParsesArtemple()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);

        using Stream stream = vfs.OpenRead(@"maps\artemple.map");
        MapFile map = MapFile.Load(stream, protos);

        Assert.Equal(20, map.Header.Version);
        Assert.Equal("ARTEMPLE.MAP", map.Header.Name);
        Assert.Equal(18492, map.Header.EnteringTile);
        Assert.Equal(0, map.Header.EnteringElevation);

        Assert.NotNull(map.Elevations[0]);
        Assert.Null(map.Elevations[1]);
        Assert.Null(map.Elevations[2]);

        MapElevation elevation = map.Elevations[0]!;
        Assert.Equal(MapElevation.SquareGridSize, elevation.Squares.Length);
        Assert.True(elevation.Objects.Count > 500, $"expected >500 objects, got {elevation.Objects.Count}");

        // Every object must sit on the 200x200 hex grid.
        Assert.All(elevation.Objects, o => Assert.InRange(o.HexTile, 0, 200 * 200 - 1));
    }

    [GameDataFact]
    public void ParsesEveryMapInTheGame()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);

        var mapPaths = vfs.Archives
            .SelectMany(a => a.Entries)
            .Select(e => e.Path)
            .Where(p => p.StartsWith(@"maps\", StringComparison.OrdinalIgnoreCase)
                && p.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(mapPaths.Count > 100, $"expected >100 maps, found {mapPaths.Count}");

        foreach (string mapPath in mapPaths)
        {
            using Stream stream = vfs.OpenRead(mapPath);
            // The parser validates stream position implicitly: a misread
            // section throws (bad counts / EndOfStreamException).
            MapFile map = MapFile.Load(stream, protos);
            Assert.Equal(20, map.Header.Version);
        }
    }
}
