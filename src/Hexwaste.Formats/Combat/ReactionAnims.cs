namespace Hexwaste.Formats.Combat;

/// <summary>
/// The defender's reaction-animation SELECTION on an attack resolve, ported from fallout2-ce
/// src/actions.cc _show_damage_to_object / _action_melee + src/animation.cc _dude_standup.
/// Pure (returns anim CODES); the host resolves art existence + builds FIDs (like DeathAnims).
/// </summary>
public static class ReactionAnims
{
    public const int DodgeAnim = 13;       // ANIM_DODGE_ANIM
    public const int HitFromFront = 14;    // ANIM_HIT_FROM_FRONT
    public const int HitFromBack = 15;     // ANIM_HIT_FROM_BACK
    public const int FallBack = 20;        // ANIM_FALL_BACK
    public const int FallFront = 21;       // ANIM_FALL_FRONT
    public const int ProneToStanding = 36; // ANIM_PRONE_TO_STANDING
    public const int BackToStanding = 37;  // ANIM_BACK_TO_STANDING

    /// <summary>The dodge anim played on a miss (the caller gates it on a non-prone defender).</summary>
    public const int Dodge = DodgeAnim;

    /// <summary>
    /// ported from actions.cc:425 _show_damage_to_object: a plain hit shows HIT_FROM_FRONT unless the
    /// blow came from behind AND the critter ships HIT_FROM_BACK art.
    /// </summary>
    public static int HitReaction(bool hitFromFront, bool backArtExists) =>
        hitFromFront || !backArtExists ? HitFromFront : HitFromBack;

    /// <summary>
    /// ported from actions.cc:401/417 _show_damage_to_object: a knockdown falls BACK when hit from the
    /// front, else FRONT (the _pick_fall blocked-tile flip is an out-of-scope refinement).
    /// </summary>
    public static int KnockdownFall(bool hitFromFront) => hitFromFront ? FallBack : FallFront;

    /// <summary>
    /// ported from animation.cc:3187 _dude_standup: a critter that fell BACK rises via BACK_TO_STANDING,
    /// otherwise PRONE_TO_STANDING (its current anim-type selects).
    /// </summary>
    public static int StandUp(int currentAnimType) =>
        currentAnimType == FallBack ? BackToStanding : ProneToStanding;
}
