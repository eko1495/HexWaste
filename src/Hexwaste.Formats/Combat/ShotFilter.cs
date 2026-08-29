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
/// <param name="ExcludesCritters">A living critter is not a hard obstruction — it is a hit
/// candidate and the walk continues.</param>
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
    /// ExcludesCritters/ExcludesTarget are deliberately FALSE here, unlike every real reference
    /// filter above: the pre-F33 single-stage predicate never excluded critters or the target at
    /// this layer (target exclusion was — and still is — identity-based inside ShootBlockerAt
    /// itself; critters were part of the coarse type test, and LineOfFire.Trace's own walker does
    /// the critter-vs-blocker split downstream, unchanged by this task). Composed with the new
    /// coarse predicate's `NO_BLOCK==0 || SHOOT_THRU==0`, `Obstructs` here reduces to exactly
    /// `NO_BLOCK==0 && SHOOT_THRU==0` — the old flag conjunction, nothing more. Setting
    /// ExcludesCritters here (as the plan's first draft did) silently drops every critter the
    /// coarse predicate hands back — including from LineOfFire.Trace's blockerAt callback, where
    /// Trace needs the raw critter object to run its own counted-not-blocking logic — which four
    /// CombatEngineTests caught immediately (bystander/collateral detection went dark).</summary>
    public static readonly ShotFilter LegacyCollapsed = new(true, false, false, ExcludesNoBlock: true);
}
