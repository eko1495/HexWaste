using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Hex;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// NPC perception/detection (P30 A-M3), ported from combat_ai.cc isWithinPerception + actions.cc
/// _can_see. The dude-sneak reduction is what lets active sneaking slip past an NPC.
/// </summary>
public class PerceptionDetectTests
{
    [Fact]
    public void CanSeeIsTheFrontalArc()
    {
        const int t = 20100;
        for (int rot = 0; rot < 6; rot++)
        {
            // dead-ahead and the two adjacent headings are visible; behind / far sides are not.
            Assert.True(PerceptionDetect.CanSee(rot, t, HexGrid.TileInDirection(t, rot)));
            Assert.True(PerceptionDetect.CanSee(rot, t, HexGrid.TileInDirection(t, (rot + 1) % 6)));
            Assert.True(PerceptionDetect.CanSee(rot, t, HexGrid.TileInDirection(t, (rot + 5) % 6)));
            Assert.False(PerceptionDetect.CanSee(rot, t, HexGrid.TileInDirection(t, (rot + 3) % 6)));
        }
    }

    // Convenience: PE 7, not glass, not the dude (no reduction).
    private static bool See(int distance, bool canSee, int perception = 7, bool inCombat = false,
        bool isDude = false, bool sneaking = false, bool flag = false, int sneak = 50, bool glass = false) =>
        PerceptionDetect.IsWithinPerception(distance, perception, sneak, canSee, glass, isDude, sneaking, flag, inCombat);

    [Fact]
    public void WithLineOfSightTheConeIsPerceptionTimesFive()
    {
        Assert.True(See(35, canSee: true));   // PE 7 * 5 = 35
        Assert.False(See(36, canSee: true));
        Assert.True(See(17, canSee: true, glass: true));   // 35 / 2 = 17
        Assert.False(See(18, canSee: true, glass: true));
    }

    [Fact]
    public void WithoutLineOfSightTheRangeIsPerceptionOrDoubleInCombat()
    {
        Assert.True(See(7, canSee: false));            // out of combat: PE
        Assert.False(See(8, canSee: false));
        Assert.True(See(14, canSee: false, inCombat: true));  // in combat: PE*2
        Assert.False(See(15, canSee: false, inCombat: true));
    }

    [Fact]
    public void ActiveSneakingQuartersTheDetectionRange()
    {
        // PE*5 = 35; the dude actively sneaking → /4 = 8.
        Assert.True(See(8, canSee: true, isDude: true, sneaking: true, flag: true, sneak: 50));
        Assert.False(See(9, canSee: true, isDude: true, sneaking: true, flag: true, sneak: 50));
        // Sneak > 120 shaves one more: 35/4 - 1 = 7.
        Assert.True(See(7, canSee: true, isDude: true, sneaking: true, flag: true, sneak: 130));
        Assert.False(See(8, canSee: true, isDude: true, sneaking: true, flag: true, sneak: 130));
    }

    [Fact]
    public void FlagSetButRollFailedIsTwoThirdsRange()
    {
        // flag set, not actively working → 35 * 2/3 = 23.
        Assert.True(See(23, canSee: true, isDude: true, sneaking: false, flag: true));
        Assert.False(See(24, canSee: true, isDude: true, sneaking: false, flag: true));
    }

    [Fact]
    public void NonDudeTargetGetsNoSneakReduction()
    {
        // The reduction is dude-only — a non-dude target uses the full PE*5 even with the flags set.
        Assert.True(See(35, canSee: true, isDude: false, sneaking: true, flag: true));
    }
}
