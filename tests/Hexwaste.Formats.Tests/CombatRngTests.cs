using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Locks the phase-9 M0 randomness seam (<see cref="ICombatRng"/>). These tests
/// are the regression net for the "extract first" refactor: they prove the seam
/// changed no behaviour (SystemCombatRng == System.Random), that seeded combat
/// is reproducible, and that a scripted RNG can force outcomes — the capability
/// the future crit / knockdown / AI tests will lean on once the turn machine is
/// lifted into Hexwaste.Formats.
/// </summary>
public class CombatRngTests
{
    /// <summary>Behaviour preservation: wrapping System.Random in
    /// SystemCombatRng must reproduce its exact sequence for a given seed, so
    /// swapping the field type in the viewer is a no-op for every roll.</summary>
    [Fact]
    public void SystemCombatRngMatchesSystemRandomForSameSeed()
    {
        var reference = new Random(12345);
        var seam = new SystemCombatRng(12345);

        for (int i = 0; i < 1000; i++)
            Assert.Equal(reference.Next(1, 101), seam.Next(1, 101));
    }

    [Fact]
    public void SeededSystemCombatRngIsReproducible()
    {
        int[] Series()
        {
            var rng = new SystemCombatRng(99);
            return [.. Enumerable.Range(0, 50).Select(_ => rng.Next(1, 101))];
        }

        Assert.Equal(Series(), Series());
    }

    /// <summary>The point of the seam: a scripted RNG lets a test force a hit or
    /// a miss through the real CombatMath path with no randomness.</summary>
    [Fact]
    public void ScriptedRngForcesDeterministicHitsAndMisses()
    {
        // RollHit compares rng.Next(1,101) <= chance.
        var alwaysLow = new FixedCombatRng(1);   // always rolls 1 → hits any chance >= 1
        var alwaysHigh = new FixedCombatRng(100); // always rolls 100 → misses any chance < 100

        Assert.True(CombatMath.RollHit(alwaysLow, 1));
        Assert.True(CombatMath.RollHit(alwaysLow, 95));
        Assert.False(CombatMath.RollHit(alwaysHigh, 50));
        Assert.False(CombatMath.RollHit(alwaysHigh, 99));
        Assert.True(CombatMath.RollHit(alwaysHigh, 100));
    }

    [Fact]
    public void ScriptedRngDrivesDamageRollLowAndHigh()
    {
        int[] baseStats = new int[35];
        baseStats[CritterStat.MeleeDamage] = 4; // unarmed raw range 1..6
        baseStats[CritterStat.MaximumHitPoints] = 20;
        var proto = new Proto.CritterProtoStats(0, 0, 0, baseStats, new int[35], new int[18], 0, 0, 0, 0);
        var obj = new Map.MapObject
        {
            Id = 1,
            HexTile = 100,
            X = 0,
            Y = 0,
            Frame = 0,
            Rotation = 0,
            Fid = 0x01000000,
            Flags = 0,
            Pid = 0x01000001,
            Sid = -1,
        };
        obj.CurrentHp = 20;
        var state = new CritterState(obj, proto);

        // Next(1, 2+meleeDmg) i.e. [1, 7): a min/low draw yields 1, a clamped-high draw yields 6.
        Assert.Equal(1, CombatMath.RollDamage(new FixedCombatRng(1), state, state));
        Assert.Equal(6, CombatMath.RollDamage(new FixedCombatRng(1000), state, state));
    }

    /// <summary>Test double: returns a fixed value clamped into the requested
    /// [min, max) range, mimicking System.Random's bounds contract.</summary>
    private sealed class FixedCombatRng(int value) : ICombatRng
    {
        public int Next(int minInclusive, int maxExclusive) =>
            Math.Clamp(value, minInclusive, maxExclusive - 1);
    }
}
