using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Combat;

/// <summary>Saveable stat indices, ported from fallout2-ce src/stat_defs.h.</summary>
public static class CritterStat
{
    public const int Strength = 0;
    public const int Perception = 1;
    public const int Endurance = 2;
    public const int Charisma = 3;
    public const int Intelligence = 4;
    public const int Agility = 5;
    public const int Luck = 6;
    public const int MaximumHitPoints = 7;
    public const int MaximumActionPoints = 8;
    public const int ArmorClass = 9;
    public const int UnarmedDamage = 10;
    public const int MeleeDamage = 11;
    public const int CarryWeight = 12; // STAT_CARRY_WEIGHT — derived 25*ST+25 (stat.cc:571)
    public const int Sequence = 13;
    public const int CriticalChance = 15;
    public const int BetterCriticals = 16;
    public const int DamageThreshold = 17;
    public const int DamageResistance = 24;
}

/// <summary>Per-stat (min,max) bounds, ported from fallout2-ce src/stat.cc gStatDescriptions — the
/// std::clamp critterGetStat applies as its LAST step (stat.cc:369). Indices CritterState doesn't read
/// are unbounded (P74-M1). PRIMARY_STAT_MIN/MAX = 1/10.</summary>
public static class StatBounds
{
    public static (int Min, int Max) For(int stat) => stat switch
    {
        >= CritterStat.Strength and <= CritterStat.Luck => (1, 10), // SPECIAL
        CritterStat.MaximumHitPoints => (0, 999),
        CritterStat.MaximumActionPoints => (1, 99),
        CritterStat.ArmorClass => (0, 999),
        CritterStat.UnarmedDamage => (0, int.MaxValue),
        CritterStat.MeleeDamage => (0, 500),
        CritterStat.CarryWeight => (0, 999),
        CritterStat.Sequence => (0, 60),
        CritterStat.CriticalChance => (0, 100),
        CritterStat.BetterCriticals => (-60, 100),
        CritterStat.DamageThreshold => (0, 100),
        CritterStat.DamageResistance => (0, 90),
        _ => (int.MinValue, int.MaxValue),
    };

    public static int Clamp(int stat, int value)
    {
        (int min, int max) = For(stat);
        return Math.Clamp(value, min, max);
    }
}

/// <summary>
/// A critter's effective combat numbers: proto stat block (base + bonus, the
/// non-dude path of fallout2-ce src/stat.cc critterGetStat()) over the MAP
/// instance's per-critter state (current HP, team, result flags).
/// </summary>
public sealed class CritterState(MapObject critter, CritterProtoStats proto, int[]? taggedSkills = null,
    int[]? traits = null, int[]? perkRanks = null)
{
    public MapObject Critter => critter;
    public CritterProtoStats Proto => proto;

    /// <summary>Effective stat = base + bonus + trait modifier + perk modifier (src/stat.cc
    /// critterGetStat() applies traitGetStatModifier + the perk stat bonuses live; P28-M1/M2).
    /// <paramref name="traits"/>/<paramref name="perkRanks"/> are the dude's — null for NPCs / none,
    /// so the modifiers are 0 (inert by default).</summary>
    public int Stat(int stat)
    {
        int value = proto.BaseStats[stat] + proto.BonusStats[stat]
            + TraitModifiers.GetStatModifier(stat, traits, proto.BaseStats)
            + Perks.PerkRules.StatModifier(stat, perkRanks)
            // P70: Adrenaline Rush — +1 ST while current HP < max/2 (stat.cc:256, a CONDITIONAL stat perk the
            // flat StatModifier fold can't express). Inert without the perk -> byte-identical.
            + (stat == CritterStat.Strength && perkRanks is not null
               && Perks.PerkRules.Rank(perkRanks, Perks.PerkId.AdrenalineRush) > 0
               && critter.CurrentHp < Stat(CritterStat.MaximumHitPoints) / 2 ? 1 : 0)
            // P74-M1: Gain STR/PER/.../LCK — +1 to the primary stat (stat.cc:252-309, hardcoded per-case,
            // NOT data-driven). Perks 84..90 are contiguous over SPECIAL 0..6. Inert without the perk.
            + (stat >= CritterStat.Strength && stat <= CritterStat.Luck && perkRanks is not null
               && Perks.PerkRules.Rank(perkRanks, Perks.PerkId.GainStrength + stat) > 0 ? 1 : 0);
        // P74-M1: the engine clamps the effective stat to gStatDescriptions bounds as its last step
        // (stat.cc:369) — so a Gain-X/Gifted stack can't push a primary past 10. Inert when in-bounds.
        return StatBounds.Clamp(stat, value);
    }

    /// <summary>True if this critter is blinded by a crit (CombatResults DAM_BLIND).</summary>
    public bool Blind => (critter.CombatResults & CriticalTables.DamBlind) != 0;

    /// <summary>Perception, minus 5 when blinded (stat.cc:191 critterGetStat). Used by
    /// the ranged to-hit; the flat -25 + ×12 distance penalty are applied in the engine.</summary>
    public int Perception => Stat(CritterStat.Perception) - (Blind ? 5 : 0);

    /// <summary>Per-hex movement-point cost for a critter's combat-results flags
    /// (critter.cc:1349 critterGetMovementPointCostAdjustedForCrippledLegs): both legs
    /// crippled → 8×, either leg → 4×, else 1×.</summary>
    public static int MovePointCost(int combatResults)
    {
        bool left = (combatResults & CriticalTables.DamCripLegLeft) != 0;
        bool right = (combatResults & CriticalTables.DamCripLegRight) != 0;
        return left && right ? 8 : (left || right) ? 4 : 1;
    }

    /// <summary>Effective % of any skill (delegates to the canonical <see cref="SkillSet"/>) plus
    /// the trait skill modifier (skillGetValue adds traitGetSkillModifier on top of the base —
    /// Gifted −10 all, Good Natured ±combat/social; P28-M1). The dude's tags feed the base bonus.</summary>
    public int SkillValue(int skill) =>
        SkillSet.Value(proto.BaseStats, proto.BonusStats, proto.Skills, taggedSkills, skill)
        + TraitModifiers.GetSkillModifier(skill, traits)
        + Perks.PerkRules.SkillModifier(skill, perkRanks); // P70: Medic/Mr.Fixit/Thief/… (perkGetSkillModifier)

    public int MaxHp => Stat(CritterStat.MaximumHitPoints);

    /// <summary>Per-instance HP from the MAP record (denbus1 critters carry
    /// individual values), not the proto maximum.</summary>
    public int CurrentHp => critter.CurrentHp;

    public int ArmorClass => Stat(CritterStat.ArmorClass);
    public int MaxActionPoints => Stat(CritterStat.MaximumActionPoints);
    /// <summary>Max carried weight in pounds — derived 25*ST+25 (stat.cc:571); the engine
    /// applies it with no special-case in critterGetStat (P24).</summary>
    public int CarryWeight => Stat(CritterStat.CarryWeight);
    public int MeleeDamage => Stat(CritterStat.MeleeDamage);
    public int Sequence => Stat(CritterStat.Sequence);
    public int DamageThreshold => Stat(CritterStat.DamageThreshold);
    public int DamageResistance => Stat(CritterStat.DamageResistance);

    public int UnarmedSkill => SkillValue(3);
    public int MeleeWeaponsSkill => SkillValue(4);
    public int SmallGunsSkill => SkillValue(0);
    public int ThrowingSkill => SkillValue(5);
    public int BarterSkill => SkillValue(15);

    public bool IsDead => critter.IsDead;
}
