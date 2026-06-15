using Hexwaste.Formats;

namespace Hexwaste.Formats.Tests;

public class GameClockTests
{
    [Fact]
    public void BootsAtEngineDefaultTime()
    {
        var clock = new GameClock();
        Assert.Equal(824, clock.Hour); // 302400 ticks = 08:24, day 1
        Assert.Equal(1, clock.Day);
    }

    [Fact]
    public void CalendarStartsOnTheFalloutDateAndWalksTheMonths()
    {
        // P20-M3: the FO2 start is July 25, 2241 (scripts.cc gameTimeGetDate).
        var clock = new GameClock();
        Assert.Equal((7, 25, 2241), clock.Date);
        Assert.Equal("July 25, 2241", clock.DateString);

        // +6 days → still July (25→31), +7th day crosses into August 1.
        clock.Ticks = 302400 + 6L * GameClock.TicksPerDay;
        Assert.Equal((7, 31, 2241), clock.Date);
        clock.Ticks = 302400 + 7L * GameClock.TicksPerDay;
        Assert.Equal((8, 1, 2241), clock.Date);

        // +160 days from July 25 lands in early January 2242 (year rollover).
        clock.Ticks = 302400 + 160L * GameClock.TicksPerDay;
        Assert.Equal(2242, clock.Date.Year);
    }

    [Fact]
    public void AdvancesRealTimeAtIdleRate()
    {
        var clock = new GameClock { IdleRate = 60 };
        clock.AdvanceRealTime(1000); // 1 real second = 1 game minute
        Assert.Equal(825, clock.Hour);
    }

    [Fact]
    public void AmbientCurveCoversDayAndNight()
    {
        var clock = new GameClock();

        clock.Ticks = 3 * GameClock.TicksPerHour; // 03:00
        Assert.Equal(0.25, clock.AmbientFraction, 2);

        clock.Ticks = 12 * GameClock.TicksPerHour; // noon
        Assert.Equal(1.0, clock.AmbientFraction, 2);

        clock.Ticks = 6 * GameClock.TicksPerHour; // dawn midpoint
        Assert.InRange(clock.AmbientFraction, 0.5, 0.8);

        clock.Ticks = 23 * GameClock.TicksPerHour;
        Assert.Equal(0.25, clock.AmbientFraction, 2);
    }

    [Fact]
    public void TravelAdvancesWholeHours()
    {
        var clock = new GameClock();
        long before = clock.Ticks;
        clock.AdvanceHours(8);
        Assert.Equal(before + 8L * GameClock.TicksPerHour, clock.Ticks);
    }
}
