namespace Hexwaste.Formats;

/// <summary>
/// The game clock: 10 ticks per game-second, booting at tick 302400
/// (08:24 on July 25, 2241 — the FO2 start date, sfall_config.cc start
/// year 2241 / month 6 / day 24, output +1) like fallout2-ce scripts.cc. The
/// original only advances time on travel/combat/skill use; this PoC additionally
/// runs a gentle idle rate so day/night is observable while walking around.
/// </summary>
public sealed class GameClock
{
    public const int TicksPerSecond = 10;
    public const int TicksPerHour = 60 * 60 * TicksPerSecond;
    public const int TicksPerDay = 24 * TicksPerHour;

    // FO2 start date (sfall_config.cc:30-32, 0-based month/day; the engine outputs +1).
    private const int StartYear = 2241;
    private const int StartMonth = 6; // July
    private const int StartDay = 24;  // the 25th
    private static readonly int[] DaysPerMonth = [31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    private static readonly string[] MonthNames =
        ["January", "February", "March", "April", "May", "June",
         "July", "August", "September", "October", "November", "December"];

    /// <summary>Game-seconds that pass per real second while just walking around.</summary>
    public double IdleRate { get; set; } = 60; // 1 game-minute per real second

    public long Ticks { get; set; } = 302400;

    private double _fraction;

    public void AdvanceRealTime(double elapsedMs)
    {
        _fraction += elapsedMs / 1000.0 * IdleRate * TicksPerSecond;
        long whole = (long)_fraction;
        if (whole > 0)
        {
            Ticks += whole;
            _fraction -= whole;
        }
    }

    public void AdvanceHours(int hours) => Ticks += (long)hours * TicksPerHour;

    /// <summary>ported from scripts.cc gameTimeGetHour(): military hhmm.</summary>
    public int Hour => HourAt(Ticks);

    public int Day => DayAt(Ticks);

    /// <summary>Military hhmm for an absolute tick count — the single source of truth
    /// so callers that walk a tick total (e.g. worldmap travel) compute the same
    /// hour the live clock would report.</summary>
    public static int HourAt(long ticks) => (int)(100 * (ticks / 600 / 60 % 24) + ticks / 600 % 60);

    /// <summary>1-based day number for an absolute tick count.</summary>
    public static int DayAt(long ticks) => (int)(ticks / TicksPerDay) + 1;

    /// <summary>The calendar (month 1-12, day 1-31, year) for the current tick count,
    /// ported from scripts.cc gameTimeGetDate(): walk months from the FO2 start
    /// (July 25, 2241), the same algorithm quirks and all (P20-M3).</summary>
    public (int Month, int Day, int Year) Date => DateAt(Ticks);

    public static (int Month, int Day, int Year) DateAt(long ticks)
    {
        int dayCount = (int)(ticks / TicksPerDay) + StartDay;
        int year = dayCount / 365 + StartYear;
        int month = StartMonth;
        int day = dayCount % 365;
        while (true)
        {
            int daysInMonth = DaysPerMonth[month];
            if (day < daysInMonth)
                break;
            month++;
            day -= daysInMonth;
            if (month == 12)
            {
                year++;
                month = 0;
            }
        }
        return (month + 1, day + 1, year);
    }

    /// <summary>The calendar date as "July 25, 2241" for an absolute tick count.</summary>
    public static string DateStringAt(long ticks)
    {
        (int month, int day, int year) = DateAt(ticks);
        return $"{MonthNames[month - 1]} {day}, {year}";
    }

    public string DateString => DateStringAt(Ticks);

    /// <summary>
    /// Ambient daylight fraction for an hour-of-day. The engine has no
    /// built-in curve (maps load fullbright; scripts set light levels) — this
    /// is the PoC's own day/night shape: night floor 0.25, dawn 05-07,
    /// dusk 19-22.
    /// </summary>
    public double AmbientFraction
    {
        get
        {
            double hour = Ticks / 600.0 / 60.0 % 24.0;
            return hour switch
            {
                < 5.0 => 0.25,
                < 7.0 => 0.25 + 0.75 * (hour - 5.0) / 2.0,
                < 19.0 => 1.0,
                < 22.0 => 1.0 - 0.75 * (hour - 19.0) / 3.0,
                _ => 0.25,
            };
        }
    }
}
