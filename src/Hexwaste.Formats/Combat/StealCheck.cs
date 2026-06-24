namespace Hexwaste.Formats.Combat;

/// <summary>
/// The Steal/pickpocket check, ported from fallout2-ce src/skill.cc skillsPerformStealing() (:1031).
/// The thief's Steal skill (adjusted by a per-session count, item size, facing and the target's state)
/// rolls for success, then a separate CATCH roll decides whether the mark notices — a crit on the steal
/// roll forces the catch (crit-success → never caught, crit-failure → always caught). Planting
/// (isPlanting) shares the math; only the message differs (out of scope — Hexwaste only lifts items).
/// </summary>
public readonly record struct StealResult(bool Stolen, bool Caught);

public static class StealCheck
{
    /// <param name="targetStealSkill">the mark's Steal skill if it's a critter; null = a non-critter
    /// container (the engine's flat catchChance 30).</param>
    /// <param name="stealCount">the engine's _gStealCount — 0 on the first lift this session, +1 per
    /// item taken, so repeated thefts in one go get harder (stealModifier = −count + 1).</param>
    public static StealResult Resolve(int thiefStealSkill, int? targetStealSkill, int itemSize,
        bool hasPickpocket, bool faceToFront, bool targetIncapacitated, int thiefCritChance,
        int stealCount, bool criticalsEnabled, ICombatRng rng)
    {
        int stealModifier = -stealCount + 1;
        if (!hasPickpocket)
        {
            stealModifier -= 4 * itemSize;                       // −4% per item size class (skill.cc:1042)
            if (targetStealSkill is not null && faceToFront)     // a critter caught face-to-face: −25 (:1048)
                stealModifier -= 25;
        }
        if (targetIncapacitated)                                 // KO'd / knocked-down mark: +20 (:1050)
            stealModifier += 20;

        int stealChance = Math.Min(stealModifier + thiefStealSkill, 95);
        RollResult stealRoll = RandomRoll.Roll(stealChance, thiefCritChance, criticalsEnabled, rng);

        RollResult catchRoll;
        if (stealRoll == RollResult.CriticalSuccess)
            catchRoll = RollResult.CriticalFailure;              // a flawless lift can't be caught (:1066)
        else if (stealRoll == RollResult.CriticalFailure)
            catchRoll = RollResult.Success;                      // a fumble is always caught (:1068)
        else
        {
            int catchChance = (targetStealSkill ?? 30) - stealModifier; // skill.cc:1074/1076
            catchRoll = RandomRoll.Roll(catchChance, 0, criticalsEnabled, rng);
        }

        // The CATCH roll decides: the mark failing to catch you = you keep the item (skill.cc:1086).
        bool caught = catchRoll is RollResult.Success or RollResult.CriticalSuccess;
        return new StealResult(Stolen: !caught, Caught: caught);
    }
}
