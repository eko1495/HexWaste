using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Combat;

/// <summary>
/// A character file (premade\*.gcd, 432 bytes): the dude's stat block in the
/// exact critter-proto data layout, then name, tagged skills, traits and
/// unspent character points. ported from fallout2-ce src/critter.cc
/// gcdLoad() / protoCritterDataRead().
/// </summary>
public sealed class GcdFile
{
    /// <summary>Stat block shaped like a critter proto's (team/ai unused).</summary>
    public required CritterProtoStats Stats { get; init; }

    public required string Name { get; init; }
    public required int[] TaggedSkills { get; init; }

    /// <summary>Two selected trait ids; -1 = none.</summary>
    public required int[] Traits { get; init; }

    public int RemainingCharPoints { get; init; }

    /// <summary>Builds an in-memory sheet from a created character (no .gcd
    /// write). Derived stats recompute per fallout2-ce stat.cc
    /// critterUpdateDerivedStats(). special = the 7 SPECIAL values; tags = the
    /// chosen skill indices (padded to 4 with -1); gender 0/1.</summary>
    public static GcdFile Create(int[] special, int[] tags, int gender, string name = "Wanderer")
    {
        int[] baseStats = new int[35];
        for (int i = 0; i < 7; i++)
            baseStats[i] = special[i];
        int st = special[0], pe = special[1], en = special[2], ag = special[5], lk = special[6];
        baseStats[7] = st + 2 * en + 15;          // MAXIMUM_HIT_POINTS
        baseStats[8] = ag / 2 + 5;                // MAXIMUM_ACTION_POINTS
        baseStats[9] = ag;                        // ARMOR_CLASS
        baseStats[11] = Math.Max(st - 5, 1);      // MELEE_DAMAGE
        baseStats[12] = 25 * st + 25;             // CARRY_WEIGHT
        baseStats[13] = 2 * pe;                   // SEQUENCE
        baseStats[14] = Math.Max(en / 3, 1);      // HEALING_RATE
        baseStats[15] = lk;                       // CRITICAL_CHANCE
        baseStats[29] = 100;                      // DAMAGE_RESISTANCE_EMP (gcdLoad)
        baseStats[31] = 2 * en;                   // RADIATION_RESISTANCE
        baseStats[32] = 5 * en;                   // POISON_RESISTANCE
        baseStats[34] = gender;                   // GENDER

        int[] tagged = [.. Enumerable.Range(0, 4).Select(i => i < tags.Length ? tags[i] : -1)];
        return new GcdFile
        {
            Stats = new CritterProtoStats(
                AiPacket: 0, Team: 0, CritterFlags: 0,
                baseStats, new int[35], new int[18],
                BodyType: 0, Experience: 0, KillType: 0, DamageType: 0),
            Name = name,
            TaggedSkills = tagged,
            Traits = [-1, -1],
            RemainingCharPoints = 0,
        };
    }

    public static GcdFile Load(Stream stream)
    {
        var reader = new BigEndianReader(stream);

        // protoCritterDataRead (critter.cc:1064)
        int critterFlags = reader.ReadInt32();
        int[] baseStats = reader.ReadInt32Array(35);
        int[] bonusStats = reader.ReadInt32Array(35);
        int[] skills = reader.ReadInt32Array(18);
        reader.Skip(3 * 4); // bodyType, experience, killType — gcdLoad zeroes them
        int damageType = reader.ReadInt32();

        // gcdLoad trailer (critter.cc:1037-1052)
        byte[] nameBytes = reader.ReadBytes(32);
        int nul = Array.IndexOf(nameBytes, (byte)0);
        string name = System.Text.Encoding.ASCII.GetString(nameBytes, 0, nul >= 0 ? nul : 32);
        int[] tagged = reader.ReadInt32Array(4);
        int[] traits = reader.ReadInt32Array(2);
        int points = reader.ReadInt32();

        // gcdLoad forces EMP resistance after load (critter.cc:1054).
        baseStats[29] = 100; // STAT_DAMAGE_RESISTANCE_EMP

        return new GcdFile
        {
            Stats = new CritterProtoStats(
                AiPacket: 0, Team: 0, CritterFlags: critterFlags,
                baseStats, bonusStats, skills,
                BodyType: 0, Experience: 0, KillType: 0, damageType),
            Name = name,
            TaggedSkills = tagged,
            Traits = traits,
            RemainingCharPoints = points,
        };
    }
}
