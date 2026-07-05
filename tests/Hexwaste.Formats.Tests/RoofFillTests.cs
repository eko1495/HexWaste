using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// P117: the connected-roof flood fill (tile.cc tile_fill_roof port) — only the roof block
/// the dude stands under hides, not every roof on the map. Hermetic: synthetic square grids.
/// </summary>
public class RoofFillTests
{
    /// <summary>Empty roof everywhere (id 1), then paint the given squares with roof id 40.</summary>
    private static MapElevation MakeElevation(params int[] roofedSquares)
    {
        int[] squares = new int[MapElevation.SquareGridSize];
        Array.Fill(squares, 1 << 16); // roof id 1 = the engine's "no roof" tile
        foreach (int square in roofedSquares)
            squares[square] = 40 << 16;
        return new MapElevation { Squares = squares };
    }

    private static int Sq(int x, int y) => y * MapElevation.SquareGridWidth + x;

    [Fact]
    public void HidesOnlyTheConnectedBlock()
    {
        // Two separate 2x2 buildings; the dude stands under the first.
        int[] buildingA = [Sq(10, 10), Sq(11, 10), Sq(10, 11), Sq(11, 11)];
        int[] buildingB = [Sq(50, 50), Sq(51, 50), Sq(50, 51), Sq(51, 51)];
        MapElevation elevation = MakeElevation([.. buildingA, .. buildingB]);

        HashSet<int> hidden = RoofFill.ConnectedRoofSquares(elevation, Sq(10, 10));

        Assert.Equal(buildingA.ToHashSet(), hidden);          // the whole connected block...
        Assert.DoesNotContain(Sq(50, 50), hidden);            // ...and nothing across the gap
    }

    [Fact]
    public void OutdoorsHidesNothing()
    {
        MapElevation elevation = MakeElevation(Sq(10, 10));
        Assert.Empty(RoofFill.ConnectedRoofSquares(elevation, Sq(5, 5)));  // no roof here
        Assert.Empty(RoofFill.ConnectedRoofSquares(elevation, -1));        // off-grid
    }

    [Fact]
    public void DiagonalRoofsAreSeparateBlocks()
    {
        // 4-connected like the engine's push (x±1, y±1 only) — a diagonal touch doesn't join.
        MapElevation elevation = MakeElevation(Sq(10, 10), Sq(11, 11));
        Assert.Equal([Sq(10, 10)], RoofFill.ConnectedRoofSquares(elevation, Sq(10, 10)));
    }
}
