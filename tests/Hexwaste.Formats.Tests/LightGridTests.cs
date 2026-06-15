using Hexwaste.Formats.Hex;
using Hexwaste.Formats.Light;

namespace Hexwaste.Formats.Tests;

public class LightGridTests
{
    private const int Source = 200 * 100 + 100;

    private static IEnumerable<LightBlocker> NoBlockers(int tile) => [];

    [Fact]
    public void AmbientFromLightLevelMatchesTheEngineMapping()
    {
        // P21: opSetLightLevel's two-segment lerp — 0→MIN, 50→MID, 100→MAX, with the
        // engine's exact intermediate values (data*step/100).
        int mid = (LightGrid.IntensityMin + LightGrid.IntensityMax) / 2;
        Assert.Equal(LightGrid.IntensityMin, LightGrid.AmbientFromLightLevel(0));
        Assert.Equal(mid, LightGrid.AmbientFromLightLevel(50));
        Assert.Equal(LightGrid.IntensityMax, LightGrid.AmbientFromLightLevel(100));
        // 25 → MIN + 25*(MID-MIN)/100 ; 75 → MID + 75*(MAX-MID)/100
        Assert.Equal(LightGrid.IntensityMin + 25 * (mid - LightGrid.IntensityMin) / 100,
            LightGrid.AmbientFromLightLevel(25));
        Assert.Equal(mid + 75 * (LightGrid.IntensityMax - mid) / 100,
            LightGrid.AmbientFromLightLevel(75));
        // out-of-range clamps
        Assert.Equal(LightGrid.IntensityMax, LightGrid.AmbientFromLightLevel(150));
    }

    [Fact]
    public void SourceTileGetsFullIntensityAdded()
    {
        var grid = new LightGrid { Ambient = 0 };
        grid.AddObjectLight(Source, 8, 30000, NoBlockers);

        // The source tile gets the unclamped intensity added on top of the
        // 655 default before the spread starts.
        Assert.Equal(LightGrid.DefaultIntensity + 30000, grid.GetTileIntensity(Source));
    }

    [Fact]
    public void UnobstructedLightFallsOffMonotonicallyAlongARay()
    {
        var grid = new LightGrid { Ambient = 0 };
        grid.AddObjectLight(Source, 8, LightGrid.IntensityMax, NoBlockers);

        for (int rotation = 0; rotation < HexGrid.RotationCount; rotation++)
        {
            int previousAdded = int.MaxValue;
            for (int distance = 1; distance <= 8; distance++)
            {
                int tile = HexGrid.TileInDirection(Source, rotation, distance);
                int added = grid.GetTileIntensity(tile) - LightGrid.DefaultIntensity;
                Assert.True(added > 0, $"rotation {rotation} distance {distance} not lit");
                Assert.True(added < previousAdded, $"rotation {rotation} distance {distance} did not decrease");
                previousAdded = added;
            }

            // The ring just beyond the light distance stays dark.
            Assert.Equal(
                LightGrid.DefaultIntensity,
                grid.GetTileIntensity(HexGrid.TileInDirection(Source, rotation, 9)));
        }
    }

    [Fact]
    public void OpaqueWallCastsAShadow()
    {
        var grid = new LightGrid { Ambient = 0 };
        int wallTile = HexGrid.TileInDirection(Source, 1); // ROTATION_E neighbor
        var wall = new LightBlocker(LightThru: false, IsWall: true, IsFlat: false, WallExtendedFlags: 0);

        grid.AddObjectLight(Source, 8, LightGrid.IntensityMax,
            tile => tile == wallTile ? [wall] : Array.Empty<LightBlocker>());

        // The wall tile itself is lit (default wall case keeps ROTATION_E lit) ...
        Assert.True(grid.GetTileIntensity(wallTile) > LightGrid.DefaultIntensity);

        // ... but the tiles directly behind it receive nothing.
        for (int distance = 2; distance <= 8; distance++)
        {
            int behind = HexGrid.TileInDirection(Source, 1, distance);
            Assert.Equal(LightGrid.DefaultIntensity, grid.GetTileIntensity(behind));
        }

        // An unobstructed direction is unaffected by the wall.
        Assert.True(grid.GetTileIntensity(HexGrid.TileInDirection(Source, 4, 2)) > LightGrid.DefaultIntensity);
    }

    [Fact]
    public void GetTileIntensityRespectsAmbientFloorAndMaxClamp()
    {
        var grid = new LightGrid { Ambient = LightGrid.IntensityMin };

        // Untouched tiles hold 655 but the ambient floor wins.
        Assert.Equal(LightGrid.IntensityMin, grid.GetTileIntensity(Source));

        // Stacked lights push the raw value past 65536; the getter clamps.
        grid.AddObjectLight(Source, 8, LightGrid.IntensityMax, NoBlockers);
        grid.AddObjectLight(Source, 8, LightGrid.IntensityMax, NoBlockers);
        Assert.Equal(LightGrid.IntensityMax, grid.GetTileIntensity(Source));

        // Out-of-grid tiles report no light at all.
        Assert.Equal(0, grid.GetTileIntensity(-1));
        Assert.Equal(0, grid.GetTileIntensity(HexGrid.Size));
    }
}
