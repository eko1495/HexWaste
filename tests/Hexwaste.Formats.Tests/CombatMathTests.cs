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
        // P84: Easy 75% / Normal 100% / Hard 125% on the post-÷2 damage, before DT — combat.cc:4603.
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

    // F36: the melee/unarmed path now reads the loaded ammo's DR modifier, damage multiplier, damage
    // divisor (RollDamage/RollWeaponDamage) and AC modifier (ToHitChance) — the same four ungated reads
    // the gun path already makes (combat.cc:4579-4587 damage, :4429-4434 to-hit). Shipped data can't
    // exercise this (the only five non-gun weapons with a real ammoTypePid all load Small Energy Cell,
    // whose modifiers are all neutral — see docs/superpowers/specs/2026-08-23-melee-ammo-mods-design.md),
    // so every value below is synthetic.

    [Fact]
    public void AmmoDamageMultiplierScalesMeleeWeaponDamage()
    {
        // Fixed 20-20 range removes the roll from the arithmetic: raw = 20 + 0 (meleeDmg) = 20.
        // damage = raw * critMult(2) * ammoMult, guarded-divide by ammoDivisor(1) = no-op, /2, no DT/DR.
        var rng = new CountingCombatRng(1);
        CritterState attacker = NewState(meleeDmg: 0);
        CritterState target = NewState();

        Assert.Equal(20, CombatMath.RollWeaponDamage(rng, attacker, target, 20, 20)); // baseline: mult=1
        Assert.Equal(60, CombatMath.RollWeaponDamage(rng, attacker, target, 20, 20, ammoDamageMultiplier: 3));
        // Exactly one rng.Next draw per helper call — the hard constraint (F36 spec): the multiplier
        // must be pure arithmetic after the draw, never a second draw.
        Assert.Equal(2, rng.CallCount);
    }

    [Fact]
    public void AmmoDamageMultiplierAppliesBeforeTheHalvingNotAfter()
    {
        // Every other multiplier case here uses a raw*critMultiplier product that's even, so
        // floor(r/2)*m == floor(r*m/2) in all of them — they can't tell "multiply then halve" apart
        // from "halve then multiply". critMultiplier: 1 removes the default ×2 that would otherwise
        // force the product even again; raw = 21 (odd) * ammoDamageMultiplier 3 is the discriminator:
        //   correct (multiply first): floor(21*1*3 / 1) / 2 = floor(63/2) = 31
        //   wrong   (halve first):    floor(21*1 / 1) / 2 * 3 = floor(21/2) * 3 = 10*3 = 30
        var rng = new CountingCombatRng(1);
        CritterState attacker = NewState(meleeDmg: 0);
        CritterState target = NewState();

        Assert.Equal(31, CombatMath.RollWeaponDamage(rng, attacker, target, 21, 21, critMultiplier: 1, ammoDamageMultiplier: 3));
        Assert.Equal(1, rng.CallCount);
    }

    [Fact]
    public void AmmoDamageDivisorReducesMeleeWeaponDamageAndGuardsZero()
    {
        var rng = new CountingCombatRng(1);
        CritterState attacker = NewState(meleeDmg: 0);
        CritterState target = NewState();

        // damage = 20*2*1 = 40; 40/4 = 10; /2 = 5.
        Assert.Equal(5, CombatMath.RollWeaponDamage(rng, attacker, target, 20, 20, ammoDamageDivisor: 4));
        // combat.cc:4596 `if (damageDivisor != 0) damage /= damageDivisor;` — a 0 divisor must not divide
        // (and must not throw): damage = 20*2*3 = 120, divide SKIPPED, /2 = 60.
        Assert.Equal(60, CombatMath.RollWeaponDamage(rng, attacker, target, 20, 20, ammoDamageMultiplier: 3, ammoDamageDivisor: 0));
        Assert.Equal(2, rng.CallCount);
    }

    [Fact]
    public void AmmoDrModifierShiftsMeleeDamageAndClampsAtBothEnds()
    {
        // raw = 100 (fixed) * critMult(2) = 200, /1, /2 = 100 before DT/DR. dt=0 throughout.
        var rng = new CountingCombatRng(1);
        CritterState attacker = NewState(meleeDmg: 0);

        // No ammo DR mod: dr=10 stands as-is -> 100*(100-10)/100 = 90.
        Assert.Equal(90, CombatMath.RollWeaponDamage(rng, attacker, NewState(dr: 10), 100, 100));
        // dr=90 + ammoDrModifier=50 -> 140, clamped to 100 -> 100*(100-100)/100 = 0.
        Assert.Equal(0, CombatMath.RollWeaponDamage(rng, attacker, NewState(dr: 90), 100, 100, ammoDrModifier: 50));
        // dr=10 + ammoDrModifier=-30 -> -20, clamped to 0 -> 100*(100-0)/100 = 100 (no reduction at all).
        Assert.Equal(100, CombatMath.RollWeaponDamage(rng, attacker, NewState(dr: 10), 100, 100, ammoDrModifier: -30));
        Assert.Equal(3, rng.CallCount);
    }

    [Fact]
    public void AmmoAcModifierShiftsMeleeToHitWithFloorClamp()
    {
        // baseline: 50 - max(5+0+0, 0) = 45.
        Assert.Equal(45, CombatMath.ToHitChance(50, NewState(ac: 5), 0));
        // ammoAcModifier -10 -> max(5-10, 0) = 0 -> toHit = 50 (the >= 0 clamp, combat.cc:4430).
        Assert.Equal(50, CombatMath.ToHitChance(50, NewState(ac: 5), 0, ammoAcModifier: -10));
        // ammoAcModifier +20 -> max(5+20, 0) = 25 -> toHit = 25.
        Assert.Equal(25, CombatMath.ToHitChance(50, NewState(ac: 5), 0, ammoAcModifier: 20));
    }

    [Fact]
    public void NeutralAmmoValuesLeaveMeleePathUnchanged()
    {
        // The guarantee every existing call site rests on: the neutral defaults (0 DR mod, ×1, ÷1, +0 AC)
        // are byte-identical to explicitly passing them, and to omitting them entirely.
        var rngA = new CountingCombatRng(1);
        var rngB = new CountingCombatRng(1);
        CritterState attacker = NewState(meleeDmg: 2);
        CritterState target = NewState(dt: 1, dr: 20);

        Assert.Equal(
            CombatMath.RollWeaponDamage(rngA, attacker, target, 3, 8),
            CombatMath.RollWeaponDamage(rngB, attacker, target, 3, 8, ammoDrModifier: 0, ammoDamageMultiplier: 1, ammoDamageDivisor: 1));

        var rngC = new CountingCombatRng(1);
        var rngD = new CountingCombatRng(1);
        Assert.Equal(
            CombatMath.RollDamage(rngC, attacker, target),
            CombatMath.RollDamage(rngD, attacker, target, ammoDrModifier: 0, ammoDamageMultiplier: 1, ammoDamageDivisor: 1));

        Assert.Equal(
            CombatMath.ToHitChance(50, target, 3),
            CombatMath.ToHitChance(50, target, 3, ammoAcModifier: 0));
    }

    private sealed class CountingCombatRng(int value) : ICombatRng
    {
        public int CallCount { get; private set; }

        public int Next(int minInclusive, int maxExclusive)
        {
            CallCount++;
            return Math.Clamp(value, minInclusive, maxExclusive - 1);
        }
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
