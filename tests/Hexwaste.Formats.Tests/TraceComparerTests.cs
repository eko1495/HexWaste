using Hexwaste.Formats.Trace;

namespace Hexwaste.Formats.Tests;

public class TraceComparerTests
{
    [Fact]
    public void IdenticalTracesHaveNoDivergence() =>
        Assert.Null(TraceComparer.FirstDivergence(["# h", "t=1 GVAR 1 0->1"], ["# other", "t=1 GVAR 1 0->1"]));

    [Fact]
    public void SiteFieldIsIgnored() =>
        Assert.Null(TraceComparer.FirstDivergence(
            ["t=5 RNG 1..100 -> 37 site=combat.cc:attackCompute"],
            ["t=5 RNG 1..100 -> 37 site=CombatEngine.TryAttack"]));

    [Fact]
    public void ReportsFirstMismatchWithContext()
    {
        string[] a = ["# h", "t=1 GVAR 1 0->1", "t=2 RNG 1..6 -> 3 site=x", "t=3 TURN obj#7 ap=8"];
        string[] b = ["# h", "t=1 GVAR 1 0->1", "t=2 RNG 1..6 -> 4 site=y", "t=3 TURN obj#7 ap=8"];
        TraceDivergence d = TraceComparer.FirstDivergence(a, b, contextLines: 1)!;
        Assert.Equal(3, d.LineNumber);
        Assert.Equal("t=2 RNG 1..6 -> 3 site=x", d.Expected);
        Assert.Equal("t=2 RNG 1..6 -> 4 site=y", d.Actual);
        Assert.Equal(["t=1 GVAR 1 0->1"], d.Context);
    }

    [Fact]
    public void ShorterTraceDivergesAtItsEnd()
    {
        TraceDivergence d = TraceComparer.FirstDivergence(["t=1 GVAR 1 0->1", "t=2 GVAR 2 0->1"], ["t=1 GVAR 1 0->1"])!;
        Assert.Equal(2, d.LineNumber);
        Assert.Null(d.Actual);
    }
}
