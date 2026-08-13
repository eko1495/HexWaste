using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Unit cover for the AI weapon-preference ranking (P43) — <see cref="AiBestWeapon"/> ported from
/// fallout2-ce combat_ai.cc _ai_best_weapon / _weapPrefOrderings / _caiHasWeapPrefType, and the
/// <see cref="WeaponClass"/> attack-type/skill classification (item.cc _attack_subtype/_attack_skill).
/// </summary>
public class AiBestWeaponTests
{
    // ATTACK_TYPE_*: UNARMED 1, MELEE 2, THROW 3, RANGED 4.
    private static AiBestWeapon.Choice W(int attackType, int avg, int cost = 0, bool ignore = false, bool flare = false)
        => new(attackType, avg, cost, ignore, flare);

    [Theory]
    // ranged_over_melee (3): ranged is order 0, melee order 1 → a ranged candidate beats a melee best.
    [InlineData(3, WeaponClass.AttackMelee, WeaponClass.AttackRanged, true)]
    [InlineData(3, WeaponClass.AttackRanged, WeaponClass.AttackMelee, false)]
    // melee_over_ranged (2): melee order 0, ranged order 1 → melee wins.
    [InlineData(2, WeaponClass.AttackRanged, WeaponClass.AttackMelee, true)]
    [InlineData(2, WeaponClass.AttackMelee, WeaponClass.AttackRanged, false)]
    // ranged (4): only ranged is in the ordering; a melee candidate (order 999) never beats a ranged best.
    [InlineData(4, WeaponClass.AttackRanged, WeaponClass.AttackMelee, false)]
    public void PrefersHonorsThePreferenceOrdering(int bestWeapon, int aType, int bType, bool bWins)
    {
        // Equal avg damage so the order term decides (not the damage tiebreak).
        Assert.Equal(bWins, AiBestWeapon.Prefers(bestWeapon, W(aType, 10), W(bType, 10)));
    }

    [Fact]
    public void WithinFiveDamageTheHigherItemCostWins()
    {
        // Same attack type + same order → |Δavg| ≤ 5 → cost tiebreak (combat_ai.cc:1949).
        Assert.True(AiBestWeapon.Prefers(4, W(WeaponClass.AttackRanged, 10, cost: 100),
                                            W(WeaponClass.AttackRanged, 12, cost: 200)));
        Assert.False(AiBestWeapon.Prefers(4, W(WeaponClass.AttackRanged, 10, cost: 200),
                                             W(WeaponClass.AttackRanged, 12, cost: 100)));
    }

    [Fact]
    public void BeyondFiveDamageTheHigherAverageWinsWhenOrdersTie()
    {
        Assert.True(AiBestWeapon.Prefers(4, W(WeaponClass.AttackRanged, 10),
                                            W(WeaponClass.AttackRanged, 20)));
        Assert.False(AiBestWeapon.Prefers(4, W(WeaponClass.AttackRanged, 20),
                                             W(WeaponClass.AttackRanged, 10)));
    }

    [Fact]
    public void NeitherInThePreferenceKeepsTheRunningBest()
    {
        // best_weapon = melee (1): two RANGED choices both order 999 → return false (keep a ≡ engine null).
        Assert.False(AiBestWeapon.Prefers(1, W(WeaponClass.AttackRanged, 10), W(WeaponClass.AttackRanged, 99)));
    }

    [Fact]
    public void IgnoredCandidateLosesToAnInOrderBest()
    {
        // b is ignored (over-range / unsafe) → order 999; a (ranged) is in the ranged pref → a wins.
        Assert.False(AiBestWeapon.Prefers(4, W(WeaponClass.AttackRanged, 10), W(WeaponClass.AttackRanged, 99, ignore: true)));
    }

    [Fact]
    public void AFlareIsDeprioritisedWhenOrdersDiffer()
    {
        // a is a flare, b a real ranged weapon (different orders) → prefer b.
        Assert.True(AiBestWeapon.Prefers(3, W(WeaponClass.AttackThrow, 5, flare: true), W(WeaponClass.AttackRanged, 5)));
        // b is the flare → keep a.
        Assert.False(AiBestWeapon.Prefers(3, W(WeaponClass.AttackRanged, 5), W(WeaponClass.AttackThrow, 5, flare: true)));
    }

    [Fact]
    public void DefaultMinusOneUsesTheDamageOverrideAcrossDifferentOrders()
    {
        // best_weapon == -1 (absent): the |Δavg| > 5 override picks the higher-damage weapon even across
        // different attack-type orders (combat_ai.cc:1963).
        Assert.True(AiBestWeapon.Prefers(-1, W(WeaponClass.AttackRanged, 10), W(WeaponClass.AttackMelee, 20)));
    }

