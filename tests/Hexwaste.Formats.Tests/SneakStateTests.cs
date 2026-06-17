using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// The two-layer sneak state (P29 A-M0), ported from fallout2-ce src/critter.cc: the FLAG (dudeHasState)
/// + Working (_sneak_working) and the periodic-reschedule ladder (sneakEventProcess).
/// </summary>
public class SneakStateTests
{
    [Theory]
    [InlineData(false, false, false)] // not toggled
    [InlineData(true, false, false)]  // flag set but the roll hasn't succeeded → not really sneaking
    [InlineData(false, true, false)]  // working without the flag is meaningless
    [InlineData(true, true, true)]    // dudeIsSneaking: flag AND working
    public void IsSneakingRequiresBothLayers(bool flag, bool working, bool expected)
    {
        var s = new SneakState { FlagSet = flag, Working = working };
        Assert.Equal(expected, s.IsSneaking);
    }

    [Fact]
    public void SuccessAlwaysReschedulesInSixHundred()
    {
        // sneakEventProcess: on a successful roll, time = 600 regardless of skill (critter.cc:1217).
        Assert.Equal(600, SneakState.RescheduleTicks(0, rollSucceeded: true));
        Assert.Equal(600, SneakState.RescheduleTicks(300, rollSucceeded: true));
    }

    [Theory]
    [InlineData(251, 100)] // > 250
    [InlineData(250, 120)] // > 200 (not > 250)
    [InlineData(201, 120)]
    [InlineData(200, 150)] // > 170
    [InlineData(171, 150)]
    [InlineData(170, 200)] // > 135
    [InlineData(136, 200)]
    [InlineData(135, 300)] // > 100
    [InlineData(101, 300)]
    [InlineData(100, 400)] // > 80
    [InlineData(81, 400)]
    [InlineData(80, 600)]  // else
    [InlineData(0, 600)]
    public void FailureRetriesSoonerTheHigherTheSkill(int sneakSkill, int expectedTicks) =>
        Assert.Equal(expectedTicks, SneakState.RescheduleTicks(sneakSkill, rollSucceeded: false));
}
