using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

/// <summary>P50: the pure companion combat-control logic (CompanionAi) — the disposition
/// presets, the flee thresholds, and the target-priority picker.</summary>
public class CompanionAiTests
{
    [Fact]
    public void DefaultIsAggressiveClosestNoFlee()
    {
        CompanionAi e = CompanionAi.Default.Effective();
        Assert.Equal(AttackWho.Closest, e.AttackWho);
        Assert.Equal(Distance.OnYourOwn, e.Distance);
        Assert.Equal(RunAway.Never, e.RunAway);
    }

    [Theory]
    [InlineData(Disposition.Berserk, AttackWho.Closest, Distance.Charge, RunAway.Never)]
    [InlineData(Disposition.Defensive, AttackWho.WhoeverAttackingMe, Distance.StayClose, RunAway.Bleeding)]
    [InlineData(Disposition.Coward, AttackWho.Weakest, Distance.StayClose, RunAway.FingerHurts)]
    public void DispositionPresetsTheKnobs(Disposition d, AttackWho aw, Distance dist, RunAway ra)
    {
        CompanionAi e = (CompanionAi.Default with { Disposition = d }).Effective();
        Assert.Equal(aw, e.AttackWho);
        Assert.Equal(dist, e.Distance);
        Assert.Equal(ra, e.RunAway);
    }

    [Fact]
    public void CustomKeepsTheExplicitKnobs()
    {
        var ai = new CompanionAi(Disposition.Custom, AttackWho.Strongest, Distance.Snipe, RunAway.Tourniquet);
        CompanionAi e = ai.Effective();
        Assert.Equal(AttackWho.Strongest, e.AttackWho);
        Assert.Equal(Distance.Snipe, e.Distance);
        Assert.Equal(RunAway.Tourniquet, e.RunAway);
    }

    [Theory]
    [InlineData(RunAway.Never, 1, 100, false)]
    [InlineData(RunAway.AbjectCoward, 99, 100, true)]   // any damage
    [InlineData(RunAway.AbjectCoward, 100, 100, false)] // unhurt
    [InlineData(RunAway.Bleeding, 60, 100, true)]       // ≤ 60%
    [InlineData(RunAway.Bleeding, 61, 100, false)]
    [InlineData(RunAway.Tourniquet, 20, 100, true)]     // ≤ 20%
    [InlineData(RunAway.Tourniquet, 21, 100, false)]
    public void ShouldFleeHonoursTheThreshold(RunAway mode, int hp, int max, bool expected) =>
        Assert.Equal(expected, CompanionAi.ShouldFlee(mode, hp, max));

    [Fact]
    public void PickTargetClosestIsTheNearest()
    {
        // (Hp, Distance, HitMe): index 1 is the nearest.
        var cands = new List<(int, int, bool)> { (50, 5, false), (90, 2, false), (10, 8, false) };
        Assert.Equal(1, CompanionAi.PickTarget(AttackWho.Closest, cands));
    }

    [Fact]
    public void PickTargetStrongestIsTheHighestHp()
    {
        var cands = new List<(int, int, bool)> { (50, 5, false), (90, 2, false), (10, 8, false) };
        Assert.Equal(1, CompanionAi.PickTarget(AttackWho.Strongest, cands)); // 90 hp
    }

    [Fact]
    public void PickTargetWeakestIsTheLowestHp()
    {
        var cands = new List<(int, int, bool)> { (50, 5, false), (90, 2, false), (10, 8, false) };
        Assert.Equal(2, CompanionAi.PickTarget(AttackWho.Weakest, cands)); // 10 hp
    }

    [Fact]
    public void PickTargetWhoeverAttackingMePrefersTheHitter()
    {
        var cands = new List<(int, int, bool)> { (50, 5, false), (90, 2, false), (10, 8, true) };
        Assert.Equal(2, CompanionAi.PickTarget(AttackWho.WhoeverAttackingMe, cands)); // the one that hit me
    }

    [Fact]
    public void PickTargetWhoeverAttackingMeFallsBackToClosest()
    {
        var cands = new List<(int, int, bool)> { (50, 5, false), (90, 2, false), (10, 8, false) };
        Assert.Equal(1, CompanionAi.PickTarget(AttackWho.WhoeverAttackingMe, cands)); // nobody hit me → closest
    }

    [Fact]
    public void PickTargetEmptyIsMinusOne() =>
        Assert.Equal(-1, CompanionAi.PickTarget(AttackWho.Closest, new List<(int, int, bool)>()));
}
