using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// P34-M4: the combat-outline classification (CombatOutline.TypeFor), a port of combat.cc
/// _combat_update_critter_outline_for_los. Clear LoS → team color (friendly/hostile); blocked LoS →
/// the perception-only outline within PE×5 (halved through glass), else none.
/// </summary>
public class CombatOutlineTests
{
    [Fact]
    public void ClearLosSameTeamIsFriendly() =>
        Assert.Equal(OutlineType.Friendly,
            CombatOutline.TypeFor(clearLos: true, dudeTeam: 0, critterTeam: 0, dist: 5, dudePerception: 7, critterIsGlass: false));

    [Fact]
    public void ClearLosDifferentTeamIsHostile() =>
        Assert.Equal(OutlineType.Hostile,
            CombatOutline.TypeFor(clearLos: true, dudeTeam: 0, critterTeam: 1, dist: 5, dudePerception: 7, critterIsGlass: false));

    [Fact]
    public void BlockedLosWithinPerceptionIsPerceptionOnly() =>
        // PE 7 → reach 35; dist 30 ≤ 35.
        Assert.Equal(OutlineType.Perception,
            CombatOutline.TypeFor(clearLos: false, dudeTeam: 0, critterTeam: 1, dist: 30, dudePerception: 7, critterIsGlass: false));

    [Fact]
    public void BlockedLosBeyondPerceptionIsNone() =>
        // PE 7 → reach 35; dist 40 > 35.
        Assert.Equal(OutlineType.None,
            CombatOutline.TypeFor(clearLos: false, dudeTeam: 0, critterTeam: 1, dist: 40, dudePerception: 7, critterIsGlass: false));

    [Fact]
    public void GlassHalvesThePerceptionReach() =>
        // PE 7 → reach 35, halved to 17 through glass; dist 35 > 17 → none.
        Assert.Equal(OutlineType.None,
            CombatOutline.TypeFor(clearLos: false, dudeTeam: 0, critterTeam: 1, dist: 35, dudePerception: 7, critterIsGlass: true));

    [Fact]
    public void PaletteIndicesMatchTheEngineSeeds()
    {
        Assert.Equal(243, CombatOutline.PaletteIndex(OutlineType.Hostile));
        Assert.Equal(229, CombatOutline.PaletteIndex(OutlineType.Friendly));
        Assert.Equal(61, CombatOutline.PaletteIndex(OutlineType.Perception));
        Assert.Equal(-1, CombatOutline.PaletteIndex(OutlineType.None));
    }
}
