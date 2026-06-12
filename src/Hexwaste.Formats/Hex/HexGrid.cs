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

    /// <summary>
    /// Hex distance, ported from fallout2-ce src/tile.cc tileDistanceBetween():
    /// greedily steps toward the target (rotation from the screen-space angle)
    /// counting hexes. -1 tiles yield 9999 like the original.
    /// </summary>
    public static int Distance(int tile1, int tile2)
    {
        if (tile1 == -1 || tile2 == -1)
            return 9999;

        (int targetX, int targetY) = ScreenEmbedding(tile2);

        int current = tile1;
        for (int steps = 0; steps < Size; steps++)
        {
            if (current == tile2)
                return steps;

            (int x, int y) = ScreenEmbedding(current);
            int dx = targetX - x;
            int dy = targetY - y;

            int rotation;
            if (dx == 0)
            {
                rotation = dy < 0 ? 0 : 2;
            }
            else
            {
                int angle = (int)Math.Truncate(Math.Atan2(-dy, dx) * 180.0 * 0.3183098862851122);
                rotation = 360 - (angle + 180) - 90;
                if (rotation < 0)
                    rotation += 360;
                rotation /= 60;
                if (rotation >= 6)
                    rotation = 5;
            }

            int next = TileInDirection(current, rotation);
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
