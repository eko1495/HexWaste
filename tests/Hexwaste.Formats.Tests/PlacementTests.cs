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

    // BACKLOG A6: every party member used to scan the same blocked set and pick the same first free
    // neighbour, so a whole party landed stacked on one hex after a map transition. Successive
    // placements must claim the tiles they hand out.
    [Fact]
    public void SuccessiveFreeTilesAroundAreDistinct()
    {
        int[] tiles = Placement.FreeTilesAround(20100, 3, _ => false);

        Assert.Equal(3, tiles.Length);
        Assert.Equal(3, tiles.Distinct().Count());
        Assert.DoesNotContain(20100, tiles); // the centre belongs to the dude
        Assert.All(tiles, t => Assert.Contains(t, Enumerable.Range(0, 6).Select(d => HexGrid.TileInDirection(20100, d))));
    }

    [Fact]
    public void FreeTilesAroundSkipsBlockedNeighbours()
    {
        int blocked = HexGrid.TileInDirection(20100, 0);

        int[] tiles = Placement.FreeTilesAround(20100, 2, t => t == blocked);

        Assert.DoesNotContain(blocked, tiles);
        Assert.Equal(2, tiles.Distinct().Count());
    }

    [Fact]
    public void FreeTilesAroundFallsBackToTheCentreOnceTheRingIsExhausted()
    {
        // Seven placements around a six-neighbour ring: the seventh has nowhere left to go and
        // falls back to the centre, matching FreeTileNear's best-effort contract.
        int[] tiles = Placement.FreeTilesAround(20100, 7, _ => false);

        Assert.Equal(7, tiles.Length);
        Assert.Equal(20100, tiles[6]);
        Assert.Equal(6, tiles.Take(6).Distinct().Count());
    }
}
