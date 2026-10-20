namespace Hexwaste.Formats.Combat;

/// <summary>
/// The AI combat-taunt decision, ported from fallout2-ce src/combat_ai.cc _combatai_msg() (:3302):
/// a critter rolls its packet's <c>chance</c> to taunt, then picks a random combatai.msg id from the
/// range for the taunt type. The viewer floats the resolved message string over the critter in the
/// packet's colour (P72-M3). Types: ATTACK, RUN, MOVE, MISS and HIT (per hit location).
/// </summary>
public static class CombatTaunt
{
    public enum Type { Attack, Run, Move, Miss, Hit }

    /// <summary>Roll whether <paramref name="pkt"/> taunts now and pick a combatai.msg id from the
    /// type's range, ported from fallout2-ce src/combat_ai.cc _combatai_msg(): always draws
    /// <c>randomBetween(1,100)</c> and skips when it is <c>&gt; chance</c>; else <c>randomBetween(start,end)</c>
    /// (inclusive). <c>end &lt; start</c> (no range) -&gt; no taunt. HIT picks the range of
    /// <paramref name="hitLocation"/> (fo2ce HIT_LOCATION_*, torso 3). Returns -1 = no taunt.</summary>
    public static int Pick(AiPacket pkt, Type type, ICombatRng rng, int hitLocation = 3)
    {
        if (rng.Next(1, 101) > pkt.Chance)
            return -1;
        (int start, int end) = type switch
        {
            Type.Attack => (pkt.AttackStart, pkt.AttackEnd),
            Type.Run => (pkt.RunStart, pkt.RunEnd),
            Type.Move => (pkt.MoveStart, pkt.MoveEnd),
            Type.Miss => (pkt.MissStart, pkt.MissEnd),
            Type.Hit when pkt.HitRanges is { } h && hitLocation >= 0 && hitLocation < h.Length => h[hitLocation],
            _ => (0, -1),
        };
        if (end < start)
            return -1;
        return rng.Next(start, end + 1); // randomBetween(start, end) is inclusive
    }
}
