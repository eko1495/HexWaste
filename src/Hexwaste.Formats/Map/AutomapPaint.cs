namespace Hexwaste.Formats.Map;

/// <summary>Which of the in-game automap's colours a mark carries. Only the wall and
/// scenery cases participate in the priority rule; every other colour is Other.</summary>
public enum AutomapMark
{
    Other,
    Wall,    // _colorTable[992],  automap.cc:534
    Scenery, // _colorTable[480],  automap.cc:537
}

/// <summary>The in-game automap's single colour-priority rule.</summary>
public static class AutomapPaint
{
    /// <summary>Whether <paramref name="incoming"/> may overpaint <paramref name="existing"/>.
    /// ported from fallout2-ce src/automap.cc:573:
    /// <c>if (*v12 != _colorTable[992] || objectColor != _colorTable[480])</c> — i.e. refuse
    /// ONLY scenery-over-wall. The dude and scanner colours still overpaint a wall.</summary>
    public static bool Overpaints(AutomapMark existing, AutomapMark incoming) =>
        existing != AutomapMark.Wall || incoming != AutomapMark.Scenery;
}
