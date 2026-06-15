using System.Text;
using Hexwaste.Formats;
using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// The worldmap travel-leg resolver (#14) lifted out of ViewerGame into pure
/// Formats logic: the Bresenham walk + per-step encounter roll + known-area
/// suppression + encounter-map pick. Deterministic under a fake RNG — the
/// testability win the extraction unlocks. The end-to-end leg is also covered by
/// the byte-identical golden fixture tests/golden-encounter/travel-arroyo-den.txt.
/// </summary>
public class WorldmapTravelTests
{
    private static WorldArea Area(int index, int x, int y, bool withEntrance = true)
    {
        var a = new WorldArea
        {
            Index = index, Name = $"Area{index}", WorldX = x, WorldY = y,
            Size = "Large", StartsOn = false,
        };
        if (withEntrance)
            a.Entrances.Add(new AreaEntrance(StartsOn: true, MapLookupName: "X", Elevation: 0, Tile: 0, Rotation: 0));
        return a;
    }

    [Fact]
    public void IsNearKnownAreaTrueInsideTheCircleFalseOutsideAndForEntranceless()
    {
        var areas = new[] { Area(1, 100, 100) };

        Assert.True(WorldmapTravel.IsNearKnownArea(areas, 100, 100));  // dead centre
        Assert.True(WorldmapTravel.IsNearKnownArea(areas, 108, 100));  // 8 px < radius 12
        Assert.False(WorldmapTravel.IsNearKnownArea(areas, 120, 100)); // 20 px > radius 12

        // An area with no entrances is not a "known" suppression circle.
        Assert.False(WorldmapTravel.IsNearKnownArea(new[] { Area(2, 100, 100, withEntrance: false) }, 100, 100));
    }

    [Fact]
    public void IsNearKnownAreaScansEveryArea()
    {
        var areas = new[] { Area(1, 0, 0), Area(2, 500, 500) };
        Assert.True(WorldmapTravel.IsNearKnownArea(areas, 503, 498)); // near the second
    }

    private static WorldmapTravel.LegOutcome RunArroyoToDenLeg(WorldmapFile worldmap, CityList cities, MapList mapList, int seed)
    {
        WorldArea den = cities.Areas.First(a => a.Index == 1);
        return WorldmapTravel.ResolveLeg(
            worldmap, cities.Areas, mapList,
            startX: 184, startY: 133, destX: den.WorldX, destY: den.WorldY,
            startClockTicks: 302400, rng: new SystemCombatRng(seed), getGlobal: _ => 0,
            dudeLevel: 1, luck: 5, outdoorsman: 0, difficulty: GameDifficulty.Normal);
    }

    [GameDataFact]
    public void ResolveLegIsDeterministicForAGivenSeed()
    {
        // The exact arroyo→den ambush bytes are owned by the golden fixture
        // (travel-arroyo-den.txt, with the viewer's real dude state). Here we lock the
        // resolver's CONTRACT: same seed + inputs → byte-identical outcome.
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        WorldmapFile worldmap = WorldmapFile.Parse(Encoding.Latin1.GetString(vfs.ReadAllBytes(@"data\worldmap.txt")));
        CityList cities = CityList.Load(vfs);
        MapList mapList = MapList.Load(vfs);

        WorldmapTravel.LegOutcome a = RunArroyoToDenLeg(worldmap, cities, mapList, seed: 2);
        WorldmapTravel.LegOutcome b = RunArroyoToDenLeg(worldmap, cities, mapList, seed: 2);

        Assert.Equal(a.FinalWorldX, b.FinalWorldX);
        Assert.Equal(a.FinalWorldY, b.FinalWorldY);
        Assert.Equal(a.ClockTicksAdded, b.ClockTicksAdded);
        Assert.Equal(a.EncounterMap, b.EncounterMap);
        Assert.Equal(a.Encounter?.Entry.Spawns.First().Group, b.Encounter?.Entry.Spawns.First().Group);
    }

    [GameDataFact]
    public void ResolveLegAcrossTheLongArroyoToDenLegEncountersOnAtransientMap()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        WorldmapFile worldmap = WorldmapFile.Parse(Encoding.Latin1.GetString(vfs.ReadAllBytes(@"data\worldmap.txt")));
        CityList cities = CityList.Load(vfs);
        MapList mapList = MapList.Load(vfs);

        WorldmapTravel.LegOutcome leg = RunArroyoToDenLeg(worldmap, cities, mapList, seed: 2);

        // The wasteland bites over this long leg: an encounter on a saved=No map, the
        // dude stopped partway, and the clock advanced by the walked distance.
        Assert.NotNull(leg.Encounter);
        Assert.NotNull(leg.EncounterMap);
        Assert.True(mapList.IsTransient(leg.EncounterMap!));
        Assert.True(leg.ClockTicksAdded > 0);
        Assert.NotEmpty(leg.Encounter!.Entry.Spawns);
    }

    [GameDataFact]
    public void StepwiseDrainMatchesResolveLeg()
    {
        // Phase-17 M0: draining TravelLeg one Step() at a time must reproduce the whole-leg
        // ResolveLeg exactly — same final pixel, clock, encounter, and map (same RNG order).
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        WorldmapFile worldmap = WorldmapFile.Parse(Encoding.Latin1.GetString(vfs.ReadAllBytes(@"data\worldmap.txt")));
        CityList cities = CityList.Load(vfs);
        MapList mapList = MapList.Load(vfs);
        WorldArea den = cities.Areas.First(a => a.Index == 1);

        WorldmapTravel.LegOutcome whole = RunArroyoToDenLeg(worldmap, cities, mapList, seed: 2);

        var leg = new TravelLeg(worldmap, cities.Areas, mapList, 184, 133, den.WorldX, den.WorldY,
            startClockTicks: 302400, new SystemCombatRng(2), _ => 0,
            dudeLevel: 1, luck: 5, outdoorsman: 0, difficulty: GameDifficulty.Normal);
        TravelStep s;
        int steps = 0;
        do { s = leg.Step(); steps++; } while (s.Encounter is null && !s.Arrived && steps < 5000);

        Assert.Equal(whole.FinalWorldX, s.X);
        Assert.Equal(whole.FinalWorldY, s.Y);
        Assert.Equal(whole.ClockTicksAdded, leg.TicksAdded);
        Assert.Equal(whole.EncounterMap, s.EncounterMap);
        Assert.Equal(whole.Encounter?.Entry.Spawns.First().Group, s.Encounter?.Entry.Spawns.First().Group);
    }

    [GameDataFact]
    public void ResolveEncounterMapPicksATransientMapFromTheTablePool()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        WorldmapFile worldmap = WorldmapFile.Parse(Encoding.Latin1.GetString(vfs.ReadAllBytes(@"data\worldmap.txt")));
        MapList mapList = MapList.Load(vfs);

        // Any table with a non-empty map pool resolves to a transient (saved=No) map.
        EncounterTable table = worldmap.Tables.Values.First(t => t.Maps.Count > 0 && t.Entries.Count > 0);
        var enc = new EncounterResult(table, table.Entries[0]);
        string map = WorldmapTravel.ResolveEncounterMap(mapList, enc, new SystemCombatRng(1));

        Assert.True(mapList.IsTransient(map), $"{map} should be a transient encounter map");
    }
}
