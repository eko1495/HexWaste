using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Combat;

/// <summary>
/// Line-of-fire check, ported from fallout2-ce src/animation.cc
/// _make_straight_path_func() (the inner screen-space Bresenham) wrapped by
/// src/combat.cc _combat_is_shot_blocked(): it walks the pixel-straight line
/// between the two tiles' screen centres, mapping each pixel back to a tile via
/// <see cref="Hex.HexGrid.FromScreenEmbedding"/>. WHAT blocks is the caller's
/// <see cref="ShotFilter"/>, not this walker: an object the filter does not treat as an
/// obstruction is walked past, and if it is a living critter that is not the caller's target it is
/// counted (the −10/critter to-hit term, combat.cc:5911). The shooter's own tile is never
/// blocker-checked, matching the engine; the target tile IS, and the filter decides.
///
/// Retained simplifications (unchanged from the prior greedy port): the host's
/// blockerAt applies only `hidden` and the reference's own coarse disjunction
/// (_obj_shoot_blocking_at's NO_BLOCK||SHOOT_THRU test) plus the dead-critter filter;
/// the NO_BLOCK/SHOOT_THRU FLAG CONJUNCTION each caller actually wants is applied by
/// the caller's <see cref="ShotFilter"/> inside Trace, not by blockerAt itself.
/// NOT PORTED (F33 Task 5 finding, see docs): _make_straight_path_func's OWN
/// `a6 != 32 || (obstacle->flags &amp; OBJECT_SHOOT_THRU) == 0` guard (animation.cc:1957/2039) —
/// every shoot caller passes a6 == 32, so the reference walker itself never stops on a SHOOT_THRU
/// object regardless of what the caller's own re-test says. We do NOT port the
/// +1 MULTIHEX crowd bump (combat.cc:5921) — no shippable-slice critter is multihex
/// mid-line, and it would shift the to-hit term.
/// </summary>
public static class LineOfFire
{
    /// <summary>blockerAt returns the RAW coarse predicate's answer for the tile — a
    /// wall/scenery/living-critter object, with only `hidden` and the reference's own coarse
    /// disjunction applied (host-side ShootBlockerAt / _obj_shoot_blocking_at); null = nothing there.
    /// It must NOT be pre-filtered: <paramref name="filter"/> is applied HERE, because the
    /// counted-not-blocking split needs the object a filter would otherwise have destroyed.
    ///
    /// F33 (Task 5) shape change: <see cref="ShotFilter.ExcludesCritters"/> and
    /// <see cref="ShotFilter.ExcludesTarget"/> used to be hard-coded in this walker (critters were
    /// always counted-and-walked-past; the target TILE was always skipped). They are caller policy —
    /// combat.cc:5908's `FID_TYPE != OBJ_TYPE_CRITTER && obstacle != targetObj` — and only one of the
    /// five reference callers applies both, so they moved into the filter. The target is now
    /// identified by OBJECT IDENTITY (<paramref name="targetObj"/>), which is what the reference
    /// compares; the reference's own walker (_make_straight_path_func, animation.cc:1951) does query
    /// the destination tile.</summary>
    /// <param name="filter">The caller's policy — what the coarse predicate's answer means to it.</param>
    /// <param name="targetObj">The caller's target, for the filter's ExcludesTarget term and for the
    /// crowd count's `obstacle != targetObj` exclusion (combat.cc:5911). null = the caller has no
    /// target identity, and nothing on the line is ever treated as one.</param>
    public static (MapObject? Blocker, int CrittersInPath) Trace(
        int fromTile, int toTile, Func<int, MapObject?> blockerAt, ShotFilter filter,
        MapObject? targetObj = null)
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
                    // The shooter's own tile is never blocker-checked (the reference
                    // excludes it host-side, via _make_straight_path_func's excludeObj).
                    // The TARGET tile is checked — the engine's "obstacle != targetObj"
                    // is an identity test in the caller's filter, not a tile skip.
                    if (tile >= 0 && tile != fromTile && blockerAt(tile) is { } obj)
                    {
                        bool isTarget = targetObj is not null && ReferenceEquals(obj, targetObj);
                        if (filter.Obstructs(obj, isTarget))
                            return (obj, critters);
                        // Not an obstruction for this caller: the walk resumes past it. A living
                        // critter that is not the target is the -10/critter to-hit term
                        // (combat.cc:5911-5919 counts `obstacle != targetObj` only).
                        if (Fid.Type(obj.Fid) is ObjectType.Critter && !isTarget)
                            critters++;
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
                    if (tile >= 0 && tile != fromTile && blockerAt(tile) is { } obj)
                    {
                        bool isTarget = targetObj is not null && ReferenceEquals(obj, targetObj);
                        if (filter.Obstructs(obj, isTarget))
                            return (obj, critters);
                        if (Fid.Type(obj.Fid) is ObjectType.Critter && !isTarget)
                            critters++;
                    }
                    prevTile = tile;
                }
            }
        }

        return (null, critters);
    }
}
