using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Map;
using Xunit;

namespace Hexwaste.Formats.Tests;

/// <summary>P113: the pure tables/math added in the fo2ce-fidelity batch (radiation bands, elevator
/// start-button fixup, the scope/long-range to-hit range term).</summary>
public class P113FidelityTests
{
    [Theory]
    [InlineData(50, 0)]   // ≤ 99 → NONE
    [InlineData(99, 0)]   // boundary: 99 is NOT > 99 → NONE (critter.cc:515)
    [InlineData(100, 1)]  // 100 > 99 → MINOR
    [InlineData(101, 1)]  // MINOR
    [InlineData(200, 2)]  // ADVANCED
    [InlineData(400, 3)]  // CRITICAL
    [InlineData(600, 4)]  // DEADLY
    [InlineData(1000, 5)] // FATAL
    public void RadiationBandFromCounter(int rad, int expectedLevel) =>
        Assert.Equal(expectedLevel, RadiationTables.CounterToLevel(rad));

    [Fact]
    public void RadiationPenaltyTableIndexedByLevelMinusOne()
    {
        // MINOR (level 1) → row idx 0 = all-zero; the first real penalty is at ADVANCED (row idx 1).
        Assert.All(RadiationTables.EffectPenalties[0], v => Assert.Equal(0, v));
        Assert.Equal(-1, RadiationTables.EffectPenalties[1][0]); // ADVANCED STR
        // FATAL row (idx 4) — verbatim critter.cc values incl. CUR_HP -15.
        Assert.Equal(new[] { -4, -3, -3, -3, -1, -5, -15, -10 }, RadiationTables.EffectPenalties[4]);
        Assert.Equal(35, RadiationTables.EffectStats[6]);  // CURRENT_HIT_POINTS
        Assert.Equal(14, RadiationTables.EffectStats[7]);  // HEALING_RATE
        Assert.Equal(6, RadiationTables.PrimaryStatCount);
    }

    [Fact]
    public void ElevatorKlamathToxicCavesHasTwoFloors()
    {
        Assert.Equal(13, ElevatorTables.KlamathToxicCaves);
        Assert.Equal(2, ElevatorTables.Levels[13]);
        // The two destinations are both map 12 (klatoxcv), elevations 1 and 2 (elevator.cc:123).
        Assert.Equal((12, 1, 16052), ElevatorTables.Descriptions[13][0]);
        Assert.Equal((12, 2, 14480), ElevatorTables.Descriptions[13][1]);
    }

    [Fact]
    public void ElevatorCurrentButtonClampsToValidRange()
    {
        // A 2-floor elevator (Klamath) can only highlight button 0 or 1.
        int b = ElevatorTables.CurrentButton(13, currentMap: 12, startLevel: 1);
        Assert.InRange(b, 0, 1);
    }

    [Fact]
    public void ElevatorBackgroundsTableMatchesTheEngine()
    {
        // P119: gElevatorBackgrounds (elevator.cc:65) — one (background, panel) pair per type,
        // parallel to Levels/Descriptions/LevelLabels. Spot rows: 0 = bare BoS column (143,−1),
        // 1 = BoS surface with the G/1 panel (143,150), 12 = the Sierra service lift's own art
        // (388,−1); shared button/gauge FRMs (elevator.cc:58).
        Assert.Equal(ElevatorTables.Levels.Length, ElevatorTables.Backgrounds.Length);
        Assert.Equal((143, -1), ElevatorTables.Backgrounds[0]);
        Assert.Equal((143, 150), ElevatorTables.Backgrounds[1]);
        Assert.Equal((388, -1), ElevatorTables.Backgrounds[12]);
        Assert.Equal((143, 150), ElevatorTables.Backgrounds[13]); // Klamath toxic caves (G/1)
        Assert.Equal(141, ElevatorTables.ButtonDownFrmId);
        Assert.Equal(142, ElevatorTables.ButtonUpFrmId);
        Assert.Equal(149, ElevatorTables.GaugeFrmId);
        Assert.Equal(13, ElevatorTables.GaugeSlices);
    }

    [Fact]
    public void ScopeRangePerkPenalizesInsideMinRange()
    {
        // SCOPE_RANGE: dist < 8 → dist += 8 (a close shot is penalized), instead of the PE bonus.
        // Compare a close scoped shot vs a plain shot: the scoped one must be worse at short range.
        int plain = RangedMath.ToHitChance(100, distance: 2, perception: 6, attackerIsDude: true,
            targetAc: 0, ammoAcModifier: 0, weaponMinStrength: 0, attackerStrength: 10, crittersInPath: 0);
        int scopedClose = RangedMath.ToHitChance(100, distance: 2, perception: 6, attackerIsDude: true,
            targetAc: 0, ammoAcModifier: 0, weaponMinStrength: 0, attackerStrength: 10, crittersInPath: 0,
            perkRangeMult: 5, perkMinRange: 8);
        Assert.True(scopedClose < plain, "scoped weapon is worse than a plain weapon at point-blank range");
    }

    [Fact]
    public void LongRangePerkHelpsAtDistance()
    {
        // LONG_RANGE mult 4 vs default 2 → a bigger distance discount for the dude (PE-2).
        int plain = RangedMath.ToHitChance(100, distance: 30, perception: 8, attackerIsDude: true,
            targetAc: 0, ammoAcModifier: 0, weaponMinStrength: 0, attackerStrength: 10, crittersInPath: 0);
        int longRange = RangedMath.ToHitChance(100, distance: 30, perception: 8, attackerIsDude: true,
            targetAc: 0, ammoAcModifier: 0, weaponMinStrength: 0, attackerStrength: 10, crittersInPath: 0,
            perkRangeMult: 4, perkMinRange: 0);
        Assert.True(longRange > plain, "long-range weapon has a smaller distance penalty far out");
    }
}
