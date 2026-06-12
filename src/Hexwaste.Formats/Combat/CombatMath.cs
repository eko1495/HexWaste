namespace Hexwaste.Formats.Combat;

/// <summary>
/// Minimal unarmed combat rolls, the PoC subset of fallout2-ce
/// src/combat.cc attackComputeToHit()/attackComputeDamage(): outcome is
/// rolled before animating; damage applies when the sequence completes.
/// </summary>
public static class CombatMath
{
    /// <summary>Punch cost, src/combat.cc _item_w_mp_cost() unarmed default.</summary>
    public const int PunchApCost = 3;

    /// <summary>toHit = attack skill − target AC, clamped to the engine's 95% cap.</summary>
    public static int ToHitChance(CritterState attacker, CritterState target) =>
        ToHitChance(attacker.UnarmedSkill, target);

    public static int ToHitChance(int attackSkill, CritterState target) =>
        Math.Clamp(attackSkill - target.ArmorClass, 0, 95);

    public static bool RollHit(Random rng, int chance) => rng.Next(1, 101) <= chance;

    /// <summary>Unarmed: damage = rand(1, 2 + meleeDmg) − DT, ×(1 − DR/100), floor 0.</summary>
    public static int RollDamage(Random rng, CritterState attacker, CritterState target)
    {
        int raw = rng.Next(1, attacker.MeleeDamage + 3); // inclusive 1 .. 2+meleeDmg
        return ReduceByArmor(raw, target);
    }

    /// <summary>Melee weapon: rand(min, max) + the attacker's melee-damage
    /// bonus (item.cc:1244), then DT/DR.</summary>
    public static int RollWeaponDamage(Random rng, CritterState attacker, CritterState target,
        int minDamage, int maxDamage)
    {
        int raw = rng.Next(minDamage, Math.Max(minDamage, maxDamage) + 1) + attacker.MeleeDamage;
        return ReduceByArmor(raw, target);
    }

    private static int ReduceByArmor(int raw, CritterState target)
    {
        int afterThreshold = Math.Max(raw - target.DamageThreshold, 0);
        return afterThreshold * (100 - Math.Clamp(target.DamageResistance, 0, 100)) / 100;
    }
}
