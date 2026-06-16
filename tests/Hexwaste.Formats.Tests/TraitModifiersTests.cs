using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Optional-trait stat/skill modifiers (P28-M1), ported from fallout2-ce trait.cc
/// traitGetStatModifier / traitGetSkillModifier. The headline is the inert-by-default invariant:
/// a dude with no traits ([-1,-1] or null) yields 0 for every stat/skill, so existing goldens hold.
/// </summary>
public class TraitModifiersTests
{
    // base stat block with distinctive values for the base-referencing cases.
    private static int[] Base()
    {
        var b = new int[35];
        b[0] = 6;   // STRENGTH
        b[9] = 8;   // ARMOR_CLASS
        b[31] = 10; // RADIATION_RESISTANCE
        b[32] = 20; // POISON_RESISTANCE
        return b;
    }

    private static int[] T(params int[] ids) => ids;

    [Fact]
    public void DefaultDudeIsInert()
    {
        int[] none = [-1, -1];
        for (int stat = 0; stat <= 34; stat++)
        {
            Assert.Equal(0, TraitModifiers.GetStatModifier(stat, none, Base()));
            Assert.Equal(0, TraitModifiers.GetStatModifier(stat, null, Base()));
        }
        for (int skill = 0; skill <= 17; skill++)
        {
            Assert.Equal(0, TraitModifiers.GetSkillModifier(skill, none));
            Assert.Equal(0, TraitModifiers.GetSkillModifier(skill, null));
        }
    }

    [Fact]
    public void GiftedRaisesEverySpecialAndLowersSkills()
    {
        int[] g = T(TraitModifiers.Gifted, -1);
        for (int special = 0; special <= 6; special++)
            Assert.Equal(1, TraitModifiers.GetStatModifier(special, g, Base()));
        for (int skill = 0; skill <= 17; skill++)
            Assert.Equal(-10, TraitModifiers.GetSkillModifier(skill, g));
    }

    [Fact]
    public void BruiserStrengthUpActionPointsDown()
    {
        int[] b = T(TraitModifiers.Bruiser, -1);
        Assert.Equal(2, TraitModifiers.GetStatModifier(0, b, Base()));  // STRENGTH +2
        Assert.Equal(-2, TraitModifiers.GetStatModifier(8, b, Base())); // MAX_AP -2
    }

    [Fact]
    public void SmallFrameAgilityUpCarryPenaltyOffBaseStrength()
    {
        int[] s = T(TraitModifiers.SmallFrame, -1);
        Assert.Equal(1, TraitModifiers.GetStatModifier(5, s, Base()));        // AGILITY +1
        Assert.Equal(-10 * 6, TraitModifiers.GetStatModifier(12, s, Base())); // CARRY -10×baseST(6)
    }

    [Fact]
    public void KamikazeNullsArmorClassRaisesSequence()
    {
        int[] k = T(TraitModifiers.Kamikaze, -1);
        Assert.Equal(-8, TraitModifiers.GetStatModifier(9, k, Base())); // AC -baseAC(8) → nets base AC to 0
        Assert.Equal(5, TraitModifiers.GetStatModifier(13, k, Base()));  // SEQUENCE +5
    }

    [Fact]
    public void FastMetabolismHealingUpRadPoisonZeroed()
    {
        int[] f = T(TraitModifiers.FastMetabolism, -1);
        Assert.Equal(2, TraitModifiers.GetStatModifier(14, f, Base()));   // HEALING_RATE +2
        Assert.Equal(-10, TraitModifiers.GetStatModifier(31, f, Base())); // RAD -base(10) → 0
        Assert.Equal(-20, TraitModifiers.GetStatModifier(32, f, Base())); // POISON -base(20) → 0
    }

    [Fact]
    public void HeavyHandedAndFinesseCritEffects()
    {
        int[] h = T(TraitModifiers.HeavyHanded, -1);
        Assert.Equal(4, TraitModifiers.GetStatModifier(11, h, Base()));   // MELEE_DAMAGE +4
        Assert.Equal(-30, TraitModifiers.GetStatModifier(16, h, Base())); // BETTER_CRITICALS -30
        int[] f = T(TraitModifiers.Finesse, -1);
        Assert.Equal(10, TraitModifiers.GetStatModifier(15, f, Base()));  // CRITICAL_CHANCE +10
    }

    [Fact]
    public void GoodNaturedShiftsCombatAndSocialSkills()
    {
        int[] gn = T(TraitModifiers.GoodNatured, -1);
        Assert.Equal(-10, TraitModifiers.GetSkillModifier(0, gn));  // Small Guns
        Assert.Equal(-10, TraitModifiers.GetSkillModifier(5, gn));  // Throwing
        Assert.Equal(15, TraitModifiers.GetSkillModifier(6, gn));   // First Aid
        Assert.Equal(15, TraitModifiers.GetSkillModifier(15, gn));  // Barter
        Assert.Equal(0, TraitModifiers.GetSkillModifier(9, gn));    // Lockpick — unaffected
    }

    [Fact]
    public void TwoTraitsStack()
    {
        int[] both = T(TraitModifiers.Gifted, TraitModifiers.Bruiser);
        Assert.Equal(3, TraitModifiers.GetStatModifier(0, both, Base())); // Gifted +1 + Bruiser +2 STR
    }

    [Fact]
    public void HasChecksBothSlots()
    {
        Assert.True(TraitModifiers.Has([TraitModifiers.Gifted, -1], TraitModifiers.Gifted));
        Assert.True(TraitModifiers.Has([-1, TraitModifiers.Bruiser], TraitModifiers.Bruiser));
        Assert.False(TraitModifiers.Has([-1, -1], TraitModifiers.Gifted));
        Assert.False(TraitModifiers.Has(null, TraitModifiers.Gifted));
    }
}
