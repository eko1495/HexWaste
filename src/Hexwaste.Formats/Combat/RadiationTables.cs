namespace Hexwaste.Formats.Combat;

/// <summary>
/// The radiation band model tables, ported VERBATIM from fallout2-ce src/critter.cc. A radiation
/// counter maps to one of 6 levels; each level (past MINOR) applies a stat-penalty band, gated by an
/// Endurance save that can bump the effective level one harder.
/// </summary>
public static class RadiationTables
{
    /// <summary>gRadiationEnduranceModifiers (critter.cc:88): added to END before the d10 save.
    /// Indexed by the radiation LEVEL directly (0=NONE..5=FATAL).</summary>
    public static readonly int[] EnduranceModifiers = [2, 0, -2, -4, -6, -8];

    /// <summary>gRadiationEffectStats (critter.cc:107): the 8 affected stat ids. The first
    /// PrimaryStatCount are the death-check primaries. Ids: STR..AGI = 0..5, CURRENT_HP = 35,
    /// HEALING_RATE = 14.</summary>
    public static readonly int[] EffectStats = [0, 1, 2, 3, 4, 5, 35, 14];
    public const int PrimaryStatCount = 6; // RADIATION_EFFECT_PRIMARY_STAT_COUNT

    /// <summary>gRadiationEffectPenalties (critter.cc:126): the per-band stat modifiers, indexed
    /// [radiationLevel − 1][effect] (critter.cc:574). Row 0 (MINOR) is all-zero — the first real
    /// penalty is ADVANCED. Columns follow <see cref="EffectStats"/>.</summary>
    public static readonly int[][] EffectPenalties =
    [
        [ 0,  0,  0,  0,  0,  0,   0,   0], // level 1 MINOR    (idx 0)
        [-1,  0,  0,  0,  0,  0,   0,   0], // level 2 ADVANCED (idx 1)
        [-1,  0,  0,  0,  0, -1,   0,  -3], // level 3 CRITICAL (idx 2)
        [-2,  0, -1,  0,  0, -2,  -5,  -5], // level 4 DEADLY   (idx 3)
        [-4, -3, -3, -3, -1, -5, -15, -10], // level 5 FATAL    (idx 4)
        [-6, -5, -5, -5, -3, -6, -20, -10], // level 6 (5+failed save, idx 5)
    ];

    /// <summary>The radiation counter → level band (critter.cc:507-518, strict &gt; thresholds).</summary>
    public static int CounterToLevel(int rad) =>
        rad > 999 ? 5 : rad > 599 ? 4 : rad > 399 ? 3 : rad > 199 ? 2 : rad > 99 ? 1 : 0;
}
