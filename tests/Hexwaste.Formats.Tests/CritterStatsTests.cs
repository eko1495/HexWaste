using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

public class CritterStatsTests
{
    [GameDataFact]
    public void EveryCritterProtoParsesWithSaneStats()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);

        using Stream lst = vfs.OpenRead(@"proto\critters\critters.lst");
        using var reader = new StreamReader(lst);

        int count = 0;
        while (reader.ReadLine() is not null)
        {
            count++;
            int pid = 0x01000000 | count;
            CritterProtoStats? c = protos.Get(pid).Critter;
            Assert.NotNull(c);

            int maxHp = c.BaseStats[CritterStat.MaximumHitPoints] + c.BonusStats[CritterStat.MaximumHitPoints];
            int strength = c.BaseStats[CritterStat.Strength] + c.BonusStats[CritterStat.Strength];
            Assert.InRange(maxHp, 0, 5000);
            Assert.InRange(strength, 0, 50);
            Assert.Equal(35, c.BaseStats.Length);
            Assert.Equal(35, c.BonusStats.Length);
            Assert.Equal(18, c.Skills.Length);
        }

        Assert.True(count > 400, $"critters.lst unexpectedly short ({count})");
    }

    [GameDataFact]
    public void DenResidentStatsMatchEmpiricalValues()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);

        using Stream stream = vfs.OpenRead(@"maps\denbus1.map");
        MapFile map = MapFile.Load(stream, protos);

        // Empirically verified instance at hex 13687 (Den townfolk).
        MapObject critter = map.Elevations[0]!.Objects
            .First(o => o.HexTile == 13687 && Fid.PidType(o.Pid) == (int)ObjectType.Critter);
        Assert.Equal(0x01000041, critter.Pid);
        Assert.Equal(33, critter.CurrentHp);
        Assert.Equal(1, critter.Team);
        Assert.Equal(14, critter.AiPacket);
        Assert.Equal(0, critter.CombatResults);
        Assert.False(critter.IsDead);

        var state = new CritterState(critter, protos.Get(critter.Pid).Critter!);
        Assert.Equal(33, state.MaxHp);
        Assert.Equal(5, state.ArmorClass);
        Assert.Equal(7, state.MaxActionPoints);
        Assert.Equal(50, state.UnarmedSkill); // 30 + 2×(AG+ST) + proto skill
    }
}
