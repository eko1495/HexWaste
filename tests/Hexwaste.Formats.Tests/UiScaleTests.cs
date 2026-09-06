using Hexwaste.Formats.Rendering;

namespace Hexwaste.Formats.Tests;

public class UiScaleTests
{
    [Fact]
    public void ComputeScale_ExactBaseSize_ReturnsOne()
    {
        Assert.Equal(1.0f, UiScale.ComputeScale(640, 480));
    }

    [Fact]
    public void ComputeScale_ExactDoubleSize_ReturnsTwo()
    {
        Assert.Equal(2.0f, UiScale.ComputeScale(1280, 960));
    }

    [Fact]
    public void ComputeScale_HeightLimiting_Returns1_5For1280x720()
    {
        // width ratio 1280/640=2.0, height ratio 720/480=1.5 -- height is the stricter (smaller) ratio.
        Assert.Equal(1.5f, UiScale.ComputeScale(1280, 720));
    }

    [Fact]
    public void ComputeScale_WidthLimiting_Returns1_0For640x1000()
    {
        // width ratio 640/640=1.0, height ratio 1000/480=2.0833 -- width is the stricter (smaller) ratio.
        Assert.Equal(1.0f, UiScale.ComputeScale(640, 1000));
    }

    [Fact]
    public void ComputeScale_DegenerateViewport_ClampsToMinimum()
    {
        Assert.Equal(0.1f, UiScale.ComputeScale(1, 1));
    }

    [Fact]
    public void ComputeVirtualViewport_HeightLimiting_HeightMatchesBase()
    {
        // 1280x720 -> scale 1.5 (height-limiting, see ComputeScale test above).
        (int w, int h) = UiScale.ComputeVirtualViewport(1280, 720, 1.5f);
        Assert.Equal(853, w); // 1280 / 1.5 = 853.33, truncated -- has slack past the 640 base
        Assert.Equal(480, h); // 720 / 1.5 = 480 exactly -- the limiting axis reproduces the base size
    }

    [Fact]
    public void ComputeVirtualViewport_WidthLimiting_WidthMatchesBase()
    {
        // 640x1000 -> scale 1.0 (width-limiting, see ComputeScale test above).
        (int w, int h) = UiScale.ComputeVirtualViewport(640, 1000, 1.0f);
        Assert.Equal(640, w); // the limiting axis reproduces the base size exactly
        Assert.Equal(1000, h); // has slack past the 480 base
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(640, 480)]
    [InlineData(1024, 768)]
    public void ComputeVirtualViewport_RoundTripsWithinOnePixel(int viewportWidth, int viewportHeight)
    {
        float scale = UiScale.ComputeScale(viewportWidth, viewportHeight);
        (int vw, int vh) = UiScale.ComputeVirtualViewport(viewportWidth, viewportHeight, scale);
        Assert.True(Math.Abs(vw * scale - viewportWidth) <= 1f);
        Assert.True(Math.Abs(vh * scale - viewportHeight) <= 1f);
    }

    [Fact]
    public void TransformMouse_OriginMapsToOrigin()
    {
        Assert.Equal((0, 0), UiScale.TransformMouse(0, 0, 2.0f));
    }

    [Fact]
    public void TransformMouse_ScalesDownByFactor()
    {
        Assert.Equal((320, 240), UiScale.TransformMouse(640, 480, 2.0f));
    }

    [Fact]
    public void TransformMouse_RealViewportCenterMapsToVirtualViewportCenter_HeightLimiting()
    {
        float scale = UiScale.ComputeScale(1280, 720);
        (int vw, int vh) = UiScale.ComputeVirtualViewport(1280, 720, scale);
        (int mx, int my) = UiScale.TransformMouse(1280 / 2, 720 / 2, scale);
        Assert.True(Math.Abs(mx - vw / 2) <= 1);
        Assert.True(Math.Abs(my - vh / 2) <= 1);
    }

    [Fact]
    public void TransformMouse_RealViewportCenterMapsToVirtualViewportCenter_WidthLimiting()
    {
        float scale = UiScale.ComputeScale(640, 1000);
        (int vw, int vh) = UiScale.ComputeVirtualViewport(640, 1000, scale);
        (int mx, int my) = UiScale.TransformMouse(640 / 2, 1000 / 2, scale);
        Assert.True(Math.Abs(mx - vw / 2) <= 1);
        Assert.True(Math.Abs(my - vh / 2) <= 1);
    }
}
