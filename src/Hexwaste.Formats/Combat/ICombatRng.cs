namespace Hexwaste.Formats.Combat;

/// <summary>
/// The randomness seam for the combat engine (phase-9 M0, "extract first").
/// The engine rolls every outcome through this interface instead of touching
/// <see cref="System.Random"/> directly, so the turn machine — once lifted out
/// of the MonoGame viewer into <c>Hexwaste.Formats</c> — can be driven by a
/// scripted RNG in unit tests (force a hit, force a miss, force a crit) with no
/// GraphicsDevice and no game data.
///
/// The single method mirrors <see cref="System.Random.Next(int,int)"/> exactly
/// (lower bound inclusive, upper bound exclusive) so wrapping the existing
/// seeded <see cref="System.Random"/> in <see cref="SystemCombatRng"/> is
/// behaviour-preserving: same seed, same call order ⇒ identical roll sequence.
///
/// fallout2-ce rolls combat through roll.cc (<c>rollRandom</c>/<c>randomBetween</c>);
/// we keep our existing System.Random distribution and only abstract the source.
/// </summary>
public interface ICombatRng
{
    /// <summary>A random integer in [minInclusive, maxExclusive), matching
    /// <see cref="System.Random.Next(int,int)"/>.</summary>
    int Next(int minInclusive, int maxExclusive);
}

/// <summary>
/// The production <see cref="ICombatRng"/>: a thin, seedable wrapper over
/// <see cref="System.Random"/>. Seeded via <c>--rng-seed</c> for deterministic
/// headless transcripts; unseeded otherwise.
/// </summary>
public sealed class SystemCombatRng : ICombatRng
{
    private readonly Random _random;

    public SystemCombatRng() => _random = new Random();

    public SystemCombatRng(int seed) => _random = new Random(seed);

    public int Next(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
}
