namespace Hexwaste.Formats.Combat;

/// <summary>The outline a visible critter gets during combat (OUTLINE_TYPE_*, obj_types.h:36).</summary>
public enum OutlineType
{
    None,
    Hostile,    // OUTLINE_TYPE_HOSTILE (1) — palette index 243 (red)
    Friendly,   // OUTLINE_TYPE_FRIENDLY (8) — palette index 229 (green)
    Perception, // OUTLINE_TYPE_32 (32) — palette index 61 (perception-only, no clear LoS)
}

/// <summary>
/// Which outline a visible critter gets while combat is active.
/// ported from fallout2-ce src/combat.cc _combat_update_critter_outline_for_los(): with a clear
/// line of fire to the dude, same-team → FRIENDLY else HOSTILE; with LoS blocked, still outline
/// (PERCEPTION) if within the dude's PE×5 reach (halved through glass), else nothing.
/// </summary>
public static class CombatOutline
{
    public static OutlineType TypeFor(bool clearLos, int dudeTeam, int critterTeam,
        int dist, int dudePerception, bool critterIsGlass)
    {
        if (clearLos)
            return dudeTeam == critterTeam ? OutlineType.Friendly : OutlineType.Hostile;

        int reach = dudePerception * 5;
        if (critterIsGlass)
            reach /= 2;
        return dist <= reach ? OutlineType.Perception : OutlineType.None;
    }

    /// <summary>The base palette index the engine's gradient starts from (object.cc:4707).</summary>
    public static int PaletteIndex(OutlineType type) => type switch
    {
        OutlineType.Hostile => 243,
        OutlineType.Friendly => 229,
        OutlineType.Perception => 61,
        _ => -1,
    };
}
