using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// P34-M6: the defender reaction-anim selection (ReactionAnims), a port of actions.cc
/// _show_damage_to_object + animation.cc _dude_standup. Locks the anim codes + branch logic.
/// </summary>
public class ReactionAnimsTests
{
    [Fact]
    public void HitFromFrontAlwaysUsesFrontReaction()
    {
        Assert.Equal(14, ReactionAnims.HitReaction(hitFromFront: true, backArtExists: true));
        Assert.Equal(14, ReactionAnims.HitReaction(hitFromFront: true, backArtExists: false));
    }

    [Fact]
    public void HitFromBackUsesBackReactionOnlyWhenArtExists()
    {
        Assert.Equal(15, ReactionAnims.HitReaction(hitFromFront: false, backArtExists: true));
        Assert.Equal(14, ReactionAnims.HitReaction(hitFromFront: false, backArtExists: false)); // fallback
    }

    [Fact]
    public void KnockdownFallsBackFromFrontElseFront()
    {
        Assert.Equal(20, ReactionAnims.KnockdownFall(hitFromFront: true));  // FALL_BACK
        Assert.Equal(21, ReactionAnims.KnockdownFall(hitFromFront: false)); // FALL_FRONT
    }

    [Fact]
    public void StandUpPicksBackToStandingOnlyAfterAFallBack()
    {
        Assert.Equal(37, ReactionAnims.StandUp(20)); // fell back → BACK_TO_STANDING
        Assert.Equal(36, ReactionAnims.StandUp(21)); // fell front → PRONE_TO_STANDING
        Assert.Equal(36, ReactionAnims.StandUp(0));  // anything else → PRONE_TO_STANDING
    }

    [Fact]
    public void DodgeIsAnim13() => Assert.Equal(13, ReactionAnims.Dodge);
}
