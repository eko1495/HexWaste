using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

/// <summary>_combatai_rating (combat_ai.cc:3449): max(melee damage, best wielded weapon max
/// damage) + armor class. The dead/KO and non-critter guards live in the engine wrapper.</summary>
public class AiRatingTests
{
    [Fact]
    public void UnarmedCritterScoresMeleeDamagePlusAc()
    {
        Assert.Equal(11, AiRating.Score(meleeDamage: 8, armorClass: 3));
    }

    [Fact]
    public void WeaponMaxDamageWinsWhenHigherThanMelee()
    {
        // A 10mm pistol (max 12) on a melee-damage-5 critter with AC 4 → 12 + 4.
        Assert.Equal(16, AiRating.Score(meleeDamage: 5, armorClass: 4, 12));
    }

    [Fact]
    public void MeleeDamageWinsWhenWeaponIsWeaker()
    {
        // combat_ai.cc only replaces melee_damage when weapon max EXCEEDS it.
        Assert.Equal(13, AiRating.Score(meleeDamage: 9, armorClass: 4, 3));
    }

    [Fact]
    public void BestOfSeveralWeaponsIsUsed()
    {
        Assert.Equal(20, AiRating.Score(meleeDamage: 5, armorClass: 5, 8, 15, 2));
    }
}
