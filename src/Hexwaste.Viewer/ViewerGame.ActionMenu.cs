using Hexwaste.Formats;
using Hexwaste.Formats.Map;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Hexwaste.Viewer;

// P82-M6: the FO2 right-click action menu — a vertical stack of 40x40 action icons next to the
// cursor (Look / Talk / Use / Use Skill / Cancel), ported from fallout2-ce src/game_mouse.cc.
// Pure move-friendly partial; the build logic is in Formats.Map.ActionMenu, the dispatch routes to
// the existing Examine / InteractWith / Skilldex handlers.
public sealed partial class ViewerGame
{
    private const int ActionIconSize = 40;

    private MapObject? _actionMenuObj;
    private List<ActionMenuItem> _actionMenuItems = [];
    private Point _actionMenuPos;
    internal bool _debugForceActionMenu; // harness: hold the menu open (skip Update's click-close) for a screenshot
    private MapObject? _actionSkillTarget; // the menu's "Use Skill" target (applied when a skill is picked)
    private readonly Dictionary<ActionMenuItem, (Texture2D? N, Texture2D? H)> _actionIcons = [];
    private bool _actionIconsTried;

    /// <summary>The interface FRM base name for an action's icon (N = normal, H = highlight at id-1).</summary>
    private static string ActionIconBase(ActionMenuItem item) => item switch
    {
        ActionMenuItem.Look => "LOOK",
        ActionMenuItem.Talk => "TALK",
        ActionMenuItem.Use => "USEGET",
        ActionMenuItem.UseSkill => "SKILL",
        _ => "CANCEL",
    };

    private (Texture2D? N, Texture2D? H) ActionIcon(ActionMenuItem item)
    {
        if (!_actionIconsTried)
        {
            _actionIconsTried = true;
            foreach (ActionMenuItem it in (ActionMenuItem[])[ActionMenuItem.Look, ActionMenuItem.Talk,
                ActionMenuItem.Use, ActionMenuItem.UseSkill, ActionMenuItem.Cancel])
            {
                string b = ActionIconBase(it);
                _actionIcons[it] = (
                    InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, $@"art\intrface\{b}N.frm"),
                    InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, $@"art\intrface\{b}H.frm"));
            }
        }
        return _actionIcons.GetValueOrDefault(item);
    }

    /// <summary>Open the action menu for <paramref name="obj"/> at the cursor — the item list is the
    /// engine's per-object-type build (Formats.Map.ActionMenu), clamped on screen.</summary>
    private void OpenActionMenu(MapObject obj, int mx, int my)
    {
        if (_dude is null)
            return;
        ObjectType type = Fid.Type(obj.Fid);
        bool isDude = obj == _dude.Dude;
        bool activeCritter = type == ObjectType.Critter && !obj.IsDead;
        bool canTalk = activeCritter && !isDude;                          // alive non-dude critter (assumed talkable)
        bool inCombat = _combat.Phase != Formats.Combat.CombatPhase.Idle;
        bool isContainer = IsContainer(obj) || (type == ObjectType.Item && obj.Inventory.Count > 0);
        bool sceneryCanUse = type == ObjectType.Scenery;                  // InteractWith routes (door/use_p_proc/no-op)
        _actionMenuItems = ActionMenu.Build(type, isDude, activeCritter, canTalk, inCombat, sceneryCanUse, isContainer);
        _actionMenuObj = obj;

        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int h = _actionMenuItems.Count * ActionIconSize;
        _actionMenuPos = new Point(
            Math.Clamp(mx, 0, Math.Max(0, vp.Width - ActionIconSize)),
            Math.Clamp(my, 0, Math.Max(0, vp.Height - h)));
    }

    private void CloseActionMenu() => _actionMenuObj = null;

    /// <summary>The menu row under (mx,my), or -1 (the SkilldexRowAt geometry pattern).</summary>
    private int ActionMenuRowAt(int mx, int my)
    {
        if (_actionMenuObj is null || mx < _actionMenuPos.X || mx >= _actionMenuPos.X + ActionIconSize)
            return -1;
        int row = (my - _actionMenuPos.Y) / ActionIconSize;
        return row >= 0 && row < _actionMenuItems.Count ? row : -1;
    }

    /// <summary>Perform the menu's <paramref name="index"/> item via the existing handlers, then close.</summary>
    private void DispatchActionMenu(int index)
    {
        if (_actionMenuObj is not { } obj || index < 0 || index >= _actionMenuItems.Count)
            return;
        ActionMenuItem item = _actionMenuItems[index];
        CloseActionMenu();
        switch (item)
        {
            case ActionMenuItem.Look:
                Examine(obj);
                break;
            case ActionMenuItem.Talk:
            case ActionMenuItem.Use: // talk/loot/open/use_p_proc — InteractWith routes by object type
                // P111: the hand/talk icons approach an out-of-range target first (walk-to-then-
                // interact), same as a plain left click — the user shouldn't have to walk manually.
                InteractOrApproach(obj);
                break;
            case ActionMenuItem.UseSkill:
                _actionSkillTarget = obj;
                _skilldexOpen = true;
                break;
            case ActionMenuItem.Cancel:
                break;
        }
    }

    /// <summary>Render the action-menu icon stack (the hovered row uses the H/highlight art), falling
    /// back to text labels when the icon FRMs are absent (the Skilldex text-then-art pattern).</summary>
    private void DrawActionMenu()
    {
        if (_actionMenuObj is null)
            return;
        MouseState m = Mouse.GetState();
        int hover = ActionMenuRowAt(m.X, m.Y);
        for (int i = 0; i < _actionMenuItems.Count; i++)
        {
            (Texture2D? n, Texture2D? h) = ActionIcon(_actionMenuItems[i]);
            var pos = new Vector2(_actionMenuPos.X, _actionMenuPos.Y + i * ActionIconSize);
            Texture2D? tex = (i == hover ? h : n) ?? n;
            if (tex is not null)
                _spriteBatch.Draw(tex, pos, Color.White);
            else if (_fontRenderer is not null)
            {
                _panelPixel ??= CreatePixel();
                _spriteBatch.Draw(_panelPixel, new Rectangle((int)pos.X, (int)pos.Y, 90, ActionIconSize),
                    new Color(8, 8, 8, 230));
                _fontRenderer.Draw(_spriteBatch, _actionMenuItems[i].ToString(), pos + new Vector2(4, 14),
                    i == hover ? new Color(252, 252, 84) : new Color(0, 252, 0));
            }
        }
    }
}
