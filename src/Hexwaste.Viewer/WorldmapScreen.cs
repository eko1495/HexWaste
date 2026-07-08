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
    private readonly FrmCache? _frmCache;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly RasterizerState _scissor = new() { ScissorTestEnable = true };

    // ---- P123 chrome: the authentic 640x480 worldmap window (worldmap.cc constants) ----
    private const int ChromeW = 640, ChromeH = 480;         // WM_WINDOW_WIDTH/HEIGHT
    private const int ViewX = 22, ViewY = 21;                // WM_VIEW_X/Y
    private const int ViewW = 450, ViewH = 443;              // WM_VIEW_WIDTH/HEIGHT
    private const int BgFrm = 136;                           // worldmap.frm — the chrome
    private const int HotspotFrm = 168;                      // hotspot1.frm — "you are here"
    private const int TargetFrm = 139;                       // wmaptarg.frm — travel destination
    private const int CircleSmallFrm = 336;                  // wmsmcir/wmmdcir/wmlgcir (336+size)
    private const int TabsFrm = 364, TabsEdgeFrm = 367;      // wmtabs / wmtbedge
    private const int DialFrm = 365;                         // wmdial — day/night dial
    private const int GlobeFrm = 366, CarOverlayFrm = 363;   // wmglobe / wmscreen
    private const int MonthsFrm = 129, NumbersFrm = 82;      // months / numbers strips
    private const int RedUpFrm = 8, RedDownFrm = 9;          // lilredup/dn
    private const int UpArrowFrm = 199, DownArrowFrm = 181;  // uparwoff / dnarwoff (+1 = pressed)
    private const int TabRowH = 27;                          // one town tab row
    private const int TabRows = 7;                           // visible quick-travel rows

    /// <summary>The map-view scroll offset in world pixels (top-left of the view).</summary>
    public int ScrollX { get; private set; }
    public int ScrollY { get; private set; }

    /// <summary>P125: the town whose townmap sub-view is showing in the chrome's map view
    /// (wmTownMapFunc), or null for the world view. Toggled by the TOWN/WORLD switch.</summary>
    public WorldArea? TownmapArea { get; set; }

    /// <summary>True when the area has townmap art to show (city.txt townmap_art_idx,
    /// worldmap.cc:3144 gates the switch on mapFid != -1).</summary>
    public bool HasTownmap(WorldArea? area) =>
        area is { TownmapArtIdx: >= 0 } && Frm(area.TownmapArtIdx) is not null;

    /// <summary>The town-tab list offset, in whole rows.</summary>
    public int TabsOffset { get; private set; }

    public WorldmapScreen(GraphicsDevice graphicsDevice, GameFileSystem vfs, Palette palette,
        CityList cities, AafFontRenderer? font, FrmCache? frmCache = null)
    {
        _cities = cities;
        _font = font;
        _frmCache = frmCache;
        _graphicsDevice = graphicsDevice;

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

    // ==================================================================================
    //  P123: the authentic chrome (wmInterfaceInit/wmInterfaceRefresh, worldmap.cc)
    // ==================================================================================

    private Texture2D? Frm(int intrfaceId, int frame = 0)
    {
        if (_frmCache is null)
            return null;
        try
        {
            int fid = Formats.Fid.Build(Formats.ObjectType.Interface, intrfaceId);
            int frames = _frmCache.FrameCount(fid);
            return _frmCache.GetTexture(fid, frames > 0 ? frame % frames : 0);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Chrome mode is on when the worldmap.frm background loads; otherwise the
    /// pre-P123 fit-to-window view stays (the art residual).</summary>
    public bool HasChrome => Frm(BgFrm) is not null;

    /// <summary>The chrome window's top-left in screen space (centered, like fo2ce's
    /// wmInterfaceInit window placement).</summary>
    public Point ChromeOrigin(Rectangle viewport) =>
        new(viewport.X + (viewport.Width - ChromeW) / 2, viewport.Y + (viewport.Height - ChromeH) / 2);

    /// <summary>The map view rect (the 450x443 cutout at window-local 22,21) in screen space.</summary>
    public Rectangle ViewRect(Rectangle viewport)
    {
        Point o = ChromeOrigin(viewport);
        return new Rectangle(o.X + ViewX, o.Y + ViewY, ViewW, ViewH);
    }

    /// <summary>Scroll the map view by a pixel delta, clamped to the world bounds.</summary>
    public void ScrollBy(int dx, int dy)
    {
        ScrollX = Math.Clamp(ScrollX + dx, 0, WorldWidth - ViewW);
        ScrollY = Math.Clamp(ScrollY + dy, 0, WorldHeight - ViewH);
    }

    /// <summary>Center the view on a world pixel (wmInterfaceCenterOnParty).</summary>
    public void CenterOn(int worldX, int worldY)
    {
        ScrollX = Math.Clamp(worldX - ViewW / 2, 0, WorldWidth - ViewW);
        ScrollY = Math.Clamp(worldY - ViewH / 2, 0, WorldHeight - ViewH);
    }

    /// <summary>Step the town-tab list by whole rows, clamped to the discovered list.</summary>
    public void ScrollTabs(int deltaRows, Formats.Map.WorldmapFog? fog) =>
        TabsOffset = Math.Clamp(TabsOffset + deltaRows, 0, Math.Max(0, TabTowns(fog).Count - TabRows));

    /// <summary>The quick-travel town list: discovered areas sorted by name
    /// (wmMakeTabsLabelList sorts alphabetically and filters on wmAreaIsKnown). Deduped by
    /// display name — fo2ce filters on labelFid, which name-sharing shadow areas ("Destroyed
    /// Arroyo" = "Arroyo") lack; our fog-derived discovery would otherwise list both.</summary>
    public List<WorldArea> TabTowns(Formats.Map.WorldmapFog? fog) =>
        [.. _cities.Areas.Where(a => IsDiscovered(a, fog))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())];

    /// <summary>The quick-travel red button rect for visible tab row 0..6 (buttons at
    /// window-local 508, 138+27·row — wmInterfaceInit :4624).</summary>
    public Rectangle TabButtonRect(Rectangle viewport, int row)
    {
        Point o = ChromeOrigin(viewport);
        Texture2D? btn = Frm(RedUpFrm);
        return new Rectangle(o.X + 508, o.Y + 138 + TabRowH * row, btn?.Width ?? 15, btn?.Height ?? 16);
    }

    /// <summary>The tab-list scroll arrow rects: (up, down) at window-local (480,137)/(480,152).</summary>
    public (Rectangle Up, Rectangle Down) TabArrowRects(Rectangle viewport)
    {
        Point o = ChromeOrigin(viewport);
        Texture2D? up = Frm(UpArrowFrm);
        int w = up?.Width ?? 24, h = up?.Height ?? 15;
        return (new Rectangle(o.X + 480, o.Y + 137, w, h), new Rectangle(o.X + 480, o.Y + 152, w, h));
    }

    /// <summary>Draw the full chrome window: background, the scissored 1:1 map view (tiles,
    /// fog, city circles + names, destination + hotspot markers), the town tabs, date/time,
    /// the day/night dial, and the globe/car monitor. The sprite batch is restarted around
    /// the scissored section (the caller's batch must be a plain PointClamp Begin).</summary>
    public void DrawChrome(SpriteBatch spriteBatch, Rectangle viewport, WorldArea? hovered,
        Formats.Map.WorldmapFog? fog, int partyX, int partyY, int destX, int destY,
        int hourHhmm, int day, int month, int year, bool inCar, int fuel, int fuelMax, int carFrame)
    {
        Point o = ChromeOrigin(viewport);
        Rectangle view = ViewRect(viewport);
        spriteBatch.Draw(Frm(BgFrm)!, new Vector2(o.X, o.Y), Color.White);

        // ---- the scissored map view (everything positioned by world − scroll) ----
        spriteBatch.End();
        Rectangle oldScissor = _graphicsDevice.ScissorRectangle;
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, rasterizerState: _scissor);
        _graphicsDevice.ScissorRectangle = Rectangle.Intersect(view, viewport);

        // P125: the townmap sub-view replaces the world content inside the same chrome
        // (wmTownMapRefresh :5915 blits the town art at the view spot; hotspot buttons at
        // the entrances' window coords; labels from worldmap.msg under each hotspot).
        if (TownmapArea is { } town && Frm(town.TownmapArtIdx) is { } townArt)
        {
            spriteBatch.Draw(townArt, new Vector2(view.X, view.Y), Color.White);
            Texture2D? spot = Frm(HotspotFrm);
            for (int i = 0; i < town.Entrances.Count; i++)
            {
                AreaEntrance e = town.Entrances[i];
                if (!e.StartsOn || e.TownmapX < 0 || e.TownmapY < 0)
                    continue;
                if (spot is not null)
                    spriteBatch.Draw(spot, new Vector2(o.X + e.TownmapX, o.Y + e.TownmapY), Color.White);
                string? label = TownmapMsg?.Invoke(200 + 10 * town.Index + i);
                if (label is not null)
                    _font?.Draw(spriteBatch, label, new Vector2(
                        o.X + e.TownmapX + (spot?.Width ?? 24) / 2 - (_font?.MeasureWidth(label) ?? 0) / 2,
                        o.Y + e.TownmapY + (spot?.Height ?? 26) + 4), new Color(0, 252, 0));
            }
        }
        else
        {
            DrawWorldView(spriteBatch, view, hovered, fog, partyX, partyY, destX, destY);
        }

        spriteBatch.End();
        _graphicsDevice.ScissorRectangle = oldScissor;
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        DrawTabs(spriteBatch, o, fog);
        DrawDate(spriteBatch, o, hourHhmm, day, month, year);
        if (Frm(DialFrm, (hourHhmm / 100 + 12) % Math.Max(1, DialFrameCount())) is { } dial)
            spriteBatch.Draw(dial, new Vector2(o.X + 532, o.Y + 48), Color.White); // WM_WINDOW_DIAL

        if (inCar)
        {
            // The car monitor at its real chrome spot (worldmap.cc:6179-6199).
            if (Frm(433, carFrame) is { } movie)
                spriteBatch.Draw(movie, new Vector2(o.X + 514, o.Y + 336), Color.White);
            if (Frm(CarOverlayFrm) is { } overlay)
                spriteBatch.Draw(overlay, new Vector2(o.X + 499, o.Y + 330), Color.White);
            int barH = (int)(70L * Math.Clamp(fuel, 0, fuelMax) / Math.Max(1, fuelMax));
            if (barH > 0)
                spriteBatch.Draw(_marker, new Rectangle(o.X + 500, o.Y + 339 + (70 - barH), 2, barH),
                    new Color(0, 196, 0));
        }
        else if (Frm(GlobeFrm) is { } globe)
        {
            spriteBatch.Draw(globe, new Vector2(o.X + 495, o.Y + 330), Color.White); // wmglobe stamp
        }
    }

    /// <summary>P125: resolves a worldmap.msg entry for townmap entrance labels
    /// (200 + 10·area + entrance) at runtime; null = no label. Set by the viewer.</summary>
    public Func<int, string?>? TownmapMsg { get; set; }

    /// <summary>The world view's scissored content (tiles, fog, circles, markers) — the
    /// pre-P125 body of DrawChrome, unchanged.</summary>
    private void DrawWorldView(SpriteBatch spriteBatch, Rectangle view, WorldArea? hovered,
        Formats.Map.WorldmapFog? fog, int partyX, int partyY, int destX, int destY)
    {
        int wx0 = view.X - ScrollX, wy0 = view.Y - ScrollY; // world (0,0) in screen space
        for (int i = 0; i < TileCount; i++)
            if (_tiles[i] is { } tile)
                spriteBatch.Draw(tile, new Vector2(wx0 + i % TileColumns * TileWidth,
                    wy0 + i / TileColumns * TileHeight), Color.White);
        if (fog is not null)
            DrawFogChrome(spriteBatch, wx0, wy0, fog);

        var green = new Color(0, 252, 0);
        foreach (WorldArea area in _cities.Areas)
        {
            if (!IsDiscovered(area, fog))
                continue;
            Color color = area == hovered ? Color.Yellow : green;
            // The circle art (336 + size, drawn at the area's world pos as its top-left —
            // wmMatchWorldPosToArea hit-tests that rect) + the name under it (font 101 green).
            Texture2D? circle = Frm(CircleSmallFrm + CitySizeIndex(area));
            int cx = wx0 + area.WorldX, cy = wy0 + area.WorldY;
            if (circle is not null)
                spriteBatch.Draw(circle, new Vector2(cx, cy), Color.White);
            else
                spriteBatch.Draw(_marker, new Rectangle(cx - 3, cy - 3, 6, 6), color);
            _font?.Draw(spriteBatch, area.Name, new Vector2(cx, cy + (circle?.Height ?? 6) + 1), color);
        }

        if (destX >= 0 && Frm(TargetFrm) is { } target) // wmaptarg — the travel destination
            spriteBatch.Draw(target, new Vector2(wx0 + destX - target.Width / 2,
                wy0 + destY - target.Height / 2), Color.White);
        if (partyX >= 0)
        {
            if (Frm(HotspotFrm) is { } hotspot) // hotspot1 — "you are here"
                spriteBatch.Draw(hotspot, new Vector2(wx0 + partyX - hotspot.Width / 2,
                    wy0 + partyY - hotspot.Height / 2), Color.White);
            else
                spriteBatch.Draw(_marker, new Rectangle(wx0 + partyX - 4, wy0 + partyY - 4, 8, 8), Color.White);
        }
    }

    /// <summary>P125: the entrance index under the mouse on the open townmap (hotspot-art
    /// sized rects at the entrances' window coords), or -1.</summary>
    public int TownmapEntranceAt(int mouseX, int mouseY, Rectangle viewport)
    {
        if (TownmapArea is not { } town)
            return -1;
        Point o = ChromeOrigin(viewport);
        Texture2D? spot = Frm(HotspotFrm);
        int w = spot?.Width ?? 24, h = spot?.Height ?? 26;
        for (int i = 0; i < town.Entrances.Count; i++)
        {
            AreaEntrance e = town.Entrances[i];
            if (e.StartsOn && e.TownmapX >= 0 && e.TownmapY >= 0
                && new Rectangle(o.X + e.TownmapX, o.Y + e.TownmapY, w, h).Contains(mouseX, mouseY))
                return i;
        }
        return -1;
    }

    /// <summary>P125: the TOWN/WORLD switch's click band (the red button baked at
    /// window-local 519,439 — wmInterfaceInit :4605).</summary>
    public Rectangle TownWorldSwitchRect(Rectangle viewport)
    {
        Point o = ChromeOrigin(viewport);
        Texture2D? btn = Frm(RedUpFrm);
        return new Rectangle(o.X + 519, o.Y + 439, btn?.Width ?? 15, btn?.Height ?? 16);
    }

    private int DialFrameCount()
    {
        if (_frmCache is null)
            return 1;
        try { return _frmCache.FrameCount(Formats.Fid.Build(Formats.ObjectType.Interface, DialFrm)); }
        catch (Exception) { return 1; }
    }

    /// <summary>city.txt size → the circle art index offset (Small/Medium/Large → 0/1/2).</summary>
    private static int CitySizeIndex(WorldArea area) => area.Size.ToLowerInvariant() switch
    {
        "small" => 0,
        "medium" => 1,
        _ => 2,
    };

    /// <summary>The town tabs rail (wmRefreshTabs :6245): the 364 underlay strip windowed at
    /// (501,135) 119x178, town names on 27px rows from (530,138), the 367 edge overlay, the
    /// scroll arrows, and the quick-travel red buttons.</summary>
    private void DrawTabs(SpriteBatch spriteBatch, Point o, Formats.Map.WorldmapFog? fog)
    {
        List<WorldArea> towns = TabTowns(fog);
        if (Frm(TabsFrm) is { } tabs)
        {
            int srcY = Math.Min(TabRowH + TabsOffset * TabRowH, Math.Max(0, tabs.Height - 178));
            spriteBatch.Draw(tabs, new Vector2(o.X + 501, o.Y + 135),
                new Rectangle(9, srcY, 119, 178), Color.White);
        }
        var dark = new Color(0, 108, 0);
        for (int row = 0; row < TabRows; row++)
        {
            int idx = TabsOffset + row;
            if (idx >= towns.Count)
                break;
            // P131: the town's per-city label FRM (city.txt townmap_label_art_idx) is the
            // authentic tab graphic (wmRefreshTabs :6274 blits labelFid); the text name is
            // the missing-art fallback.
            if (towns[idx].LabelArtIdx >= 0 && Frm(towns[idx].LabelArtIdx) is { } label)
                spriteBatch.Draw(label, new Vector2(o.X + 519, o.Y + 138 + TabRowH * row), Color.White);
            else
                _font?.Draw(spriteBatch, towns[idx].Name, new Vector2(o.X + 530, o.Y + 141 + TabRowH * row), dark);
        }
        if (Frm(TabsEdgeFrm) is { } edge)
            spriteBatch.Draw(edge, new Vector2(o.X + 501, o.Y + 135), Color.White);
        if (Frm(UpArrowFrm) is { } up)
            spriteBatch.Draw(up, new Vector2(o.X + 480, o.Y + 137), Color.White);
        if (Frm(DownArrowFrm) is { } down)
            spriteBatch.Draw(down, new Vector2(o.X + 480, o.Y + 152), Color.White);
        if (Frm(RedUpFrm) is { } btn)
            for (int row = 0; row < TabRows && TabsOffset + row < towns.Count; row++)
                spriteBatch.Draw(btn, new Vector2(o.X + 508, o.Y + 138 + TabRowH * row), Color.White);
    }

    /// <summary>The date/time readout (wmInterfaceRefreshDate :5310): day digits at (487,12),
    /// the month name cell at (+26,+1), the year right-aligned, then the HHMM clock — all from
    /// the numbers (82) 9x17 digit strip and the months (129) 29x14 name cells (15px stride).</summary>
    private void DrawDate(SpriteBatch spriteBatch, Point o, int hourHhmm, int day, int month, int year)
    {
        Texture2D? numbers = Frm(NumbersFrm);
        if (numbers is null)
            return;
        void Digit(int x, int digit) => spriteBatch.Draw(numbers, new Vector2(o.X + x, o.Y + 12),
            new Rectangle(9 * Math.Clamp(digit, 0, 9), 0, 9, 17), Color.White);

        Digit(487, day / 10);
        Digit(496, day % 10);
        if (Frm(MonthsFrm) is { } months)
            spriteBatch.Draw(months, new Vector2(o.X + 513, o.Y + 13),
                new Rectangle(0, 15 * Math.Clamp(month, 0, 11), 29, 14), Color.White);
        int y4 = year;
        for (int k = 1; k <= 4; k++) { Digit(487 + 98 - 9 * k, y4 % 10); y4 /= 10; }
        int t = hourHhmm;
        for (int k = 0; k < 4; k++) { Digit(621 - 9 * k, t % 10); t /= 10; }
    }

    /// <summary>Fog cells for the chrome view (1:1 pixels, clipped by the scissor).</summary>
    private void DrawFogChrome(SpriteBatch spriteBatch, int wx0, int wy0, Formats.Map.WorldmapFog fog)
    {
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
                        _ => null,
                    };
                    if (veil is { } v)
                        spriteBatch.Draw(_marker, new Rectangle(wx0 + worldX, wy0 + worldY,
                            SubtileSize, SubtileSize), v);
                }
        }
    }

    /// <summary>Chrome-mode hit test: the discovered area whose circle rect (world pos =
    /// top-left, circle-art sized — wmMatchWorldPosToArea :5359) contains the cursor, view-
    /// clipped. Falls back to the fitted-view test when the chrome is off.</summary>
    public WorldArea? HitTestChrome(int mouseX, int mouseY, Rectangle viewport, Formats.Map.WorldmapFog? fog)
    {
        Rectangle view = ViewRect(viewport);
        if (!view.Contains(mouseX, mouseY))
            return null;
        int worldX = mouseX - view.X + ScrollX;
        int worldY = mouseY - view.Y + ScrollY;
        foreach (WorldArea area in _cities.Areas)
        {
            if (!IsDiscovered(area, fog))
                continue;
            Texture2D? circle = Frm(CircleSmallFrm + CitySizeIndex(area));
            int w = circle?.Width ?? 16, h = circle?.Height ?? 16;
            if (worldX >= area.WorldX && worldX <= area.WorldX + w
                && worldY >= area.WorldY && worldY <= area.WorldY + h)
                return area;
        }
        return null;
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
        _scissor.Dispose(); // chrome art itself lives in the shared FrmCache
    }
}