    [Fact]
    public void RandomIsResolvedByTheCallerCoin()
    {
        Assert.True(AiBestWeapon.Prefers(7, W(WeaponClass.AttackRanged, 10), W(WeaponClass.AttackMelee, 1), randomFavorsB: true));
        Assert.False(AiBestWeapon.Prefers(7, W(WeaponClass.AttackRanged, 10), W(WeaponClass.AttackMelee, 1), randomFavorsB: false));
    }

    [Theory]
    [InlineData(3, WeaponClass.AttackRanged, true)]   // ranged_over_melee includes ranged
    [InlineData(3, WeaponClass.AttackMelee, true)]    // …and melee
    [InlineData(3, WeaponClass.AttackThrow, false)]   // …but not throw
    [InlineData(4, WeaponClass.AttackMelee, false)]   // ranged-only excludes melee
    [InlineData(5, WeaponClass.AttackUnarmed, true)]  // unarmed includes unarmed
    [InlineData(-1, WeaponClass.AttackThrow, true)]   // default ordering includes throw
    public void HasWeapPrefTypeMatchesTheOrdering(int bestWeapon, int attackType, bool expected)
        => Assert.Equal(expected, AiBestWeapon.HasWeapPrefType(bestWeapon, attackType));

    [Theory]
    [InlineData(0x1, WeaponClass.AttackUnarmed)] // punch
    [InlineData(0x3, WeaponClass.AttackMelee)]   // swing
    [InlineData(0x5, WeaponClass.AttackThrow)]   // throw
    [InlineData(0x6, WeaponClass.AttackRanged)]  // single fire
    [InlineData(0x7, WeaponClass.AttackRanged)]  // burst
    [InlineData(0x0, WeaponClass.AttackNone)]    // none
    public void WeaponClassMapsAttackType(int extFlags, int expected)
        => Assert.Equal(expected, WeaponClass.AttackType(extFlags));

    [Theory]
    [InlineData(0x1, 0, 3)]  // punch → SKILL_UNARMED (damage type irrelevant)
    [InlineData(0x3, 0, 4)]  // swing → SKILL_MELEE_WEAPONS
    [InlineData(0x5, 0, 5)]  // throw → SKILL_THROWING
    [InlineData(0x6, 0, 0)]  // single, normal damage → SKILL_SMALL_GUNS
    [InlineData(0x6, 1, 2)]  // single, laser → SKILL_ENERGY_WEAPONS
    [InlineData(0x106, 0, 1)] // single, BigGun flag (0x100), normal → SKILL_BIG_GUNS
    public void WeaponClassMapsSkill(int extFlags, int damageType, int expected)
        => Assert.Equal(expected, WeaponClass.Skill(extFlags, damageType));

    [Fact]
    public void AvgDamageIsTheMidpointWithoutAPerk()
    {
        // weaponPerk -1 = no perk (WeaponProtoStats.WeaponPerk's default).
        Assert.Equal(7, AiBestWeapon.AvgDamage(minDamage: 4, maxDamage: 10, weaponPerk: -1));
    }

    [Fact]
    public void AvgDamageDoublesWhenTheWeaponHasAPerk()
    {
        // combat_ai.cc:1866 — SFALL "Lower weapon score multiplier for having perk": avgDamage *= 2.
        // PerkAccurate == 59.
        Assert.Equal(14, AiBestWeapon.AvgDamage(minDamage: 4, maxDamage: 10, weaponPerk: 59));
    }

    [Fact]
    public void AvgDamageUsesIntegerDivisionLikeTheEngine()
    {
        Assert.Equal(7, AiBestWeapon.AvgDamage(minDamage: 5, maxDamage: 10, weaponPerk: -1));
    }

    [Fact]
    public void AvgDamageMultipliesByTheExplosionExtrasCount()
    {
        // combat_ai.cc:1861 — avgDamage *= attack.extrasLength + 1, applied BEFORE the perk doubling.
        // (4+10)/2 = 7, two extra victims -> 7 * 3 = 21.
        Assert.Equal(21, AiBestWeapon.AvgDamage(minDamage: 4, maxDamage: 10, weaponPerk: -1, explosionExtras: 2));
    }

    [Fact]
    public void ExplosionExtrasApplyBeforeThePerkDoubling()
    {
        // 7 * (1+1) extras = 14, then *2 for the perk = 28. PerkAccurate == 59.
        Assert.Equal(28, AiBestWeapon.AvgDamage(minDamage: 4, maxDamage: 10, weaponPerk: 59, explosionExtras: 1));
    }

    [Fact]
    public void ZeroExtrasLeavesTheScoreUnchanged()
    {
        // The default keeps every pre-existing call site byte-identical.
        Assert.Equal(7, AiBestWeapon.AvgDamage(minDamage: 4, maxDamage: 10, weaponPerk: -1, explosionExtras: 0));
        Assert.Equal(7, AiBestWeapon.AvgDamage(minDamage: 4, maxDamage: 10, weaponPerk: -1));
    }
}
