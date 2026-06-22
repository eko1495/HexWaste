namespace Hexwaste.Formats.Hex;

/// <summary>
/// 200x200 hex grid geometry, ported from fallout2-ce src/tile.cc
/// tileSetupTileGrid() (_dir_tile deltas) and tileGetTileInDirection().
/// Rotation 0 = NE, clockwise. Tile x runs right-to-left (tile % width is
/// mirrored), tile y = tile / width.
/// </summary>
public static class HexGrid
{
    public const int Width = 200;
    public const int Height = 200;
    public const int Size = Width * Height;
    public const int RotationCount = 6;

    // ported from fallout2-ce src/tile.cc tileSetupTileGrid(); indexed
    // [column parity][rotation], hexGridWidth = 200.
    private static readonly int[][] DirDeltas =
    [
        [-1, Width - 1, Width, Width + 1, 1, -Width],
        [-Width - 1, -1, Width, 1, 1 - Width, -Width],
    ];

    /// <summary>One-hex screen deltas per rotation: tile.cc _off_tile / dword_51D984.</summary>
    public static readonly int[] StepScreenX = [16, 32, 16, -16, -32, -16];
    public static readonly int[] StepScreenY = [-12, 0, 12, 12, 0, -12];

    public static bool IsValid(int tile) => tile is >= 0 and < Size;

    /// <summary>ported from fallout2-ce src/tile.cc tileIsEdge().</summary>
    public static bool IsEdge(int tile) =>
        tile % Width == 0 || tile % Width == Width - 1 || tile / Width == 0 || tile / Width == Height - 1;

    /// <summary>ported from fallout2-ce src/tile.cc tileGetTileInDirection().</summary>
    public static int TileInDirection(int tile, int rotation, int distance = 1)
    {
        int newTile = tile;
        for (int i = 0; i < distance; i++)
        {
            if (IsEdge(newTile))
                break;
            int parity = (newTile % Width) & 1;
            newTile += DirDeltas[parity][rotation];
        }

        return newTile;
    }

    /// <summary>
    /// Camera-independent screen embedding of a hex tile (the tileToScreenXY()
    /// formula anchored at tile grid origin). Differences between two tiles'
    /// embeddings equal their on-screen distance, which is what the
    /// pathfinder's heuristic needs.
    /// </summary>
    public static (int X, int Y) ScreenEmbedding(int tile)
    {
        int v3 = Width - 1 - tile % Width;
        int v4 = tile / Width;

        int screenX = 48 * (v3 / 2);
        int screenY = 12 * (v3 / -2);

        if ((v3 & 1) != 0)
        {
            if (v3 <= 0)
            {
                screenX -= 16;
                screenY += 12;
            }
            else
            {
                screenX += 32;
            }
        }

        screenX += 16 * v4;
        screenY += 12 * v4;
        return (screenX, screenY);
    }

    /// <summary>Corner-correction mask for the 32-wide sub-cell, built exactly like
    /// fallout2-ce src/tile.cc tileSetupTileGrid(): 0 = inside the hex, 1..4 =
    /// NW/NE/SW/SE neighbour correction. Verbatim mirror of the proven viewer port
    /// (Camera.BuildTileMask) — kept here so the pure layer can invert the embedding.</summary>
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

    /// <summary>
    /// Inverse of <see cref="ScreenEmbedding"/>: the hex tile whose cell contains the
    /// embedding-space pixel (screenX, screenY), or -1 if off the grid. Ported from
    /// fallout2-ce src/tile.cc tileFromScreenXY() with all camera offsets zeroed (the
    /// proven viewer port is Camera.ScreenToHex). This is the shared primitive the
    /// screen-Bresenham line-of-fire and the burst cone's end-tile extrapolator both
    /// need — it walks the same pixel space ScreenEmbedding produces.
    /// </summary>
    public static int FromScreenEmbedding(int screenX, int screenY)
    {
        int v2 = screenY;
        int v3 = v2 >= 0 ? v2 / 12 : (v2 + 1) / 12 - 1;

        int v4 = screenX - 16 * v3;
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

        int v10 = v7;
        int v11 = v9;

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

        int v12 = Width - 1 - v11;
        if (v12 >= 0 && v12 < Width && v10 >= 0 && v10 < Height)
            return Width * v10 + v12;

        return -1;
    }

