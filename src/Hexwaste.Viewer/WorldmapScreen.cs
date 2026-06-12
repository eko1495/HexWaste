using Hexwaste.Formats;
using Hexwaste.Formats.Frm;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Pal;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Hexwaste.Viewer;

/// <summary>
/// Minimal click-to-travel worldmap: the 4x5 grid of 350x300
/// art\intrface\wrldmpNN.frm tiles (layout per fallout2-ce
/// src/worldmap.cc wmInterfaceRefresh) scaled to fit the window, with all
/// areas from city.txt shown as labeled markers. No encounters, no travel
/// time, no fog — per the phase-3 scope.
/// </summary>
public sealed class WorldmapScreen : IDisposable
{
    private const int TileColumns = 4;
    private const int TileWidth = 350;
    private const int TileHeight = 300;
    private const int TileCount = 20;
    private const int WorldWidth = TileColumns * TileWidth; // 1400
    private const int WorldHeight = TileCount / TileColumns * TileHeight; // 1500

    private readonly Texture2D?[] _tiles = new Texture2D?[TileCount];
    private readonly Texture2D _marker;
    private readonly CityList _cities;
    private readonly AafFontRenderer? _font;

    public WorldmapScreen(GraphicsDevice graphicsDevice, GameFileSystem vfs, Palette palette,
        CityList cities, AafFontRenderer? font)
    {
        _cities = cities;
        _font = font;

        byte[] paletteRgba = palette.ToRgba();
        for (int i = 0; i < TileCount; i++)
        {
            string path = $@"art\intrface\wrldmp{i:00}.frm";
            if (!vfs.Exists(path))
                continue;

            FrmFrame frame = FrmFile.Load(vfs.ReadAllBytes(path)).GetFrame(0);
            byte[] rgba = new byte[frame.Pixels.Length * 4];
            for (int p = 0; p < frame.Pixels.Length; p++)
                Buffer.BlockCopy(paletteRgba, frame.Pixels[p] * 4, rgba, p * 4, 4);

            var texture = new Texture2D(graphicsDevice, frame.Width, frame.Height, false, SurfaceFormat.Color);
            texture.SetData(rgba);
            _tiles[i] = texture;
        }

        _marker = new Texture2D(graphicsDevice, 1, 1);
        _marker.SetData(new[] { Color.White });
    }

    private (float Scale, int OffsetX, int OffsetY) Layout(Rectangle viewport)
    {
        float scale = Math.Min((float)viewport.Width / WorldWidth, (float)viewport.Height / WorldHeight);
        int offsetX = (viewport.Width - (int)(WorldWidth * scale)) / 2;
        int offsetY = (viewport.Height - (int)(WorldHeight * scale)) / 2;
        return (scale, offsetX, offsetY);
    }

    public void Draw(SpriteBatch spriteBatch, Rectangle viewport, WorldArea? hovered)
    {
        (float scale, int offsetX, int offsetY) = Layout(viewport);

        for (int i = 0; i < TileCount; i++)
        {
            if (_tiles[i] is not { } tile)
                continue;
            var destination = new Rectangle(
                offsetX + (int)(i % TileColumns * TileWidth * scale),
                offsetY + (int)(i / TileColumns * TileHeight * scale),
                (int)Math.Ceiling(TileWidth * scale),
                (int)Math.Ceiling(TileHeight * scale));
            spriteBatch.Draw(tile, destination, Color.White);
        }

        var green = new Color(0, 252, 0);
        foreach (WorldArea area in _cities.Areas)
        {
            if (area.Entrances.Count == 0)
                continue;

            int x = offsetX + (int)(area.WorldX * scale);
            int y = offsetY + (int)(area.WorldY * scale);
            Color color = area == hovered ? Color.Yellow : green;

            spriteBatch.Draw(_marker, new Rectangle(x - 3, y - 3, 6, 6), color);
            _font?.Draw(spriteBatch, area.Name, new Vector2(x + 6, y - 6), color);
        }
    }

    public WorldArea? HitTest(int mouseX, int mouseY, Rectangle viewport)
    {
        (float scale, int offsetX, int offsetY) = Layout(viewport);

        WorldArea? best = null;
        double bestDistance = 18 * 18; // generous click radius in screen px
        foreach (WorldArea area in _cities.Areas)
        {
            if (area.Entrances.Count == 0)
                continue;
            double dx = offsetX + area.WorldX * scale - mouseX;
            double dy = offsetY + area.WorldY * scale - mouseY;
            double distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = area;
            }
        }

        return best;
    }

    public void Dispose()
    {
        foreach (Texture2D? tile in _tiles)
            tile?.Dispose();
        _marker.Dispose();
    }
}
