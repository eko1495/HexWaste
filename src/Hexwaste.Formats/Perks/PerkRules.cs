namespace Hexwaste.Formats.Perks;

/// <summary>
/// Perk selection + the data-driven stat effects (P28-M2), ported from fallout2-ce src/perk.cc
/// (perkCanAdd 0x496A60, the table's stat/statModifier fields) + character_editor.cc:5713 (the
/// every-3-levels cadence, +1 with the Skilled trait). A perk's effect that is a plain stat
/// modifier (Toughness → DR, Action Boy → AP, More Criticals → crit chance, Lifegiver → HP, …)
/// is realised here via <see cref="StatModifier"/>; the combat-/skill-path perks are wired in M3.
///
/// Inert by default: a zero ranks array yields 0 for every stat, so a perk-less dude is
/// byte-identical (the same invariant as <see cref="Combat.TraitModifiers"/>).
/// </summary>
public static class PerkRules
{
    private const int GvarBit = 0x4000000; // param marks a global-var gate rather than a skill
    private const int MaxSelectablePerks = 37; // character_editor.cc:5707

    /// <summary>Levels between perk picks: 3, or 4 with the Skilled trait (character_editor.cc:5714).</summary>
    public static int Progression(bool skilled) => skilled ? 4 : 3;

    /// <summary>Total perk picks earned by <paramref name="level"/> (one per progression-multiple
    /// level), capped at 37.</summary>
    public static int PicksEarned(int level, bool skilled) =>
        Math.Min(level / Progression(skilled), MaxSelectablePerks);

    /// <summary>A perk's current rank (perkGetRank); 0 if out of range / null.</summary>
    public static int Rank(int[]? ranks, int perk) =>
        ranks is not null && perk >= 0 && perk < ranks.Length ? ranks[perk] : 0;

    /// <summary>The total stat modifier from all ranked perks affecting <paramref name="stat"/>
    /// (each perk's stat/statModifier × rank). 0 for a perk-less critter (inert by default).</summary>
    public static int StatModifier(int stat, int[]? ranks)
    {
        if (ranks is null)
            return 0;
        int m = 0;
        var entries = PerkTable.Entries;
        for (int i = 0; i < entries.Length && i < ranks.Length; i++)
            if (ranks[i] > 0 && entries[i].Stat == stat)
                m += ranks[i] * entries[i].StatModifier;
        return m;
    }

    /// <summary>The flat % a skill perk adds to <paramref name="skill"/> (the engine skill index).
    /// Ported verbatim from fallout2-ce src/perk.cc perkGetSkillModifier() — Medic/Mr.Fixit/Thief/
    /// Master Thief/Harmless/Speaker/Negotiator/Salesman/Gambler/Ranger/Survivalist/Vault City Training/
    /// Living Anatomy(Doctor). 0 for a null rank set (NPCs / no perks) -> byte-identical by default.
    /// DOCUMENTED CUT: Ghost's Sneak bonus is light-gated (objectGetLightIntensity) — no light model in
    /// CritterState — so it's omitted.</summary>
    public static int SkillModifier(int skill, int[]? ranks)
    {
        if (ranks is null)
            return 0;
        int Has(int perk) => Rank(ranks, perk) > 0 ? 1 : 0;
        return skill switch
        {
            6 => 10 * Has(PerkId.Medic) + 5 * Has(PerkId.VaultCityTraining),                 // First Aid
            7 => 10 * Has(PerkId.Medic) + 10 * Has(PerkId.LivingAnatomy) + 5 * Has(PerkId.VaultCityTraining), // Doctor
            8 => 10 * Has(PerkId.Thief),                                                       // Sneak (Ghost light-term cut)
            9 or 10 => 10 * Has(PerkId.Thief) + 15 * Has(PerkId.MasterThief)                   // Lockpick / Steal
                       + (skill == 10 ? 20 * Has(PerkId.Harmless) : 0),
            11 => 10 * Has(PerkId.Thief),                                                      // Traps
            12 or 13 => 10 * Has(PerkId.MrFixit),                                              // Science / Repair
            14 => 20 * Has(PerkId.Speaker) + 5 * Has(PerkId.ExpertExcrementExpeditor) + 10 * Has(PerkId.Negotiator), // Speech (+Negotiator via fallthrough)
            15 => 10 * Has(PerkId.Negotiator) + 20 * Has(PerkId.Salesman),                     // Barter
            16 => 20 * Has(PerkId.Gambler),                                                    // Gambling
            17 => 15 * Has(PerkId.Ranger) + 25 * Has(PerkId.Survivalist),                      // Outdoorsman
            _ => 0,
        };
    }

