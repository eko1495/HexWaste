using Hexwaste.Formats.Int;

namespace Hexwaste.Formats.Tests;

/// <summary>P122: the talking-head mood machine — fallout2-ce game_dialog.cc:2884
/// _talk_to_critter_reacts. A dialogue_reaction nudge steps the fidget family ONE notch
/// toward good/bad with the matching transition anim; a good nudge on a BAD head only
/// recovers to neutral (and vice versa); neutral nudges are no-ops.</summary>
public class HeadReactionTests
{
    [Theory]
    // value < 0 = GOOD nudge (GAME_DIALOG_REACTION_GOOD)
    [InlineData(HeadReaction.FidgetNeutral, -1, 3, HeadReaction.FidgetGood)]  // NEUTRAL_TO_GOOD
    [InlineData(HeadReaction.FidgetGood, -1, 0, HeadReaction.FidgetGood)]     // VERY_GOOD_REACTION
    [InlineData(HeadReaction.FidgetBad, -1, 6, HeadReaction.FidgetNeutral)]   // BAD only recovers
    // value > 0 = BAD nudge
    [InlineData(HeadReaction.FidgetNeutral, 1, 5, HeadReaction.FidgetBad)]    // NEUTRAL_TO_BAD
    [InlineData(HeadReaction.FidgetBad, 1, 8, HeadReaction.FidgetBad)]        // VERY_BAD_REACTION
    [InlineData(HeadReaction.FidgetGood, 1, 2, HeadReaction.FidgetNeutral)]   // GOOD only drops one
    public void StepMovesOneNotchWithTheRightTransition(int current, int value, int transition, int next)
    {
        Assert.Equal(((int?)transition, next), HeadReaction.Step(current, value));
    }

    [Fact]
    public void NeutralNudgeIsANoOp()
    {
        Assert.Equal(((int?)null, HeadReaction.FidgetBad), HeadReaction.Step(HeadReaction.FidgetBad, 0));
    }

    [Fact]
    public void PhonemesFollowTheFamily()
    {
        Assert.Equal(9, HeadReaction.PhonemesFor(HeadReaction.FidgetGood));
        Assert.Equal(10, HeadReaction.PhonemesFor(HeadReaction.FidgetNeutral));
        Assert.Equal(11, HeadReaction.PhonemesFor(HeadReaction.FidgetBad));
    }
}
