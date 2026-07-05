namespace Hexwaste.Formats.Map;

/// <summary>
/// The connected-roof flood fill, ported from fallout2-ce src/tile.cc tile_fill_roof /
/// roof_fill_off_process_task (:1284-1325): starting at the square under the dude, walk
/// 4-connected square-grid neighbours collecting every NON-EMPTY roof tile (id &amp; 0xFFF != 1
/// — the engine's buildFid(TILE, id) != buildFid(TILE, 1) check). The engine toggles a hide
/// flag (bit 0x01 of the high-word flags nibble) in the square data; Hexwaste recomputes the
/// set per dude-square change instead (stateless, same visible result). Triggered like
/// object.cc _obj_move_to_tile (:1446): only the block CONNECTED to the dude hides —
/// stepping under one building no longer blanks every roof on the map. (P117.)
/// </summary>
public static class RoofFill
{
    /// <summary>The set of square indices whose roofs hide for a dude standing on
    /// <paramref name="startSquare"/>; empty when that square has no roof.</summary>
    public static HashSet<int> ConnectedRoofSquares(MapElevation elevation, int startSquare)
    {
        var hidden = new HashSet<int>();
        if (startSquare < 0 || startSquare >= MapElevation.SquareGridSize
            || (elevation.RoofTileId(startSquare) & 0xFFF) == 1)
            return hidden;

        var stack = new Stack<int>();
        stack.Push(startSquare);
        while (stack.Count > 0)
        {
            int square = stack.Pop();
            if (!hidden.Add(square))
                continue;
            int x = square % MapElevation.SquareGridWidth;
            int y = square / MapElevation.SquareGridWidth;
            foreach ((int nx, int ny) in stackalloc[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                if (nx < 0 || nx >= MapElevation.SquareGridWidth || ny < 0 || ny >= MapElevation.SquareGridHeight)
                    continue;
                int neighbour = ny * MapElevation.SquareGridWidth + nx;
                if (!hidden.Contains(neighbour) && (elevation.RoofTileId(neighbour) & 0xFFF) != 1)
                    stack.Push(neighbour);
            }
        }
        return hidden;
    }
}
