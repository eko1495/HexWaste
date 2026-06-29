using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Combat;

/// <summary>
/// The combat-difficulty damage modifier (P84). Fallout 2's Easy/Normal/Hard preference scales the
/// damage dealt by critters NOT on the dude's team — Easy 75%, Normal 100%, Hard 125% — making the
/// game easier/harder for the player without touching the dude's or party's own damage.
/// ported from fallout2-ce src/combat.cc attackComputeDamage(): COMBAT_DIFFICULTY_EASY → 75,
/// COMBAT_DIFFICULTY_HARD → 125, else 100 (combat.cc:4554).
/// </summary>
public static class CombatDifficulty
{
    /// <summary>The damage modifier percentage (75 / 100 / 125) for a given game difficulty.</summary>
    public static int DamageModifier(GameDifficulty difficulty) => difficulty switch
    {
        GameDifficulty.Easy => 75,
        GameDifficulty.Hard => 125,
        _ => 100,
    };
}
