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
                yield break; // walked off the grid edge — TileInDirection clamps, so stop rather than spin
            ringFirstTile = tile;
            int rotation = RotationSe;
            int ringTileIdx = 0;
            yield return tile;

            // 6*radius steps close a hex ring; the guard is a backstop for edge-clamped tiles.
            for (int step = 0; step < 6 * radius; step++)
            {
                int next = HexGrid.TileInDirection(tile, rotation);
                if (next == ringFirstTile || next == tile)
                    break; // ring closed (or clamped at the grid edge)
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
