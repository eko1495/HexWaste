using Hexwaste.Formats.Int;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Dialogue reaction classification (P52-M1): the Empathy perk tints each option by the NPC's
/// reaction, ported from fallout2-ce game_dialog.cc gameDialogOptionOnMouseEnter (:2120) — the
/// switch matches GAME_DIALOG_REACTION_GOOD/NEUTRAL/BAD (49/50/51), everything else → Neutral.
/// </summary>
public class DialogReactionTests
{
    [Theory]
    [InlineData(49, DialogReactionLevel.Good)]
    [InlineData(50, DialogReactionLevel.Neutral)]
    [InlineData(51, DialogReactionLevel.Bad)]
    public void ClassifiesTheThreeEngineConstants(int reaction, DialogReactionLevel expected)
        => Assert.Equal(expected, DialogReaction.Classify(reaction));

    [Theory]
    [InlineData(0)]
    [InlineData(48)]
    [InlineData(52)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void OutOfRangeValuesFallBackToNeutral(int reaction)
        => Assert.Equal(DialogReactionLevel.Neutral, DialogReaction.Classify(reaction));

    [Fact]
    public void ConstantsMatchTheEngineEnum()
    {
        Assert.Equal(49, DialogReaction.Good);
        Assert.Equal(50, DialogReaction.Neutral);
        Assert.Equal(51, DialogReaction.Bad);
    }
}
