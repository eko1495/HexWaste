namespace Hexwaste.Formats;

/// <summary>The green message-monitor scrollback window math, ported from fallout2-ce
/// src/display_monitor.cc (displayMonitorRefresh :343 + the scroll guards :382/391). Pure so
/// the ring off-by-one the engine guards against is unit-tested independently of the renderer.</summary>
public static class MonitorScrollback
{
    /// <summary>DISPLAY_MONITOR_LINES_CAPACITY — the engine keeps 100 wrapped lines (display_monitor.cc:25).</summary>
    public const int Capacity = 100;

    /// <summary>Given the total wrapped-line count, the rows that fit the monitor, and the
    /// scroll-back offset (0 = newest line at the bottom, like _disp_curr == _disp_start), return
    /// the [Start, End) slice to draw and the offset clamped to the available history. The engine's
    /// scroll-up stops at the oldest line and scroll-down stops at the newest (displayMonitorScroll*).</summary>
    public static (int Start, int End, int Scroll) Window(int lineCount, int maxLines, int scroll)
    {
        if (maxLines < 1)
            maxLines = 1;
        int maxScroll = System.Math.Max(0, lineCount - maxLines);
        scroll = System.Math.Clamp(scroll, 0, maxScroll);
        int end = lineCount - scroll;                       // exclusive — the bottom-most visible line + 1
        int start = System.Math.Max(0, end - maxLines);
        return (start, end, scroll);
    }
}
