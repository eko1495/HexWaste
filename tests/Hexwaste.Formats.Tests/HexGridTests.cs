using Hexwaste.Formats.Hex;

namespace Hexwaste.Formats.Tests;

public class HexGridTests
{
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
}
