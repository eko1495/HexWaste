namespace Hexwaste.Formats.Int;

/// <summary>
/// The talking-head mood machine, ported from fallout2-ce src/game_dialog.cc:2884
/// _talk_to_critter_reacts: a dialogue_reaction nudge moves the fidget family ONE STEP
/// toward good/bad (a good nudge on a BAD head only recovers to neutral, and vice versa),
/// playing the matching transition anim (art.h head anims). (P122.)
/// </summary>
public static class HeadReaction
{
    public const int FidgetGood = 1;     // art.h FIDGET_GOOD
    public const int FidgetNeutral = 4;  // FIDGET_NEUTRAL
    public const int FidgetBad = 7;      // FIDGET_BAD

    /// <summary>Apply a dialogue_reaction value (−1 good / 0 neutral no-op / +1 bad) to the
    /// current fidget family; returns the one-shot transition anim to play (null on the
    /// neutral no-op) and the new family.</summary>
    public static (int? Transition, int Fidget) Step(int currentFidget, int value)
    {
        if (value < 0) // GAME_DIALOG_REACTION_GOOD (49)
            return currentFidget switch
            {
                FidgetGood => (0, FidgetGood),       // HEAD_ANIMATION_VERY_GOOD_REACTION
                FidgetBad => (6, FidgetNeutral),     // HEAD_ANIMATION_BAD_TO_NEUTRAL
                _ => (3, FidgetGood),                // HEAD_ANIMATION_NEUTRAL_TO_GOOD
            };
        if (value > 0) // GAME_DIALOG_REACTION_BAD (51)
            return currentFidget switch
            {
                FidgetGood => (2, FidgetNeutral),    // HEAD_ANIMATION_GOOD_TO_NEUTRAL
                FidgetBad => (8, FidgetBad),         // HEAD_ANIMATION_VERY_BAD_REACTION
                _ => (5, FidgetBad),                 // HEAD_ANIMATION_NEUTRAL_TO_BAD
            };
        return (null, currentFidget); // NEUTRAL (50): the engine's switch has no case body
    }

    /// <summary>The phoneme-talk anim for a fidget family (1→9, 4→10, 7→11 — _gdSetupFidget's
    /// reaction→*_PHONEMES mapping).</summary>
    public static int PhonemesFor(int fidget) => fidget switch
    {
        FidgetGood => 9,   // HEAD_ANIMATION_GOOD_PHONEMES
        FidgetBad => 11,   // HEAD_ANIMATION_BAD_PHONEMES
        _ => 10,           // HEAD_ANIMATION_NEUTRAL_PHONEMES
    };
}
