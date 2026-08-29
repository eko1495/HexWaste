namespace Hexwaste.Formats.Art;

/// <summary>
/// Which of a talking head's fidget variants plays next.
/// ported from fallout2-ce src/game_dialog.cc _gdSetupFidget() (:2505-2529)
/// </summary>
public static class HeadFidget
{
    /// <summary>The head-anim types that name a FIDGET family; the fidget NUMBER this rolls
    /// then goes in the FID's weapon nibble (art.cc artBuildFilePath type 8 formats a head as
    /// "&lt;name&gt;&lt;emotion&gt;f&lt;digit&gt;.frm", the digit being (fid &amp; 0xF000) &gt;&gt; 12).
    /// ported from fallout2-ce src/art.h FIDGET_GOOD/NEUTRAL/BAD (:32,35,38)</summary>
    public const int Good = 1;
    public const int Neutral = 4;
    public const int Bad = 7;

    /// <summary>The engine's roll input: <c>randomBetween(1, 100) + secondsSinceLastInput / 2</c>
    /// (game_dialog.cc:2505). The idle term makes a long pause bias toward the later, showier
    /// fidgets — integer division, so it only starts shifting the outcome after two seconds.</summary>
    public static int Chance(int roll1To100, int secondsSinceLastInput) =>
        roll1To100 + secondsSinceLastInput / 2;

    /// <summary>Picks the fidget number for a head with <paramref name="fidgetCount"/> variants.
    /// ported from fallout2-ce src/game_dialog.cc _gdSetupFidget() (:2507-2529).
    /// The thresholds are the engine's own: with 2 variants the split is 68, with 3 it is 52/77.
    /// Any other count (0, or above 3) falls through to the reference's <c>fidget = fidgetCount</c>
    /// initialiser — head 0 ("reser") really does resolve to 0 there, and the caller must not
    /// build art from a count it cannot satisfy.</summary>
    public static int Roll(int fidgetCount, int chance) => fidgetCount switch
    {
        1 => 1,
        2 => chance < 68 ? 1 : 2,
        3 => chance < 52 ? 1 : chance < 77 ? 2 : 3,
        _ => fidgetCount,
    };

    /// <summary>Whether this roll also zeroes the idle accumulator. The reference resets
    /// <c>_dialogue_seconds_since_last_input</c> INSIDE the 3-variant case only
    /// (game_dialog.cc:2520) — a 1- or 2-variant head keeps accumulating idle time across
    /// rolls. Quirk of the original, reproduced deliberately rather than regularised.</summary>
    public static bool ResetsIdleAccumulator(int fidgetCount) => fidgetCount == 3;
}
