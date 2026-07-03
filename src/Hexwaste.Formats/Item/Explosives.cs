namespace Hexwaste.Formats.Item;

/// <summary>
/// Timed placeable explosives — Dynamite (PID 51) and Plastic Explosives (PID 85). Both are
/// ITEM_TYPE_MISC (not weapons): you USE them (arm + set a timer), you can't throw them.
/// ported from fallout2-ce src/item.cc explosiveIsExplosive (:3435), explosiveActivate (:3459),
/// explosiveGetDamage (:3502) + the damage table (item.cc:3379-3382). Detonation runs an area explosion
/// (critters) + the _scr_explode_scenery scenery/door damage sweep.
/// </summary>
public static class Explosives
{
    public const int DynamitePid = 51, DynamiteArmedPid = 206;
    public const int PlasticPid = 85, PlasticArmedPid = 209;

    /// <summary>explosiveIsExplosive (item.cc:3435): only Dynamite (51) and Plastic Explosives (85).</summary>
    public static bool IsExplosive(int pid) => pid is DynamitePid or PlasticPid;

    /// <summary>explosiveActivate (item.cc:3459): 51→206, 85→209 (the inert item becomes the armed pid).</summary>
    public static int Activate(int pid) => pid switch
    {
        DynamitePid => DynamiteArmedPid,
        PlasticPid => PlasticArmedPid,
        _ => pid,
    };

    /// <summary>explosiveGetDamage (item.cc:3502 + the table :3379-3382): dynamite 30-50, plastic 40-80
    /// (+10 each with Demolition Expert, applied by the caller).</summary>
    public static (int Min, int Max) Damage(int pid) => pid switch
    {
        DynamitePid or DynamiteArmedPid => (30, 50),
        PlasticPid or PlasticArmedPid => (40, 80),
        _ => (0, 0),
    };
}
