namespace Hexwaste.Formats.Combat;

/// <summary>The four-state skill/stat roll, ported from fallout2-ce src/random.cc randomRoll() +
/// randomTranslateRoll() (:85). delta = difficulty − d100; a failure can become a CRITICAL failure
/// (≤ −delta/10) and a success a CRITICAL success (≤ delta/10 + critModifier), both gated on the same
/// day≥1 window the rest of Hexwaste calls <c>criticalsEnabled</c> (P9-M2). Used by the steal check
/// (P78); combat keeps its own inlined delta form.</summary>
public enum RollResult { CriticalFailure, Failure, Success, CriticalSuccess }

public static class RandomRoll
{
    public static RollResult Roll(int difficulty, int critModifier, bool criticalsEnabled, ICombatRng rng)
    {
        int delta = difficulty - rng.Next(1, 101); // randomBetween(1,100)
        if (delta < 0)
        {
            if (criticalsEnabled && rng.Next(1, 101) <= -delta / 10)
                return RollResult.CriticalFailure;
            return RollResult.Failure;
        }
        if (criticalsEnabled && rng.Next(1, 101) <= delta / 10 + critModifier)
            return RollResult.CriticalSuccess;
        return RollResult.Success;
    }
}
