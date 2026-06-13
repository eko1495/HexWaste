namespace Hexwaste.Formats.Combat;

/// <summary>
/// The skill table and growth math, ported from fallout2-ce src/skill.cc
/// (gSkillDescriptions, skillGetValue, skillsGetCost) and the level-up award
/// in src/character_editor.cc. Single source of truth for effective skill
/// percentages — CritterState's combat getters delegate here.
/// </summary>
public static class SkillSet
{
    public const int SkillCount = 18;
    public const int MaxSkill = 300;     // skill.cc:264
    public const int PointsBankCap = 99; // character_editor.cc banked-pool cap

    // ported from gSkillDescriptions (skill.cc): {defaultValue, statModifier,
    // stat1, stat2}. baseValueMult is 1 for every skill. Stat indices follow
    // stat_defs.h: ST0 PE1 EN2 CH3 IN4 AG5 LK6; -1 = none.
    private static readonly (int Default, int StatMod, int Stat1, int Stat2)[] Desc =
    [
        (5, 4, 5, -1),  // 0  Small Guns
        (0, 2, 5, -1),  // 1  Big Guns
        (0, 2, 5, -1),  // 2  Energy Weapons
        (30, 2, 5, 0),  // 3  Unarmed
        (20, 2, 5, 0),  // 4  Melee Weapons
        (0, 4, 5, -1),  // 5  Throwing
        (0, 2, 1, 4),   // 6  First Aid
        (5, 1, 1, 4),   // 7  Doctor
        (5, 3, 5, -1),  // 8  Sneak
        (10, 1, 1, 5),  // 9  Lockpick
        (0, 3, 5, -1),  // 10 Steal
        (10, 1, 1, 5),  // 11 Traps
        (0, 4, 4, -1),  // 12 Science
        (0, 3, 4, -1),  // 13 Repair
        (0, 5, 3, -1),  // 14 Speech
        (0, 4, 3, -1),  // 15 Barter
        (0, 5, 6, -1),  // 16 Gambling
        (0, 2, 2, 4),   // 17 Outdoorsman
    ];

    public static readonly string[] Names =
    [
        "Small Guns", "Big Guns", "Energy Weapons", "Unarmed", "Melee Weapons",
        "Throwing", "First Aid", "Doctor", "Sneak", "Lockpick", "Steal", "Traps",
        "Science", "Repair", "Speech", "Barter", "Gambling", "Outdoorsman",
    ];

    /// <summary>
    /// Effective skill %, ported from skill.cc skillGetValue(): default +
    /// statModifier × (stat1 [+ stat2]) + base points; for the dude, a tagged
    /// skill counts its base points a second time and adds +20. Clamp 300.
    /// taggedSkills null = an NPC (never tagged).
    /// </summary>
    public static int Value(int[] baseStats, int[] bonusStats, int[] skills, int[]? taggedSkills, int skill)
    {
        (int def, int statMod, int stat1, int stat2) = Desc[skill];
        int sum = Stat(stat1) + (stat2 != -1 ? Stat(stat2) : 0);
        int basePts = skills[skill];
        int value = def + statMod * sum + basePts; // baseValueMult = 1
        if (taggedSkills is not null && Array.IndexOf(taggedSkills, skill) >= 0)
            value += basePts + 20; // skill.cc:251-256
        return Math.Min(value, MaxSkill);

        int Stat(int i) => baseStats[i] + bonusStats[i];
    }

    /// <summary>Point cost to raise a skill of this effective value by one
    /// (skill.cc skillsGetCost): 1/2/3/4/5/6 at ≤100/125/150/175/200/above.</summary>
    public static int Cost(int effectiveValue) => effectiveValue switch
    {
        <= 100 => 1,
        <= 125 => 2,
        <= 150 => 3,
        <= 175 => 4,
        <= 200 => 5,
        _ => 6,
    };

    /// <summary>Skill points granted per level: 5 + 2 × IN
    /// (character_editor.cc:5686, before perk/trait modifiers).</summary>
    public static int PointsPerLevel(int intelligence) => 5 + 2 * intelligence;
}
