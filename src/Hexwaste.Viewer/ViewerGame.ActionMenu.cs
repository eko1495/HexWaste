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
        ActionMenuItem.Push => "PUSH", // P113 (item 6)
        _ => "CANCEL",
    };

    private (Texture2D? N, Texture2D? H) ActionIcon(ActionMenuItem item)
    {
        if (!_actionIconsTried)
        {
            _actionIconsTried = true;
            foreach (ActionMenuItem it in (ActionMenuItem[])[ActionMenuItem.Look, ActionMenuItem.Talk,
                ActionMenuItem.Use, ActionMenuItem.UseSkill, ActionMenuItem.Push, ActionMenuItem.Cancel])
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
        bool canPush = activeCritter && !isDude && CanPushCritter(obj);   // P113 (item 6)
        _actionMenuItems = ActionMenu.Build(type, isDude, activeCritter, canTalk, inCombat, sceneryCanUse, isContainer, canPush);
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
            case ActionMenuItem.Push:
                PushCritter(obj);
                break;
            case ActionMenuItem.Cancel:
                break;
        }
    }

    /// <summary>P113 (item 6): actionCheckPush (actions.cc:1996-2037) — a live non-dude critter whose
    /// script DEFINES push_p_proc, within talk reach (distance ≤ 12; the sight-path half of
    /// _action_can_talk_to is a documented cut), and in combat neither (same-team AND the dude's own
    /// last attacker) nor (its attacker being on the dude's team).</summary>
    private bool CanPushCritter(MapObject target)
    {
        if (_dude is null || _scriptHost is null)
            return false;
        if (Formats.Hex.HexGrid.Distance(_dude.Dude.HexTile, target.HexTile) > 12)
            return false;
        if (!_scriptHost.ObjectHasProc(target, _map, "push_p_proc"))
            return false;
        if (_combat.Phase != Formats.Combat.CombatPhase.Idle)
        {
            MapObject dude = _dude.Dude;
            if (target.Team == dude.Team && ReferenceEquals(target, dude.WhoHitMe))
                return false;
            if (target.WhoHitMe is { } attacker && attacker.Team == dude.Team)
                return false;
        }
        return true;
    }

    /// <summary>P113 (item 6): actionPush (actions.cc:2040-2108) — run the target's push_p_proc
    /// (source = dude, self = target; script_overrides aborts the shove), then move it one hex in
    /// the first unblocked of rotations rotationTo+{0,1,5,2,4,3}; all blocked = no-op. fo2ce walks
    /// the critter there (AP-limited in combat) — we walk via StartNpcWalk and fall back to a
    /// direct placement when the critter has no walk art.</summary>
    private void PushCritter(MapObject target)
    {
        if (_dude is null)
            return;
        var scripted = _scriptHost?.RunObjectProc(target, _map, _dude.Dude, "push_p_proc");
        if (scripted is not null)
            foreach (string line in scripted.Messages)
                Log(line);
        if (scripted is { Overridden: true })
            return;

        int rotation = Formats.Hex.HexGrid.RotationTo(_dude.Dude.HexTile, target.HexTile);
        foreach (int offset in (int[])[0, 1, 5, 2, 4, 3])
        {
            int tile = Formats.Hex.HexGrid.TileInDirection(target.HexTile, (rotation + offset) % 6);
            if (_blockedTiles.Contains(tile))
                continue;
            if (!StartNpcWalk(target, tile))
                PlaceCritter(target, tile); // no walk art — the Shove-style direct placement
            return;
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
