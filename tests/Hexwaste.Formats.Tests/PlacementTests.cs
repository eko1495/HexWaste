using Hexwaste.Formats.Hex;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Object placement search (P32), the simplified _obj_attempt_placement port behind
/// critter_attempt_placement: the requested tile if free, else a free immediate neighbour, else the tile.
/// </summary>
public class PlacementTests
{
    [Fact]
    public void ReturnsTheRequestedTileWhenFree() =>
        Assert.Equal(20100, Placement.FreeTileNear(20100, _ => false));

    [Fact]
    public void FindsAFreeNeighbourWhenTheTileIsBlocked()
    {
        int tile = 20100;
        int free = HexGrid.TileInDirection(tile, 2); // the only unblocked tile
        Assert.Equal(free, Placement.FreeTileNear(tile, t => t != free));
    }

    [Fact]
    public void FallsBackToTheTileWhenEverythingIsBlocked() =>
        Assert.Equal(20100, Placement.FreeTileNear(20100, _ => true));
}
