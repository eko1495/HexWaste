using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

public class WorldmapRealGameDataTests
{
    [GameDataFact]
    public void ParsesCityListWithEntrances()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        CityList cities = CityList.Load(vfs);

        Assert.True(cities.Areas.Count > 20, $"expected >20 areas, got {cities.Areas.Count}");

        WorldArea arroyo = cities.Areas.First(a => a.Name == "Arroyo");
        Assert.Equal(0, arroyo.Index);
        Assert.True(arroyo.WorldX > 0 && arroyo.WorldY > 0);
        Assert.NotEmpty(arroyo.Entrances);
        Assert.Equal("Arroyo Bridge", arroyo.Entrances[0].MapLookupName);

        // Every area position must be inside the 1400x1500 tile canvas.
        Assert.All(cities.Areas, a => Assert.InRange(a.WorldX, 0, 1400));
        Assert.All(cities.Areas, a => Assert.InRange(a.WorldY, 0, 1500));
    }

    [GameDataFact]
    public void ResolvesEntranceLookupNamesToMapFiles()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        CityList cities = CityList.Load(vfs);
        MapList maps = MapList.Load(vfs);

        int resolved = 0;
        foreach (WorldArea area in cities.Areas)
        {
            foreach (AreaEntrance entrance in area.Entrances)
            {
                int index = maps.FindByLookupName(entrance.MapLookupName);
                if (index >= 0 && maps.GetMapFileName(index) is not null)
                    resolved++;
            }
        }

        // The vast majority of entrances must resolve (a few may reference
        // special maps); Arroyo Bridge specifically must.
        Assert.True(resolved > 50, $"only {resolved} entrances resolved");
        int bridge = maps.FindByLookupName("Arroyo Bridge");
        Assert.True(bridge >= 0);
        Assert.Equal("arbridge.map", maps.GetMapFileName(bridge));
    }
}
