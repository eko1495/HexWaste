namespace Hexwaste.Formats.Combat;

/// <summary>
/// Weapon attack-type + skill classification from a weapon's primary animation code
/// (<c>extendedFlags &amp; 0xF</c>), ported from fallout2-ce src/item.cc
/// <c>_attack_subtype[]</c>/<c>_attack_skill[]</c> (:116) and
/// <c>weaponGetSkillForHitMode()</c>'s SMALL_GUNS→ENERGY/BIG_GUNS refinement (:1186).
/// The AI's <c>best_weapon</c> preference ranking (<see cref="AiBestWeapon"/>) needs both.
/// </summary>
public static class WeaponClass
{
    // ATTACK_TYPE_* (item.h): NONE 0, UNARMED 1, MELEE 2, THROW 3, RANGED 4.
    public const int AttackNone = 0, AttackUnarmed = 1, AttackMelee = 2, AttackThrow = 3, AttackRanged = 4;

    // SKILL_* (skill_defs.h): SMALL_GUNS 0, BIG_GUNS 1, ENERGY 2, UNARMED 3, MELEE 4, THROWING 5.
    // ported from fallout2-ce src/item.cc _attack_subtype[9] / _attack_skill[9].
    private static readonly int[] AttackSubtype = { 0, 1, 1, 2, 2, 3, 4, 4, 4 };
    private static readonly int[] AttackSkill = { -1, 3, 3, 4, 4, 5, 0, 0, 0 };

    /// <summary>ATTACK_TYPE of the weapon's PRIMARY hit mode. Out-of-range codes → NONE
    /// (the engine indexes a fixed [9] array; real protos only use 0-8).</summary>
    public static int AttackType(int extendedFlags)
    {
        int index = extendedFlags & 0xF;
        return index < AttackSubtype.Length ? AttackSubtype[index] : AttackNone;
    }

    /// <summary>SKILL_* for the weapon's primary hit mode, with the SMALL_GUNS→ENERGY (laser/
    /// plasma/electrical damage) / →BIG_GUNS (0x100 BigGun flag) refinement (item.cc:1186).</summary>
    public static int Skill(int extendedFlags, int damageType)
    {
        int index = extendedFlags & 0xF;
        int skill = index < AttackSkill.Length ? AttackSkill[index] : -1;
        if (skill == 0) // SKILL_SMALL_GUNS
        {
            if (damageType is 1 or 3 or 4) // DAMAGE_TYPE_LASER / _PLASMA / _ELECTRICAL
                skill = 2; // SKILL_ENERGY_WEAPONS
            else if ((extendedFlags & 0x100) != 0) // ItemProtoExtendedFlags_BigGun
                skill = 1; // SKILL_BIG_GUNS
        }
        return skill;
    }
}
