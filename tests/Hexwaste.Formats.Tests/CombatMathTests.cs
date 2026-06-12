using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

public class CombatMathTests
{
    private static CritterState NewState(int strength = 5, int agility = 5, int unarmedPoints = 0,
        int ac = 0, int meleeDmg = 1, int dt = 0, int dr = 0, int hp = 20)
    {
        int[] baseStats = new int[35];
        baseStats[CritterStat.Strength] = strength;
        baseStats[CritterStat.Agility] = agility;
        baseStats[CritterStat.ArmorClass] = ac;
        baseStats[CritterStat.MeleeDamage] = meleeDmg;
        baseStats[CritterStat.DamageThreshold] = dt;
        baseStats[CritterStat.DamageResistance] = dr;
        baseStats[CritterStat.MaximumHitPoints] = hp;
        int[] skills = new int[18];
        skills[3] = unarmedPoints;

        var proto = new CritterProtoStats(0, 0, 0, baseStats, new int[35], skills, 0, 0, 0, 0);
        var obj = new MapObject
        {
            Id = 1,
            HexTile = 100,
            X = 0,
            Y = 0,
            Frame = 0,
            Rotation = 0,
            Fid = 0x01000000,
            Flags = 0,
            Pid = 0x01000001,
            Sid = -1,
        };
        obj.CurrentHp = hp;
        return new CritterState(obj, proto);
    }

    [Fact]
    public void ToHitIsSkillMinusAcClampedTo95()
    {
        // unarmed = 30 + 2×(5+5) + 0 = 50
        Assert.Equal(50, NewState().UnarmedSkill);
        Assert.Equal(45, CombatMath.ToHitChance(NewState(), NewState(ac: 5)));
        Assert.Equal(95, CombatMath.ToHitChance(NewState(unarmedPoints: 100), NewState(ac: 0)));
        Assert.Equal(0, CombatMath.ToHitChance(NewState(), NewState(ac: 200)));
    }

    [Fact]
    public void DamageRespectsThresholdAndResistance()
    {
        var rng = new Random(7);
        CritterState attacker = NewState(meleeDmg: 4); // raw 1..6

        for (int i = 0; i < 200; i++)
        {
            Assert.InRange(CombatMath.RollDamage(rng, attacker, NewState()), 1, 6);
            Assert.InRange(CombatMath.RollDamage(rng, attacker, NewState(dt: 2)), 0, 4);
            // 50% resistance halves (integer division)
            Assert.InRange(CombatMath.RollDamage(rng, attacker, NewState(dr: 50)), 0, 3);
            Assert.Equal(0, CombatMath.RollDamage(rng, attacker, NewState(dr: 100)));
            Assert.Equal(0, CombatMath.RollDamage(rng, attacker, NewState(dt: 6)));
        }
    }

    [Fact]
    public void SeededRollsAreDeterministic()
    {
        int[] RollSeries()
        {
            var rng = new Random(42);
            CritterState attacker = NewState();
            CritterState target = NewState(ac: 5);
            return [.. Enumerable.Range(0, 20).Select(_ =>
                CombatMath.RollHit(rng, CombatMath.ToHitChance(attacker, target))
                    ? CombatMath.RollDamage(rng, attacker, target)
                    : 0)];
        }

        Assert.Equal(RollSeries(), RollSeries());
    }
}
