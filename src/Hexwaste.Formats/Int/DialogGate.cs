namespace Hexwaste.Formats.Int;

/// <summary>
/// Dialogue option gating, ported from fallout2-ce src/interpreter_extra.cc _op_giq_option
/// (0x8121, P25). An option carries an <c>iq</c> requirement checked against the dude's
/// STAT_INTELLIGENCE: a positive iq is a MINIMUM ("smart" option, IN ≥ iq), a negative iq a
/// MAXIMUM ("dumb"/stupid option, IN ≤ -iq). iq 0 is always visible. The engine adds the
/// Smooth Talker perk rank to intelligence first — out of scope here (no perk system).
/// </summary>
public static class DialogGate
{
    /// <summary>True if a giq_option with requirement <paramref name="iq"/> is shown to a dude of
    /// the given <paramref name="intelligence"/>. The engine SKIPS the option when
    /// <c>iq &lt; 0 ? -intelligence &lt; iq : intelligence &lt; iq</c>; this is its negation.</summary>
    public static bool IqOptionVisible(int iq, int intelligence) =>
        iq < 0 ? -intelligence >= iq : intelligence >= iq;
}
