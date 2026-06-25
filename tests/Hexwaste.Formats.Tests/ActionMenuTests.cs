using Hexwaste.Formats;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>P82-M6: the right-click action-menu item builder, ported from fallout2-ce
/// src/game_mouse.cc the LEFT_BUTTON_DOWN_REPEAT switch (the MVP verbs).</summary>
public class ActionMenuTests
{
    static ActionMenuItem[] Build(ObjectType type, bool isDude = false, bool active = false,
        bool talk = false, bool combat = false, bool canUse = false, bool container = false) =>
        [.. ActionMenu.Build(type, isDude, active, talk, combat, canUse, container)];

    [Fact]
    public void AliveTalkableCritterOutOfCombatOffersTalk() =>
        Assert.Equal([ActionMenuItem.Talk, ActionMenuItem.Look, ActionMenuItem.UseSkill, ActionMenuItem.Cancel],
            Build(ObjectType.Critter, active: true, talk: true));

    [Fact]
    public void TalkableCritterInCombatDropsTalk() => // combat -> Look, not Talk
        Assert.Equal([ActionMenuItem.Look, ActionMenuItem.UseSkill, ActionMenuItem.Cancel],
            Build(ObjectType.Critter, active: true, talk: true, combat: true));

    [Fact]
    public void DeadCritterOffersUseToLoot() => // corpse: !active -> Use
        Assert.Equal([ActionMenuItem.Use, ActionMenuItem.Look, ActionMenuItem.UseSkill, ActionMenuItem.Cancel],
            Build(ObjectType.Critter, active: false, talk: false));

    [Fact]
    public void TheDudeHasNoTalkOrUse() =>
        Assert.Equal([ActionMenuItem.Look, ActionMenuItem.UseSkill, ActionMenuItem.Cancel],
            Build(ObjectType.Critter, isDude: true, active: true));

    [Fact]
    public void UsableSceneryOffersUse() =>
        Assert.Equal([ActionMenuItem.Use, ActionMenuItem.Look, ActionMenuItem.UseSkill, ActionMenuItem.Cancel],
            Build(ObjectType.Scenery, canUse: true));

    [Fact]
    public void NonUsableSceneryIsLookOnly() =>
        Assert.Equal([ActionMenuItem.Look, ActionMenuItem.UseSkill, ActionMenuItem.Cancel],
            Build(ObjectType.Scenery, canUse: false));

    [Fact]
    public void PlainItemIsUseLookCancel() =>
        Assert.Equal([ActionMenuItem.Use, ActionMenuItem.Look, ActionMenuItem.Cancel],
            Build(ObjectType.Item));

    [Fact]
    public void ContainerItemAddsUseSkill() =>
        Assert.Equal([ActionMenuItem.Use, ActionMenuItem.Look, ActionMenuItem.UseSkill, ActionMenuItem.Cancel],
            Build(ObjectType.Item, container: true));

    [Fact]
    public void WallIsLookCancel() =>
        Assert.Equal([ActionMenuItem.Look, ActionMenuItem.Cancel], Build(ObjectType.Wall));

    [Fact]
    public void CancelIsAlwaysLast()
    {
        foreach (ObjectType t in (ObjectType[])[ObjectType.Item, ObjectType.Critter, ObjectType.Scenery, ObjectType.Wall])
            Assert.Equal(ActionMenuItem.Cancel, Build(t, active: true, talk: true, canUse: true)[^1]);
    }
}
