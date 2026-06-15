using Hexwaste.Formats;
using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Hex;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

public class LineOfFireTests
{
    // Raw FIDs: the high nibble is the object type (1 critter, 2 wall, 3 scenery).
    private static MapObject Obj(int rawFid, int tile) => new()
    {
        Id = 1, HexTile = tile, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = rawFid, Flags = 0, Pid = 0x01000001, Sid = -1,
    };

    private const int Critter = 0x01000000;
    private const int Wall = 0x02000000;

    private static int Tile(int x, int y) => y * HexGrid.Width + x;

    /// <summary>The intermediate tiles the Bresenham visits (excludes from/to, which
    /// the engine never blocker-checks).</summary>
    private static List<int> Path(int from, int to)
    {
        var seen = new List<int>();
        LineOfFire.Trace(from, to, t => { seen.Add(t); return null; });
        return seen;
    }

    [Fact]
    public void ClearLineReturnsNoBlocker()
    {
        var (blocker, critters) = LineOfFire.Trace(Tile(100, 100), Tile(112, 106), _ => null);
        Assert.Null(blocker);
        Assert.Equal(0, critters);
    }

    [Fact]
    public void WallOnTheLineBlocks()
    {
        int from = Tile(100, 100), to = Tile(112, 106);
        List<int> path = Path(from, to);
        Assert.NotEmpty(path);
        int wallTile = path[path.Count / 2];
        var wall = Obj(Wall, wallTile);

        var (blocker, _) = LineOfFire.Trace(from, to, t => t == wallTile ? wall : null);
        Assert.Same(wall, blocker);
    }

    [Fact]
    public void LivingCritterIsCountedNotBlocking()
    {
        int from = Tile(100, 100), to = Tile(112, 106);
        List<int> path = Path(from, to);
        int critterTile = path[path.Count / 2];
        var critter = Obj(Critter, critterTile);

        var (blocker, critters) = LineOfFire.Trace(from, to, t => t == critterTile ? critter : null);
        Assert.Null(blocker);             // critters never block the shot
        Assert.Equal(1, critters);        // but they are counted (the -10/critter term)
    }

    [Fact]
    public void EndpointsAreNeverBlockerChecked()
    {
        int from = Tile(100, 100), to = Tile(112, 106);
        // A wall sitting on the shooter's own tile or the target tile is ignored.
        var (b1, _) = LineOfFire.Trace(from, to, t => t == from ? Obj(Wall, from) : null);
        var (b2, _) = LineOfFire.Trace(from, to, t => t == to ? Obj(Wall, to) : null);
        Assert.Null(b1);
        Assert.Null(b2);
    }

    [Fact]
    public void AdjacentTilesHaveNoIntermediateToBlock()
    {
        int from = Tile(100, 100);
        for (int rotation = 0; rotation < 6; rotation++)
        {
            int to = HexGrid.TileInDirection(from, rotation);
            // Even a blocker that fires on every queried tile can't block an
            // adjacent shot — there is no tile strictly between the two.
            var (blocker, critters) = LineOfFire.Trace(from, to, t => Obj(Wall, t));
            Assert.Null(blocker);
            Assert.Equal(0, critters);
        }
    }
}
