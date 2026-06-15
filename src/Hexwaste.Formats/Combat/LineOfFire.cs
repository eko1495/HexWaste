using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Combat;

/// <summary>
/// Line-of-fire check, ported from fallout2-ce src/animation.cc
/// _make_straight_path_func() (the inner screen-space Bresenham) wrapped by
/// src/combat.cc _combat_is_shot_blocked(): it walks the pixel-straight line
/// between the two tiles' screen centres, mapping each pixel back to a tile via
/// <see cref="Hex.HexGrid.FromScreenEmbedding"/>. Walls and scenery block; living
/// critters never block — they are counted (the −10/critter to-hit term) and the
/// walk resumes past them. The endpoints (from = the shooter, to = the target) are
/// never blocker-checked, matching the engine.
///
/// Retained simplifications (unchanged from the prior greedy port): the host's
/// blockerAt applies the NO_BLOCK / hidden / shoot-thru rules and the dead-critter
/// filter; we do NOT port the +1 MULTIHEX crowd bump (combat.cc:5921) — no
/// shippable-slice critter is multihex mid-line, and it would shift the to-hit term.
/// </summary>
public static class LineOfFire
{
    /// <summary>blockerAt returns a wall/scenery/living-critter object on the
    /// tile (the host applies the NO_BLOCK/hidden/shoot-thru rules); null = clear.</summary>
    public static (MapObject? Blocker, int CrittersInPath) Trace(
        int fromTile, int toTile, Func<int, MapObject?> blockerAt)
    {
        if (fromTile == toTile)
            return (null, 0);

        // Tile screen centres (+16,+8 = the visual centre; animation.cc:1966-1973).
        (int fromX, int fromY) = Hex.HexGrid.ScreenEmbedding(fromTile);
        fromX += 16; fromY += 8;
        (int toX, int toY) = Hex.HexGrid.ScreenEmbedding(toTile);
        toX += 16; toY += 8;

        int stepX = Math.Sign(toX - fromX);
        int stepY = Math.Sign(toY - fromY);
        int ddx = 2 * Math.Abs(toX - fromX);
        int ddy = 2 * Math.Abs(toY - fromY);

        int tileX = fromX, tileY = fromY;
        int prevTile = fromTile;
        int critters = 0;

        // Guard against any FromScreenEmbedding edge case never terminating; the
        // Bresenham reaches the destination in ~max(ddx,ddy)/2 steps.
        int guard = ddx + ddy + 8;

        if (ddx <= ddy)
        {
            int middle = ddx - ddy / 2;
            while (guard-- > 0)
            {
                int tile = Hex.HexGrid.FromScreenEmbedding(tileX, tileY);
                if (tileY == toY)
                    break;
                if (middle >= 0) { tileX += stepX; middle -= ddy; }
                tileY += stepY; middle += ddx;

                if (tile != prevTile)
                {
                    // Endpoints are never blockers: the shooter (from) is excluded
                    // host-side, and the target (to) is the engine's "obstacle !=
                    // targetObj" — the pixel cursor maps to `to` for a few steps
                    // before reaching its exact centre, so guard it here.
                    if (tile >= 0 && tile != fromTile && tile != toTile && blockerAt(tile) is { } obj)
                    {
                        if (Fid.Type(obj.Fid) is ObjectType.Critter)
                            critters++; // counted, resume past (combat.cc:5912-5938)
                        else
                            return (obj, critters);
                    }
                    prevTile = tile;
                }
            }
        }
        else
        {
            int middle = ddy - ddx / 2;
            while (guard-- > 0)
            {
                int tile = Hex.HexGrid.FromScreenEmbedding(tileX, tileY);
                if (tileX == toX)
                    break;
                if (middle >= 0) { tileY += stepY; middle -= ddx; }
                tileX += stepX; middle += ddy;

                if (tile != prevTile)
                {
                    if (tile >= 0 && tile != fromTile && tile != toTile && blockerAt(tile) is { } obj)
                    {
                        if (Fid.Type(obj.Fid) is ObjectType.Critter)
                            critters++;
                        else
                            return (obj, critters);
                    }
                    prevTile = tile;
                }
            }
        }

        return (null, critters);
    }
}
