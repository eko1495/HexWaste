using Hexwaste.Formats;
using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// F33: the reference splits line-of-fire into a coarse predicate
/// (_obj_shoot_blocking_at, object.cc:2440) and a per-caller filter. These pin each
/// caller's filter as the combination of independent terms it actually is, so Task 5
/// can assign them without guessing. Two of these were wrong in the plan's first draft
/// and only fixed by reading the call sites — do not relax them without doing the same.
///
/// These pin the FILTERS in isolation. The SHOOT_THRU arms of the three callers that apply no flag
/// test are unreachable in a real trace: _make_straight_path_func's own guard (animation.cc:1956)
/// drops SHOOT_THRU objects before any shoot caller sees them. LineOfFireTests pins that.
/// </summary>
public class ShotFilterTests
{
    private const int NoBlock = 0x10;
    private const int ShootThru = unchecked((int)0x80000000);

    private static MapObject Obj(int flags, ObjectType type) => new()
    {
        Id = 1, HexTile = 100, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = ((int)type << 24), Flags = flags, Pid = ((int)type << 24) | 5,
    };

    [Fact]
    public void ShotBlockedRollSkipsAShootThruObject() =>
        // combat.cc:3586 re-tests the flag the coarse predicate let through.
        Assert.False(ShotFilter.ShotBlockedRoll.Obstructs(Obj(ShootThru, ObjectType.Wall), isTarget: false));

    [Fact]
    public void ShotBlockedRollSkipsALivingCritter() =>
        // combat.cc:3587 — a critter there runs a to-hit roll; it is not a hard obstruction.
        Assert.False(ShotFilter.ShotBlockedRoll.Obstructs(Obj(0, ObjectType.Critter), isTarget: false));

    [Fact]
    public void ShotBlockedRollBlocksAPlainWall() =>
        Assert.True(ShotFilter.ShotBlockedRoll.Obstructs(Obj(0, ObjectType.Wall), isTarget: false));

    [Fact]
    public void BurstWalkAppliesNoFlagTestOfItsOwn() =>
        // combat.cc:3644 applies no flag test — only the type test. This pins the FILTER, not the
        // trace: in a shoot trace the walker's own guard (animation.cc:1956) has already dropped a
        // SHOOT_THRU object, so this arm is unreachable and such an object does NOT end the walk.
        // See LineOfFireTests.ShootTraceNeverReportsAShootThruObject.
        Assert.True(ShotFilter.BurstWalk.Obstructs(Obj(ShootThru, ObjectType.Wall), isTarget: false));

    [Fact]
    public void BurstWalkSkipsALivingCritter() =>
        // combat.cc:3644 breaks only for NON-critters; a critter is a hit candidate.
        Assert.False(ShotFilter.BurstWalk.Obstructs(Obj(0, ObjectType.Critter), isTarget: false));

    [Fact]
    public void AccidentalTargetCountsACritter() =>
        // combat.cc:3963 has NO type test at all — this is the one caller where a critter counts.
        Assert.True(ShotFilter.AccidentalTarget.Obstructs(Obj(0, ObjectType.Critter), isTarget: false));

    [Fact]
    public void AccidentalTargetSkipsAShootThruObject() =>
        Assert.False(ShotFilter.AccidentalTarget.Obstructs(Obj(ShootThru, ObjectType.Wall), isTarget: false));

    [Fact]
    public void ShotBlockedSkipsTheTarget() =>
        // combat.cc:5908's `obstacle != targetObj`.
        Assert.False(ShotFilter.ShotBlocked.Obstructs(Obj(0, ObjectType.Wall), isTarget: true));

    [Fact]
    public void ShotBlockedAppliesNoFlagTestOfItsOwn() =>
        // No flag test at this caller (combat.cc:5908). Filter-level only: the walker never hands
        // this caller a SHOOT_THRU object, so the arm is unreachable in a real trace.
        Assert.True(ShotFilter.ShotBlocked.Obstructs(Obj(ShootThru, ObjectType.Wall), isTarget: false));

    [Fact]
    public void ShotBlockedSkipsALivingCritter() =>
        // combat.cc:5908's FID_TYPE(obstacle->fid) != OBJ_TYPE_CRITTER half — the other of the
        // two terms this caller distinguishes on. A wrong filter that dropped ExcludesCritters
        // would pass every other test in this file.
        Assert.False(ShotFilter.ShotBlocked.Obstructs(Obj(0, ObjectType.Critter), isTarget: false));

    [Fact]
    public void FriendlyFireCountsEverythingTheWalkerReports() =>
        // combat_ai.cc:2586 compares identity only; it applies no flag or type test of its own —
        // the SHOOT_THRU one it would need was already applied by the walker (a6 == 32).
        Assert.True(ShotFilter.FriendlyFire.Obstructs(Obj(ShootThru, ObjectType.Critter), isTarget: true));

    [Fact]
    public void NoBlockIsNotAFilterTermForAnyCaller()
    {
        // F33 (Task 7): the placeholder ShotFilter.LegacyCollapsed carried an ExcludesNoBlock term
        // purely to reproduce the pre-F33 flag CONJUNCTION while consumers were migrated. It has no
        // reference counterpart: _obj_shoot_blocking_at (object.cc:2440) gates on the DISJUNCTION
        // `NO_BLOCK == 0 || SHOOT_THRU == 0`, and NO reference caller re-tests NO_BLOCK, so a
        // NO_BLOCK-but-not-SHOOT_THRU wall the coarse predicate reports really does obstruct. This
        // pins that for every shipped filter, so the collapsed behaviour cannot creep back.
        MapObject noBlockWall = Obj(NoBlock, ObjectType.Wall);
        Assert.True(ShotFilter.ShotBlockedRoll.Obstructs(noBlockWall, isTarget: false));
        Assert.True(ShotFilter.BurstWalk.Obstructs(noBlockWall, isTarget: false));
        Assert.True(ShotFilter.AccidentalTarget.Obstructs(noBlockWall, isTarget: false));
        Assert.True(ShotFilter.ShotBlocked.Obstructs(noBlockWall, isTarget: false));
        Assert.True(ShotFilter.FriendlyFire.Obstructs(noBlockWall, isTarget: false));
    }
}
