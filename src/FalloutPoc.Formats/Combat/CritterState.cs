using FalloutPoc.Formats.Map;
using FalloutPoc.Formats.Proto;

namespace FalloutPoc.Formats.Combat;

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
    public const int Sequence = 13;
    public const int CriticalChance = 15;
    public const int DamageThreshold = 17;
    public const int DamageResistance = 24;
}

/// <summary>
/// A critter's effective combat numbers: proto stat block (base + bonus, the
/// non-dude path of fallout2-ce src/stat.cc critterGetStat()) over the MAP
/// instance's per-critter state (current HP, team, result flags).
/// </summary>
public sealed class CritterState(MapObject critter, CritterProtoStats proto)
{
    public MapObject Critter => critter;
    public CritterProtoStats Proto => proto;

    /// <summary>Effective stat = base + bonus (src/stat.cc critterGetStat()).</summary>
    public int Stat(int stat) => proto.BaseStats[stat] + proto.BonusStats[stat];

    public int MaxHp => Stat(CritterStat.MaximumHitPoints);

    /// <summary>Per-instance HP from the MAP record (denbus1 critters carry
    /// individual values), not the proto maximum.</summary>
    public int CurrentHp => critter.CurrentHp;

    public int ArmorClass => Stat(CritterStat.ArmorClass);
    public int MaxActionPoints => Stat(CritterStat.MaximumActionPoints);
    public int MeleeDamage => Stat(CritterStat.MeleeDamage);
    public int Sequence => Stat(CritterStat.Sequence);
    public int DamageThreshold => Stat(CritterStat.DamageThreshold);
    public int DamageResistance => Stat(CritterStat.DamageResistance);

    /// <summary>ported from fallout2-ce src/skill.cc skillGetValue(): unarmed =
    /// 30 + 2 × (AG + ST) + proto skill points (skill index 3).</summary>
    public int UnarmedSkill => 30 + 2 * (Stat(CritterStat.Agility) + Stat(CritterStat.Strength))
        + proto.Skills[3];

    public bool IsDead => critter.IsDead;
}
