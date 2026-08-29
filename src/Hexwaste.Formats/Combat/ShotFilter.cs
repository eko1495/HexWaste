using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Combat;

/// <summary>
/// What one line-of-fire caller does with the object the COARSE predicate hands back.
/// ported from fallout2-ce: _obj_shoot_blocking_at (src/object.cc:2440) is deliberately
/// coarse, and each caller applies its own filter. Those filters are combinations of three
/// independent terms, so they are modelled as terms rather than as opaque named policies —
/// the differences between callers are the whole point and should be readable.
///
/// ExcludesShootThru is NOT one of those differences. _make_straight_path_func's own guard
/// (animation.cc:1957/:2039, ported as <see cref="LineOfFire.Suppresses"/>) already hides every
/// SHOOT_THRU object from every shoot caller, because all five pass a6 == 32. Each filter below
/// therefore mirrors its caller's SOURCE LINES — true only where the reference itself re-tests the
/// flag (combat.cc:3586 and :3963) — and on a walker-reported object that term is unreachable
/// either way. It is load-bearing in exactly one place: <see cref="AccidentalTarget"/> is also
/// applied OUTSIDE the walker, to combat.cc:3961's _obj_blocking_at endpoint fallback, which the
/// walker guard never touches and :3963 does test.
/// </summary>
/// <param name="ExcludesShootThru">A SHOOT_THRU object is not an obstruction for this caller. A
/// redundant confirmation for walker-reported objects (see the type remarks) — never the mechanism.</param>
/// <param name="ExcludesCritters">An object whose FID type is OBJ_TYPE_CRITTER is not a hard
/// obstruction — it is a hit candidate and the walk continues. No liveness test is applied here;
/// the coarse predicate (ShootBlockerAt/_obj_shoot_blocking_at) already drops corpses before this
/// filter ever runs, matching the reference callers, which test FID_TYPE only.</param>
/// <param name="ExcludesTarget">The caller's own target is not an obstruction.</param>
public sealed record ShotFilter(
    bool ExcludesShootThru,
    bool ExcludesCritters,
    bool ExcludesTarget)
{
    private const int ShootThru = unchecked((int)0x80000000);

    public bool Obstructs(MapObject candidate, bool isTarget) =>
        !(ExcludesShootThru && (candidate.Flags & ShootThru) != 0)
        && !(ExcludesCritters && Fid.Type(candidate.Fid) is ObjectType.Critter)
        && !(ExcludesTarget && isTarget);

    /// NOTE on NO_BLOCK: it is deliberately NOT a term here. _obj_shoot_blocking_at
    /// (object.cc:2440) gates on the DISJUNCTION `NO_BLOCK == 0 || SHOOT_THRU == 0`, and no
    /// reference caller re-tests NO_BLOCK afterwards, so a NO_BLOCK-but-not-SHOOT_THRU object
    /// reported by the coarse predicate really does obstruct. A pre-F33 `ExcludesNoBlock` term
    /// existed only to reproduce the collapsed flag CONJUNCTION while consumers were migrated;
    /// every consumer now carries its reference filter, so the term is gone.

    /// <summary>ported from fallout2-ce src/combat.cc:3586-3587 — the shot-blocked roll.</summary>
    public static readonly ShotFilter ShotBlockedRoll = new(true, true, false);

    /// <summary>ported from fallout2-ce src/combat.cc:3644 — the burst / continuous walk. Its only
    /// test is `FID_TYPE(critter->fid) != OBJ_TYPE_CRITTER`; it does not re-test the flag because it
    /// does not have to — the walker never hands it a SHOOT_THRU object (animation.cc:1957), so such
    /// an object does NOT end this walk.</summary>
    public static readonly ShotFilter BurstWalk = new(false, true, false);

    /// <summary>ported from fallout2-ce src/combat.cc:3963 — the missed-shot collateral target.
    /// No type test: a critter DOES count here, unlike every other caller. Its SHOOT_THRU term is
    /// the one that is load-bearing, for the :3961 _obj_blocking_at endpoint fallback the walker
    /// never sees.</summary>
    public static readonly ShotFilter AccidentalTarget = new(true, false, false);

    /// <summary>ported from fallout2-ce src/combat.cc:5908 — the filter INSIDE
    /// _combat_is_shot_blocked itself: `FID_TYPE(obstacle->fid) != OBJ_TYPE_CRITTER &amp;&amp;
    /// obstacle != targetObj`. No flag test: the walker already dropped SHOOT_THRU objects, which
    /// is also why such an object is never counted in numCrittersOnLof (:5911). Because the filter
    /// belongs to the FUNCTION and not to one call site, it is the filter for every caller of
    /// _combat_is_shot_blocked — the to-hit penalty (combat.cc:5906), the explosion victim's
    /// line-of-sight (combat.cc:4055) and the combat outline (combat.cc:2684). Those callers differ
    /// only in what they pass as sourceObj and targetObj, which are Trace's own arguments.</summary>
    public static readonly ShotFilter ShotBlocked = new(false, true, true);

    /// <summary>ported from fallout2-ce src/combat_ai.cc:2586 — the friendly-fire check, which
    /// applies no flag or type test at all to what it is handed; the flag it would need was already
    /// applied by the walker (animation.cc:1957, a6 == 32).</summary>
    public static readonly ShotFilter FriendlyFire = new(false, false, false);
}
