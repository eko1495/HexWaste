namespace Hexwaste.Formats.Combat;

/// <summary>
/// The AI combat-taunt decision, ported from fallout2-ce src/combat_ai.cc _combatai_msg() (:3302):
/// a critter rolls its packet's <c>chance</c> to taunt, then picks a random combatai.msg id from the
/// range for the taunt type. The viewer floats the resolved message string over the critter in the
/// packet's colour (P72-M3). Only the two clean self-perspective single-range taunts are wired —
/// ATTACK (the attacker on its swing, actions.cc:630) and RUN (a fleeing critter, combat_ai.cc:1209);
/// MISS/HIT (attacker-vs-defender + per-hit-location ranges) and MOVE are documented residuals.
/// </summary>
public static class CombatTaunt
{
    public enum Type { Attack, Run }

    /// <summary>Roll whether <paramref name="pkt"/> taunts now and pick a combatai.msg id from the
    /// type's range, ported from _combatai_msg: <c>randomBetween(1,100) &gt; chance</c> skips; else
    /// <c>randomBetween(start,end)</c> (inclusive). <c>end &lt; start</c> (no range) → no taunt.
    /// Returns -1 = no taunt. DIVERGENCE: a chance ≤ 0 short-circuits WITHOUT drawing (the engine
    /// rolls then skips); harmless because the taunt rng is isolated from the combat stream.</summary>
    public static int Pick(AiPacket pkt, Type type, ICombatRng rng)
    {
        if (pkt.Chance <= 0 || rng.Next(1, 101) > pkt.Chance)
            return -1;
        (int start, int end) = type switch
        {
            Type.Attack => (pkt.AttackStart, pkt.AttackEnd),
            Type.Run => (pkt.RunStart, pkt.RunEnd),
            _ => (0, -1),
        };
        if (end < start)
            return -1;
        return rng.Next(start, end + 1); // randomBetween(start, end) is inclusive
    }
}
