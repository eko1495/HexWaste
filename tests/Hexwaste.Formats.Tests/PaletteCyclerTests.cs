using Hexwaste.Formats.Pal;

namespace Hexwaste.Formats.Tests;

public class PaletteCyclerTests
{
    private static PaletteCycler CreateCycler() => new(Palette.Load(new byte[768]));

    [Fact]
    public void FillsCyclingRangesOnConstruction()
    {
        var cycler = CreateCycler();
        byte[] raw = cycler.Palette.Raw;

        // fire_slow starts at palette index 238 with 255,0,0 (8-bit) -> 63,0,0 (6-bit).
        Assert.Equal(63, raw[238 * 3]);
        Assert.Equal(0, raw[238 * 3 + 1]);
        Assert.Equal(0, raw[238 * 3 + 2]);

        // slime starts at 229 with 0,108,0 -> 0,27,0.
        Assert.Equal(0, raw[229 * 3]);
        Assert.Equal(27, raw[229 * 3 + 1]);
    }

    [Fact]
    public void NothingChangesBeforeFirstPeriodElapses()
    {
        var cycler = CreateCycler();
        Assert.False(cycler.Update(20.0)); // shortest period is ~33 ms
    }

    [Fact]
    public void SlowGroupRotatesBackwardByOneEntry()
    {
        var cycler = CreateCycler();
        byte[] raw = cycler.Palette.Raw;

        Assert.True(cycler.Update(200.0));

        // After one slow tick slime_start becomes 9, so palette index 229
        // shows table entry 3 (8-bit 43,131,27 -> 6-bit 10,32,6).
        Assert.Equal(43 >> 2, raw[229 * 3]);
        Assert.Equal(131 >> 2, raw[229 * 3 + 1]);
        Assert.Equal(27 >> 2, raw[229 * 3 + 2]);
    }

    [Fact]
    public void SlimeCycleReturnsToStartAfterFourSlowTicks()
    {
        var cycler = CreateCycler();
        byte[] raw = cycler.Palette.Raw;
        byte[] initial = [.. raw[(229 * 3)..(233 * 3)]];

        for (int i = 0; i < 4; i++)
            cycler.Update(200.0);

        Assert.Equal(initial, raw[(229 * 3)..(233 * 3)]);
    }

    [Fact]
    public void GroupsTickIndependently()
    {
        var cycler = CreateCycler();
        byte[] raw = cycler.Palette.Raw;
        byte[] slimeBefore = [.. raw[(229 * 3)..(233 * 3)]];
        byte[] monitorsBefore = [.. raw[(233 * 3)..(238 * 3)]];

        // 100 ms: fast (monitors) fires, slow (slime) does not.
        Assert.True(cycler.Update(100.0));
        Assert.Equal(slimeBefore, raw[(229 * 3)..(233 * 3)]);
        Assert.NotEqual(monitorsBefore, raw[(233 * 3)..(238 * 3)]);
    }

    [Fact]
    public void AlarmBobberBouncesBetween0And60()
    {
        var cycler = CreateCycler();
        byte[] raw = cycler.Palette.Raw;

        var seen = new List<byte>();
        for (int i = 0; i < 40; i++)
        {
            cycler.Update(1000.0 / 30);
            seen.Add(raw[254 * 3]);
        }

        Assert.Equal(60, seen.Max());
        Assert.Contains((byte)0, seen);
        Assert.All(seen, v => Assert.True(v <= 60));
        // Green and blue stay zero.
        Assert.Equal(0, raw[254 * 3 + 1]);
        Assert.Equal(0, raw[254 * 3 + 2]);
    }
}
