using Hexwaste.Formats.Hex;

namespace Hexwaste.Formats.Tests;

public class HexGridTests
{
    [Fact]
    public void TileInTileRectUsesTheEngineAsymmetricCorners()
    {
        // P56: rect x in [10,20], y in [30,40]. Engine corners (interpreter_extra.cc:1447): c1=(minX,maxY)
        // = (10,40), c4=(maxX,minY)=(20,30). tile = 200*y + x. Args c2/c3 are popped-but-IGNORED.
        int c1 = 200 * 40 + 10, c4 = 200 * 30 + 20;
        Assert.Equal(1, HexGrid.TileInTileRect(200 * 35 + 15, c1, 99999, 88888, c4)); // (15,35) inside; junk c2/c3
        Assert.Equal(1, HexGrid.TileInTileRect(200 * 40 + 10, c1, 0, 0, c4));         // (10,40) corner — inclusive
        Assert.Equal(0, HexGrid.TileInTileRect(200 * 35 + 5, c1, 0, 0, c4));          // (5,35)  x < minX
        Assert.Equal(0, HexGrid.TileInTileRect(200 * 45 + 15, c1, 0, 0, c4));         // (15,45) y > maxY
    }

    [Fact]
    public void OppositeDirectionsRoundTrip()
    {
        // Walking one hex in direction r then in direction (r+3)%6 must
        // return to the start, for both column parities.
        foreach (int start in new[] { 200 * 100 + 100, 200 * 100 + 101 })
        {
            for (int rotation = 0; rotation < 6; rotation++)
            {
                int there = HexGrid.TileInDirection(start, rotation);
                Assert.NotEqual(start, there);
                int back = HexGrid.TileInDirection(there, (rotation + 3) % 6);
                Assert.Equal(start, back);
            }
        }
    }

    [Fact]
    public void NeighborsAreDistinct()
    {
        int start = 200 * 50 + 77;
        var neighbors = Enumerable.Range(0, 6).Select(r => HexGrid.TileInDirection(start, r)).ToHashSet();
        Assert.Equal(6, neighbors.Count);
    }

    [Fact]
    public void ScreenEmbeddingMatchesStepDeltas()
    {
        // The embedding difference for a one-hex step must equal the
        // _off_tile/dword_51D984 movement deltas for every rotation.
        int start = 200 * 100 + 100;
        for (int rotation = 0; rotation < 6; rotation++)
        {
            (int x1, int y1) = HexGrid.ScreenEmbedding(start);
            (int x2, int y2) = HexGrid.ScreenEmbedding(HexGrid.TileInDirection(start, rotation));
            Assert.Equal((HexGrid.StepScreenX[rotation], HexGrid.StepScreenY[rotation]), (x2 - x1, y2 - y1));
        }
    }

    [Fact]
    public void FromScreenEmbeddingInvertsScreenEmbedding()
    {
        // A point at each tile's cell centre (+16,+8 of its embedding top-left)
        // must map back to that tile — the load-bearing invariant for the
        // screen-Bresenham line-of-fire and the burst cone's end-tile walk.
        for (int y = 2; y < HexGrid.Height - 2; y += 7)
            for (int x = 2; x < HexGrid.Width - 2; x += 5)
            {
                int tile = y * HexGrid.Width + x;
                (int sx, int sy) = HexGrid.ScreenEmbedding(tile);
                Assert.Equal(tile, HexGrid.FromScreenEmbedding(sx + 16, sy + 8));
            }
    }

    [Fact]
    public void EdgeTilesDoNotEscapeTheGrid()
    {
        for (int rotation = 0; rotation < 6; rotation++)
        {
            Assert.Equal(0, HexGrid.TileInDirection(0, rotation));
            Assert.Equal(HexGrid.Size - 1, HexGrid.TileInDirection(HexGrid.Size - 1, rotation));
        }
    }
}

public class PathfinderTests
{
    private static int Tile(int x, int y) => y * HexGrid.Width + x;

    [Fact]
    public void FindsStraightPath()
    {
        int from = Tile(100, 100);
        int to = HexGrid.TileInDirection(HexGrid.TileInDirection(from, 2), 2);

        byte[]? path = Pathfinder.FindPath(from, to, _ => false);

        Assert.NotNull(path);
        Assert.Equal(2, path.Length);
        Assert.All(path, r => Assert.Equal(2, r));
    }

    [Fact]
    public void WalksAroundObstacles()
    {
        int from = Tile(100, 100);
        int to = HexGrid.TileInDirection(HexGrid.TileInDirection(from, 2), 2);
        int wall = HexGrid.TileInDirection(from, 2);

        byte[]? path = Pathfinder.FindPath(from, to, tile => tile == wall);

        Assert.NotNull(path);
        Assert.True(path.Length > 2, "detour must be longer than the straight path");

        // Replay the rotations and verify the path is connected, avoids the
        // wall, and ends at the target.
        int current = from;
        foreach (byte rotation in path)
        {
            current = HexGrid.TileInDirection(current, rotation);
            Assert.NotEqual(wall, current);
        }
        Assert.Equal(to, current);
    }

    [Fact]
    public void ReturnsNullWhenWalledIn()
    {
        int from = Tile(100, 100);
        var ring = Enumerable.Range(0, 6).Select(r => HexGrid.TileInDirection(from, r)).ToHashSet();

        byte[]? path = Pathfinder.FindPath(from, Tile(120, 120), ring.Contains);

        Assert.Null(path);
    }

    [Fact]
    public void GoalTileIsNotBlockingChecked()
    {
        int from = Tile(100, 100);
        int to = HexGrid.TileInDirection(from, 2);

        // Target itself reported blocked — path should still reach it.
        byte[]? path = Pathfinder.FindPath(from, to, tile => tile == to);

        Assert.NotNull(path);
        Assert.Single(path);
    }

    [Fact]
    public void IsOnSegmentDetectsACollinearInBetweenTile()
    {
        // Three tiles stepped straight out in direction 0: the middle one is on the a→b segment; the
        // endpoints and an off-line tile are not (P78-M3 friendly-fire collinear test).
        int a = 20100;
        int mid = HexGrid.TileInDirection(a, 0);
        int b = HexGrid.TileInDirection(mid, 0);
        Assert.True(HexGrid.IsOnSegment(a, mid, b));
        Assert.False(HexGrid.IsOnSegment(a, a, b));                             // an endpoint is not "between"
        Assert.False(HexGrid.IsOnSegment(a, b, b));                             // the other endpoint either
        Assert.False(HexGrid.IsOnSegment(a, HexGrid.TileInDirection(a, 2), b)); // a tile off the line
    }
}
