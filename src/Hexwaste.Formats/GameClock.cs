namespace Hexwaste.Formats;

/// <summary>
/// The game clock: 10 ticks per game-second, booting at tick 302400
/// (08:24, June 24 2241) like fallout2-ce scripts.cc. The original only
/// advances time on travel/combat/skill use; this PoC additionally runs a
/// gentle idle rate so day/night is observable while walking around.
/// </summary>
public sealed class GameClock
{
    public const int TicksPerSecond = 10;
    public const int TicksPerHour = 60 * 60 * TicksPerSecond;
    public const int TicksPerDay = 24 * TicksPerHour;

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
    public int Hour => (int)(100 * (Ticks / 600 / 60 % 24) + Ticks / 600 % 60);

    public int Day => (int)(Ticks / TicksPerDay) + 1;

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
