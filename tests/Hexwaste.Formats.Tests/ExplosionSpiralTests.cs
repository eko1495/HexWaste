using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Hex;

namespace Hexwaste.Formats.Tests;

/// <summary>The ring-and-rotation tile walk of _compute_explosion_on_extras (combat.cc:4022-4045).
/// Expectations are built from the REFERENCE'S RULES (open each ring at the NE neighbour with
/// rotation SE; rotate one step whenever ringTileIdx % radius == 0), not from the implementation's
/// own output. HexGrid.TileInDirection is a trusted pre-existing primitive.</summary>
public class ExplosionSpiralTests
{
    private const int NE = 0, E = 1, SE = 2, SW = 3, W = 4, NW = 5;
    private const int Center = 20100; // mid-grid, far from any edge

    [Fact]
    public void RadiusOneVisitsTheSixNeighboursStartingNorthEast()
    {
        // radius 1: ringTileIdx % 1 == 0 every step, so the rotation advances every step:
        // open at the NE neighbour, then step SE, SW, W, NW, NE (the sixth step, E, closes the ring).
        int t0 = HexGrid.TileInDirection(Center, NE);
        int t1 = HexGrid.TileInDirection(t0, SE);
        int t2 = HexGrid.TileInDirection(t1, SW);
        int t3 = HexGrid.TileInDirection(t2, W);
        int t4 = HexGrid.TileInDirection(t3, NW);
        int t5 = HexGrid.TileInDirection(t4, NE);

        Assert.Equal(new[] { t0, t1, t2, t3, t4, t5 }, ExplosionSpiral.Tiles(Center, 1).ToArray());
    }

    [Fact]
    public void RadiusOneRingClosesBackOnItsFirstTile()
    {
        // The sixth step (rotation E) must return to the ring's first tile, which is what ends the ring.
        int[] ring = ExplosionSpiral.Tiles(Center, 1).ToArray();
        Assert.Equal(6, ring.Length);
        Assert.Equal(ring[0], HexGrid.TileInDirection(ring[5], E));
    }

    [Fact]
    public void RadiusTwoRotatesEveryTwoStepsAndHasTwelveTiles()
    {
        // radius 2: rotate only when ringTileIdx % 2 == 0, i.e. two steps per direction —
        // SE,SE, SW,SW, W,W, NW,NW, NE,NE, E,E — 12 tiles, closing on the first.
        int[] all = ExplosionSpiral.Tiles(Center, 2).ToArray();
        int[] ring2 = all.Skip(6).ToArray();
        Assert.Equal(12, ring2.Length);

        int start = HexGrid.TileInDirection(HexGrid.TileInDirection(Center, NE), NE);
        Assert.Equal(start, ring2[0]);

        int[] dirs = [SE, SE, SW, SW, W, W, NW, NW, NE, NE, E];
        int tile = start;
        for (int i = 0; i < dirs.Length; i++)
        {
            tile = HexGrid.TileInDirection(tile, dirs[i]);
            Assert.Equal(tile, ring2[i + 1]);
        }
    }

    [Fact]
    public void RingsAreEmittedOutwardAndStopAtMaxRadius()
    {
        // 6 tiles at radius 1, 12 at radius 2, 18 at radius 3 (6*r per ring).
        Assert.Equal(6, ExplosionSpiral.Tiles(Center, 1).Count());
        Assert.Equal(18, ExplosionSpiral.Tiles(Center, 2).Count());
        Assert.Equal(36, ExplosionSpiral.Tiles(Center, 3).Count());
        Assert.Empty(ExplosionSpiral.Tiles(Center, 0));
    }

    [Fact]
    public void TheCentreTileIsNeverEnumerated()
    {
        // combat.cc:4033 opens at radius 1 — the blast tile itself is the primary defender's,
        // handled by the main attack path, never an "extra".
        Assert.DoesNotContain(Center, ExplosionSpiral.Tiles(Center, 3));
    }
}
