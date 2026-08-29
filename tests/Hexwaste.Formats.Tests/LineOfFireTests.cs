using Hexwaste.Formats;
using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Hex;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

public class LineOfFireTests
{
    // Raw FIDs: the high nibble is the object type (1 critter, 2 wall, 3 scenery).
    private static MapObject Obj(int rawFid, int tile, int flags = 0) => new()
    {
        Id = 1, HexTile = tile, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = rawFid, Flags = flags, Pid = 0x01000001, Sid = -1,
    };

    private const int ShootThruFlag = unchecked((int)0x80000000);

    private const int Critter = 0x01000000;
    private const int Wall = 0x02000000;

    private static int Tile(int x, int y) => y * HexGrid.Width + x;

    /// <summary>The policy these walker tests exercise: the crowd-count caller
    /// (combat.cc:5908), which is the one whose terms the walker used to hard-code —
    /// critters counted rather than blocking, the target never its own obstruction.</summary>
    private static readonly ShotFilter Walker = ShotFilter.ShotBlockedPenalty;

    /// <summary>The intermediate tiles the Bresenham visits (excludes from/to, which
    /// the engine never blocker-checks).</summary>
    private static List<int> Path(int from, int to)
    {
        var seen = new List<int>();
        LineOfFire.Trace(from, to, t => { seen.Add(t); return null; }, Walker);
        return seen;
    }

    [Fact]
    public void ClearLineReturnsNoBlocker()
    {
        var (blocker, critters) = LineOfFire.Trace(Tile(100, 100), Tile(112, 106), _ => null, Walker);
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

        var (blocker, _) = LineOfFire.Trace(from, to, t => t == wallTile ? wall : null, Walker);
        Assert.Same(wall, blocker);
    }

    [Fact]
    public void LivingCritterIsCountedNotBlocking()
    {
        int from = Tile(100, 100), to = Tile(112, 106);
        List<int> path = Path(from, to);
        int critterTile = path[path.Count / 2];
        var critter = Obj(Critter, critterTile);

        var (blocker, critters) = LineOfFire.Trace(from, to, t => t == critterTile ? critter : null, Walker);
        Assert.Null(blocker);             // critters never block the shot
        Assert.Equal(1, critters);        // but they are counted (the -10/critter term)
    }

    [Fact]
    public void EndpointsAreNeverBlockerChecked()
    {
        int from = Tile(100, 100), to = Tile(112, 106);
        // A wall sitting on the shooter's own tile or the target tile is ignored.
        var (b1, _) = LineOfFire.Trace(from, to, t => t == from ? Obj(Wall, from) : null, Walker);
        Assert.Null(b1);

        // The TARGET tile is queried (the reference walker queries it too) — what saves it is the
        // caller's ExcludesTarget term, matched on object IDENTITY, not a tile skip.
        var targetWall = Obj(Wall, to);
        var (b2, _) = LineOfFire.Trace(from, to, t => t == to ? targetWall : null, Walker, targetWall);
        Assert.Null(b2);

        // A DIFFERENT object on the target tile is not the target, so it does block — the one
        // behaviour the identity test has that the old tile skip did not.
        var (b3, _) = LineOfFire.Trace(from, to, t => t == to ? Obj(Wall, to) : null, Walker, targetWall);
        Assert.NotNull(b3);
    }

    [Fact]
    public void AdjacentTilesHaveNoIntermediateToBlock()
    {
        int from = Tile(100, 100);
        for (int rotation = 0; rotation < 6; rotation++)
        {
            int to = HexGrid.TileInDirection(from, rotation);
            // There is no tile strictly between the two, and the only tiles the walker can
            // query are the shooter's own (never checked) and the target's (checked, but the
            // object there IS the target). So an adjacent shot is never blocked.
            var targetWall = Obj(Wall, to);
            var (blocker, critters) = LineOfFire.Trace(
                from, to, t => t == from ? Obj(Wall, from) : targetWall, Walker, targetWall);
            Assert.Null(blocker);
            Assert.Equal(0, critters);
        }
    }

    /// <summary>ported from fallout2-ce src/animation.cc:1957/:2039 — _make_straight_path_func's own
    /// guard. Every line-of-fire caller passes a6 == 32, so a SHOOT_THRU object is never assigned to
    /// the caller's obstacle pointer: the walker walks straight past it, whatever the caller's own
    /// filter would have said. BurstWalk is deliberately the filter used here — it applies no flag
    /// test of its own (combat.cc:3644), so only the walker's guard can produce this result.</summary>
    [Fact]
    public void ShootTraceNeverReportsAShootThruObject()
    {
        int from = Tile(100, 100), to = Tile(112, 106);
        List<int> path = Path(from, to);
        int wallTile = path[path.Count / 2];
        var wall = Obj(Wall, wallTile, ShootThruFlag);

        var (blocker, _) = LineOfFire.Trace(from, to, t => t == wallTile ? wall : null, ShotFilter.BurstWalk);
        Assert.Null(blocker);
    }

    /// <summary>The guard SUPPRESSES rather than merely un-blocks: the reference counts critters in
    /// _combat_is_shot_blocked's own loop over the obstacles the walker REPORTED (combat.cc:5911),
    /// so a SHOOT_THRU critter — never reported — is never counted either.</summary>
    [Fact]
    public void AShootThruCritterIsNeitherBlockingNorCounted()
    {
        int from = Tile(100, 100), to = Tile(112, 106);
        List<int> path = Path(from, to);
        int critterTile = path[path.Count / 2];
        var critter = Obj(Critter, critterTile, ShootThruFlag);

        var (blocker, critters) = LineOfFire.Trace(from, to, t => t == critterTile ? critter : null, Walker);
        Assert.Null(blocker);
        Assert.Equal(0, critters);
    }

    /// <summary>The guard is armed by a6 == 32 alone. obj_can_see_obj's SIGHT trace
    /// (interpreter_extra.cc:1797) passes 16, so a SHOOT_THRU object still blocks sight.</summary>
    [Fact]
    public void SightTraceIsNotSubjectToTheShootThruGuard()
    {
        int from = Tile(100, 100), to = Tile(112, 106);
        List<int> path = Path(from, to);
        int wallTile = path[path.Count / 2];
        var wall = Obj(Wall, wallTile, ShootThruFlag);

        var (blocker, _) = LineOfFire.Trace(from, to, t => t == wallTile ? wall : null,
            ShotFilter.BurstWalk, targetObj: null, stride: LineOfFire.SightTraceStride);
        Assert.Same(wall, blocker);
    }
}
