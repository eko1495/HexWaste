namespace Hexwaste.Formats.Combat;

/// <summary>One critical-hit table entry. <paramref name="DamageMultiplier"/> is the
/// engine's hardcoded ×2 slot; <paramref name="Flags"/> are the always-applied DAM_*
/// effects; <paramref name="MassiveStat"/>/<paramref name="StatMod"/>/<paramref
/// name="MassiveFlags"/> are the secondary "massive critical" roll (combat.cc:4134) —
/// on a FAILED <c>stat+statMod</c> check the MassiveFlags are added too (wired in
/// P14-M4). MassiveStat == -1 ⇒ no secondary roll.</summary>
public readonly record struct CriticalEffect(
    int DamageMultiplier, int Flags, int MassiveStat, int StatMod, int MassiveFlags);

/// <summary>
/// The Fallout 2 critical-hit tables (data in <c>CriticalTables.g.cs</c>, ported
/// from fallout2-ce combat.cc by tools/gen_critical_tables.py). Lookup keyed by
/// <c>critterGetKillType</c> × hit location × severity (combat.cc:4089-4159).
/// </summary>
public static partial class CriticalTables
{
    // DAM_* effect flags (obj_types.h:127-142).
    public const int DamKnockedOut = 0x01;
    public const int DamKnockedDown = 0x02;
    public const int DamCripLegLeft = 0x04;
    public const int DamCripLegRight = 0x08;
    public const int DamCripArmLeft = 0x10;
    public const int DamCripArmRight = 0x20;
    public const int DamBlind = 0x40;
    public const int DamDead = 0x80;
    public const int DamCritical = 0x200;
    public const int DamOnFire = 0x400;
    public const int DamBypass = 0x800;
    public const int DamExplode = 0x1000;
    public const int DamDestroy = 0x2000;
    public const int DamDrop = 0x4000;
    public const int DamLoseTurn = 0x8000;
    // Critical-FAILURE-only effect bits (obj_types.h:143-148; P41).
    public const int DamHitSelf = 0x10000;
    public const int DamLoseAmmo = 0x20000;
    public const int DamDud = 0x40000;
    public const int DamHurtSelf = 0x80000;
    public const int DamRandomHit = 0x100000;
    public const int DamCripRandom = 0x200000;

    public const int DamCripLegAny = DamCripLegLeft | DamCripLegRight;   // 0x0C
    public const int DamCripArmAny = DamCripArmLeft | DamCripArmRight;   // 0x30
    public const int DamCripLimbs = DamCripLegAny | DamCripArmAny;       // 0x3C
    /// <summary>The Doctor-healable damage flags (skill.cc gHealableDamageFlags):
    /// blind + the four crippled limbs.</summary>
    public const int DamHealable = DamCripLimbs | DamBlind;              // 0x7C

    /// <summary>The flags honoured at apply time. P14 widened this from the phase-9
    /// set (knockdown/dead/bypass/critical) to also carry knockout, lose-turn, the
    /// crippled limbs and blind — the engine's _set_new_results mask (combat.cc:4809).</summary>
    public const int HonoredFlags = DamKnockedDown | DamDead | DamBypass | DamCritical
        | DamKnockedOut | DamLoseTurn | DamCripLimbs | DamBlind;

    /// <summary>hit_location_penalty_default (combat.cc:172) — HEAD, L_ARM, R_ARM,
    /// TORSO, R_LEG, L_LEG, EYES, GROIN, UNCALLED. Full for ranged, halved for
    /// melee (combat.cc:4437). Negative ⇒ harder to hit but more likely to crit.</summary>
    public static readonly int[] LocationPenalty = { -40, -30, -30, 0, -20, -20, -60, -30, 0 };

    /// <summary>HIT_LOCATION_UNCALLED — the default, no aiming (penalty 0).</summary>
    public const int LocationUncalled = 8;

    /// <summary>Severity bucket from rand(1,100)+STAT_BETTER_CRITICALS
    /// (combat.cc:4105-4117); effect 5 needs the Better Criticals perk.</summary>
    public static int Severity(int chance) =>
        chance <= 20 ? 0 : chance <= 45 ? 1 : chance <= 70 ? 2 : chance <= 90 ? 3 : chance <= 100 ? 4 : 5;

    /// <summary>The crit effect for a (killType, hitLocation, severity). The dude
    /// uses the player table; an out-of-range killType falls back to KILL_TYPE_MAN
    /// (0) — the slice's non-tabulated critters resolve to MAN by design.</summary>
    public static CriticalEffect Lookup(int killType, int location, int severity, bool defenderIsDude)
    {
        location = Math.Clamp(location, 0, LocationCount - 1);
        severity = Math.Clamp(severity, 0, SeverityCount - 1);

        if (defenderIsDude)
        {
            int p = (location * SeverityCount + severity) * Stride;
            return new CriticalEffect(PlayerData[p], PlayerData[p + 1], PlayerData[p + 2], PlayerData[p + 3], PlayerData[p + 4]);
        }

        if (killType < 0 || killType >= KillTypeCount)
            killType = 0; // KILL_TYPE_MAN
        int i = ((killType * LocationCount + location) * SeverityCount + severity) * Stride;
        return new CriticalEffect(CritterData[i], CritterData[i + 1], CritterData[i + 2], CritterData[i + 3], CritterData[i + 4]);
    }

    /// <summary>Recompute the FNV-1a checksum from the in-memory data — must equal
    /// <see cref="DataChecksum"/> (guards the generated file against corruption).</summary>
    public static ulong ComputeChecksum()
    {
        ulong h = 0xCBF29CE484222325UL;
        static ulong Hash(ulong h, int[] data)
        {
            foreach (int v in data)
                foreach (char c in v.ToString() + ",")
                    h = (h ^ (byte)c) * 0x100000001B3UL;
            return h;
        }
        h = Hash(h, CritterData);
        h = Hash(h, PlayerData);
        h = Hash(h, CritFailTable);
        return h;
    }

    /// <summary>The DAM_* flags of a critical FAILURE (the attacker's fumble), keyed by the weapon's
    /// criticalFailureType row (0..6; -1/out-of-range → row 0, the unarmed/default) and the Luck-bucketed
    /// severity column (0..4). _cf_table[failureType*CritFailEffectCount + effect], combat.cc:1875.</summary>
    public static int CritFailFlags(int failureType, int effect)
    {
        if (failureType < 0 || failureType >= CritFailTypeCount)
            failureType = 0;
        effect = Math.Clamp(effect, 0, CritFailEffectCount - 1);
        return CritFailTable[failureType * CritFailEffectCount + effect];
    }
}
