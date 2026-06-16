namespace Hexwaste.Formats.Combat;

/// <summary>
/// Optional-trait stat/skill modifiers (P28-M1), ported verbatim from fallout2-ce src/trait.cc
/// traitGetStatModifier (0x4B3F18) + traitGetSkillModifier (0x4B40FC). The engine applies these
/// LIVE on every stat/skill read (critterGetStat / skillGetValue), so this is a per-read additive
/// layer — NOT a bake. A dude with no traits (<c>[-1,-1]</c>) yields 0 for every stat/skill, so a
/// fresh/created character is byte-identical to the pre-trait engine (the inert-by-default invariant).
///
/// PoC scope: the SPECIAL→derived propagation (Gifted/Bruiser raising HP/melee via the recompute in
/// critterUpdateDerivedStats) is baked at character-creation time, NOT here — matching the engine,
/// where stat reads only add the direct modifiers below. Chem Reliant/Resistant (need the addiction
/// system) and Sex Appeal (no engine implementation) are out; their stat/skill modifiers are simply
/// absent from the engine's tables too, so there is nothing to port.
/// </summary>
public static class TraitModifiers
{
    // Trait ids (trait_defs.h Trait enum).
    public const int FastMetabolism = 0, Bruiser = 1, SmallFrame = 2, OneHander = 3, Finesse = 4,
        Kamikaze = 5, HeavyHanded = 6, FastShot = 7, BloodyMess = 8, Jinxed = 9, GoodNatured = 10,
        ChemReliant = 11, ChemResistant = 12, SexAppeal = 13, Skilled = 14, Gifted = 15;

    /// <summary>True if <paramref name="trait"/> is one of the (up to two) selected traits — the
    /// engine's traitIsSelected (gSelectedTraits[0]/[1]). Null/empty = no traits.</summary>
    public static bool Has(int[]? traits, int trait) =>
        traits is not null && (traits.Length > 0 && traits[0] == trait || traits.Length > 1 && traits[1] == trait);

    /// <summary>The trait modifier for a stat (added to base+bonus), ported from
    /// traitGetStatModifier. <paramref name="baseStats"/> is the critter's own base block — three
    /// cases subtract a base stat (Kamikaze nulls AC, Small Frame the carry penalty off base ST,
    /// Fast Metabolism zeroes rad/poison).</summary>
    public static int GetStatModifier(int stat, int[]? traits, int[] baseStats)
    {
        if (traits is null)
            return 0;

        int m = 0;
        switch (stat)
        {
            case CritterStat.Strength:
                if (Has(traits, Gifted)) m += 1;
                if (Has(traits, Bruiser)) m += 2;
                break;
            case CritterStat.Perception:
            case CritterStat.Endurance:
            case CritterStat.Charisma:
            case CritterStat.Intelligence:
            case CritterStat.Luck:
                if (Has(traits, Gifted)) m += 1;
                break;
            case CritterStat.Agility:
                if (Has(traits, Gifted)) m += 1;
                if (Has(traits, SmallFrame)) m += 1;
                break;
            case CritterStat.MaximumActionPoints:
                if (Has(traits, Bruiser)) m -= 2;
                break;
            case CritterStat.ArmorClass:
                if (Has(traits, Kamikaze)) m -= baseStats[CritterStat.ArmorClass]; // nulls base AC
                break;
            case CritterStat.MeleeDamage:
                if (Has(traits, HeavyHanded)) m += 4;
                break;
            case CritterStat.CarryWeight:
                if (Has(traits, SmallFrame)) m -= 10 * baseStats[CritterStat.Strength];
                break;
            case CritterStat.Sequence:
                if (Has(traits, Kamikaze)) m += 5;
                break;
            case HealingRate:
                if (Has(traits, FastMetabolism)) m += 2;
                break;
            case CritterStat.CriticalChance:
                if (Has(traits, Finesse)) m += 10;
                break;
            case CritterStat.BetterCriticals:
                if (Has(traits, HeavyHanded)) m -= 30;
                break;
            case RadiationResistance:
                if (Has(traits, FastMetabolism)) m -= baseStats[RadiationResistance];
                break;
            case PoisonResistance:
                if (Has(traits, FastMetabolism)) m -= baseStats[PoisonResistance];
                break;
        }
        return m;
    }

    /// <summary>The trait skill-% modifier, ported from traitGetSkillModifier: Gifted −10 to every
    /// skill; Good Natured −10 combat / +15 first-aid/doctor/speech/barter.</summary>
    public static int GetSkillModifier(int skill, int[]? traits)
    {
        if (traits is null)
            return 0;

        int m = 0;
        if (Has(traits, Gifted))
            m -= 10;
        if (Has(traits, GoodNatured))
            m += skill switch
            {
                0 or 1 or 2 or 3 or 4 or 5 => -10,   // small/big guns, energy, unarmed, melee, throwing
                6 or 7 or 14 or 15 => 15,            // first aid, doctor, speech, barter
                _ => 0,
            };
        return m;
    }

    // Stat indices not named in CritterStat (stat_defs.h).
    private const int HealingRate = 14, RadiationResistance = 31, PoisonResistance = 32;
}
