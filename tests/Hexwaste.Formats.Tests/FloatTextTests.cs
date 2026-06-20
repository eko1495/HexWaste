using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

/// <summary>P45: the pure floating-combat-text timing + placement math
/// (FloatText), ported from fallout2-ce src/text_object.cc. Locks the engine
/// constants/lifetime/anchor and the Hexwaste rise+fade presentation curve.</summary>
public class FloatTextTests
{
    [Fact]
    public void EngineConstantsMatchTextObjectCc()
    {
        Assert.Equal(20, FloatText.MaxCount);     // TEXT_OBJECTS_MAX_COUNT :19
        Assert.Equal(3500, FloatText.BaseDelayMs); // gTextObjectsBaseDelay :48
        Assert.Equal(1399, FloatText.LineDelayMs); // gTextObjectsLineDelay :51
    }

    [Theory]
    [InlineData(1, 4899)] // 1399*1 + 3500
    [InlineData(2, 6298)] // 1399*2 + 3500
    [InlineData(3, 7697)]
    public void LifetimeIsLineDelayTimesLinesPlusBase(int lines, int expected) =>
        Assert.Equal(expected, FloatText.LifetimeMs(lines)); // text_object.cc:337

    [Fact]
    public void AnchorCentresHorizontallyAndLiftsAboveTheHead()
    {
        // text_object.cc:379-383: x = +16 - width/2 (centre on the 32px tile), y = -(height + 60).
        Assert.Equal((16, -60), FloatText.AnchorOffset(0, 0));
        Assert.Equal((8, -71), FloatText.AnchorOffset(16, 11));
        Assert.Equal((6, -90), FloatText.AnchorOffset(20, 30));
    }

    [Fact]
    public void AlphaIsFullAtBirthAndZeroAtExpiry()
    {
        int life = FloatText.LifetimeMs(1);
        Assert.Equal(1f, FloatText.Alpha(0, life));
        Assert.Equal(0f, FloatText.Alpha(life, life));
        Assert.Equal(0f, FloatText.Alpha(life + 1000, life));
    }

    [Fact]
    public void AlphaHoldsSolidUntilTheFadeStartThenDecreases()
    {
        int life = FloatText.LifetimeMs(1);
        double fadeStart = life * FloatText.FadeStartFraction;
        Assert.Equal(1f, FloatText.Alpha(fadeStart - 1, life));        // still solid
        Assert.Equal(1f, FloatText.Alpha(fadeStart, life), 0.001f);    // boundary still full
        float a1 = FloatText.Alpha(fadeStart + 100, life);
        float a2 = FloatText.Alpha(fadeStart + 500, life);
        Assert.True(a1 < 1f && a1 > 0f);
        Assert.True(a2 < a1); // monotonically fading
    }

    [Fact]
    public void RiseDriftsUpwardProportionalToAge()
    {
        Assert.Equal(0f, FloatText.RiseOffsetPx(0));
        Assert.Equal(-16f, FloatText.RiseOffsetPx(1000), 0.001f); // 16 px in 1 s, upward (negative)
        Assert.True(FloatText.RiseOffsetPx(2000) < FloatText.RiseOffsetPx(1000)); // keeps rising
    }
}
