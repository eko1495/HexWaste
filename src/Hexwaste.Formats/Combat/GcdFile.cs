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
