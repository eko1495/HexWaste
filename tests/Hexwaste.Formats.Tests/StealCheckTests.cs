using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// The Steal check + the four-state randomRoll it rides on (P78), ported from skill.cc
/// skillsPerformStealing + random.cc randomRoll/randomTranslateRoll.
/// </summary>
public class StealCheckTests
{
    // Returns the queued d100s in order; clamps into each call's range like the engine's randomBetween.
    private sealed class SeqRng(params int[] values) : ICombatRng
    {
        private int _i;
        public int Next(int minInclusive, int maxExclusive) =>
            Math.Clamp(values[Math.Min(_i++, values.Length - 1)], minInclusive, maxExclusive - 1);
    }

    // ---- RandomRoll -----------------------------------------------------

    [Fact]
    public void NegativeDeltaIsAFailure()
    {
        // difficulty 40 − d100 60 = −20 < 0; criticals off → plain failure.
        Assert.Equal(RollResult.Failure, RandomRoll.Roll(40, 0, criticalsEnabled: false, new SeqRng(60)));
    }

    [Fact]
    public void NegativeDeltaUpgradesToCriticalFailureWhenTheSecondRollIsLow()
    {
        // delta −20 → −delta/10 = 2; a second d100 of 1 ≤ 2 → critical failure (only with criticals on).
        Assert.Equal(RollResult.CriticalFailure, RandomRoll.Roll(40, 0, criticalsEnabled: true, new SeqRng(60, 1)));
        Assert.Equal(RollResult.Failure, RandomRoll.Roll(40, 0, criticalsEnabled: false, new SeqRng(60, 1)));
    }

    [Fact]
    public void NonNegativeDeltaIsASuccessThatCanCrit()
    {
        // difficulty 80 − d100 30 = 50 ≥ 0 → success; delta/10 + critMod 10 = 15; second d100 10 ≤ 15 → crit.
        Assert.Equal(RollResult.Success, RandomRoll.Roll(80, 0, criticalsEnabled: false, new SeqRng(30)));
        Assert.Equal(RollResult.CriticalSuccess, RandomRoll.Roll(80, 10, criticalsEnabled: true, new SeqRng(30, 10)));
    }

    // ---- StealCheck -----------------------------------------------------

    private static StealResult Steal(int thiefSkill, int? targetSkill, params int[] rolls) =>
        StealCheck.Resolve(thiefSkill, targetSkill, itemSize: 0, hasPickpocket: false, faceToFront: false,
            targetIncapacitated: false, thiefCritChance: 0, stealCount: 0, criticalsEnabled: false, new SeqRng(rolls));

    [Fact]
    public void ACleanLiftIsStolenAndNotCaught()
    {
        // skill 100 → chance 95; steal d100 50 → delta 45 success; catch chance 10−1=9, d100 50 → fail to catch.
        StealResult r = Steal(100, targetSkill: 10, 50, 50);
        Assert.True(r.Stolen);
        Assert.False(r.Caught);
    }

    [Fact]
    public void AHighTargetStealSkillCatchesYou()
    {
        // steal succeeds (chance 95, d100 50), but the mark's Steal 90 → catch chance 89, d100 10 → caught.
        StealResult r = Steal(100, targetSkill: 90, 50, 10);
        Assert.False(r.Stolen);
        Assert.True(r.Caught);
    }

    [Fact]
    public void AFumbledStealRollIsAlwaysCaught()
    {
        // low skill 10 → chance 11; d100 90 → delta −79 < 0, crit-fail second roll 1 ≤ 7 → forced caught (no 3rd roll).
        StealResult r = StealCheck.Resolve(10, 10, itemSize: 0, hasPickpocket: false, faceToFront: false,
            targetIncapacitated: false, thiefCritChance: 0, stealCount: 0, criticalsEnabled: true, new SeqRng(90, 1));
        Assert.True(r.Caught);
    }

    [Fact]
    public void ACriticalStealIsNeverCaught()
    {
        // chance 95, d100 1 → delta 94; crit-success (delta/10 + critChance 20 = 29, second d100 5 ≤ 29).
        // The catch roll is forced to critical-failure → never caught (no third draw needed).
        StealResult r = StealCheck.Resolve(95, 90, itemSize: 0, hasPickpocket: false, faceToFront: false,
            targetIncapacitated: false, thiefCritChance: 20, stealCount: 0, criticalsEnabled: true, new SeqRng(1, 5));
        Assert.True(r.Stolen);
        Assert.False(r.Caught);
    }

    [Fact]
    public void PickpocketWaivesTheItemSizeAndFrontPenalties()
    {
        // A face-to-face lift of a size-10 item: without the perk the steal chance collapses
        // (−4×10 size − 25 front), so the same rolls catch you; with it, the modifier is just +1.
        const int skill = 60;
        var withoutPerk = StealCheck.Resolve(skill, 50, itemSize: 10, hasPickpocket: false, faceToFront: true,
            targetIncapacitated: false, thiefCritChance: 0, stealCount: 0, criticalsEnabled: false, new SeqRng(40, 99));
        var withPerk = StealCheck.Resolve(skill, 50, itemSize: 10, hasPickpocket: true, faceToFront: true,
            targetIncapacitated: false, thiefCritChance: 0, stealCount: 0, criticalsEnabled: false, new SeqRng(40, 99));
        // Without the perk: chance 60+1−40(size)−25(front) = −4 → the steal roll fails outright → caught
        // (the catch chance is 50−(−64) = 114, so even a d100 of 99 still catches). With the perk: chance
        // 61 ≥ 40 → succeeds, and the catch chance is only 50−1 = 49 < 99 → the mark doesn't notice.
        Assert.False(withoutPerk.Stolen);
        Assert.True(withPerk.Stolen);
    }
}
