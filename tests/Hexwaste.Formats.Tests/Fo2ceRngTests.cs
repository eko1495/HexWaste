using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

/// <summary>Locks <see cref="Fo2ceRng"/> to the draws the trace-patched fallout2-ce e97087b
/// emits (Data/fo2ce-rng-vectors.txt, produced by tools/fo2ce-trace run.sh rng_dump=...).</summary>
public class Fo2ceRngTests
{
    private static IEnumerable<(int Seed, int Min, int Max, int Value)> Vectors()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "fo2ce-rng-vectors.txt");
        foreach (string line in File.ReadLines(path))
        {
            // seed=12345 min=1 max=100 -> 37
            string[] p = line.Split(' ');
            yield return (int.Parse(p[0][5..]), int.Parse(p[1][4..]), int.Parse(p[2][4..]), int.Parse(p[4]));
        }
    }

    [Fact]
    public void MatchesFo2ceVectorsForEverySeed()
    {
        Fo2ceRng? rng = null;
        int currentSeed = int.MinValue, checkedCount = 0;
        foreach (var (seed, min, max, value) in Vectors())
        {
            if (seed != currentSeed)
            {
                rng = new Fo2ceRng(seed);
                currentSeed = seed;
            }
            Assert.Equal(value, rng!.Between(min, max));
            checkedCount++;
        }
        Assert.Equal(3000, checkedCount);
    }

    [Fact]
    public void NextIsMaxExclusiveView()
    {
        var a = new Fo2ceRng(7);
        var b = new Fo2ceRng(7);
        for (int i = 0; i < 200; i++)
            Assert.Equal(a.Between(1, 100), b.Next(1, 101));
    }

    [Fact]
    public void OnDrawReportsEveryDraw()
    {
        var rng = new Fo2ceRng(1);
        var seen = new List<(int, int, int)>();
        rng.OnDraw = (min, max, v) => seen.Add((min, max, v));
        int v = rng.Between(1, 6);
        Assert.Equal([(1, 6, v)], seen);
    }

    [Fact]
    public void AdapterRoutesNextMinMaxThroughTheSameStream()
    {
        var a = new Fo2ceRng(3);
        var adapter = new Fo2ceRandomAdapter(new Fo2ceRng(3));
        for (int i = 0; i < 100; i++)
            Assert.Equal(a.Next(1, 11), adapter.Next(1, 11));
    }
}
