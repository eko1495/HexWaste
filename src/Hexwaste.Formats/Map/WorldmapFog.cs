namespace Hexwaste.Formats.Map;

/// <summary>
/// Worldmap subtile fog-of-war (phase-22): the per-subtile UNKNOWN/KNOWN/VISITED grid the
/// engine keeps on <c>wmTileInfoList[].subtiles[][].state</c>. As the party dot walks, the
/// radius-1 neighbourhood is marked KNOWN (fogged) and the centre VISITED (clear), with the
/// SUBTILE_FILL_S/W spread revealing contiguous coast/water strips. Pure data + position math
/// (no RNG, no I/O) so a travel leg can reveal silently and the state round-trips in the save.
///
/// Ported from fallout2-ce src/worldmap.cc: wmSubTileMarkRadiusVisited (0x4C35A8),
/// wmMarkSubTileOffsetVisitedFunc (0x4C3408), wmSubTileGetVisitedState (0x4C3740). The
/// PERK_SCOUT radius-2 branch is out of scope (no perk system) — radius is fixed at 1.
/// </summary>
public sealed class WorldmapFog
{
    public const int Unknown = 0; // SUBTILE_STATE_UNKNOWN — hidden (drawn solid black)
    public const int Known = 1;   // SUBTILE_STATE_KNOWN   — seen from afar (drawn fogged/dim)
    public const int Visited = 2; // SUBTILE_STATE_VISITED — walked through (drawn clear)

    private const int TileWidth = 350;   // WM_TILE_WIDTH
    private const int TileHeight = 300;   // WM_TILE_HEIGHT
    private const int SubtileSize = 50;   // WM_SUBTILE_SIZE
    private const int GridW = WorldmapFile.SubtileGridWidth;  // SUBTILE_GRID_WIDTH = 7
    private const int GridH = WorldmapFile.SubtileGridHeight; // SUBTILE_GRID_HEIGHT = 6
    private const int PerTile = GridW * GridH;                // 42 subtiles per worldmap tile
    private const int NumHorizontal = 4;                      // wmNumHorizontalTiles
    private const int NumVertical = 5;
    private const int TileCount = NumHorizontal * NumVertical; // wmMaxTileNum = 20

    private readonly byte[] _state = new byte[TileCount * PerTile];
    private readonly int[] _fill; // per-subtile SUBTILE_FILL_* code, for the spread

    public WorldmapFog(WorldmapFile world)
    {
        _fill = new int[TileCount * PerTile];
        foreach (WorldTile tile in world.Tiles)
        {
            if (tile.Index < 0 || tile.Index >= TileCount)
                continue;
            for (int sx = 0; sx < GridW; sx++)
                for (int sy = 0; sy < GridH; sy++)
                    _fill[Flat(tile.Index, sx, sy)] = tile.Subtiles[sx, sy]?.Fill ?? SubtileFill.None;
        }
    }

    private static int Flat(int tile, int subtileX, int subtileY) => tile * PerTile + subtileX * GridH + subtileY;

    /// <summary>The reveal state of the subtile under a worldmap pixel (UNKNOWN off-grid).</summary>
    public int StateAt(int worldX, int worldY)
    {
        // ported from fallout2-ce src/worldmap.cc wmSubTileGetVisitedState()
        if (!Locate(worldX, worldY, out int tile, out int sx, out int sy))
            return Unknown;
        return _state[Flat(tile, sx, sy)];
    }

