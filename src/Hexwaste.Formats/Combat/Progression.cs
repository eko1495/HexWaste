namespace Hexwaste.Formats.Combat;

/// <summary>
/// Player experience and level-up math, ported from fallout2-ce src/stat.cc
/// pcGetExperienceForLevel() and the level-up branch of pcAddExperience().
/// </summary>
public static class Progression
{
    public const int MaxLevel = 99; // PC_LEVEL_MAX

    /// <summary>XP required to reach a level (stat.cc:662): odd L →
    /// 1000·(L/2)·L, even L → 1000·(L/2)·(L−1); -1 past the cap.</summary>
    public static int XpForLevel(int level)
    {
        if (level >= MaxLevel)
            return -1;
        int half = level / 2;
        return (level & 1) != 0 ? 1000 * half * level : 1000 * half * (level - 1);
    }

    public static int LevelForXp(int xp)
    {
        int level = 1;
        while (level + 1 < MaxLevel && xp >= XpForLevel(level + 1))
            level++;
        return level;
    }

    /// <summary>Bonus max HP gained per level (stat.cc:771): EN/2 + 2
    /// (Lifegiver perks are out of PoC scope).</summary>
    public static int HpPerLevel(int endurance) => endurance / 2 + 2;

    /// <summary>Healing rate = max(EN/3, 1) (stat.cc:573).</summary>
    public static int HealingRate(int endurance) => Math.Max(endurance / 3, 1);

    /// <summary>Game-hours to heal a wound by resting (pipboy.cc:2113):
    /// (int)(hpToHeal / healingRate × 3).</summary>
    public static int RestHoursToHeal(int hpToHeal, int healingRate) =>
        hpToHeal <= 0 ? 0 : (int)((double)hpToHeal / Math.Max(healingRate, 1) * 3.0);

    /// <summary>HP healed by resting <paramref name="minutes"/> game-minutes — the
    /// inverse of <see cref="RestHoursToHeal"/> (healingRate HP per 3 hours = 180 min).
    /// Used by the Pip-Boy timed-rest options (P12 M1); "until healed" still rests the
    /// exact hours to full.</summary>
    public static int HpHealedResting(int minutes, int healingRate) =>
        minutes <= 0 ? 0 : (int)((double)minutes * Math.Max(healingRate, 1) / 180.0);
}

/// <summary>
/// Scriptless combat rules shared by the viewer's controller and tests —
/// the joiner predicate from fallout2-ce combat_ai's team check.
/// </summary>
public static class CombatRules
{
    /// <summary>Sight range used by the joiner check and obj_can_see (PoC: flat 20 hexes).</summary>
    public const int SightRangeHexes = 20;

    public static bool ShouldJoin(Map.MapObject candidate, IEnumerable<Map.MapObject> hostiles, int dudeTile) =>
        !candidate.IsDead
        && hostiles.Any(h => h.Team == candidate.Team)
        && Hex.HexGrid.Distance(candidate.HexTile, dudeTile) <= SightRangeHexes;
}
