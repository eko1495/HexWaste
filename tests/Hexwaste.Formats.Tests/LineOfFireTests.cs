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
    private static readonly ShotFilter Walker = ShotFilter.ShotBlocked;

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

    /// <summary>ported from fallout2-ce src/animation.cc:1956/:2050/:2103 — _make_straight_path_func's own
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
    /// _combat_is_shot_blocked's own loop over the obstacles the walker REPORTED (combat.cc:5912),
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
    /// (interpreter_extra.cc:1797) passes 16, so a SHOOT_THRU object still blocks sight.
    /// BurstWalk is used deliberately here too: it applies no flag test of its own, so if the
    /// wall is still reported as a blocker, that can only be the stride guard's doing (or, on a
    /// sight trace, its absence) — not the filter.</summary>
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

    /// <summary>ported from fallout2-ce src/animation.cc:1956/:2050/:2103 — the guard's FIRST
    /// conjunct, `obstacle != *obstaclePtr`. Every looping reference caller re-enters the walker
    /// with *obstaclePtr still holding the object found last time (_combat_is_shot_blocked's while
    /// at combat.cc:5905), so the walker never reports the same object twice in a row. Our
    /// single-pass Trace folds that loop away, so the conjunct is carried explicitly.
    ///
    /// This matters because ShootBlockerAt returns MULTIHEX objects that are NOT on the queried
    /// tile — _obj_shoot_blocking_at's adjacency loop (object.cc:2464-2490) scans the six
    /// neighbours. A line passing beside one multihex critter therefore touches two or three tiles
    /// that each hand back the SAME object, and without the conjunct each would increment
    /// numCrittersOnLof (combat.cc:5912), triple-charging one Brahmin against the −10/critter
    /// to-hit term. Trace dedupes by TILE only, so tile-dedup cannot catch this.</summary>
    [Fact]
    public void AMultihexObjectReportedFromSeveralAdjacentTilesIsSeenOnlyOnce()
    {
        int from = Tile(100, 100), to = Tile(112, 106);
        List<int> path = Path(from, to);
        Assert.True(path.Count >= 3);

        // ONE object, handed back for three consecutive traced tiles — exactly what the adjacency
        // phase does for a multihex critter sitting beside the line.
        var brahmin = Obj(Critter, path[1]);
        var reportedFrom = new HashSet<int> { path[0], path[1], path[2] };

        var seen = new List<MapObject>();
        var (blocker, critters) = LineOfFire.Trace(
            from, to, t => reportedFrom.Contains(t) ? brahmin : null, Walker,
            onCandidate: (obj, _) => seen.Add(obj));

        Assert.Null(blocker);                 // a critter is not an obstruction for this filter
        Assert.Equal(1, critters);            // counted ONCE, not three times
        Assert.Single(seen);                  // and reported to the caller once
        Assert.Same(brahmin, seen[0]);
    }

    /// <summary>The reference's pointer holds only the MOST RECENT obstacle, so the conjunct is
    /// last-reported, not a set: an object seen earlier IS re-reported once a different object has
    /// been reported in between. Pinning that so the dedupe is not "improved" into a HashSet.</summary>
    [Fact]
    public void TheDedupeIsLastReportedOnlyNotASet()
    {
        int from = Tile(100, 100), to = Tile(112, 106);
        List<int> path = Path(from, to);
        Assert.True(path.Count >= 4);

        var brahmin = Obj(Critter, path[1]);
        var other = Obj(Critter, path[2]); // a DIFFERENT object (identity, not Id, is what matters)

        var seen = new List<MapObject>();
        var (_, critters) = LineOfFire.Trace(
            from, to,
            t => t == path[1] || t == path[3] ? brahmin : t == path[2] ? other : null,
            Walker, onCandidate: (obj, _) => seen.Add(obj));

        Assert.Equal(new[] { brahmin, other, brahmin }, seen);
        Assert.Equal(3, critters);
    }
}