    /// <summary>Mark the subtiles around a worldmap pixel as explored — the radius-1 ring KNOWN,
    /// the centre VISITED, plus the SUBTILE_FILL_S/W strip spread. Called per pixel-step as the
    /// party walks (worldmap.cc:870/990/3094 wmMarkSubTileRadiusVisited, radius 1).</summary>
    public void MarkRadiusVisited(int worldX, int worldY)
    {
        // ported from fallout2-ce src/worldmap.cc wmSubTileMarkRadiusVisited() (radius = 1;
        // the PERK_SCOUT radius-2 branch is out of scope).
        if (!Locate(worldX, worldY, out int tile, out int subtileX, out int subtileY))
            return;

        const int radius = 1;
        for (int offsetY = -radius; offsetY <= radius; offsetY++)
            for (int offsetX = -radius; offsetX <= radius; offsetX++)
                MarkOffset(tile, subtileX, subtileY, offsetX, offsetY, Known);

        _state[Flat(tile, subtileX, subtileY)] = Visited;

        switch (_fill[Flat(tile, subtileX, subtileY)])
        {
            case SubtileFill.S:
                while (subtileY-- > 0)
                    MarkOffset(tile, subtileX, subtileY, 0, 0, Visited);
                break;
            case SubtileFill.W:
                while (subtileX-- >= 0)
                    MarkOffset(tile, subtileX, subtileY, 0, 0, Visited);
                if (tile % NumHorizontal > 0)
                    for (subtileX = 0; subtileX < GridW; subtileX++)
                        MarkOffset(tile - 1, subtileX, subtileY, 0, 0, Visited);
                break;
        }
    }

    /// <summary>Mark one subtile (at tile + offset, wrapping across tile boundaries) to the
    /// given state. KNOWN never downgrades an already-VISITED subtile.</summary>
    private void MarkOffset(int tile, int subtileX, int subtileY, int offsetX, int offsetY, int state)
    {
        // ported from fallout2-ce src/worldmap.cc wmMarkSubTileOffsetVisitedFunc()
        int actualTile = tile;
        int actualSubtileX = subtileX + offsetX;
        int actualSubtileY = subtileY + offsetY;

        if (actualSubtileX >= 0)
        {
            if (actualSubtileX >= GridW)
            {
                if (tile % NumHorizontal == NumHorizontal - 1)
                    return;
                actualTile = tile + 1;
                actualSubtileX %= GridW;
            }
        }
        else
        {
            if (tile % NumHorizontal == 0)
                return;
            actualSubtileX += GridW;
            actualTile = tile - 1;
        }

        if (actualSubtileY >= 0)
        {
            if (actualSubtileY >= GridH)
            {
                if (actualTile > TileCount - NumHorizontal - 1)
                    return;
                actualTile += NumHorizontal;
                actualSubtileY %= GridH;
            }
        }
        else
        {
            if (actualTile < NumHorizontal)
                return;
            actualSubtileY += GridH;
            actualTile -= NumHorizontal;
        }

        if (actualTile < 0 || actualTile >= TileCount)
            return;
        int idx = Flat(actualTile, actualSubtileX, actualSubtileY);
        if (state != Known || _state[idx] == Unknown)
            _state[idx] = (byte)state;
    }

    private static bool Locate(int worldX, int worldY, out int tile, out int subtileX, out int subtileY)
    {
        tile = subtileX = subtileY = 0;
        if (worldX < 0 || worldY < 0)
            return false;
        tile = worldX / TileWidth % NumHorizontal + worldY / TileHeight * NumHorizontal;
        if (tile < 0 || tile >= TileCount)
            return false;
        subtileX = worldX % TileWidth / SubtileSize;
        subtileY = worldY % TileHeight / SubtileSize;
        return subtileX < GridW && subtileY < GridH;
    }

    /// <summary>Count subtiles in a given state (UNKNOWN/KNOWN/VISITED) — for the probe/tests.</summary>
    public int CountState(int state)
    {
        int n = 0;
        foreach (byte s in _state)
            if (s == state)
                n++;
        return n;
    }

    /// <summary>Sparse snapshot for the save: flat subtile index → state, only the explored
    /// (non-UNKNOWN) entries (a fresh game saves nothing; a fully-walked map ≤ 840 ints).</summary>
    public Dictionary<int, int> Export()
    {
        var result = new Dictionary<int, int>();
        for (int i = 0; i < _state.Length; i++)
            if (_state[i] != Unknown)
                result[i] = _state[i];
        return result;
    }

    /// <summary>Restore an <see cref="Export"/> snapshot over a freshly-constructed (all-UNKNOWN)
    /// fog. Out-of-range keys / unknown states are ignored so a stale save degrades gracefully.</summary>
    public void Import(IReadOnlyDictionary<int, int> saved)
    {
        foreach ((int index, int state) in saved)
            if (index >= 0 && index < _state.Length && state is Known or Visited)
                _state[index] = (byte)state;
    }
}
