using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// F7: the in-game automap's one colour-priority rule, ported from
/// fallout2-ce src/automap.cc:573 — scenery may not overpaint a wall pixel.
/// Everything else overpaints, including a wall arriving after scenery.
/// </summary>
public class AutomapPaintTests
{
    [Fact]
    public void SceneryDoesNotOverpaintAWall() =>
        Assert.False(AutomapPaint.Overpaints(AutomapMark.Wall, AutomapMark.Scenery));

    [Fact]
    public void AWallOverpaintsScenery() =>
        Assert.True(AutomapPaint.Overpaints(AutomapMark.Scenery, AutomapMark.Wall));

    [Fact]
    public void TheDudeMarkOverpaintsAWall() =>
        // _colorTable[31744] is not the scenery colour, so the guard's second term is false.
        Assert.True(AutomapPaint.Overpaints(AutomapMark.Wall, AutomapMark.Other));

    [Fact]
    public void SceneryOverpaintsAnythingThatIsNotAWall() =>
        Assert.True(AutomapPaint.Overpaints(AutomapMark.Other, AutomapMark.Scenery));

    [Fact]
    public void AWallOverpaintsAWall() =>
        Assert.True(AutomapPaint.Overpaints(AutomapMark.Wall, AutomapMark.Wall));
}
