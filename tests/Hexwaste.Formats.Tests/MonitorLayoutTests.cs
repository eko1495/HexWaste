using Hexwaste.Formats.Text;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// F6: the message monitor's wrap budget, ported from
/// fallout2-ce src/display_monitor.cc:262 —
///   DISPLAY_MONITOR_WIDTH - _max_disp - knobWidth
/// where _max_disp is a LINE COUNT (height / lineHeight) subtracted from a PIXEL
/// width. That is the original's own arithmetic and is reproduced, not corrected.
/// </summary>
public class MonitorLayoutTests
{
    [Fact]
    public void MaxDisplayLinesIsTheHeightOverTheLineHeight() =>
        // DISPLAY_MONITOR_HEIGHT (60) / fontGetLineHeight(), display_monitor.cc:115.
        Assert.Equal(6, MonitorLayout.MaxDisplayLines(10));

    [Fact]
    public void TheFirstLineBudgetSubtractsBothTheLineCountAndTheKnob() =>
        // 167 - 6 - 5
        Assert.Equal(156, MonitorLayout.WrapBudget(lineHeight: 10, knobWidth: 5));

    [Fact]
    public void ContinuationLinesGetTheFullBudgetBecauseKnobWidthIsZeroed() =>
        // display_monitor.cc:270 sets knob = '\0' and knobWidth = 0 after the first line.
        Assert.Equal(161, MonitorLayout.WrapBudget(lineHeight: 10, knobWidth: 0));

    [Fact]
    public void TheKnobIsTheBulletCharacter() =>
        Assert.Equal('\x95', MonitorLayout.Knob);

    [Fact]
    public void TheRectMatchesTheReference()
    {
        // display_monitor.cc:31-34
        Assert.Equal(23, MonitorLayout.X);
        Assert.Equal(24, MonitorLayout.Y);
        Assert.Equal(167, MonitorLayout.Width);
        Assert.Equal(60, MonitorLayout.Height);
    }
}
