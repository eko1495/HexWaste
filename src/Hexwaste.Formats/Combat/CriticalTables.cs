namespace Hexwaste.Formats.Combat;

/// <summary>One critical-hit table entry: the damage multiplier (the engine's
/// hardcoded ×2 slot) and the resolved DAM_* effect flags. phase-9 M2 honours
/// <see cref="CriticalTables.DamDead"/> / <see cref="CriticalTables.DamKnockedDown"/>
/// / <see cref="CriticalTables.DamBypass"/>; other flags are masked at apply time.</summary>
public readonly record struct CriticalEffect(int DamageMultiplier, int Flags);

/// <summary>
/// The Fallout 2 critical-hit tables (data in <c>CriticalTables.g.cs</c>, ported
/// from fallout2-ce combat.cc by tools/gen_critical_tables.py). Lookup keyed by
/// <c>critterGetKillType</c> × hit location × severity (combat.cc:4089-4159).
/// </summary>
public static partial class CriticalTables
{
    // DAM_* effect flags (obj_types.h:127-142).
    public const int DamKnockedDown = 0x02;
    public const int DamDead = 0x80;
    public const int DamCritical = 0x200;
    public const int DamBypass = 0x800;

    /// <summary>The crit effect for a (killType, hitLocation, severity). The dude
    /// uses the player table; an out-of-range killType falls back to KILL_TYPE_MAN
    /// (0) — the slice's non-tabulated critters resolve to MAN by design.</summary>
    public static CriticalEffect Lookup(int killType, int location, int severity, bool defenderIsDude)
    {
        location = Math.Clamp(location, 0, LocationCount - 1);
        severity = Math.Clamp(severity, 0, SeverityCount - 1);

        if (defenderIsDude)
        {
            int p = (location * SeverityCount + severity) * 2;
            return new CriticalEffect(PlayerData[p], PlayerData[p + 1]);
        }

        if (killType < 0 || killType >= KillTypeCount)
            killType = 0; // KILL_TYPE_MAN
        int i = ((killType * LocationCount + location) * SeverityCount + severity) * 2;
        return new CriticalEffect(CritterData[i], CritterData[i + 1]);
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
        return h;
    }
}
