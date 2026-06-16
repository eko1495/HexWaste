using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Gory death-animation selection (P26), ported from fallout2-ce src/actions.cc _pick_death.
/// The corpse takes a gore variant by damage type + damage + attack animation, gated by the
/// violence level (the viewer fixes it at NORMAL). bloodyMess (trait) defaults false — out of scope.
/// </summary>
public class DeathAnimsTests
{
    private const int Normal = 0, Laser = 1, Fire = 2, Electrical = 4, Emp = 5, Explosion = 6;

    [Fact]
    public void SingleNormalShotNeverGibsAtNormalViolence()
    {
        // A single normal-damage gunshot stays FALL_BACK regardless of damage (only MAX_BLOOD +
        // big damage big-holes it) — so plain pistol kills don't gib.
        Assert.Equal(DeathAnims.FallBack, DeathAnims.Pick(Normal, 100, DeathAnims.FireSingle, DeathAnims.ViolenceNormal));
    }

    [Fact]
    public void BurstNormalGibsOnASolidHit()
    {
        // FIRE_BURST is not the single-shot special case → the table fires at damage >= 15.
        Assert.Equal(DeathAnims.DancingAutofire, DeathAnims.Pick(Normal, 20, DeathAnims.FireBurst, DeathAnims.ViolenceNormal));
        Assert.Equal(DeathAnims.FallBack, DeathAnims.Pick(Normal, 14, DeathAnims.FireBurst, DeathAnims.ViolenceNormal)); // under the threshold
    }

    [Fact]
    public void MeleeStaysFallBackWithoutBloodyMess()
    {
        Assert.Equal(DeathAnims.FallBack, DeathAnims.Pick(Normal, 50, DeathAnims.SwingAnim, DeathAnims.ViolenceNormal));
        Assert.Equal(DeathAnims.FallBack, DeathAnims.Pick(Normal, 50, DeathAnims.ThrowPunch, DeathAnims.ViolenceNormal));
    }

    [Theory]
    [InlineData(Laser, DeathAnims.SlicedInHalf)]
    [InlineData(Fire, DeathAnims.CharredBody)]
    [InlineData(Electrical, DeathAnims.Electrify)]
    [InlineData(Emp, DeathAnims.FallBack)]          // EMP has no gore corpse
    [InlineData(Explosion, DeathAnims.BigHole)]
    public void NormalTableByDamageType(int damageType, int expected) =>
        // A burst/non-single attack of each damage type at a solid hit (NORMAL violence).
        Assert.Equal(expected, DeathAnims.Pick(damageType, 20, DeathAnims.FireBurst, DeathAnims.ViolenceNormal));

    [Fact]
    public void MinimalViolenceSuppressesAllGore()
    {
        Assert.Equal(DeathAnims.FallBack, DeathAnims.Pick(Laser, 100, DeathAnims.FireBurst, DeathAnims.ViolenceMinimal));
    }

    [Fact]
    public void MaxBloodEscalatesOnBigDamage()
    {
        // > NORMAL violence + damage >= 45 → the bloodier table (normal → chunks of flesh).
        Assert.Equal(DeathAnims.ChunksOfFlesh, DeathAnims.Pick(Normal, 50, DeathAnims.FireBurst, DeathAnims.ViolenceMaxBlood));
        // A single normal shot big-holes only at MAX_BLOOD with big damage.
        Assert.Equal(DeathAnims.BigHole, DeathAnims.Pick(Normal, 50, DeathAnims.FireSingle, DeathAnims.ViolenceMaxBlood));
        // 15..44 at MAX_BLOOD still uses the normal table.
        Assert.Equal(DeathAnims.DancingAutofire, DeathAnims.Pick(Normal, 20, DeathAnims.FireBurst, DeathAnims.ViolenceMaxBlood));
    }

    [Fact]
    public void ExplosiveThrowGibsButASpearDoesNot()
    {
        // THROW_ANIM with EXPLOSION damage leaves the melee-like branch → the table (BIG_HOLE).
        Assert.Equal(DeathAnims.BigHole, DeathAnims.Pick(Explosion, 20, DeathAnims.ThrowAnim, DeathAnims.ViolenceNormal));
        // A thrown spear (NORMAL damage) is melee-like → FALL_BACK.
        Assert.Equal(DeathAnims.FallBack, DeathAnims.Pick(Normal, 20, DeathAnims.ThrowAnim, DeathAnims.ViolenceNormal));
    }

    [Fact]
    public void OutOfRangeDamageTypeIsSafe() =>
        Assert.Equal(DeathAnims.FallBack, DeathAnims.Pick(99, 50, DeathAnims.FireBurst, DeathAnims.ViolenceNormal));

    [Theory]
    [InlineData(true, false, DeathAnims.FireSingle)]  // gun
    [InlineData(false, true, DeathAnims.SwingAnim)]   // melee weapon
    [InlineData(false, false, DeathAnims.ThrowPunch)] // unarmed
    public void AttackAnimForPicksTheRightAnimation(bool isGun, bool hasWeapon, int expected) =>
        Assert.Equal(expected, DeathAnims.AttackAnimFor(isGun, hasWeapon));
}