    /// <summary>The tile <paramref name="distance"/> hex-steps from <paramref name="from"/>
    /// along the screen-straight line through <paramref name="to"/> (extrapolating past
    /// it). Ported from fallout2-ce src/tile.cc _tile_num_beyond() — the burst cone's
    /// end-tile extrapolator (combat.cc _compute_spray). Stops early at a grid edge.
    /// </summary>
    public static int TileNumBeyond(int from, int to, int distance)
    {
        if (distance <= 0 || from == to)
            return from;

        (int fromX, int fromY) = ScreenEmbedding(from);
        fromX += 16; fromY += 8;
        (int toX, int toY) = ScreenEmbedding(to);
        toX += 16; toY += 8;

        int stepX = Math.Sign(toX - fromX);
        int stepY = Math.Sign(toY - fromY);
        int v27 = 2 * Math.Abs(toX - fromX);
        int v26 = 2 * Math.Abs(toY - fromY);

        int prev = from;
        int tileX = fromX, tileY = fromY;
        int count = 0;
        int guard = v27 + v26 + 8;

        if (v27 > v26)
        {
            int middle = v26 - v27 / 2;
            while (guard-- > 0)
            {
                int tile = FromScreenEmbedding(tileX, tileY);
                if (tile != prev)
                {
                    if (++count == distance || IsEdge(tile))
                        return tile;
                    prev = tile;
                }
                if (middle >= 0) { middle -= v27; tileY += stepY; }
                middle += v26; tileX += stepX;
            }
        }
        else
        {
            int middle = v27 - v26 / 2;
            while (guard-- > 0)
            {
                int tile = FromScreenEmbedding(tileX, tileY);
                if (tile != prev)
                {
                    if (++count == distance || IsEdge(tile))
                        return tile;
                    prev = tile;
                }
                if (middle >= 0) { middle -= v26; tileX += stepX; }
                middle += v27; tileY += stepY;
            }
        }

        return prev;
    }

    /// <summary>op_tile_in_tile_rect (interpreter_extra.cc:1436 opTileInTileRect): is the test tile inside
    /// the rectangle defined by two corner tiles? Ported VERBATIM incl. the engine's asymmetric corner
    /// mapping — the 5 args are the popped points[0..4]; only [0] (test), [1] and [4] (corners) are used
    /// ([2]/[3] are popped-but-ignored). tile = 200*y + x.</summary>
    public static int TileInTileRect(int testTile, int c1, int c2, int c3, int c4)
    {
        int x = testTile % 200, y = testTile / 200;
        int minX = c1 % 200, maxX = c4 % 200;
        int minY = c4 / 200, maxY = c1 / 200;
        return x >= minX && x <= maxX && y >= minY && y <= maxY ? 1 : 0;
    }

    /// <summary>ported from fallout2-ce src/tile.cc tileGetRotationTo():
    /// the facing rotation from one hex toward another (screen-space angle).</summary>
    public static int RotationTo(int tile1, int tile2)
    {
        (int x1, int y1) = ScreenEmbedding(tile1);
        (int x2, int y2) = ScreenEmbedding(tile2);
        int dx = x2 - x1;
        int dy = y2 - y1;

        if (dx == 0)
            return dy < 0 ? 0 : 2;

        int angle = (int)Math.Truncate(Math.Atan2(-dy, dx) * 180.0 * 0.3183098862851122);
        int rotation = 360 - (angle + 180) - 90;
        if (rotation < 0)
            rotation += 360;
        rotation /= 60;
        return rotation >= RotationCount ? 5 : rotation;
    }

    /// <summary>
    /// Hex distance, ported from fallout2-ce src/tile.cc tileDistanceBetween():
    /// greedily steps toward the target (rotation from the screen-space angle)
    /// counting hexes. -1 tiles yield 9999 like the original.
    /// </summary>
    public static int Distance(int tile1, int tile2)
    {
        if (tile1 == -1 || tile2 == -1)
            return 9999;

        int current = tile1;
        for (int steps = 0; steps < Size; steps++)
        {
            if (current == tile2)
                return steps;

            int next = TileInDirection(current, RotationTo(current, tile2));
            if (next == current)
                return steps; // hit the map edge
            current = next;
        }

        return 9999;
    }

    /// <summary>ported from fallout2-ce src/animation.cc _idist(): octile-ish integer distance.</summary>
    public static int ScreenDistance(int tile1, int tile2)
    {
        (int x1, int y1) = ScreenEmbedding(tile1);
        (int x2, int y2) = ScreenEmbedding(tile2);
        int dx = Math.Abs(x2 - x1);
        int dy = Math.Abs(y2 - y1);
        return dx + dy - Math.Min(dx, dy) / 2;
    }
}
