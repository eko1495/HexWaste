using Hexwaste.Formats.Int;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Dialogue IQ-gating (P25): giq_option's dumb/smart option visibility, ported from
/// fallout2-ce interpreter_extra.cc _op_giq_option. A positive iq is a minimum INT ("smart"),
/// a negative iq a maximum ("dumb"/stupid), 0 always visible.
/// </summary>
public class DialogGateTests
{
    [Theory]
    // Smart options (positive iq = minimum INT): shown only at IN >= iq.
    [InlineData(4, 3, false)]
    [InlineData(4, 4, true)]
    [InlineData(4, 9, true)]
    [InlineData(5, 4, false)] // the combat premade (IN 4) loses the iq=5 option
    // Dumb options (negative iq = maximum INT): shown only at IN <= -iq.
    [InlineData(-3, 2, true)]  // IN 2 <= 3 → the stupid option appears
    [InlineData(-3, 3, true)]  // IN 3 <= 3 → boundary, still shown
    [InlineData(-3, 5, false)] // IN 5 > 3 → too smart for the dumb line
    // iq 0 is ungated.
    [InlineData(0, 1, true)]
    [InlineData(0, 10, true)]
    public void IqOptionVisibilityMatchesTheEngine(int iq, int intelligence, bool visible) =>
        Assert.Equal(visible, DialogGate.IqOptionVisible(iq, intelligence));

    [Fact]
    public void TheNeutralFiveSeesSmartButNotDumb()
    {
        // The pre-P25 hardcoded IN of 5: shows iq=4 smart options, hides iq=-3 dumb ones —
        // exactly why feeding the real INT (and not 5) changes a low/high-IN dude's options.
        Assert.True(DialogGate.IqOptionVisible(4, 5));
        Assert.False(DialogGate.IqOptionVisible(-3, 5));
    }
}
