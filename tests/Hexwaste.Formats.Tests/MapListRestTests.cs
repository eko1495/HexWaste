using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

public class MapListRestTests
{
    [GameDataFact]
    public void CanRestHereParsesThePerElevationFlags()
    {
        // P118 (WATCH): the maps.txt can_rest_here key (worldmap.cc:2683 → wmMapCanRestHere
        // :2840). desert1/arcaves ship "No,No,No"; artemple has no key → the engine default
        // (all elevations restable); unknown maps default to restable too.
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        MapList maps = MapList.Load(vfs);

        Assert.False(maps.CanRestHere("desert1.map", 0));
        Assert.False(maps.CanRestHere("arcaves.map", 2));
        Assert.True(maps.CanRestHere("artemple.map", 0));
        Assert.True(maps.CanRestHere("no-such-map.map", 0));
    }
}
