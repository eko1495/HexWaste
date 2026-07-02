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
        ToHitChance(attackSkill, target, 0);

    /// <summary><paramref name="extraAc"/> (P77) is the defender's remaining-AP dodge bonus, folded into
    /// the AC before the clamp (stat.cc:239 adds it into STAT_ARMOR_CLASS); 0 = no change.</summary>
    public static int ToHitChance(int attackSkill, CritterState target, int extraAc) =>
        Math.Clamp(attackSkill - (target.ArmorClass + extraAc), 0, 95);

    public static bool RollHit(ICombatRng rng, int chance) => rng.Next(1, 101) <= chance;

    /// <summary>Unarmed: damage = rand(1, 2 + meleeDmg), ×critMult/2 (default 2 =
    /// identity; the crit multiplier slots where the engine's hardcoded 2 lives),
    /// then DT/DR. BYPASS cuts DT/DR to 20% (combat.cc:4530). <paramref name="extraDr"/>
    /// (P29-M1 Finesse) is added to the defender's DR on the non-bypass path.</summary>
    public static int RollDamage(ICombatRng rng, CritterState attacker, CritterState target,
        int critMultiplier = 2, bool bypassArmor = false, int extraDr = 0, bool penetrate = false,
        int difficultyDamageModifier = 100)
    {
        int raw = rng.Next(1, attacker.MeleeDamage + 3); // inclusive 1 .. 2+meleeDmg
        return ReduceByArmor(raw * critMultiplier / 2, target, bypassArmor, extraDr, penetrate, difficultyDamageModifier);
    }

    /// <summary>Melee weapon: rand(min, max) + the attacker's melee-damage
    /// bonus (item.cc:1244), ×critMult/2, then DT/DR.</summary>
    public static int RollWeaponDamage(ICombatRng rng, CritterState attacker, CritterState target,
        int minDamage, int maxDamage, int critMultiplier = 2, bool bypassArmor = false, int extraDr = 0,
        bool penetrate = false, int difficultyDamageModifier = 100)
    {
        int raw = rng.Next(minDamage, Math.Max(minDamage, maxDamage) + 1) + attacker.MeleeDamage;
        return ReduceByArmor(raw * critMultiplier / 2, target, bypassArmor, extraDr, penetrate, difficultyDamageModifier);
    }

    private static int ReduceByArmor(int raw, CritterState target, bool bypassArmor = false, int extraDr = 0,
        bool penetrate = false, int difficultyDamageModifier = 100)
    {
        // P84: the Easy/Hard combat-difficulty damage modifier (75/100/125) scales damage dealt by
        // attackers NOT on the dude's team — applied AFTER the ×crit/2 wrapper and BEFORE the DT
        // subtraction, exactly as the engine. 100 (Normal / a dude or ally attacker) = identity, so the
        // combat goldens stay byte-identical. ported from fallout2-ce src/combat.cc attackComputeDamage()
        // (the team gate combat.cc:4554, the `damage *= combatDifficultyDamageModifier; damage /= 100` at :4602).
        raw = raw * difficultyDamageModifier / 100;
        int dt = target.DamageThreshold;
        int dr = target.DamageResistance;
        if (bypassArmor)
        {
            dt = 20 * dt / 100;
            dr = 20 * dr / 100;
        }
        else
        {
            dr += extraDr; // P29-M1 Finesse: a dude attacker raises the defender's DR +30 (combat.cc:4540)
        }
        // P74-M2: the Penetrate weapon perk cuts DT to 20% — DT ONLY, NOT DR (combat.cc:4535; distinct from
        // BYPASS which cuts both). Applied after bypass, so a bypass+penetrate weapon cuts DT twice.
        if (penetrate)
            dt = 20 * dt / 100;
        int afterThreshold = Math.Max(raw - dt, 0);
        return afterThreshold * (100 - Math.Clamp(dr, 0, 100)) / 100;
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
        int crittersInPath, bool attackerBlind = false,
        int perkRangeMult = 2, int perkMinRange = 0)
    {
        int toHit = skill;

        // P113 (combat.cc:4337-4392): the PE range multiplier is a weapon-perk property —
        // 2 default, 4 LONG_RANGE, 5 SCOPE_RANGE (which also penalizes INSIDE its 8-hex minimum:
        // dist < minRange → dist += minRange instead of the PE bonus). Dude uses PE-2, NPCs PE.
        int distanceMod = distance >= perkMinRange
            ? distance - perkRangeMult * (attackerIsDude ? perception - 2 : perception)
            : distance + perkMinRange;
        distanceMod = Math.Max(distanceMod, -2 * perception);
        // A blind shooter triples the distance PENALTY (×12 vs ×4), but not the
        // close-range bonus (combat.cc:4383-4388).
        toHit += (attackerBlind && distanceMod >= 0 ? -12 : -4) * distanceMod;

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
    public static int RollDamage(ICombatRng rng, int minDamage, int maxDamage, CritterState target,
        int ammoDrModifier, int ammoDamageMultiplier, int ammoDamageDivisor,
        int critMultiplier = 2, bool bypassArmor = false, int extraDr = 0, int rangedDamageBonus = 0,
        bool penetrate = false, int difficultyDamageModifier = 100)
    {
        // P29-M4 Bonus Ranged Damage (combat.cc:4592): the perk's +2/rank is added to the raw roll
        // BEFORE the multiplier (so the ÷2 wrapper nets +2/rank in the final). 0 = no perk → unchanged.
        int raw = rng.Next(minDamage, Math.Max(minDamage, maxDamage) + 1) + rangedDamageBonus;
        // critMultiplier replaces the engine's hardcoded ×2 (default 2 = identity).
        int damage = raw * critMultiplier * Math.Max(ammoDamageMultiplier, 1);
        damage /= Math.Max(ammoDamageDivisor, 1);
        damage /= 2;
        // P84: the Easy/Hard combat-difficulty damage modifier (75/125 for a non-dude-team attacker),
        // applied after the ÷2 wrapper and before DT (combat.cc:4602). 100 (Normal/dude/ally) = identity
        // → byte-identical. ported from fallout2-ce src/combat.cc attackComputeDamage().
        damage = damage * difficultyDamageModifier / 100;

        int dt = target.DamageThreshold;
        int dr = target.DamageResistance;
        if (bypassArmor) // BYPASS cuts DT/DR to 20% (combat.cc:4530); ammo DR mod still applies
        {
            dt = 20 * dt / 100;
            dr = 20 * dr / 100;
        }
        else
        {
            dr += extraDr; // P29-M1 Finesse: a dude attacker raises the defender's DR +30 (combat.cc:4540)
        }
        if (penetrate) // P74-M2: Penetrate weapon perk cuts DT only to 20% (combat.cc:4535)
            dt = 20 * dt / 100;
        damage -= dt;
        if (damage <= 0)
            return 0;
        int resistance = Math.Clamp(dr + ammoDrModifier, 0, 100);
        return damage - damage * resistance / 100;
    }
}
