using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Hexwaste.Viewer;

// P130 (gap batch B): the Preferences screen — the authentic PREFSCRN window (interface FRM
// 240) reached from the Options menu. Renders every setting's section title + current value
// from preferences.msg at runtime with the slider knob (241/247) on its track; a click cycles
// a discrete setting or sets a continuous one, and DEFAULT/DONE/CANCEL commit or revert.
// The BACKED settings drive real behavior (difficulty, violence gore gate, always-run, the
// four volumes); the rest render, adjust, and persist for the session but are documented
// no-ops (Hexwaste has no combat-speed/text-delay/brightness/mouse system, and no config
// file — preferences reset each launch). ported from fallout2-ce src/preferences.cc.
public sealed partial class ViewerGame
{
    private bool _preferencesOpen;
    private bool _prefsFromMenu; // P139: opened from the Title main menu (OPTIONS) → close back to the menu, not the in-game options panel
    private readonly Formats.GamePreferences _preferences = new();
    private int[]? _preferencesSnapshot; // for CANCEL revert

    // Per-setting layout from preferences.cc, parallel to GamePreferences.Settings:
    // the TITLE position (_row1/2/3Ytab + the label x — col1 centered at 99, col2 at 206,
    // col3 at 384; :1026/1032/1037) and the KNOB slider (descriptor minX/knobY, :371-389).
    private readonly record struct PrefSlot(int TitleX, int TitleY, bool TitleCentered, int KnobX, int KnobY);
    private static readonly PrefSlot[] PrefLayout =
    [
        // col 1 (primary) — title centered at x=99
        new(99, 48, true, 76, 71), new(99, 125, true, 76, 149), new(99, 203, true, 76, 226),
        new(99, 286, true, 76, 309), new(99, 363, true, 76, 387),
        // col 2 (secondary) — title left at x=206
        new(206, 49, false, 299, 74), new(206, 116, false, 299, 141), new(206, 181, false, 299, 207),
        new(206, 247, false, 299, 271), new(206, 313, false, 299, 338), new(206, 380, false, 299, 404),
        // col 3 (range) — title left at x=384
        new(384, 19, false, 374, 50), new(384, 94, false, 374, 125), new(384, 165, false, 374, 196),
        new(384, 216, false, 374, 247), new(384, 268, false, 374, 298), new(384, 319, false, 374, 349),
        new(384, 369, false, 374, 400), new(384, 420, false, 374, 451),
    ];
    private const int PrefTrackWidth = 96;

    private const int PrefWindowFrm = 240;   // prefscrn.frm
    private const int PrefKnobOffFrm = 241;  // prfsldof.frm — the slider knob
    private const int PrefDefaultMsg = 120, PrefDoneMsg = 4, PrefCancelMsg = 121;

    private Point PrefWindowPos()
    {
        Viewport vp = GraphicsDevice.Viewport;
        Texture2D? bg = InterfaceFrm(PrefWindowFrm);
        return new Point((vp.Width - (bg?.Width ?? 640)) / 2, (vp.Height - (bg?.Height ?? 480)) / 2);
    }

    private void OpenPreferences(bool fromMenu = false)
    {
        _preferencesOpen = true;
        _prefsFromMenu = fromMenu;
        _preferencesSnapshot = [.. _preferences.Values]; // CANCEL restores this
        SyncPreferencesFromGame();
    }

    /// <summary>Seed the panel from the live game state so it opens showing the truth
    /// (difficulty + volumes are held outside the model).</summary>
    private void SyncPreferencesFromGame()
    {
        _preferences.Set("game_difficulty", (int)Difficulty);
        if (_audio is not null)
            _preferences.Set("master_volume", _audio.MasterVolume);
    }

    /// <summary>Apply the BACKED settings to the live game (called on any adjust + on DONE).</summary>
    private void ApplyBackedPreferences()
    {
        Difficulty = (Formats.Map.GameDifficulty)Math.Clamp(_preferences.Get("game_difficulty"), 0, 2);
        _audio?.SetMasterVolume(_preferences.Get("master_volume"));
        // violence_level + running are read live where they matter (gore gate, run default).
    }

    private void UpdatePreferences(KeyboardState keyboard, MouseState mouse)
    {
        bool click = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        Point o = PrefWindowPos();

        if (IsKeyPressed(keyboard, Keys.Escape))
        {
            CancelPreferences();
            return;
        }

        if (click)
        {
            // The bottom buttons (y≈449): DEFAULT ~x43, DONE ~x169, CANCEL ~x283.
            int by = o.Y + 449, lx = mouse.X - o.X, ly = mouse.Y - o.Y;
            if (ly is >= 440 and <= 470)
            {
                if (lx is >= 40 and < 150) { _preferences.ResetDefaults(); ApplyBackedPreferences(); return; }
                if (lx is >= 160 and < 270) { ClosePreferences(commit: true); return; }
                if (lx is >= 275 and < 390) { CancelPreferences(); return; }
            }
            // A click in a setting's row: left half decrements, right half increments (a
            // discrete cycle), or sets the continuous slider to the click fraction.
            for (int i = 0; i < PrefLayout.Length; i++)
            {
                PrefSlot slot = PrefLayout[i];
                var row = new Rectangle(o.X + slot.KnobX, o.Y + slot.KnobY - 12, PrefTrackWidth, 24);
                if (!row.Contains(mouse.X, mouse.Y))
                    continue;
                if (Formats.GamePreferences.Settings[i].Continuous)
                    _preferences.SetFraction(i, (double)(mouse.X - (o.X + slot.KnobX)) / PrefTrackWidth);
                else
                    _preferences.Adjust(i, mouse.X < o.X + slot.KnobX + PrefTrackWidth / 2 ? -1 : +1);
                ApplyBackedPreferences();
                break;
            }
        }
    }

