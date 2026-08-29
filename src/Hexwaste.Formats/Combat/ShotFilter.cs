using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Combat;

/// <summary>
/// What one line-of-fire caller does with the object the COARSE predicate hands back.
/// ported from fallout2-ce: _obj_shoot_blocking_at (src/object.cc:2440) is deliberately
/// coarse, and each caller applies its own filter. Those filters are combinations of three
/// independent terms, so they are modelled as terms rather than as opaque named policies —
/// the differences between callers are the whole point and should be readable.
/// </summary>
/// <param name="ExcludesShootThru">A SHOOT_THRU object is not an obstruction for this caller.</param>
/// <param name="ExcludesCritters">An object whose FID type is OBJ_TYPE_CRITTER is not a hard
/// obstruction — it is a hit candidate and the walk continues. No liveness test is applied here;
/// the coarse predicate (ShootBlockerAt/_obj_shoot_blocking_at) already drops corpses before this
/// filter ever runs, matching the reference callers, which test FID_TYPE only.</param>
/// <param name="ExcludesTarget">The caller's own target is not an obstruction.</param>
/// <param name="ExcludesNoBlock">TEMPORARY, no reference counterpart: reproduces the pre-F33
/// collapsed behaviour. Every consumer moves off <see cref="LegacyCollapsed"/> in Task 5 and it
/// is deleted in Task 7.</param>
public sealed record ShotFilter(
    bool ExcludesShootThru,
    bool ExcludesCritters,
    bool ExcludesTarget,
    bool ExcludesNoBlock = false)
{
    private const int NoBlock = 0x10;
    private const int ShootThru = unchecked((int)0x80000000);

    public bool Obstructs(MapObject candidate, bool isTarget) =>
        !(ExcludesShootThru && (candidate.Flags & ShootThru) != 0)
        && !(ExcludesCritters && Fid.Type(candidate.Fid) is ObjectType.Critter)
        && !(ExcludesTarget && isTarget)
        && !(ExcludesNoBlock && (candidate.Flags & NoBlock) != 0);

    /// <summary>ported from fallout2-ce src/combat.cc:3586-3587 — the shot-blocked roll.</summary>
    public static readonly ShotFilter ShotBlockedRoll = new(true, true, false);

    /// <summary>ported from fallout2-ce src/combat.cc:3644 — the burst / continuous walk.
    /// No flag test: a SHOOT_THRU object DOES end this walk.</summary>
    public static readonly ShotFilter BurstWalk = new(false, true, false);

    /// <summary>ported from fallout2-ce src/combat.cc:3963 — the missed-shot collateral target.
    /// No type test: a critter DOES count here, unlike every other caller.</summary>
    public static readonly ShotFilter AccidentalTarget = new(true, false, false);

    /// <summary>ported from fallout2-ce src/combat.cc:5908 — combat_is_shot_blocked's penalty.</summary>
    public static readonly ShotFilter ShotBlockedPenalty = new(false, true, true);

    /// <summary>ported from fallout2-ce src/combat_ai.cc:2586 — the friendly-fire check,
    /// which applies no flag or type test at all.</summary>
    public static readonly ShotFilter FriendlyFire = new(false, false, false);

    /// <summary>TEMPORARY. The pre-F33 collapsed behaviour, so the coarse predicate can be made
    /// faithful without changing what any consumer sees. Has no reference counterpart and must
    /// never be the answer for a shipped consumer.
    ///
    /// ExcludesCritters is FALSE — load-bearing, not incidental. The pre-F33 single-stage predicate
    /// never excluded critters at this layer: they were part of the coarse type test, and
    /// LineOfFire.Trace's own walker does the critter-vs-blocker split downstream, unchanged by this
    /// task. Setting it here (as the plan's first draft did) silently drops every critter the coarse
    /// predicate hands back — including from LineOfFire.Trace's blockerAt callback, where Trace needs
    /// the raw critter object to run its own counted-not-blocking logic — which four CombatEngineTests
    /// caught immediately (bystander/collateral detection went dark).
    ///
    /// ExcludesTarget is TRUE — unlike the other terms, this one is NOT chosen for today's behaviour.
    /// It is inert today: ShootBlockerAt still filters `o != target` identity-based inside itself
    /// (pre-Task-4), so `isTarget` can never be true at any of the 11 call sites and this term never
    /// fires either way. It is set to `true` purely for FORWARD SAFETY: Task 4 moves that exclusion
    /// out of ShootBlockerAt into a caller-supplied parameter, at which point `isTarget` goes live at
    /// every site, and only `true` here reproduces the pre-F33 collapsed behaviour from that point on.
    /// Composed with the new coarse predicate's `NO_BLOCK==0 || SHOOT_THRU==0`, `Obstructs` here
    /// reduces (today) to exactly `NO_BLOCK==0 && SHOOT_THRU==0` — the old flag conjunction, nothing
    /// more; the `ExcludesTarget` term simply has no candidate to fire on yet.</summary>
    public static readonly ShotFilter LegacyCollapsed = new(true, false, true, ExcludesNoBlock: true);
}
