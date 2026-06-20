namespace Hexwaste.Formats.Combat;

/// <summary>
/// Pure timing + placement math for floating combat text, ported from
/// fallout2-ce src/text_object.cc. The engine's text-object layer floats AI
/// taunts / skill-use responses / level-up notices / the script <c>float_msg</c>
/// string above a critter's tile; Hexwaste reuses the SAME mechanism (cap,
/// lifetime, anchor, one-per-owner) to present combat damage numbers / "Missed" /
/// crit feedback (P45).
///
/// DOCUMENTED DIVERGENCE: Fallout 2 does NOT float combat outcomes — combat.cc
/// <c>_combat_display</c> routes every hit/miss/crit/damage line to the scrolling
/// MONITOR LOG (displayMonitorAddMessage), one colour, no float. So "floating
/// damage numbers" is a Hexwaste presentation layer built on the engine's real
/// float-text mechanism + its real float colour constants (see CombatFloatColors
/// in the viewer). The rise + alpha fade are a further presentation choice: the
/// engine's text objects are static and non-fading — they hold solid, then expire
/// (text_object.cc:338 textObjectsTicker has no fade).
/// </summary>
public static class FloatText
{
    /// <summary>TEXT_OBJECTS_MAX_COUNT (text_object.cc:19) — at most 20 floats at once.</summary>
    public const int MaxCount = 20;

    /// <summary>gTextObjectsBaseDelay (text_object.cc:48) — base on-screen time, ms.</summary>
    public const int BaseDelayMs = 3500;

    /// <summary>gTextObjectsLineDelay (text_object.cc:51) — added per text line, ms.</summary>
    public const int LineDelayMs = 1399;

    /// <summary>The float's on-screen lifetime: <c>lineDelay*lines + baseDelay</c>
    /// (text_object.cc:337 textObjectsTicker — the expiry threshold). A one-line
    /// damage number lives 3500 + 1399 = 4899 ms.</summary>
    public static int LifetimeMs(int linesCount) => LineDelayMs * linesCount + BaseDelayMs;

    /// <summary>The text box's placement offset relative to the critter's tile
    /// screen anchor, ported from text_object.cc:379-383 textObjectFindPlacement:
    /// <c>x = tileScreenX + 16 - width/2</c> (centre the box on the 32-px-wide tile),
    /// <c>y = tileScreenY - (height + 60)</c> (lift it above the head). The engine
    /// then runs an 8-position on-screen-bounds cascade (text_object.cc:386-454);
    /// Hexwaste keeps the primary placement — a documented simplification, since the
    /// camera clamps the world so the off-screen cascade rarely fires.</summary>
    public static (int Sx, int Sy) AnchorOffset(int width, int height) =>
        (16 - width / 2, -(height + 60));

    /// <summary>The fraction of the lifetime the float holds at full opacity before
    /// it begins to fade. Presentation value (the engine never fades).</summary>
    public const double FadeStartFraction = 0.6;

    /// <summary>Alpha 0..1 over the float's age: solid until <see cref="FadeStartFraction"/>
    /// of the lifetime, then linear to 0 at expiry. A Hexwaste presentation divergence
    /// (the engine holds solid until it removes the object — text_object.cc:338).</summary>
    public static float Alpha(double ageMs, int lifetimeMs)
    {
        if (lifetimeMs <= 0 || ageMs >= lifetimeMs) return 0f;
        if (ageMs <= 0) return 1f;
        double fadeStart = lifetimeMs * FadeStartFraction;
        if (ageMs <= fadeStart) return 1f;
        return (float)(1.0 - (ageMs - fadeStart) / (lifetimeMs - fadeStart));
    }

    /// <summary>Pixels per second the float drifts upward (a presentation value —
    /// the engine's floats are static).</summary>
    public const double RiseVelocityPxPerSec = 16.0;

    /// <summary>The upward drift (negative screen Y) for a float of the given age.</summary>
    public static float RiseOffsetPx(double ageMs) =>
        (float)(-ageMs / 1000.0 * RiseVelocityPxPerSec);
}
