namespace Hexwaste.Formats.Map;

/// <summary>The seven town-reputation standing bands (character_editor.cc:5574 message ids 2006..2000).</summary>
public enum TownRepLevel { Vilified, Hated, Antipathy, Neutral, Accepted, Liked, Idolized }

/// <summary>
/// Per-town reputation, ported from fallout2-ce src/character_editor.cc:5574-5595 (the level thresholds)
/// + gTownReputationEntries (517). A town's reputation lives in its GVAR (gGameGlobalVars[townGvar]); the
/// VM already maintains those, so this is pure classification — inert until a script (or the harness)
/// sets a town GVAR (no shippable slice script does). The slice towns are GVAR ids 47/48/49.
/// </summary>
public static class TownReputation
{
    /// <summary>Classify a reputation value into its standing band (character_editor.cc:5574). Note the
    /// asymmetry around 0: &lt;-30 Vilified, &lt;-15 Hated, &lt;0 Antipathy, ==0 Neutral, &lt;15 Accepted,
    /// &lt;30 Liked, &gt;=30 Idolized.</summary>
    public static TownRepLevel LevelFor(int value) =>
        value < -30 ? TownRepLevel.Vilified
        : value < -15 ? TownRepLevel.Hated
        : value < 0 ? TownRepLevel.Antipathy
        : value == 0 ? TownRepLevel.Neutral
        : value < 15 ? TownRepLevel.Accepted
        : value < 30 ? TownRepLevel.Liked
        : TownRepLevel.Idolized;

    /// <summary>The reputation.msg message id for a band (character_editor.cc townReputationBaseMessageId).</summary>
    public static int MessageId(TownRepLevel level) => level switch
    {
        TownRepLevel.Vilified => 2006,
        TownRepLevel.Hated => 2005,
        TownRepLevel.Antipathy => 2004,
        TownRepLevel.Neutral => 2003,
        TownRepLevel.Accepted => 2002,
        TownRepLevel.Liked => 2001,
        _ => 2000, // Idolized
    };

    /// <summary>The Arroyo→Klamath→Den slice towns: (town-reputation GVAR id, place name). The full
    /// gTownReputationEntries table has 19 entries; these are the ones the shipping slice covers.</summary>
    public static readonly (int Gvar, string Name)[] SliceTowns =
        [(47, "Arroyo"), (48, "Klamath"), (49, "The Den")];
}
