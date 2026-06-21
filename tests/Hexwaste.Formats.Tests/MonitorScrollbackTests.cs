using Hexwaste.Formats;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// The green message-monitor scrollback window math (P52-M5), ported from fallout2-ce
/// display_monitor.cc (displayMonitorRefresh + the scroll-up/down guards). Locks the ring
/// off-by-one: scroll 0 shows the newest, scrolling up walks back and clamps at the oldest line.
/// </summary>
public class MonitorScrollbackTests
{
    [Fact]
    public void ScrollZeroShowsTheNewestWindow()
    {
        var (start, end, scroll) = MonitorScrollback.Window(lineCount: 10, maxLines: 3, scroll: 0);
        Assert.Equal(7, start);   // last 3 of 10
        Assert.Equal(10, end);
        Assert.Equal(0, scroll);
    }

    [Fact]
    public void ScrollUpWalksBackByLines()
    {
        var (start, end, scroll) = MonitorScrollback.Window(10, 3, 2);
        Assert.Equal(5, start);   // window 5..8
        Assert.Equal(8, end);
        Assert.Equal(2, scroll);
    }

    [Fact]
    public void ScrollClampsAtTheOldestLine()
    {
        // The most you can scroll back is lineCount - maxLines (= 7); a larger request is clamped.
        var (start, end, scroll) = MonitorScrollback.Window(10, 3, 999);
        Assert.Equal(0, start);
        Assert.Equal(3, end);
        Assert.Equal(7, scroll);
    }

    [Fact]
    public void NegativeScrollClampsToNewest()
    {
        var (start, end, scroll) = MonitorScrollback.Window(10, 3, -5);
        Assert.Equal(7, start);
        Assert.Equal(10, end);
        Assert.Equal(0, scroll);
    }

    [Fact]
    public void FewerLinesThanFitShowAllWithNoScroll()
    {
        var (start, end, scroll) = MonitorScrollback.Window(2, 5, 3);
        Assert.Equal(0, start);
        Assert.Equal(2, end);
        Assert.Equal(0, scroll); // maxScroll = max(0, 2-5) = 0
    }

    [Fact]
    public void EmptyHistoryIsAnEmptyWindow()
    {
        var (start, end, scroll) = MonitorScrollback.Window(0, 3, 0);
        Assert.Equal(0, start);
        Assert.Equal(0, end);
        Assert.Equal(0, scroll);
    }

    [Fact]
    public void ZeroMaxLinesIsTreatedAsOne()
    {
        var (start, end, _) = MonitorScrollback.Window(4, 0, 0);
        Assert.Equal(3, start);
        Assert.Equal(4, end);
    }
}
