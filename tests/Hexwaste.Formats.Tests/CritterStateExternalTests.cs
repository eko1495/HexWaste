using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// P34-M1: the critter_state (0x80FB) bitfield mapping (ScriptHost.CritterStateOf), a verbatim port
/// of interpreter_extra.cc opGetCritterState. Locks the truth table so a future mask refactor can't
/// silently change the value scripts read. CRITTER_STATE: NORMAL=0, DEAD=1, PRONE=2; an active critter
/// ORs in its DAM_CRIP bits (DamHealable == engine DAM_CRIP == 0x7C).
/// </summary>
public class CritterStateExternalTests
{
    // A critter pid has PID_TYPE (high byte) == 1; an upright critter FID has anim-type 0,
    // a prone (lying) FID has anim-type in 48..49 (ANIM_FALL_BACK_SF..ANIM_FALL_FRONT_SF).
    private const int CritterPid = 0x01000005;
    private const int UprightFid = 0x01000000;              // type 1, anim 0
    private const int ProneFid = 0x01000000 | (48 << 16);   // type 1, anim 48 (ANIM_FALL_BACK_SF)

    private static MapObject Critter(int combatResults, int fid = UprightFid, int pid = CritterPid) => new()
    {
        Id = 1, HexTile = 100, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = fid, Flags = 0, Pid = pid, CombatResults = combatResults,
    };

    [Fact]
    public void NullHandleIsDead() => Assert.Equal(0x01, ScriptHost.CritterStateOf(null));

    [Fact]
    public void NonCritterIsDead() =>
        // an item pid (PID_TYPE 0) is never a critter -> DEAD, like the engine's else-branch.
        Assert.Equal(0x01, ScriptHost.CritterStateOf(Critter(0, pid: 0x00000029)));

    [Fact]
    public void ActiveUprightUnhurtIsNormal() => Assert.Equal(0x00, ScriptHost.CritterStateOf(Critter(0)));

    [Fact]
    public void ActiveLyingIsProne() => Assert.Equal(0x02, ScriptHost.CritterStateOf(Critter(0, ProneFid)));

    [Fact]
    public void ActiveCrippledOrsInTheDamCripBits()
    {
        // crippled-left-leg (0x04) + blind (0x40) = 0x44; active + upright -> NORMAL(0) | 0x44.
        int results = CriticalTables.DamCripLegLeft | CriticalTables.DamBlind;
        Assert.Equal(0x44, ScriptHost.CritterStateOf(Critter(results)));
    }

    [Fact]
    public void KnockedOutButAliveIsProne() =>
        // inactive (knocked out) but not dead -> PRONE, via the engine's !critterIsDead branch.
        Assert.Equal(0x02, ScriptHost.CritterStateOf(Critter(CriticalTables.DamKnockedOut)));

    [Fact]
    public void LoseTurnButAliveIsProne() =>
        Assert.Equal(0x02, ScriptHost.CritterStateOf(Critter(CriticalTables.DamLoseTurn)));

    [Fact]
    public void DeadIsDead() =>
        Assert.Equal(0x01, ScriptHost.CritterStateOf(Critter(CriticalTables.DamDead)));
}
