using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// P34-M3: the dude's run-vs-walk decision (RunGuard.MovementAnimCode), a port of the 3 guards in
/// animation.cc animationRegisterRunToTile(). Locks ANIM_RUNNING=19 / ANIM_WALK=1 and the guard order
/// without a GraphicsDevice. Each guard independently forces a walk; otherwise the dude runs.
/// </summary>
public class RunGuardTests
{
    [Fact]
    public void RunsByDefaultWhenUnimpairedAndRunArtExists() =>
        Assert.Equal(19, RunGuard.MovementAnimCode(combatResults: 0, sneakFlag: false, silentRunning: false, runArtExists: true));

    [Fact]
    public void CrippledLegForcesWalk() =>
        Assert.Equal(1, RunGuard.MovementAnimCode(CriticalTables.DamCripLegLeft, false, false, runArtExists: true));

    [Fact]
    public void SneakingWithoutSilentRunningForcesWalk() =>
        Assert.Equal(1, RunGuard.MovementAnimCode(0, sneakFlag: true, silentRunning: false, runArtExists: true));

    [Fact]
    public void SneakingWithSilentRunningStillRuns() =>
        Assert.Equal(19, RunGuard.MovementAnimCode(0, sneakFlag: true, silentRunning: true, runArtExists: true));

    [Fact]
    public void MissingRunArtFallsBackToWalk() =>
        Assert.Equal(1, RunGuard.MovementAnimCode(0, false, false, runArtExists: false));
}
