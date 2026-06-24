using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>P75-M2: ammo-box stacking consolidation (item.cc:371 itemAdd). A stack is (StackCount-1)
/// full boxes + 1 partial holding AmmoQuantity rounds; merging must consolidate, not invent rounds.</summary>
public class AmmoStackTests
{
    [Fact]
    public void TotalRoundsCountsFullBoxesPlusThePartialTop()
    {
        Assert.Equal(12, AmmoStack.TotalRounds(1, 12, 24));   // one partial box
        Assert.Equal(36, AmmoStack.TotalRounds(2, 12, 24));   // one full + one partial(12)
        Assert.Equal(48, AmmoStack.TotalRounds(2, 24, 24));   // two full
        Assert.Equal(24, AmmoStack.TotalRounds(1, -1, 24));   // a pristine box (-1) reads as full
    }

    [Theory]
    [InlineData(24, 24, 1, 24)]   // exact multiple → all full, top box = capacity
    [InlineData(36, 24, 2, 12)]   // 1 full + 1 partial(12)
    [InlineData(12, 24, 1, 12)]   // a single partial box
    [InlineData(60, 24, 3, 12)]   // 2 full + 1 partial(12)
    public void FromTotalReBoxes(int total, int capacity, int stack, int qty) =>
        Assert.Equal((stack, qty), AmmoStack.FromTotal(total, capacity));

    [Fact]
    public void MergeConsolidatesInsteadOfInventingRounds()
    {
        // THE BUG: two 12-round boxes (24-cap) must be ONE full box (24), NOT "1 full + 1 partial" (36).
        Assert.Equal((1, 24), AmmoStack.Merge(1, 12, 1, 12, 24));
        // a partial + a pristine full (-1) → 36 = 1 full + 1 partial(12).
        Assert.Equal((2, 12), AmmoStack.Merge(1, 12, 1, -1, 24));
        // a 2-box stack (1 full + a full top) + a 12-round box → 60 = 2 full + 1 partial(12).
        Assert.Equal((3, 12), AmmoStack.Merge(2, 24, 1, 12, 24));
        // two full boxes stay two full boxes.
        Assert.Equal((2, 24), AmmoStack.Merge(1, 24, 1, 24, 24));
    }
}
