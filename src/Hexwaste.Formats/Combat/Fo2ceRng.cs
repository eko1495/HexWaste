namespace Hexwaste.Formats.Combat;

/// <summary>
/// fallout2-ce's single game RNG, for the trace oracle (<c>--rng fo2ce</c>): a Park-Miller
/// minimal-standard generator behind a 32-entry shuffle table.
/// ported from fallout2-ce src/random.cc randomSeedPrerandomInternal() (:192), getRandom() (:153),
/// randomBetween() (:134). Seeding mirrors the oracle patch's oracleRandomSeed(): the table is
/// zeroed first (the engine's own reseed never writes _iv[0] — its loop runs 40..1 — so a vanilla
/// reseed would inherit a wall-clock-dependent _iv[0]).
/// </summary>
public sealed class Fo2ceRng : ICombatRng
{
    private readonly int[] _iv = new int[32];
    private int _iy;
    private int _idum;

    /// <summary>Trace hook: (min, max, result) after every draw, with fo2ce's inclusive bounds.</summary>
    public Action<int, int, int>? OnDraw { get; set; }

    public Fo2ceRng(int seed)
    {
        // ported from fallout2-ce src/random.cc randomSeedPrerandomInternal() (:192)
        int num = seed < 1 ? 1 : seed;
        for (int index = 40; index > 0; index--)
        {
            num = 16807 * (num % 127773) - 2836 * (num / 127773);
            if (num < 0)
                num &= int.MaxValue;
            if (index < 32)
                _iv[index] = num;
        }
        _iy = _iv[0];
        _idum = num;
    }

    // ported from fallout2-ce src/random.cc getRandom() (:153)
    private int GetRandom(int max)
    {
        int v1 = 16807 * (_idum % 127773) - 2836 * (_idum / 127773);
        if (v1 < 0)
            v1 += 0x7FFFFFFF;
        if (v1 < 0)
            v1 += 0x7FFFFFFF;
        int v2 = _iy & 0x1F;
        int v3 = _iv[v2];
        _iv[v2] = v1;
        _iy = v3;
        _idum = v1;
        return v3 % max;
    }

    /// <summary>ported from fallout2-ce src/random.cc randomBetween() (:134) — inclusive, and a
    /// reversed range is legal (draws from [max, min]).</summary>
    public int Between(int min, int max)
    {
        int result = min <= max ? min + GetRandom(max - min + 1) : max + GetRandom(min - max + 1);
        if (result < min || result > max)
            result = min;
        OnDraw?.Invoke(min, max, result);
        return result;
    }

    public int Next(int minInclusive, int maxExclusive) => Between(minInclusive, maxExclusive - 1);
}

/// <summary>Presents a <see cref="Fo2ceRng"/> as a <see cref="Random"/> so the script VM and
/// ScriptHost (which take <c>System.Random</c>) draw from the one shared oracle stream.</summary>
public sealed class Fo2ceRandomAdapter(Fo2ceRng rng) : Random
{
    public Fo2ceRng Rng { get; } = rng;
    public override int Next(int minValue, int maxValue) => Rng.Next(minValue, maxValue);
    public override int Next(int maxValue) => Rng.Next(0, maxValue);
    public override int Next() => Rng.Next(0, int.MaxValue);
    protected override double Sample() => Rng.Next(0, int.MaxValue) / (double)int.MaxValue;
}
