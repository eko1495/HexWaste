using Hexwaste.Formats.Hex;

namespace Hexwaste.Formats.Combat;

/// <summary>
/// NPC perception/detection, ported from fallout2-ce src/combat_ai.cc <c>isWithinPerception</c> (0x42B4..)
/// + src/actions.cc <c>_can_see</c> (0x412BEC). The engine uses this to decide whether a critter notices a
/// target; for a SNEAKING dude the detection range shrinks sharply, so active sneaking lets you slip past.
/// Pure math (distance + facing + the dude-sneak reduction); the viewer feeds in live state and gates the
/// scripted-aggro decision on it (P30 A-M3).
/// </summary>
public static class PerceptionDetect
{
    /// <summary>True if <paramref name="targetTile"/> lies in the observer's frontal arc — ported from
    /// _can_see (actions.cc:1523): <c>diff = abs(observerRotation - rotationTo(observer, target))</c>,
    /// visible when <c>diff ∈ {0, 1, 5}</c> (dead-ahead or one hex-heading either side).</summary>
    public static bool CanSee(int observerRotation, int observerTile, int targetTile)
    {
        int diff = Math.Abs(observerRotation - HexGrid.RotationTo(observerTile, targetTile));
        return diff == 0 || diff == 1 || diff == 5;
    }

    /// <summary>isWithinPerception (combat_ai.cc:3499): two tiers — a wide cone WITH line-of-sight
    /// (PE×5, halved through glass) and a short fallback WITHOUT it (PE×2 in combat, else PE). When the
    /// target is the dude the range shrinks: actively sneaking (flag AND a successful roll) quarters it
    /// (−1 more if Sneak > 120); the flag set but not actively working takes it to two-thirds.</summary>
    public static bool IsWithinPerception(int distance, int perception, int sneakSkill,
        bool canSee, bool targetIsGlass, bool targetIsDude, bool dudeIsSneaking, bool dudeHasSneakFlag, bool inCombat)
    {
        if (canSee)
        {
            int max = perception * 5;
            if (targetIsGlass)
                max /= 2;
            max = ApplyDudeSneakReduction(max, targetIsDude, dudeIsSneaking, dudeHasSneakFlag, sneakSkill);
            if (distance <= max)
                return true;
        }

        int fallback = inCombat ? perception * 2 : perception;
        fallback = ApplyDudeSneakReduction(fallback, targetIsDude, dudeIsSneaking, dudeHasSneakFlag, sneakSkill);
        return distance <= fallback;
    }

    private static int ApplyDudeSneakReduction(int max, bool targetIsDude, bool dudeIsSneaking, bool dudeHasFlag, int sneakSkill)
    {
        if (!targetIsDude)
            return max;
        if (dudeIsSneaking) // dudeIsSneaking() = flag AND working
        {
            max /= 4;
            if (sneakSkill > 120)
                max -= 1;
        }
        else if (dudeHasFlag) // the flag is set but the last roll failed
        {
            max = max * 2 / 3;
        }
        return max;
    }
}
