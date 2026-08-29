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
/// test are unreachable in a real trace: _make_straight_path_func's own guard (animation.cc:1957)
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
        // trace: in a shoot trace the walker's own guard (animation.cc:1957) has already dropped a
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
    public void ShotBlockedPenaltySkipsTheTarget() =>
        // combat.cc:5908's `obstacle != targetObj`.
        Assert.False(ShotFilter.ShotBlockedPenalty.Obstructs(Obj(0, ObjectType.Wall), isTarget: true));

    [Fact]
    public void ShotBlockedPenaltyAppliesNoFlagTestOfItsOwn() =>
        // No flag test at this caller (combat.cc:5908). Filter-level only: the walker never hands
        // this caller a SHOOT_THRU object, so the arm is unreachable in a real trace.
        Assert.True(ShotFilter.ShotBlockedPenalty.Obstructs(Obj(ShootThru, ObjectType.Wall), isTarget: false));

    [Fact]
    public void ShotBlockedPenaltySkipsALivingCritter() =>
        // combat.cc:5908's FID_TYPE(obstacle->fid) != OBJ_TYPE_CRITTER half — the other of the
        // two terms this caller distinguishes on. A wrong filter that dropped ExcludesCritters
        // would pass every other test in this file.
        Assert.False(ShotFilter.ShotBlockedPenalty.Obstructs(Obj(0, ObjectType.Critter), isTarget: false));

    [Fact]
    public void FriendlyFireCountsEverythingTheWalkerReports() =>
        // combat_ai.cc:2586 compares identity only; it applies no flag or type test of its own —
        // the SHOOT_THRU one it would need was already applied by the walker (a6 == 32).
        Assert.True(ShotFilter.FriendlyFire.Obstructs(Obj(ShootThru, ObjectType.Critter), isTarget: true));

    [Fact]
    public void LegacyCollapsedReproducesTodaysBehaviour()
    {
        Assert.False(ShotFilter.LegacyCollapsed.Obstructs(Obj(NoBlock, ObjectType.Wall), isTarget: false));
        Assert.False(ShotFilter.LegacyCollapsed.Obstructs(Obj(ShootThru, ObjectType.Wall), isTarget: false));
        Assert.True(ShotFilter.LegacyCollapsed.Obstructs(Obj(0, ObjectType.Wall), isTarget: false));

        // ExcludesCritters is TRUE here as of Task 5: the critter-vs-blocker split moved OUT of
        // LineOfFire.Trace and into the filter, so reproducing the collapsed behaviour (critters
        // counted, never a hard obstruction) now requires the term to be set here.
        Assert.False(ShotFilter.LegacyCollapsed.Obstructs(Obj(0, ObjectType.Critter), isTarget: false));

        // ExcludesTarget is TRUE — as of Task 5 this is live, not forward-safety: it reproduces the
        // target-tile skip Trace used to hard-code, now expressed as the reference's own identity
        // test.
        Assert.False(ShotFilter.LegacyCollapsed.Obstructs(Obj(0, ObjectType.Wall), isTarget: true));
    }
}
