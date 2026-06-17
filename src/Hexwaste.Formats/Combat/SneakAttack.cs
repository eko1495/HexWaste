namespace Hexwaste.Formats.Combat;

/// <summary>
/// Sneak-attack (backstab) facing math, ported from fallout2-ce src/actions.cc <c>_is_hit_from_front</c>
/// (0x412BC4). The Silent Death perk's damage multiplier (combat.cc:3870-3875 / 3913-3921) requires the
/// dude to strike from behind/the side while the sneaking FLAG is set; this is the facing predicate.
/// </summary>
public static class SneakAttack
{
    /// <summary>True when the attacker faces the defender head-on (a "front" hit). The engine compares
    /// the two rotations: <c>diff = abs(attRot - defRot)</c>, front = <c>diff ∉ {0, 1, 5}</c> — so a
    /// behind/side hit (the backstab) is <c>diff ∈ {0, 1, 5}</c> (the two critters face the same / a
    /// near-same direction). Rotations are the 6 hex headings (0..5).</summary>
    public static bool IsHitFromFront(int attackerRotation, int defenderRotation)
    {
        int diff = Math.Abs(attackerRotation - defenderRotation);
        return diff != 0 && diff != 1 && diff != 5;
    }
}
