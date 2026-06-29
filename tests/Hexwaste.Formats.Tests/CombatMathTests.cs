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
        var rng = new SystemCombatRng(7);
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
    public void PenetrateWeaponPerkCutsOnlyDamageThreshold()
    {
        // P74-M2: Penetrate reduces DT to 20%, leaving DR (combat.cc:4535) — distinct from BYPASS (both).
        var rng = new SystemCombatRng(1);
        CritterState target = NewState(dt: 10, dr: 50);
        // raw 30 (min=max=30), critMult 2 / ÷2 = 30 before armor.
        //   normal:    30−10=20, 20−20·50/100 = 10
        //   penetrate: DT 10→2, 30−2=28, 28−28·50/100 = 14  (DR still 50)
        //   bypass:    DT→2, DR→10, 30−2=28, 28−28·10/100 = 26  (DR also cut → more damage than penetrate)
        Assert.Equal(10, RangedMath.RollDamage(rng, 30, 30, target, 0, 1, 1));
        Assert.Equal(14, RangedMath.RollDamage(rng, 30, 30, target, 0, 1, 1, penetrate: true));
        Assert.Equal(26, RangedMath.RollDamage(rng, 30, 30, target, 0, 1, 1, bypassArmor: true));
    }

    [Fact]
    public void DifficultyDamageModifierScalesAfterHalvingBeforeThreshold()
    {
        // P84: Easy 75% / Normal 100% / Hard 125% on the post-÷2 damage, before DT — combat.cc:4602.
        var rng = new SystemCombatRng(1);
        CritterState target = NewState(dt: 4, dr: 0);

        // Ranged: raw 20 (min=max), ×2/÷2 = 20, ×mod/100, −DT 4.
        //   easy:   20×75/100=15, 15−4 = 11
        //   normal: 20,           20−4 = 16
        //   hard:   20×125/100=25, 25−4 = 21
        Assert.Equal(16, RangedMath.RollDamage(rng, 20, 20, target, 0, 1, 1));                                    // default 100
        Assert.Equal(11, RangedMath.RollDamage(rng, 20, 20, target, 0, 1, 1, difficultyDamageModifier: 75));
        Assert.Equal(21, RangedMath.RollDamage(rng, 20, 20, target, 0, 1, 1, difficultyDamageModifier: 125));

        // Melee weapon: raw 20 (+0 melee bonus), same wrapper.
        CritterState attacker = NewState(meleeDmg: 0);
        Assert.Equal(16, CombatMath.RollWeaponDamage(rng, attacker, target, 20, 20));                             // default 100
        Assert.Equal(11, CombatMath.RollWeaponDamage(rng, attacker, target, 20, 20, difficultyDamageModifier: 75));
        Assert.Equal(21, CombatMath.RollWeaponDamage(rng, attacker, target, 20, 20, difficultyDamageModifier: 125));
    }

    [Fact]
    public void SeededRollsAreDeterministic()
    {
        int[] RollSeries()
        {
            var rng = new SystemCombatRng(42);
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
