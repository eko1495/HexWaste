using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// The NPC non-healing combat-drug decision (P78-M2), ported from combat_ai.cc _ai_check_drugs.
/// </summary>
public class AiCombatDrugTests
{
    private sealed class SeqRng(params int[] values) : ICombatRng
    {
        private int _i;
        public int Next(int minInclusive, int maxExclusive) =>
            Math.Clamp(values[Math.Min(_i++, values.Length - 1)], minInclusive, maxExclusive - 1);
    }

    // A rng that fails the test if anything draws from it (proves the short-circuit).
    private sealed class NeverRng : ICombatRng
    {
        public int Next(int minInclusive, int maxExclusive) => throw new Xunit.Sdk.XunitException("drew rng");
    }

    [Theory]
    [InlineData(0, 0, 0)]   // clean
    [InlineData(1, 0, 0)]   // stims_when_hurt_little — healing mode, no combat drug
    [InlineData(3, 0, 25)]  // sometimes, turn 0 (0 % 3 == 0)
    [InlineData(3, 1, 0)]   // sometimes, off-turn
    [InlineData(4, 3, 75)]  // anytime, turn 3 (3 % 3 == 0)
    [InlineData(4, 4, 0)]   // anytime, off-turn
    [InlineData(5, 1, 100)] // always — every turn
    public void UseChanceMatchesTheEngineTable(int chemUse, int turns, int expected) =>
        Assert.Equal(expected, AiCombatDrug.UseChance(chemUse, turns));

    [Fact]
    public void ShouldUseShortCircuitsWithoutDrawingWhenTheChanceIsZero()
    {
        Assert.False(AiCombatDrug.ShouldUse(0, 0, new NeverRng())); // clean → no draw
        Assert.False(AiCombatDrug.ShouldUse(3, 1, new NeverRng())); // sometimes off-turn → no draw
    }

    [Fact]
    public void ShouldUseDrawsAndComparesWhenTheChanceIsPositive()
    {
        Assert.True(AiCombatDrug.ShouldUse(5, 0, new SeqRng(50)));   // 50 < 100 (always)
        Assert.False(AiCombatDrug.ShouldUse(3, 0, new SeqRng(50)));  // 50 < 25? no
        Assert.True(AiCombatDrug.ShouldUse(3, 0, new SeqRng(10)));   // 10 < 25? yes
    }

    [Fact]
    public void MaxPerTurnCapsByMode()
    {
        Assert.Equal(1, AiCombatDrug.MaxPerTurn(3));               // sometimes: 1
        Assert.Equal(2, AiCombatDrug.MaxPerTurn(4));               // anytime: 2
        Assert.Equal(int.MaxValue, AiCombatDrug.MaxPerTurn(5));    // always: AP-limited
    }

    [Fact]
    public void PickPrefersAPrimaryDesireDrugThenAnyNonHealing()
    {
        // 40 = stimpak (healing — filtered out); 33 Jet, 24 Psycho. Desire lists Psycho first.
        Assert.Equal(24, AiCombatDrug.Pick([40, 33, 24], primaryDesire: [24, 33]));
        Assert.Equal(33, AiCombatDrug.Pick([40, 33, 24], primaryDesire: [999])); // no desire match → first non-healing
        Assert.Equal(33, AiCombatDrug.Pick([40, 33], primaryDesire: null));       // no desire → first non-healing
        Assert.Equal(-1, AiCombatDrug.Pick([40], primaryDesire: [33]));           // only a healing item → none
        Assert.Equal(-1, AiCombatDrug.Pick([], primaryDesire: [33]));             // empty bag → none
    }

    [Fact]
    public void ChemPrimaryDesireParsesAsAnIntList()
    {
        AiPacket p = AiPacketTable.Parse("""
            [Junkie]
            packet_num=200
            chem_use=always
            chem_primary_desire=33,24,0
            """).Get(200)!;
        Assert.Equal(5, p.ChemUse);
        Assert.Equal([33, 24, 0], p.ChemPrimaryDesire);
    }
}