    /// <summary>The one-shot stat effect of a maxRank==-1 perk — the perkAddEffect maxRank==-1 path
    /// (perk.cc): the (Stat, StatModifier) pair when Stat != -1, PLUS the StatReqs[0..6] SPECIAL array
    /// (which for a granted/maxRank==-1 perk is the EFFECT, not a requirement). Returns the per-stat
    /// deltas to fold into a bonus-stat array (apply +1 on withdrawal onset, -1 on recovery). Used for
    /// the addiction-withdrawal penalties (a withdrawal IS such a perk: e.g. Buffout addiction 54 →
    /// ST-2/EN-2/AG-3, Jet addiction 70 → MaxAP-1/ST-1/PE-1). Empty for a rank-based / out-of-range perk.</summary>
    public static IEnumerable<(int Stat, int Delta)> MaxRankPerkEffect(int perkIndex)
    {
        if (perkIndex < 0 || perkIndex >= PerkTable.Entries.Length)
            yield break;
        PerkData p = PerkTable.Entries[perkIndex];
        if (p.MaxRank != -1)
            yield break;
        if (p.Stat != -1)
            yield return (p.Stat, p.StatModifier);
        for (int s = 0; s < 7; s++)
            if (p.StatReqs[s] != 0)
                yield return (s, p.StatReqs[s]);
    }

    /// <summary>Whether the critter may take another rank of a perk (perkCanAdd port): not maxed,
    /// PC level ≥ minLevel, the skill/gvar param gates (first-only / OR / AND), and the per-SPECIAL
    /// requirements (positive = minimum, negative = "at most").</summary>
    public static bool CanAdd(PerkData perk, int[]? ranks, int level,
        Func<int, int> getStat, Func<int, int> getSkill, Func<int, int> getGlobal)
    {
        if (perk.MaxRank == -1 || Rank(ranks, perk.Index) >= perk.MaxRank)
            return false;
        if (level < perk.MinLevel)
            return false;

        bool req1 = perk.Param1 == -1 || ParamMet(perk.Param1, perk.Value1, getSkill, getGlobal);

        // ported from perk.cc: param2 is consulted when param1 failed (OR fallback) or the mode
        // is AND (both required); first-only with a failed param1 is an outright reject.
        if (!req1 || perk.ParamMode == 2 /* AND */)
        {
            if (perk.ParamMode == 0 /* FIRST_ONLY */)
                return false;
            if (!req1 && perk.ParamMode == 2 /* AND */)
                return false;
            if (perk.Param2 == -1 || !ParamMet(perk.Param2, perk.Value2, getSkill, getGlobal))
                return false;
        }

        for (int s = 0; s < 7; s++)
        {
            int req = perk.StatReqs[s];
            int actual = getStat(s);
            if (req < 0 ? actual >= -req : actual < req)
                return false;
        }
        return true;
    }

    private static bool ParamMet(int param, int value, Func<int, int> getSkill, Func<int, int> getGlobal)
    {
        bool isVar = (param & GvarBit) != 0;
        int p = param & ~GvarBit;
        int actual = isVar ? getGlobal(p) : getSkill(p);
        // value < 0 is an "at most" gate: a skill must be below |value|, a gvar below value.
        if (value < 0)
            return isVar ? actual < value : actual < -value;
        return actual >= value;
    }
}
