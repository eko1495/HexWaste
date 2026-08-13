using Hexwaste.Formats.Hex;

namespace Hexwaste.Formats.Combat;

/// <summary>
/// ported from fallout2-ce src/combat.cc _compute_explosion_on_extras (:4022-4045): the ring-by-ring
/// tile walk an explosion uses to find its victims. Each ring opens at the NE neighbour of the
/// previous ring's first tile with rotation SE, advances one tile per step, and rotates one step
/// further whenever <c>ringTileIdx % radius == 0</c> ("the larger the radius, the slower we rotate",
/// :4026); a ring ends when the walk returns to its first tile. The BLAST TILE ITSELF IS NEVER
/// ENUMERATED — the reference starts at radius 1, because the critter standing there is the primary
/// defender handled by the main attack path.
///
/// PURE: tile arithmetic only. The caller applies radius limits, line-of-sight, damage and caps.
///
/// DOCUMENTED DIVERGENCE: When a ring walk reaches a grid edge, the reference re-processes the clamped
/// tile as duplicate extras in subsequent steps, whereas this port stops early instead. Both the reference's
/// <c>tileGetTileInDirection</c> (tile.cc:893-906) and this port's <c>TileInDirection</c> clamp at grid edges
/// and return the last valid tile unchanged — neither returns -1 or a sentinel. No committed test exercises
/// a near-edge blast radius.
/// </summary>
public static class ExplosionSpiral
{
    private const int RotationNe = 0, RotationSe = 2, RotationCount = 6;

    /// <summary>Tiles in reference order, outward from (but excluding) <paramref name="centerTile"/>,
    /// for rings 1..<paramref name="maxRadius"/>.</summary>
    public static IEnumerable<int> Tiles(int centerTile, int maxRadius)
    {
        if (maxRadius < 1)
            yield break;

        int ringFirstTile = centerTile;
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            // Each ring opens NE of the previous ring's first tile (combat.cc:4040).
            int tile = HexGrid.TileInDirection(ringFirstTile, RotationNe);
            if (tile == ringFirstTile)
                yield break; // grid edge: TileInDirection clamped and returned the same tile; stop to avoid duplicates
            ringFirstTile = tile;
            int rotation = RotationSe;
            int ringTileIdx = 0;
            yield return tile;

            // 6*radius steps close a hex ring. If TileInDirection clamps at a grid edge, it returns the same
            // tile unchanged — we detect this and stop to avoid re-processing it as duplicate extras.
            for (int step = 0; step < 6 * radius; step++)
            {
                int next = HexGrid.TileInDirection(tile, rotation);
                if (next == ringFirstTile || next == tile)
                    break; // ring closed (or next tile is same as current, indicating edge clamp)
                tile = next;
                yield return tile;

                ringTileIdx++;
                if (ringTileIdx % radius == 0)
                {
                    rotation++;
                    if (rotation == RotationCount)
                        rotation = RotationNe;
                }
            }
        }
    }
}
