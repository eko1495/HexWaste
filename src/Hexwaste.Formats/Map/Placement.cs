namespace Hexwaste.Formats.Map;

/// <summary>
/// Object placement search, a simplified port of fallout2-ce src/object.cc <c>_obj_attempt_placement</c>
/// (used by <c>critter_attempt_placement</c>): put the object on the requested tile, or — if it is
/// blocked — on a nearby free tile. The engine spiral-searches outward; the PoC checks the requested
/// tile then its six immediate neighbours (radius 1), falling back to the requested tile (best-effort).
/// </summary>
public static class Placement
{
    public static int FreeTileNear(int tile, Func<int, bool> isBlocked)
    {
        if (!Hex.HexGrid.IsValid(tile) || !isBlocked(tile))
            return tile;
        for (int dir = 0; dir < 6; dir++)
        {
            int t = Hex.HexGrid.TileInDirection(tile, dir);
            if (Hex.HexGrid.IsValid(t) && !isBlocked(t))
                return t;
        }
        return tile; // best-effort: everything nearby is blocked
    }
}
