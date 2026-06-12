using FalloutPoc.Formats.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FalloutPoc.Viewer;

/// <summary>
/// Draws .aaf fonts via a 16x16-glyph atlas texture: white pixels with alpha
/// from the font's opacity levels, so any color is a SpriteBatch tint.
/// Layout rules ported from fallout2-ce src/font_manager.cc
/// interfaceFontDrawImpl(): glyphs bottom-align to MaxHeight, the space
/// character advances by WordSpacing, every character adds LetterSpacing.
/// </summary>
public sealed class AafFontRenderer : IDisposable
{
    private readonly AafFont _font;
    private readonly Texture2D _atlas;
    private readonly Rectangle[] _sources = new Rectangle[256];

    public int LineHeight => _font.MaxHeight + _font.LineSpacing;

    public AafFontRenderer(GraphicsDevice graphicsDevice, AafFont font)
    {
        _font = font;

        int cellWidth = font.Glyphs.Max(g => (int)g.Width) + 1;
        int cellHeight = font.MaxHeight + 1;
        int atlasWidth = cellWidth * 16;
        int atlasHeight = cellHeight * 16;

        byte[] rgba = new byte[atlasWidth * atlasHeight * 4];
        for (int i = 0; i < 256; i++)
        {
            AafGlyph glyph = font.Glyphs[i];
            int cellX = (i % 16) * cellWidth;
            int cellY = (i / 16) * cellHeight;
            int top = font.MaxHeight - glyph.Height; // bottom-aligned

            for (int y = 0; y < glyph.Height; y++)
            {
                for (int x = 0; x < glyph.Width; x++)
                {
                    byte level = glyph.Pixels[y * glyph.Width + x];
                    if (level == 0)
                        continue;
                    int pixel = ((cellY + top + y) * atlasWidth + cellX + x) * 4;
                    byte alpha = (byte)Math.Min(level * 255 / font.MaxLevel, 255);
                    rgba[pixel] = rgba[pixel + 1] = rgba[pixel + 2] = 255;
                    rgba[pixel + 3] = alpha;
                }
            }

            _sources[i] = new Rectangle(cellX, cellY, glyph.Width, font.MaxHeight);
        }

        _atlas = new Texture2D(graphicsDevice, atlasWidth, atlasHeight, false, SurfaceFormat.Color);
        _atlas.SetData(rgba);
    }

    public int MeasureWidth(string text) => _font.MeasureWidth(text);

    /// <summary>Greedy word wrap to a pixel width.</summary>
    public List<string> WrapText(string text, int maxWidth)
    {
        var lines = new List<string>();
        var current = "";
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = current.Length == 0 ? word : $"{current} {word}";
            if (current.Length > 0 && MeasureWidth(candidate) > maxWidth)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
            lines.Add(current);
        return lines;
    }

    public void Draw(SpriteBatch spriteBatch, string text, Vector2 position, Color color, bool shadow = true)
    {
        if (shadow)
            DrawGlyphs(spriteBatch, text, position + new Vector2(1, 1), Color.Black);
        DrawGlyphs(spriteBatch, text, position, color);
    }

    private void DrawGlyphs(SpriteBatch spriteBatch, string text, Vector2 position, Color color)
    {
        float x = position.X;
        foreach (char ch in text)
        {
            if (ch != ' ')
                spriteBatch.Draw(_atlas, new Vector2(x, position.Y), _sources[(byte)ch], color);
            x += _font.CharWidth(ch) + _font.LetterSpacing;
        }
    }

    public void Dispose() => _atlas.Dispose();
}
