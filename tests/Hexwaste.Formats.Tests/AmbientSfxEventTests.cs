using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>Oracle F13: fo2ce game_sound.cc ambientSoundEffectEventProcess draw order.</summary>
public class AmbientSfxEventTests
{
    private static readonly (string Name, int Chance)[] Two = [("a", 30), ("b", 70)];

    // Scripted fo2ce randomBetween: returns the queued values in order and records each (min, max).
    private static Func<int, int, int> Scripted(List<(int Min, int Max)> calls, params int[] values)
    {
        var queue = new Queue<int>(values);
        return (min, max) => { calls.Add((min, max)); return queue.Dequeue(); };
    }

    [Fact]
    public void NoDataDrawsIndexDelayNextIndex()
    {
        var calls = new List<(int, int)>();
        var r = AmbientSfx.RunEvent(Two, null, false, Scripted(calls, 0, 15, 40));
        Assert.Equal([(0, 100), (15, 20), (0, 100)], calls);
        Assert.Equal(0, r.PlayIdx);       // 0 < 30 -> "a"
        Assert.Equal(1, r.NextIdx);       // 40 -> past "a" (30), into "b"
        Assert.Equal(150, r.DelayTicks);  // 10 * 15
    }

    [Fact]
    public void StoredIndexDrawsDelayAndNextIndexOnly()
    {
        var calls = new List<(int, int)>();
        var r = AmbientSfx.RunEvent(Two, 1, false, Scripted(calls, 18, 82));
        Assert.Equal([(15, 20), (0, 100)], calls);
        Assert.Equal(1, r.PlayIdx);
        Assert.Equal(1, r.NextIdx);
        Assert.Equal(180, r.DelayTicks);
    }

    [Fact]
    public void CombatDrawsTheSameButPlaysNothing()
    {
        var calls = new List<(int, int)>();
        var r = AmbientSfx.RunEvent(Two, null, true, Scripted(calls, 0, 15, 40));
        Assert.Equal(3, calls.Count);
        Assert.Equal(-1, r.PlayIdx);
        Assert.Equal(1, r.NextIdx);
        Assert.Equal(150, r.DelayTicks);
    }

    [Fact]
    public void EmptyListDrawsDelayOnlyAndStopsTheChain()
    {
        var calls = new List<(int, int)>();
        var r = AmbientSfx.RunEvent([], null, false, Scripted(calls, 17));
        Assert.Equal([(15, 20)], calls);
        Assert.Equal(-1, r.PlayIdx);
        Assert.Null(r.NextIdx);
        Assert.Equal(-1, r.DelayTicks);
    }
}