    private void CancelPreferences()
    {
        if (_preferencesSnapshot is { } snap)
            for (int i = 0; i < snap.Length; i++)
                _preferences.SetIndex(i, snap[i]);
        ApplyBackedPreferences();
        ClosePreferences(commit: false);
    }

    private void ClosePreferences(bool commit)
    {
        if (commit)
            ApplyBackedPreferences();
        _preferencesOpen = false;
        // In-game: back to the options menu, like the engine. From the Title main menu (P139): just close,
        // leaving _menu == Title so the main menu re-draws — do NOT open the (game-less) options panel.
        if (!_prefsFromMenu)
            _optionsOpen = true;
        _prefsFromMenu = false;
    }

    private void DrawPreferences()
    {
        if (!_preferencesOpen || _fontRenderer is null)
            return;
        Point o = PrefWindowPos();
        Texture2D? bg = InterfaceFrm(PrefWindowFrm);
        if (bg is not null)
            _spriteBatch.Draw(bg, new Vector2(o.X, o.Y), Color.White);
        else
        {
            _panelPixel ??= CreatePixel();
            _spriteBatch.Draw(_panelPixel, new Rectangle(o.X, o.Y, 640, 480), new Color(20, 20, 20, 240));
        }

        var value = new Color(0, 108, 0); // _colorTable[18979] — the baked dark-green label ink
        var knob = new Color(0, 252, 0);
        Texture2D? knobArt = InterfaceFrm(PrefKnobOffFrm);

        for (int i = 0; i < Formats.GamePreferences.Settings.Length; i++)
        {
            Formats.GamePreferences.Setting s = Formats.GamePreferences.Settings[i];
            PrefSlot slot = PrefLayout[i];
            // Title at its own label position (centered for col1, left-anchored for col2/3).
            string title = PrefMsg(s.TitleMsg);
            int titleX = slot.TitleCentered ? slot.TitleX - _fontRenderer.MeasureWidth(title) / 2 : slot.TitleX;
            _fontRenderer.Draw(_spriteBatch, title, new Vector2(o.X + titleX, o.Y + slot.TitleY), value);

            double fraction;
            if (s.Continuous)
            {
                fraction = s.Max > s.Min ? (double)(_preferences.Get(s.Key) - s.Min) / (s.Max - s.Min) : 0;
            }
            else
            {
                int v = _preferences.Get(s.Key);
                fraction = s.Positions > 1 ? (double)v / (s.Positions - 1) : 0;
                string label = PrefMsg(s.ValueMsgs[Math.Clamp(v, 0, s.Positions - 1)]);
                _fontRenderer.Draw(_spriteBatch, label,
                    new Vector2(o.X + slot.KnobX + (PrefTrackWidth - _fontRenderer.MeasureWidth(label)) / 2, o.Y + slot.KnobY + 6), value);
            }
            int kx = o.X + slot.KnobX + (int)(fraction * (PrefTrackWidth - (knobArt?.Width ?? 8)));
            if (knobArt is not null)
                _spriteBatch.Draw(knobArt, new Vector2(kx, o.Y + slot.KnobY - (knobArt.Height / 2)), Color.White);
            else
                _spriteBatch.Draw(_panelPixel!, new Rectangle(kx, o.Y + slot.KnobY - 4, 8, 8), knob);
        }

        // DEFAULT / DONE / CANCEL.
        var gold = new Color(252, 252, 84);
        void Btn(int lx, int msg) => _fontRenderer.Draw(_spriteBatch, PrefMsg(msg), new Vector2(o.X + lx, o.Y + 449), gold);
        Btn(43, PrefDefaultMsg);
        Btn(169, PrefDoneMsg);
        Btn(283, PrefCancelMsg);
    }

    private Formats.Text.MessageFile? _preferencesMsg;
    private bool _preferencesMsgTried;
    // The preference strings live in options.msg (preferences.cc:975 loads "options.msg"
    // into gPreferencesMessageList), NOT a preferences.msg.
    private string PrefMsg(int id) =>
        LazyMsg(@"text\english\game\options.msg", ref _preferencesMsgTried, ref _preferencesMsg)?.GetText(id)
        ?? id.ToString();
}
