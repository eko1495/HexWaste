using Hexwaste.Formats.Item;
using Xunit;

namespace Hexwaste.Formats.Tests;

/// <summary>P: timed placeable explosives (Dynamite 51 / Plastic 85) — the item-side table used by the
/// viewer's arm-and-detonate path (ported from item.cc explosiveIsExplosive/Activate/GetDamage).</summary>
public class ExplosivesTests
{
    [Theory]
    [InlineData(51, true)]   // Dynamite
    [InlineData(85, true)]   // Plastic Explosives
    [InlineData(25, false)]  // Frag grenade — a WEAPON, thrown not placed
    [InlineData(206, false)] // already-armed dynamite is not re-armable via "use"
    public void OnlyDynamiteAndPlasticAreTimedExplosives(int pid, bool expected) =>
        Assert.Equal(expected, Explosives.IsExplosive(pid));

    [Fact]
    public void ActivateSwapsToTheArmedProto()
    {
        Assert.Equal(206, Explosives.Activate(51));   // dynamite → armed dynamite
        Assert.Equal(209, Explosives.Activate(85));   // plastic → armed plastic
        Assert.Equal(25, Explosives.Activate(25));    // non-explosive unchanged
    }

    [Fact]
    public void DamageMatchesTheProtoTable()
    {
        Assert.Equal((30, 50), Explosives.Damage(51));   // dynamite
        Assert.Equal((30, 50), Explosives.Damage(206));  // armed dynamite same
        Assert.Equal((40, 80), Explosives.Damage(85));   // plastic (the bigger blast)
        Assert.Equal((40, 80), Explosives.Damage(209));  // armed plastic same
    }
}
