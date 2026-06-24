namespace Hexwaste.Formats.Combat;

/// <summary>
/// The AI aimed/called-shot decision, ported from fallout2-ce src/combat_ai.cc _ai_called_shot()
/// (:2634): an NPC with an aim-capable weapon has a 1/<c>called_freq</c> chance, gated on INT, to aim
/// at a RANDOM body part instead of the torso — reverting to uncalled if the to-hit there falls below
/// the packet's min_to_hit. The location then feeds the existing RollAttack penalty + location-crit
/// path (so an enemy can cripple/blind the dude). P75-M4.
/// </summary>
public static class AiCalledShot
{
    /// <summary>Difficulty INT requirement (combat_ai.cc:2645 — Hard 7 / Normal 5 / Easy 3). Hexwaste
    /// fixes difficulty at Normal (no preferences screen), like the rest of the AI.</summary>
    public const int IntelligenceRequiredNormal = 5;

    /// <summary>Pick the hit location for an NPC attack. Returns <see cref="CriticalTables.LocationUncalled"/>
    /// (a plain torso shot) unless the called-shot roll fires. The rng is drawn ONLY when called_freq ≥ 1
    /// (an absent/0 packet skips it entirely — no draw), and it must be an ISOLATED stream so the combat
    /// to-hit/damage rolls stay byte-identical. <paramref name="toHitAt"/> returns the % to hit a given
    /// location (for the min_to_hit revert).</summary>
    public static int Pick(int calledFreq, int attackerIntelligence, bool canAim, int minToHit,
        ICombatRng rng, Func<int, int> toHitAt, int intelligenceRequired = IntelligenceRequiredNormal)
    {
        if (!canAim || calledFreq < 1)
            return CriticalTables.LocationUncalled;
        if (rng.Next(1, calledFreq + 1) != 1)                 // randomBetween(1, called_freq) == 1
            return CriticalTables.LocationUncalled;
        if (attackerIntelligence < intelligenceRequired)
            return CriticalTables.LocationUncalled;
        // randomBetween(0, HIT_LOCATION_SPECIFIC_COUNT) — inclusive of the uncalled slot, matched here.
        int location = rng.Next(0, CriticalTables.LocationUncalled + 1);
        // Revert to uncalled when the to-hit at that part is below the packet's floor (combat_ai.cc:2658
        // reverts to TORSO; Uncalled has the same 0 penalty).
        return toHitAt(location) < minToHit ? CriticalTables.LocationUncalled : location;
    }
}
