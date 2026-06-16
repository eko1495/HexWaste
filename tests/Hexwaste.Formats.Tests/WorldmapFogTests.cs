using System.Text;
using Hexwaste.Formats;
using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Worldmap subtile fog-of-war (phase-22): the per-subtile UNKNOWN/KNOWN/VISITED reveal model
/// ported from fallout2-ce wmSubTileMarkRadiusVisited. Pure position math (no RNG), so a travel
/// leg can reveal silently — verified here against synthetic worldmaps plus the real arroyo→den
/// leg, and the save round-trip via Export/Import.
/// </summary>
public class WorldmapFogTests
{
    private sealed class MinRng : ICombatRng
    {
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
    }

    // One tile, all None=0% so no encounter ever rolls; subtile (4,2) carries Fill_W.
    private static WorldmapFile FillWWorld() => WorldmapFile.Parse("""
        [Data]
        None=0%
        [Tile 0]
        encounter_difficulty=0
        4_2=Ocean,Fill_W,None,None,None,T
        [Encounter Table 0]
        lookup_name=T
        enc_00=Chance:50%,Enc:(1-1) G AMBUSH
        """);

    [Fact]
    public void FreshFogIsAllUnknown()
    {
        var fog = new WorldmapFog(FillWWorld());
        Assert.Equal(WorldmapFog.Unknown, fog.StateAt(125, 125));
        Assert.Equal(0, fog.CountState(WorldmapFog.Known));
        Assert.Equal(0, fog.CountState(WorldmapFog.Visited));
    }

    [Fact]
    public void MarkRadiusVisitedSetsCentreVisitedAndRingKnown()
    {
        var fog = new WorldmapFog(FillWWorld());
        fog.MarkRadiusVisited(125, 125); // tile 0, subtile (2,2)

        Assert.Equal(WorldmapFog.Visited, fog.StateAt(125, 125)); // centre
        Assert.Equal(WorldmapFog.Known, fog.StateAt(75, 75));     // ring (1,1)
        Assert.Equal(WorldmapFog.Known, fog.StateAt(175, 175));   // ring (3,3)
        Assert.Equal(WorldmapFog.Unknown, fog.StateAt(275, 275)); // (5,5), outside the ring
        Assert.Equal(1, fog.CountState(WorldmapFog.Visited));     // just the centre
        Assert.Equal(8, fog.CountState(WorldmapFog.Known));       // the 3x3 ring minus centre
    }

    [Fact]
    public void KnownNeverDowngradesVisited()
    {
        var fog = new WorldmapFog(FillWWorld());
        fog.MarkRadiusVisited(125, 125);          // (2,2) VISITED
        fog.MarkRadiusVisited(175, 125);          // (3,2) VISITED; its ring re-touches (2,2) as KNOWN
        Assert.Equal(WorldmapFog.Visited, fog.StateAt(125, 125)); // stays VISITED, not downgraded
    }

    [Fact]
    public void FillWSpreadsVisitedAcrossTheRowToTheWest()
    {
        // worldmap.cc SUBTILE_FILL_W: reaching a Fill_W subtile marks the whole row to its west
        // VISITED (the western ocean reveals as one strip). Subtile (4,2) is Fill_W.
        var fog = new WorldmapFog(FillWWorld());
        fog.MarkRadiusVisited(225, 125); // centre of subtile (4,2)

        for (int sx = 0; sx <= 4; sx++)                              // (0..4, 2) all VISITED
            Assert.Equal(WorldmapFog.Visited, fog.StateAt(sx * 50 + 25, 125));
        Assert.Equal(WorldmapFog.Known, fog.StateAt(275, 125));      // (5,2): only the ring reaches it
        Assert.Equal(WorldmapFog.Unknown, fog.StateAt(325, 125));    // (6,2): untouched
    }

    [Fact]
    public void ExportImportRoundTrips()
    {
        var fog = new WorldmapFog(FillWWorld());
        fog.MarkRadiusVisited(125, 125);
        Dictionary<int, int> snapshot = fog.Export();
        Assert.Equal(9, snapshot.Count); // 1 visited + 8 known, the rest omitted (sparse)

        var restored = new WorldmapFog(FillWWorld());
        restored.Import(snapshot);
        Assert.Equal(WorldmapFog.Visited, restored.StateAt(125, 125));
        Assert.Equal(WorldmapFog.Known, restored.StateAt(75, 75));
        Assert.Equal(WorldmapFog.Unknown, restored.StateAt(275, 275));
        Assert.Equal(fog.CountState(WorldmapFog.Visited), restored.CountState(WorldmapFog.Visited));
        Assert.Equal(fog.CountState(WorldmapFog.Known), restored.CountState(WorldmapFog.Known));
    }

    [Fact]
    public void OffGridPixelsAreUnknownAndNoOpToMark()
    {
        var fog = new WorldmapFog(FillWWorld());
        Assert.Equal(WorldmapFog.Unknown, fog.StateAt(-5, 50));
        fog.MarkRadiusVisited(-5, 50);     // off-grid: must not throw / reveal anything
        fog.MarkRadiusVisited(5000, 5000); // past the 1400x1500 canvas
        Assert.Equal(0, fog.CountState(WorldmapFog.Visited));
    }

    [GameDataFact]
    public void TravelLegRevealsSubtilesAlongTheArroyoToDenPath()
    {
        // The reveal rides on the SAME stepwise TravelLeg the live travel + the goldens drain,
        // so a leg leaves an explored trail. No RNG is drawn by the fog, so the encounter
        // outcome is unchanged (locked by the byte-identical travel goldens).
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        WorldmapFile worldmap = WorldmapFile.Parse(Encoding.Latin1.GetString(vfs.ReadAllBytes(@"data\worldmap.txt")));
        CityList cities = CityList.Load(vfs);
        MapList mapList = MapList.Load(vfs);
        WorldArea den = cities.Areas.First(a => a.Index == 1);
        var fog = new WorldmapFog(worldmap);

        var leg = new TravelLeg(worldmap, cities.Areas, mapList, 184, 133, den.WorldX, den.WorldY,
            startClockTicks: 302400, new SystemCombatRng(2), _ => 0,
            dudeLevel: 1, luck: 5, outdoorsman: 0, difficulty: GameDifficulty.Normal, fog);

        Assert.Equal(WorldmapFog.Visited, fog.StateAt(184, 133)); // the start is revealed by the ctor

        TravelStep s;
        int steps = 0;
        do { s = leg.Step(); steps++; } while (s.Encounter is null && !s.Arrived && steps < 5000);

        Assert.True(steps > 1, "the leg walked several pixels");
        Assert.Equal(WorldmapFog.Visited, fog.StateAt(s.X, s.Y)); // the latest pixel is VISITED
        Assert.True(fog.CountState(WorldmapFog.Visited) > 1, "the walked trail is VISITED");
        Assert.True(fog.CountState(WorldmapFog.Known) > 0, "the trail's neighbourhood is KNOWN");
    }
}
