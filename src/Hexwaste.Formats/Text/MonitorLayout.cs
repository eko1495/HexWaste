namespace Hexwaste.Formats.Text;

/// <summary>The green message monitor's geometry and wrap budget.
/// ported from fallout2-ce src/display_monitor.cc (:31-34 geometry, :115 _max_disp,
/// :262 the wrap condition, :266-272 the knob).</summary>
public static class MonitorLayout
{
    /// <summary>The bullet knob prefixed to the FIRST line of every message
    /// (display_monitor.cc:244).</summary>
    public const char Knob = '\x95';

    // DISPLAY_MONITOR_X/Y/WIDTH/HEIGHT, display_monitor.cc:31-34. WIDTH is
    // `167 + gInterfaceBarContentOffset`; the offset is 0 for the vanilla 640-wide bar.
    public const int X = 23;
    public const int Y = 24;
    public const int Width = 167;
    public const int Height = 60;

    /// <summary>`_max_disp` — how many lines fit in the monitor (display_monitor.cc:115).</summary>
    public static int MaxDisplayLines(int lineHeight) =>
        lineHeight > 0 ? Height / lineHeight : 0;

    /// <summary>The wrap budget: `DISPLAY_MONITOR_WIDTH - _max_disp - knobWidth`
    /// (display_monitor.cc:262). NOTE the original subtracts `_max_disp`, a LINE COUNT,
    /// from a PIXEL width. That is what the shipped engine does; it is reproduced here
    /// verbatim and is NOT a unit error to be corrected.
    /// Pass the knob's pixel width for the first line of a message and 0 for every
    /// continuation line, matching the `knobWidth = 0` arm at :270.</summary>
    public static int WrapBudget(int lineHeight, int knobWidth) =>
        Width - MaxDisplayLines(lineHeight) - knobWidth;
}
