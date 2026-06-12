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

/// <summary>
/// Ranged additions, ported from fallout2-ce src/combat.cc
/// attackDetermineToHit() (the PoC subset: distance/perception, ammo AC mod,
/// min strength, crowd penalty; perks/lighting/called shots dropped) and the
/// attackComputeDamage() ammo wrapper.
/// </summary>
public static class RangedMath
{
    public const int ReloadApCost = 2; // item.cc:1650 HIT_MODE_*_RELOAD

    /// <summary>To-hit for a gun shot (combat.cc:4314-4498 subset). No lower
    /// clamp, 95 cap — exactly like the engine.</summary>
    public static int ToHitChance(int skill, int distance, int perception, bool attackerIsDude,
        int targetAc, int ammoAcModifier, int weaponMinStrength, int attackerStrength,
        int crittersInPath)
    {
        int toHit = skill;

        // mult = 2 (no long-range/scope perks); dude uses PE-2, NPCs PE.
        int distanceMod = distance - 2 * (attackerIsDude ? perception - 2 : perception);
        distanceMod = Math.Max(distanceMod, -2 * perception);
        toHit += -4 * distanceMod;

        toHit -= 10 * crittersInPath;

        int minStrengthMod = weaponMinStrength - attackerStrength;
        if (minStrengthMod > 0)
            toHit -= 20 * minStrengthMod;

        toHit -= Math.Max(targetAc + ammoAcModifier, 0);
        return Math.Min(toHit, 95);
    }

    /// <summary>Gun damage (combat.cc:4581-4614 default path): the ×2 default
    /// multiplier then ÷2 wrapper is identity until criticals exist; ammo
    /// mult/div and DR modifier land here. Guns get no melee bonus.</summary>
    public static int RollDamage(Random rng, int minDamage, int maxDamage, CritterState target,
        int ammoDrModifier, int ammoDamageMultiplier, int ammoDamageDivisor)
    {
        int raw = rng.Next(minDamage, Math.Max(minDamage, maxDamage) + 1);
        int damage = raw * 2 * Math.Max(ammoDamageMultiplier, 1);
        damage /= Math.Max(ammoDamageDivisor, 1);
        damage /= 2;
        damage -= target.DamageThreshold;
        if (damage <= 0)
            return 0;
        int resistance = Math.Clamp(target.DamageResistance + ammoDrModifier, 0, 100);
        return damage - damage * resistance / 100;
    }
}
