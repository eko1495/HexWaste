using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

public class AiHealingTests
{
    [Theory]
    [InlineData(40, true)]   // Stimpak
    [InlineData(144, true)]  // Super Stimpak
    [InlineData(273, true)]  // Healing Powder
    [InlineData(259, false)] // Jet — a chem, not a healing item
    [InlineData(8, false)]   // 10mm pistol
    public void IsHealingItemMatchesTheEngineList(int pid, bool healing)
    {
        Assert.Equal(healing, AiHealing.IsHealingItem(pid));
    }

    [Theory]
    [InlineData(0, 0)]   // clean — never heals
    [InlineData(1, 60)]  // stims_when_hurt_little
    [InlineData(2, 30)]  // stims_when_hurt_lots
    [InlineData(3, 50)]  // sometimes → default heal threshold
    [InlineData(4, 50)]  // anytime
    [InlineData(5, 50)]  // always
    public void HealHpRatioMatchesTheEngine(int chemUse, int ratio)
    {
        Assert.Equal(ratio, AiHealing.HealHpRatio(chemUse));
    }
}
