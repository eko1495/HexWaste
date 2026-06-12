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
