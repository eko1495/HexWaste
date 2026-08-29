using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Combat;

/// <summary>
/// Line-of-fire check, ported from fallout2-ce src/animation.cc
/// _make_straight_path_func() (the inner screen-space Bresenham) wrapped by
/// src/combat.cc _combat_is_shot_blocked(): it walks the pixel-straight line
/// between the two tiles' screen centres, mapping each pixel back to a tile via
/// <see cref="Hex.HexGrid.FromScreenEmbedding"/>. A shoot trace's SHOOT_THRU objects are dropped by
/// the walker itself (<see cref="Suppresses"/>); of what survives that, WHAT blocks is the caller's
/// <see cref="ShotFilter"/>: an object the filter does not treat as an
/// obstruction is walked past, and if it is a living critter that is not the caller's target it is
/// counted (the −10/critter to-hit term, combat.cc:5912). The shooter's own tile is never
/// blocker-checked here — a DIVERGENCE from the reference, which does probe it
/// (_make_straight_path_func, animation.cc:1954; see the in-loop comment in Trace for the
/// mechanism). The target tile IS checked, and the filter decides.
///
/// Retained simplifications (unchanged from the prior greedy port): the host's
/// blockerAt applies only `hidden` and the reference's own coarse disjunction
/// (_obj_shoot_blocking_at's NO_BLOCK||SHOOT_THRU test) plus the dead-critter filter;
/// the NO_BLOCK/SHOOT_THRU FLAG CONJUNCTION each caller actually wants is applied by
/// the caller's <see cref="ShotFilter"/> inside Trace, not by blockerAt itself.
/// The WALKER's own guard is ported here (see <see cref="Suppresses"/>): a SHOOT_THRU object is
/// never reported to a shoot caller at all, so the callers' <see cref="ShotFilter"/> SHOOT_THRU
/// terms are redundant confirmations rather than the mechanism. We do NOT port the
/// +1 MULTIHEX crowd bump (combat.cc:5921) — no shippable-slice critter is multihex
/// mid-line, and it would shift the to-hit term.
/// </summary>
public static class LineOfFire
{
    private const int ShootThru = unchecked((int)0x80000000);

    /// <summary>The reference's `a6` for a LINE-OF-FIRE trace. All five shoot callers pass 32
    /// (combat.cc:3584, :3641, :3956, :5906 and combat_ai.cc:2585), which is what arms the walker's
    /// SHOOT_THRU guard — see <see cref="Suppresses"/>.</summary>
    public const int ShootTraceStride = 32;

    /// <summary>The reference's `a6` for the obj_can_see_obj SIGHT trace
    /// (interpreter_extra.cc:1797 passes 16 to _make_straight_path / _obj_blocking_at). Not 32, so
    /// the walker's SHOOT_THRU guard does not apply to it.</summary>
    public const int SightTraceStride = 16;

    /// <summary>ported from fallout2-ce src/animation.cc:1956, :2050 and :2103 — _make_straight_path_func's
    /// OWN guard, repeated at ALL THREE of its callback sites (the `from` probe at :1954 and the two
    /// Bresenham loops):
    /// `if (obstacle != *obstaclePtr &amp;&amp; (a6 != 32 || (obstacle->flags &amp; OBJECT_SHOOT_THRU) == 0))`.
    /// All five line-of-fire callers pass a6 == 32 (combat.cc:3584, :3641, :3956, :5906,
    /// combat_ai.cc:2585), so for every shoot trace the guard reduces to
    /// `(flags &amp; OBJECT_SHOOT_THRU) == 0`: the walker never assigns a SHOOT_THRU object to the
    /// caller's obstacle pointer and never stops on it, so NO shoot caller ever sees one. That is
    /// stronger than "not a blocker" — the object is invisible to the caller, which is why a
    /// SHOOT_THRU critter is also never counted in _combat_is_shot_blocked's numCrittersOnLof
    /// (combat.cc:5912 counts only the obstacles the walker reported). The sight caller passes
    /// a6 == 16, so the guard is off for it.
    ///
    /// The stride is deliberately NOT defaulted: the guard's whole content is "for a6 == 32", and a
    /// default would let a call site silently assume a stride it never chose. <see cref="Trace"/> is
    /// its only caller — callers that need to observe walked objects use Trace's
    /// <c>onCandidate</c> hook, which fires AFTER this guard.</summary>
    public static bool Suppresses(MapObject candidate, int stride) =>
        stride == ShootTraceStride && (candidate.Flags & ShootThru) != 0;

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
    /// crowd count's `obstacle != targetObj` exclusion (combat.cc:5912). null = the caller has no
    /// target identity, and nothing on the line is ever treated as one.</param>
    /// <param name="stride">The reference's `a6`. <see cref="ShootTraceStride"/> (32, the default —
    /// every line-of-fire caller) arms the walker's own SHOOT_THRU guard; the obj_can_see_obj SIGHT
    /// trace passes <see cref="SightTraceStride"/> (16) and opts out of it. See
    /// <see cref="Suppresses"/>.</param>
    /// <param name="onCandidate">Optional bookkeeping hook, called with (object, tile) for every
    /// object the walk actually SEES — i.e. after <see cref="Suppresses"/> and before the filter,
    /// exactly where the reference's callers read back their obstacle pointer. Callers that need to
    /// record objects (the burst walk's victim list, the missed-shot walk's accidental target) must
    /// use this rather than doing the bookkeeping inside <paramref name="blockerAt"/>: blockerAt is
    /// the raw per-TILE coarse predicate and runs before the walker's guard, so a side effect there
    /// would see suppressed objects the caller must never see, and no walker-side guard could undo
    /// it. F33 (Task 7): both callers previously repeated Suppresses by hand in their blockerAt; a
    /// future one would have silently forgotten to.</param>
    public static (MapObject? Blocker, int CrittersInPath) Trace(
        int fromTile, int toTile, Func<int, MapObject?> blockerAt, ShotFilter filter,
        MapObject? targetObj = null, int stride = ShootTraceStride,
        Action<MapObject, int>? onCandidate = null)
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
                    // DIVERGENCE from the reference: we never blocker-check the shooter's own
                    // tile at all, but _make_straight_path_func (animation.cc:1954) DOES probe
                    // `from` first, via a callback compared against excludeObj — and excludeObj
                    // excludes the shooter OBJECT, not the shooter's tile. So a second object
                    // standing on the shooter's hex is reported as blocking there and is not
                    // here. The TARGET tile is checked — the engine's "obstacle != targetObj"
                    // is an identity test in the caller's filter, not a tile skip.
                    if (tile >= 0 && tile != fromTile && blockerAt(tile) is { } obj
                        && !Suppresses(obj, stride))
                    {
                        onCandidate?.Invoke(obj, tile);
                        bool isTarget = targetObj is not null && ReferenceEquals(obj, targetObj);
                        if (filter.Obstructs(obj, isTarget))
                            return (obj, critters);
                        // Not an obstruction for this caller: the walk resumes past it. A living
                        // critter that is not the target is the -10/critter to-hit term
                        // (combat.cc:5912-5919 counts `obstacle != targetObj` only).
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
                    if (tile >= 0 && tile != fromTile && blockerAt(tile) is { } obj
                        && !Suppresses(obj, stride))
                    {
                        onCandidate?.Invoke(obj, tile);
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
