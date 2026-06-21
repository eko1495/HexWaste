namespace Hexwaste.Formats.Int;

/// <summary>The classified NPC reaction to a dialogue option, used by the Empathy
/// perk to tint the option text.</summary>
public enum DialogReactionLevel
{
    Good,
    Neutral,
    Bad,
}

/// <summary>Maps a stored dialogue-option reaction value to its display level.
/// Ported from fallout2-ce src/game_dialog.cc gameDialogOptionOnMouseEnter (:2120) /
/// _gdProcessChoice (:2050): the engine switches the raw reaction against three
/// constants and falls back to Neutral for anything else.</summary>
public static class DialogReaction
{
    // src/game_dialog.cc:85-87 GAME_DIALOG_REACTION_{GOOD,NEUTRAL,BAD}.
    public const int Good = 49;
    public const int Neutral = 50;
    public const int Bad = 51;

    public static DialogReactionLevel Classify(int reaction) => reaction switch
    {
        Good => DialogReactionLevel.Good,
        Bad => DialogReactionLevel.Bad,
        _ => DialogReactionLevel.Neutral, // NEUTRAL + the engine's default/invalid branch
    };
}
