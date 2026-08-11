namespace Hexwaste.Formats.Combat;

/// <summary>
/// ported from fallout2-ce src/combat_ai.cc _combatai_rating (:3449): a critter's threat rating —
/// the best of its melee damage and its wielded weapons' MAX damage, plus its armor class. Drives
/// retaliation (_combatai_check_retaliation, :3484) and the strength/weakness target comparators
/// (_compare_strength/_compare_weakness, :1330/:1366). PURE: the caller resolves the stats.
/// </summary>
public static class AiRating
{
    /// <summary>rating = max(meleeDamage, best weaponMaxDamage) + armorClass. The engine only
    /// replaces melee_damage when a weapon's max damage EXCEEDS it, so a weaker weapon is ignored.
    /// The dead/KO and non-critter → 0 guards belong to the caller (see CombatEngine.Rating).</summary>
    public static int Score(int meleeDamage, int armorClass, params int[] weaponMaxDamages)
    {
        int best = meleeDamage;
        foreach (int max in weaponMaxDamages)
            if (max > best)
                best = max;
        return best + armorClass;
    }
}
