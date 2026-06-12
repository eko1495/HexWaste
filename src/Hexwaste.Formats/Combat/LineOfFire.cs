using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Combat;

/// <summary>
/// Line-of-fire check, modeled on fallout2-ce src/combat.cc
/// _combat_is_shot_blocked() + object.cc _obj_shoot_blocking_at(): walls and
/// scenery block; living critters never block — they are counted (the
/// −10/critter to-hit term) and the walk resumes past them. DEVIATION: the
/// engine traces a pixel Bresenham in screen space (animation.cc:1951); this
/// walks hexes greedily by screen angle (the tileDistanceBetween step rule) —
/// equivalent for practical cover, off by a corner case at long diagonals.
/// </summary>
public static class LineOfFire
{
    /// <summary>blockerAt returns a wall/scenery/living-critter object on the
    /// tile (the host applies the NO_BLOCK/hidden/shoot-thru rules); null = clear.</summary>
    public static (MapObject? Blocker, int CrittersInPath) Trace(
        int fromTile, int toTile, Func<int, MapObject?> blockerAt)
    {
        int critters = 0;
        int current = fromTile;
        for (int guard = 0; guard < Hex.HexGrid.Size && current != toTile; guard++)
        {
            int next = Hex.HexGrid.TileInDirection(current, Hex.HexGrid.RotationTo(current, toTile));
            if (next == current)
                break; // map edge
            current = next;
            if (current == toTile)
                break;

            if (blockerAt(current) is { } obj)
            {
                if (Fid.Type(obj.Fid) is ObjectType.Critter)
                    critters++; // counted, resumed past (combat.cc:5912-5938)
                else
                    return (obj, critters);
            }
        }

        return (null, critters);
    }
}
