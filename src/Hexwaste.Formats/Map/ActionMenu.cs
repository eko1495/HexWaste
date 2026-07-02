namespace Hexwaste.Formats.Map;

/// <summary>The FO2 right-click action-menu verbs (values == the engine's
/// GameMouseActionMenuItem so a probe/log is engine-comparable). The MVP wires
/// Look/Talk/Use/UseSkill/Push/Cancel; Drop(1)/Inventory(2)/Rotate(4)/Unload(7)
/// are deferred (no Hexwaste handler).</summary>
public enum ActionMenuItem
{
    Cancel = 0,
    Look = 3,
    Talk = 5,
    Use = 6,
    UseSkill = 8,
    Push = 9, // P113 (item 6): push_p_proc — the only content-rich minor proc (124 scripts define it)
}

/// <summary>Builds the ordered action-menu item list for an object, ported from
/// fallout2-ce src/game_mouse.cc the LEFT_BUTTON_DOWN_REPEAT menu builder (~1070-1121).
/// Cancel is always last (the engine invariant). The deferred verbs (Inventory/Rotate/
/// Push) are dropped from each branch; everything else is a verbatim mirror.</summary>
public static class ActionMenu
{
    public static List<ActionMenuItem> Build(
        ObjectType type, bool isDude, bool isActiveCritter, bool canTalk, bool inCombat,
        bool sceneryCanUse, bool isContainer, bool canPush = false)
    {
        var m = new List<ActionMenuItem>(4);
        switch (type)
        {
            case ObjectType.Item:
                m.Add(ActionMenuItem.Use);   // get / use
                m.Add(ActionMenuItem.Look);
                if (isContainer)
                    m.Add(ActionMenuItem.UseSkill);
                break;
            case ObjectType.Critter:
                if (!isDude)
                {
                    // can_talk -> Talk (Look in combat); else the corpse/non-talker loots/steals via Use.
                    if (canTalk && !inCombat)
                        m.Add(ActionMenuItem.Talk);
                    else if (!isActiveCritter)
                        m.Add(ActionMenuItem.Use); // corpse -> loot
                }
                // P113: PUSH before LOOK (game_mouse.cc:1096-1098 — actionCheckPush gates it).
                if (canPush)
                    m.Add(ActionMenuItem.Push);
                m.Add(ActionMenuItem.Look);
                m.Add(ActionMenuItem.UseSkill);
                break;
            case ObjectType.Scenery:
                if (sceneryCanUse)
                    m.Add(ActionMenuItem.Use);
                m.Add(ActionMenuItem.Look);
                m.Add(ActionMenuItem.UseSkill);
                break;
            default: // Wall / Misc — examine only
                m.Add(ActionMenuItem.Look);
                break;
        }
        m.Add(ActionMenuItem.Cancel);
        return m;
    }
}
