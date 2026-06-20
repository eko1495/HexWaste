using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Map;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Hexwaste.Viewer;

// P50: the companion combat-control / AI-disposition window (the engine's
// game_dialog.cc:3354 partyMemberControlWindowInit). Opened from the companion hub
// ("Set tactics."); each row cycles a setting that the ally AI (CombatEngine.TryAllyAction)
// actually HONOURS — disposition (presets the rest), attack-who (target priority), distance
// (stay-close / charge / hold), run-away (flee threshold), use-chems (heal when hurt).
// DOCUMENTED RESIDUALS: the engine's area-attack + best-weapon rows are omitted (Hexwaste's
// allies don't burst, and ally best-weapon is a P43 residual); WhoeverAttackingMe degrades to
// Closest (no per-ally whoHitMe tracker); a dark text panel, not the authentic control.frm art.
public sealed partial class ViewerGame
{
    /// <summary>Per-companion combat-control settings (P50). Default = the pre-P50 ally behaviour.</summary>
    private readonly Dictionary<MapObject, CompanionAi> _companionAi = [];

    /// <summary>The companion whose combat-control window is open, or null.</summary>
    private MapObject? _tacticsMember;

    /// <summary>ICombatHost seam — the ally's settings (default = the byte-identical pre-P50 behaviour).</summary>
    public CompanionAi CompanionSettings(MapObject ally) => _companionAi.GetValueOrDefault(ally, CompanionAi.Default);

    /// <summary>Set a companion's settings (the harness + load path); null clears to the default.</summary>
    private void SetCompanionAi(MapObject member, CompanionAi ai)
    {
        if (ai == CompanionAi.Default)
            _companionAi.Remove(member);
        else
            _companionAi[member] = ai;
    }

    private void OpenTactics(MapObject member)
    {
        _tacticsMember = member;
        Console.WriteLine($"tactics: open for {ObjectName(member)} disposition={CompanionSettings(member).Disposition}");
    }

    private const int TacticsRowCount = 6; // 5 cycle-able settings + a Done row

    // Show the EFFECTIVE knobs (the resolved preset under a non-Custom disposition), so the window
    // reflects what the ally actually does — not stale stored values a preset overrides.
    private static string TacticsRowLabel(int row, CompanionAi ai)
    {
        CompanionAi e = ai.Effective();
        return row switch
        {
            0 => $"Disposition:  {ai.Disposition}",
            1 => $"Attack who:   {e.AttackWho}",
            2 => $"Distance:     {e.Distance}",
            3 => $"Run away at:  {e.RunAway}",
            4 => $"Use chems:    {e.ChemUse}",
            _ => "Done",
        };
    }

    // Cycling a detail row bakes the current EFFECTIVE settings into Custom (so it continues from the
    // preset, not a stale stored value), then bumps the one field. Row 0 cycles the disposition itself.
    private static CompanionAi CycleTacticsRow(int row, CompanionAi ai)
    {
        if (row == 0)
            return ai with { Disposition = NextEnum(ai.Disposition) };
        CompanionAi c = ai.Effective() with { Disposition = Disposition.Custom };
        return row switch
        {
            1 => c with { AttackWho = NextEnum(c.AttackWho) },
            2 => c with { Distance = NextEnum(c.Distance) },
            3 => c with { RunAway = NextEnum(c.RunAway) },
            4 => c with { ChemUse = NextEnum(c.ChemUse) },
            _ => ai,
        };
    }

    private static T NextEnum<T>(T value) where T : struct, Enum
    {
        T[] vals = Enum.GetValues<T>();
        return vals[(Array.IndexOf(vals, value) + 1) % vals.Length];
    }

    private Rectangle TacticsPanelRect()
    {
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int lh = (_fontRenderer?.LineHeight ?? 16) + 8;
        int w = 440, h = (TacticsRowCount + 2) * lh + 12;
        return new Rectangle(Math.Max(0, (vp.Width - w) / 2), Math.Max(0, (vp.Height - h) / 2), w, h);
    }

    private Rectangle TacticsRowRect(int row)
    {
        Rectangle p = TacticsPanelRect();
        int lh = (_fontRenderer?.LineHeight ?? 16) + 8;
        return new Rectangle(p.X + 8, p.Y + 10 + lh + row * lh, p.Width - 16, lh);
    }

    private int TacticsRowAt(int mx, int my)
    {
        for (int i = 0; i < TacticsRowCount; i++)
            if (TacticsRowRect(i).Contains(mx, my))
                return i;
        return -1;
    }

    private void TacticsActivate(int row)
    {
        if (_tacticsMember is not { } member)
            return;
        if (row == TacticsRowCount - 1) // Done
        {
            _tacticsMember = null;
            return;
        }
        if (row >= 0 && row < TacticsRowCount - 1)
            SetCompanionAi(member, CycleTacticsRow(row, CompanionSettings(member)));
    }

    private void HandleTacticsInput(MouseState mouse, KeyboardState keyboard)
    {
        if (IsKeyPressed(keyboard, Keys.Escape))
        {
            _tacticsMember = null;
            return;
        }
        for (int i = 0; i < TacticsRowCount; i++)
            if (IsKeyPressed(keyboard, Keys.D1 + i) || IsKeyPressed(keyboard, Keys.NumPad1 + i))
            {
                TacticsActivate(i);
                return;
            }
        if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
            && TacticsRowAt(mouse.X, mouse.Y) is int r && r >= 0)
            TacticsActivate(r);
    }

    private void DrawTactics()
    {
        if (_tacticsMember is not { } member || _fontRenderer is null)
            return;
        _panelPixel ??= CreatePixel();
        Rectangle p = TacticsPanelRect();
        _spriteBatch.Draw(_panelPixel, p, new Color(8, 16, 8, 240));
        var green = new Color(0, 252, 0);
        var hot = new Color(252, 252, 84);
        CompanionAi ai = CompanionSettings(member);
        _fontRenderer.Draw(_spriteBatch, $"COMBAT CONTROL - {ObjectName(member)} (1-6 / click cycles, Esc done)",
            new Vector2(p.X + 12, p.Y + 8), Color.LightGray);
        int hovered = TacticsRowAt(Mouse.GetState().X, Mouse.GetState().Y);
        for (int i = 0; i < TacticsRowCount; i++)
        {
            Rectangle rr = TacticsRowRect(i);
            _fontRenderer.Draw(_spriteBatch, $"{i + 1}. {TacticsRowLabel(i, ai)}",
                new Vector2(rr.X + 6, rr.Y + 2), i == hovered ? hot : green);
        }
    }
}
