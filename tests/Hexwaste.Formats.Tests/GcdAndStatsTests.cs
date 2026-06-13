using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

public class GcdAndStatsTests
{
    [GameDataFact]
    public void PlayerGcdParsesFully()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        using Stream stream = vfs.OpenRead(@"premade\player.gcd");
        GcdFile gcd = GcdFile.Load(stream);

        // The blank premade character: all SPECIAL at 5.
        for (int stat = 0; stat <= 6; stat++)
            Assert.InRange(gcd.Stats.BaseStats[stat] + gcd.Stats.BonusStats[stat], 1, 10);
        Assert.Equal(100, gcd.Stats.BaseStats[29]); // forced EMP resistance (gcdLoad)
        Assert.Equal(4, gcd.TaggedSkills.Length);
        Assert.Equal(2, gcd.Traits.Length);
        Assert.Equal(-1, stream.ReadByte()); // 432 bytes fully consumed (stream is non-seekable zlib)
    }

    [GameDataFact]
    public void CritterStatValueUsesOverrideAndPseudostats()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        var host = new ScriptHost(vfs, ScriptList.Load(vfs), protos);

        var dude = new MapObject
        {
            Id = -1,
            HexTile = 100,
            X = 0,
            Y = 0,
            Frame = 0,
            Rotation = 0,
            Fid = 0x01000000,
            Pid = 0x01000001,
            Flags = 0,
            Sid = -1,
        };
        dude.CurrentHp = 17;
        dude.Poison = 3;

        using Stream stream = vfs.OpenRead(@"premade\player.gcd");
        GcdFile gcd = GcdFile.Load(stream);
        host.StatsResolver = obj => obj == dude ? gcd.Stats : null;

        int agility = gcd.Stats.BaseStats[5] + gcd.Stats.BonusStats[5];
        Assert.Equal(agility, host.CritterStatValue(dude, 5));
        Assert.Equal(17, host.CritterStatValue(dude, 35)); // STAT_CURRENT_HIT_POINTS
        Assert.Equal(3, host.CritterStatValue(dude, 36)); // poison
        Assert.Equal(-1, host.CritterStatValue(dude, 99));

        // Non-overridden critters fall back to their prototype.
        var peasant = new MapObject
        {
            Id = 2,
            HexTile = 200,
            X = 0,
            Y = 0,
            Frame = 0,
            Rotation = 0,
            Fid = 0x01000000,
            Pid = 0x01000041, // Average Peasant (verified phase 6 M2 research)
            Flags = 0,
            Sid = -1,
        };
        CritterProtoStats proto = protos.Get(peasant.Pid).Critter!;
        Assert.Equal(proto.BaseStats[7] + proto.BonusStats[7], host.CritterStatValue(peasant, 7));
    }
}

public class RotationToTests
{
    [Fact]
    public void RotationToMatchesStepDirections()
    {
        // Walking one hex in rotation r, the rotation back to start is (r+3)%6
        // and toward the destination is r — tileGetRotationTo round-trip.
        int start = 100 * Hexwaste.Formats.Hex.HexGrid.Width + 100;
        for (int rotation = 0; rotation < 6; rotation++)
        {
            int next = Hexwaste.Formats.Hex.HexGrid.TileInDirection(start, rotation);
            Assert.Equal(rotation, Hexwaste.Formats.Hex.HexGrid.RotationTo(start, next));
            Assert.Equal((rotation + 3) % 6, Hexwaste.Formats.Hex.HexGrid.RotationTo(next, start));
        }
    }
}

public class GcdCreateTests
{
    [Fact]
    public void CreateComputesDerivedStatsLikeTheEngine()
    {
        // ST8 PE5 EN9 CH3 IN4 AG7 LK4, female, tags Small Guns/Melee/Throwing.
        int[] special = [8, 5, 9, 3, 4, 7, 4];
        Hexwaste.Formats.Combat.GcdFile g =
            Hexwaste.Formats.Combat.GcdFile.Create(special, [0, 4, 5], gender: 1);

        int[] bs = g.Stats.BaseStats;
        Assert.Equal(8 + 2 * 9 + 15, bs[7]);   // MaxHP = ST + 2*EN + 15 = 41
        Assert.Equal(7 / 2 + 5, bs[8]);        // MaxAP = AG/2 + 5 = 8
        Assert.Equal(7, bs[9]);                // AC = AG
        Assert.Equal(Math.Max(8 - 5, 1), bs[11]); // Melee = max(ST-5,1) = 3
        Assert.Equal(2 * 5, bs[13]);           // Sequence = 2*PE = 10
        Assert.Equal(4, bs[15]);               // CritChance = LK
        Assert.Equal(1, bs[34]);               // gender female
        Assert.Equal(100, bs[29]);             // EMP resist forced
        Assert.Equal([0, 4, 5, -1], g.TaggedSkills);

        // The tag bonus flows through SkillSet for the created dude.
        Assert.Equal(5 + 4 * 7 + 20, Hexwaste.Formats.Combat.SkillSet.Value(
            bs, g.Stats.BonusStats, g.Stats.Skills, g.TaggedSkills, 0)); // Small Guns tagged = 53
    }
}
