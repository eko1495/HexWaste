using Hexwaste.Formats.Combat;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Hexwaste.Viewer;

/// <summary>
/// The floating combat-text layer (P45): a capped list of short texts (damage
/// numbers, "Missed", crit feedback) that float above a critter's tile, drift up
/// and fade out. The cap / lifetime / anchor / one-per-owner come from
/// <see cref="FloatText"/> (the fallout2-ce src/text_object.cc port); the rise +
/// fade are a Hexwaste presentation choice (documented there).
///
/// Draw-only + wall-time-ticked: it never writes stdout or the transcript, so the
/// golden suites stay byte-identical. It is inert in the headless harness, which
/// pumps neither Update's ticker nor Draw — the float spawns still run (in Log),
/// but they only mutate an in-memory list, never the console.
/// </summary>
public sealed class CombatTextLayer
{
    private sealed class Float
    {
        public int Tile;
        public int Elevation;
        public string Text = "";
        public Color Color;
        public int Lines;
        public double AgeMs;
        public int LifetimeMs;
    }

    private readonly List<Float> _floats = new();

    public int Count => _floats.Count;

    /// <summary>Spawn a float anchored to a critter's tile. Honours the engine's
    /// one-float-per-owner rule (textObjectsRemoveByOwner, text_object.cc:276/460 — a
    /// new float over the same critter replaces the prior, so rapid hits never stack)
    /// and the global cap (<see cref="FloatText.MaxCount"/>).</summary>
    public void Add(int tile, int elevation, string text, Color color, int lines = 1)
    {
        if (string.IsNullOrEmpty(text))
            return;
        // One float per tile: a fresh one supersedes the prior over the same critter.
        _floats.RemoveAll(f => f.Tile == tile && f.Elevation == elevation);
        // The engine returns -1 at the cap; dropping the oldest keeps the freshest visible.
        if (_floats.Count >= FloatText.MaxCount)
            _floats.RemoveAt(0);
        _floats.Add(new Float
        {
            Tile = tile,
            Elevation = elevation,
            Text = text,
            Color = color,
            Lines = lines,
            LifetimeMs = FloatText.LifetimeMs(lines),
        });
    }

    public void Clear() => _floats.Clear();

    /// <summary>Age the floats over wall-time and expire each at its lifetime
    /// (text_object.cc:338 textObjectsTicker).</summary>
    public void Tick(double elapsedMs)
    {
        for (int i = _floats.Count - 1; i >= 0; i--)
        {
            _floats[i].AgeMs += elapsedMs;
            if (_floats[i].AgeMs >= _floats[i].LifetimeMs)
                _floats.RemoveAt(i);
        }
    }

    /// <summary>Render every float on the current elevation, above its tile, drifting
    /// up and fading. The screen anchor is recomputed each frame from the tile
    /// (text_object.cc:298-300 recomputes position per frame), so floats scroll with
    /// the camera. The engine draws float text with a black bufferOutline (outline
    /// colour index 0, text_object.cc:257); Hexwaste draws a faithful, fading
    /// 4-direction black outline (the AafFontRenderer's built-in shadow is a fixed,
    /// non-fading black, so it is bypassed here).</summary>
    public void Draw(SpriteBatch spriteBatch, AafFontRenderer font, Func<int, (int X, int Y)> hexToScreen, int elevation)
    {
        foreach (Float f in _floats)
        {
            if (f.Elevation != elevation)
                continue;
            float alpha = FloatText.Alpha(f.AgeMs, f.LifetimeMs);
            if (alpha <= 0f)
                continue;

            int width = font.MeasureWidth(f.Text);
            int height = font.LineHeight * f.Lines;
            (int sx, int sy) = FloatText.AnchorOffset(width, height);
            (int tileX, int tileY) = hexToScreen(f.Tile);
            var pos = new Vector2(tileX + sx, tileY + sy + FloatText.RiseOffsetPx(f.AgeMs));

            Color tint = f.Color * alpha;        // Color*float fade (the egg-fade path)
            Color outline = Color.Black * alpha; // black outline (idx 0), faded with the text
            font.Draw(spriteBatch, f.Text, pos + new Vector2(-1, 0), outline, shadow: false);
            font.Draw(spriteBatch, f.Text, pos + new Vector2(1, 0), outline, shadow: false);
            font.Draw(spriteBatch, f.Text, pos + new Vector2(0, -1), outline, shadow: false);
            font.Draw(spriteBatch, f.Text, pos + new Vector2(0, 1), outline, shadow: false);
            font.Draw(spriteBatch, f.Text, pos, tint, shadow: false);
        }
    }
}

/// <summary>
/// The float-text colour vocabulary (P45). These are the engine's REAL float colours
/// — the <c>float_msg</c> / <c>_colorTable</c> constants used by text_object.cc callers
/// (interpreter_extra.cc:3150-3190; AI taunts read ai.txt; level-up uses WHITE,
/// party_member.cc:1554). Fallout 2 never colours combat OUTCOMES (those go to the
/// monitor, one colour), so mapping these genuine engine float colours onto combat
/// outcomes is a documented Hexwaste choice. The RGB values are the nominal RGB555 →
/// RGB8 expansion of each _colorTable index (color.cc:89; component*255/31).
/// </summary>
public static class CombatFloatColors
{
    /// <summary>RED, _colorTable[31744] — damage dealt to an NPC.</summary>
    public static readonly Color DamageNpc = new(255, 0, 0);

    /// <summary>LIGHT_RED, _colorTable[32074] — damage taken by the dude. A distinct
    /// shade so the player can tell whose number it is; the engine has no colour basis
    /// for a player/NPC distinction (it picks a different message-id, same monitor
    /// colour — combat.cc:4935-4949), so this is a readability divergence.</summary>
    public static readonly Color DamageDude = new(255, 82, 82);

    /// <summary>YELLOW, _colorTable[32747] — a critical hit, emphasised regardless of
    /// side (the engine's "normal" float colour, reused here for crit emphasis).</summary>
    public static readonly Color Critical = new(255, 255, 90);

    /// <summary>WHITE, _colorTable[32767] — a miss / dodge.</summary>
    public static readonly Color Miss = new(255, 255, 255);

    /// <summary>WHITE, _colorTable[0x7FFF] — a level-up float (party_member.cc:1554, P72-M1).</summary>
    public static readonly Color LevelUp = new(255, 255, 255);

    /// <summary>YELLOW, _colorTable[32747] — a skill-use response float (actions.cc:1461, P72-M2).</summary>
    public static readonly Color SkillResponse = new(255, 255, 90);
}
