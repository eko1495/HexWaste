using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

public class CombatDifficultyTests
{
    [Theory]
    [InlineData(GameDifficulty.Easy, 75)]
    [InlineData(GameDifficulty.Normal, 100)]
    [InlineData(GameDifficulty.Hard, 125)]
    public void DamageModifierMapsEachDifficulty(GameDifficulty difficulty, int expected) =>
        Assert.Equal(expected, CombatDifficulty.DamageModifier(difficulty));
}
