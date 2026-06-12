using Hexwaste.Formats.Map;

namespace Hexwaste.Viewer;

/// <summary>
/// View state and grid-to-screen projection, ported from fallout2-ce src/tile.cc.
/// Fallout uses two grids with different projections: objects sit on the
/// 200x200 hex grid, floors/roofs on the 100x100 square grid (one square =
/// 2x2 hexes). The projection is oblique, not standard 2:1 isometric.
/// </summary>
public sealed class Camera
{
    public const int HexGridWidth = 200;
    public const int HexGridHeight = 200;

    private int _tileX;
    private int _tileY;
    private int _tileOffX;
    private int _tileOffY;
    private int _squareX;
    private int _squareY;
    private int _squareOffX;
    private int _squareOffY;

    private int _windowWidth;
    private int _windowHeight;

    public int CenterHexTile { get; private set; }

    /// <summary>User pan offset in screen pixels, added on top of the centered view.</summary>
    public int PanX { get; set; }
    public int PanY { get; set; }

    public void SetWindowSize(int width, int height)
    {
        _windowWidth = width;
        _windowHeight = height;
        SetCenter(CenterHexTile);
    }

    /// <summary>ported from fallout2-ce src/tile.cc tileSetCenter().</summary>
    public void SetCenter(int hexTile)
    {
        CenterHexTile = hexTile;

        int tileX = HexGridWidth - 1 - hexTile % HexGridWidth;
        int tileY = hexTile / HexGridWidth;

        _tileX = tileX;
        _tileY = tileY;
        _tileOffX = (_windowWidth - 32) / 2;
        _tileOffY = (_windowHeight - 16) / 2;

        if ((tileX & 1) != 0)
        {
            _tileX -= 1;
            _tileOffX -= 32;
        }

        _squareX = _tileX / 2;
        _squareY = _tileY / 2;
        _squareOffX = _tileOffX - 16;
        _squareOffY = _tileOffY - 2;

        if ((_tileY & 1) != 0)
        {
            _squareOffY -= 12;
            _squareOffX -= 16;
        }
    }

    /// <summary>ported from fallout2-ce src/tile.cc tileToScreenXY().</summary>
    public (int X, int Y) HexToScreen(int hexTile)
    {
        int v3 = HexGridWidth - 1 - hexTile % HexGridWidth;
        int v4 = hexTile / HexGridWidth;

        int screenX = _tileOffX;
        int screenY = _tileOffY;

        // C-style truncating division, sign matters: (v3 - _tile_x) / 2 and / -2
        // are NOT the same as a shifted divide for negative values.
        screenX += 48 * ((v3 - _tileX) / 2);
        screenY += 12 * ((v3 - _tileX) / -2);

        if ((v3 & 1) != 0)
        {
            if (v3 <= _tileX)
            {
                screenX -= 16;
                screenY += 12;
            }
            else
            {
                screenX += 32;
            }
        }

        int v6 = v4 - _tileY;
        screenX += 16 * v6;
        screenY += 12 * v6;

        return (screenX + PanX, screenY + PanY);
    }

    /// <summary>ported from fallout2-ce src/tile.cc squareTileToScreenXY().</summary>
    public (int X, int Y) SquareToScreen(int squareTile)
    {
        int v5 = MapElevation.SquareGridWidth - 1 - squareTile % MapElevation.SquareGridWidth;
        int v6 = squareTile / MapElevation.SquareGridWidth;

        int coordX = _squareOffX;
        int coordY = _squareOffY;

        int v8 = v5 - _squareX;
        coordX += 48 * v8;
        coordY -= 12 * v8;

        int v9 = v6 - _squareY;
        coordX += 32 * v9;
        coordY += 24 * v9;

        return (coordX + PanX, coordY + PanY);
    }

    /// <summary>
    /// ported from fallout2-ce src/tile.cc squareTileToRoofScreenXY():
    /// identical to the floor projection, shifted up 96 px.
    /// </summary>
    public (int X, int Y) SquareToRoofScreen(int squareTile)
    {
        (int x, int y) = SquareToScreen(squareTile);
        return (x, y - 96);
    }

    /// <summary>
    /// Corner-correction mask for hex hit testing inside the 32x16 cell,
    /// built exactly like fallout2-ce src/tile.cc tileSetupTileGrid():
    /// 0 = inside, 1..4 = NW/NE/SW/SE neighbor corrections.
    /// </summary>
    private static readonly byte[] TileMask = BuildTileMask();

    private static byte[] BuildTileMask()
    {
        var mask = new byte[512];
        int i = 0;
        for (int row = 0; row != 64; row += 16)
        {
            for (int v = 64; v != 0; v -= 4)
                mask[i++] = (byte)(v > row ? 1 : 0);
            for (int v = 0; v != 64; v += 4)
                mask[i++] = (byte)(v > row ? 2 : 0);
        }

        i += 8 * 32; // middle rows are all 0 (inside the hex)

        for (int row = 0; row != 64; row += 16)
        {
            for (int v = 0; v != 64; v += 4)
                mask[i++] = (byte)(v > row ? 0 : 3);
            for (int v = 64; v != 0; v -= 4)
                mask[i++] = (byte)(v > row ? 0 : 4);
        }

        return mask;
    }

    /// <summary>ported from fallout2-ce src/tile.cc tileFromScreenXY().</summary>
    public int ScreenToHex(int screenX, int screenY)
    {
        screenX -= PanX;
        screenY -= PanY;

        int v2 = screenY - _tileOffY;
        int v3 = v2 >= 0 ? v2 / 12 : (v2 + 1) / 12 - 1;

        int v4 = screenX - _tileOffX - 16 * v3;
        int v5 = v2 - 12 * v3;

        int v6 = v4 >= 0 ? v4 / 64 : (v4 + 1) / 64 - 1;

        int v7 = v6 + v3;
        int v8 = v4 - v6 * 64;
        int v9 = 2 * v6;

        if (v8 >= 32)
        {
            v8 -= 32;
            v9++;
        }

        int v10 = _tileY + v7;
        int v11 = _tileX + v9;

        switch (TileMask[32 * v5 + v8])
        {
            case 2:
                v11++;
                if ((v11 & 1) != 0)
                    v10--;
                break;
            case 1:
                v10--;
                break;
            case 3:
                v11--;
                if ((v11 & 1) == 0)
                    v10++;
                break;
            case 4:
                v10++;
                break;
        }

        int v12 = HexGridWidth - 1 - v11;
        if (v12 >= 0 && v12 < HexGridWidth && v10 >= 0 && v10 < HexGridHeight)
            return HexGridWidth * v10 + v12;

        return -1;
    }
}
