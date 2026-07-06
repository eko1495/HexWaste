using Hexwaste.Formats;
using Hexwaste.Formats.Frm;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Pal;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Hexwaste.Viewer;

/// <summary>
/// The click-to-travel worldmap RENDERER: the 4x5 grid of 350x300
/// art\intrface\wrldmpNN.frm tiles (layout per fallout2-ce
/// src/worldmap.cc wmInterfaceRefresh) scaled to fit the window, all areas from
/// city.txt shown as labeled markers, plus the moving party dot (<see cref="DrawPartyDot"/>).
/// This is render + hit-test only: a click routes through the ONE unified travel path
/// (ViewerGame.TravelTo), which rolls encounters (phase-10/16) and advances the clock and
/// animates the dot (phase-17). Subtile fog-of-war (phase-22): a <see cref="Formats.Map.WorldmapFog"/>
/// hides UNKNOWN subtiles (solid black), dims KNOWN ones (fogged), and shows VISITED ones clear;
/// area markers are gated on discovery (city.txt start_state=On, or a revealed location subtile).
/// </summary>
public sealed class WorldmapScreen : IDisposable
{
    private const int TileColumns = 4;
    private const int TileWidth = 350;
    private const int TileHeight = 300;
    private const int TileCount = 20;
    private const int SubtileSize = 50; // WM_SUBTILE_SIZE — the fog reveal granularity
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

    public void Draw(SpriteBatch spriteBatch, Rectangle viewport, WorldArea? hovered,
        Formats.Map.WorldmapFog? fog = null)
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

        // Phase-22 fog: hide UNKNOWN subtiles (solid black), dim KNOWN ones (a translucent
        // black veil ≈ the engine's intensityColorTable[..][75]), leave VISITED clear. Drawn
        // over the terrain but under the markers/dot so discovered cities stay legible.
        if (fog is not null)
            DrawFog(spriteBatch, scale, offsetX, offsetY, fog);

        var green = new Color(0, 252, 0);
        foreach (WorldArea area in _cities.Areas)
        {
            if (!IsDiscovered(area, fog))
                continue;

            int x = offsetX + (int)(area.WorldX * scale);
            int y = offsetY + (int)(area.WorldY * scale);
            Color color = area == hovered ? Color.Yellow : green;

            spriteBatch.Draw(_marker, new Rectangle(x - 3, y - 3, 6, 6), color);
            _font?.Draw(spriteBatch, area.Name, new Vector2(x + 6, y - 6), color);
        }
    }

    private static readonly Color FogUnknown = new(0, 0, 0, 255);  // hidden — opaque black
    private static readonly Color FogKnown = new(0, 0, 0, 120);    // seen from afar — fogged veil

    private void DrawFog(SpriteBatch spriteBatch, float scale, int offsetX, int offsetY,
        Formats.Map.WorldmapFog fog)
    {
        int subPx = (int)Math.Ceiling(SubtileSize * scale);
        for (int i = 0; i < TileCount; i++)
        {
            int tileX = i % TileColumns * TileWidth, tileY = i / TileColumns * TileHeight;
            for (int sx = 0; sx < Formats.Map.WorldmapFile.SubtileGridWidth; sx++)
                for (int sy = 0; sy < Formats.Map.WorldmapFile.SubtileGridHeight; sy++)
                {
                    int worldX = tileX + sx * SubtileSize, worldY = tileY + sy * SubtileSize;
                    Color? veil = fog.StateAt(worldX + SubtileSize / 2, worldY + SubtileSize / 2) switch
                    {
                        Formats.Map.WorldmapFog.Unknown => FogUnknown,
                        Formats.Map.WorldmapFog.Known => FogKnown,
                        _ => null, // VISITED — clear
                    };
                    if (veil is { } v)
                        spriteBatch.Draw(_marker, new Rectangle(
                            offsetX + (int)(worldX * scale), offsetY + (int)(worldY * scale), subPx, subPx), v);
                }
        }
    }

    /// <summary>A city marker shows once the area is "known" — its city.txt start_state was On
    /// (visible from game start, e.g. Arroyo) OR the party has explored its location subtile
    /// (worldmap.cc gates markers on city->state != UNKNOWN; we derive that state from the
    /// subtile fog, a documented approximation of the engine's separate circle-hotspot detect).</summary>
    private static bool IsDiscovered(WorldArea area, Formats.Map.WorldmapFog? fog)
    {
        if (area.Entrances.Count == 0)
            return false;
        if (fog is null || area.StartsOn)
            return true;
        return fog.StateAt(area.WorldX, area.WorldY) != Formats.Map.WorldmapFog.Unknown;
    }

    /// <summary>Draw the moving party dot at a worldmap pixel position, using the same
    /// world→screen transform as the area markers (phase-17 M2).</summary>
    public void DrawPartyDot(SpriteBatch spriteBatch, Rectangle viewport, int worldX, int worldY)
    {
        (float scale, int offsetX, int offsetY) = Layout(viewport);
        int x = offsetX + (int)(worldX * scale);
        int y = offsetY + (int)(worldY * scale);
        spriteBatch.Draw(_marker, new Rectangle(x - 4, y - 4, 8, 8), Color.White);
    }

    /// <summary>P122: draw an arbitrary party sprite (the driving Highwayman, wmcarmve.frm)
    /// centred on the worldmap pixel with the same transform as the dot. The sprite scales
    /// with the map (fo2ce's worldmap view is 1:1; ours fits the window) so the car keeps
    /// its on-map proportions.</summary>
    public void DrawPartySprite(SpriteBatch spriteBatch, Rectangle viewport, int worldX, int worldY, Texture2D sprite)
    {
        (float scale, int offsetX, int offsetY) = Layout(viewport);
        int x = offsetX + (int)(worldX * scale);
        int y = offsetY + (int)(worldY * scale);
        int w = Math.Max(8, (int)(sprite.Width * scale));
        int h = Math.Max(8, (int)(sprite.Height * scale));
        spriteBatch.Draw(sprite, new Rectangle(x - w / 2, y - h / 2, w, h), Color.White);
    }

    public WorldArea? HitTest(int mouseX, int mouseY, Rectangle viewport,
        Formats.Map.WorldmapFog? fog = null)
    {
        (float scale, int offsetX, int offsetY) = Layout(viewport);

        WorldArea? best = null;
        double bestDistance = 18 * 18; // generous click radius in screen px
        foreach (WorldArea area in _cities.Areas)
        {
            if (!IsDiscovered(area, fog)) // can only travel to a discovered city (phase-22)
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
