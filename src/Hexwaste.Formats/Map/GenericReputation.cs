namespace Hexwaste.Formats.Map;

/// <summary>A generic-reputation threshold → title-message-id mapping row (data\genrep.txt).</summary>
public readonly record struct ReputationEntry(int Threshold, int MessageId);

/// <summary>
/// The generic-reputation title system, ported from fallout2-ce src/character_editor.cc
/// <c>genericReputationInit</c> (7077) + the lookup at 5509. <c>data\genrep.txt</c> lists
/// "threshold message_id" rows (whitespace/comma-delimited, '#' comments); the engine sorts them
/// DESCENDING by threshold and the player's title is the highest-threshold row whose threshold the
/// reputation value meets. In Hexwaste the value is the dude's <c>_dudeReputation</c> PC-stat (the
/// engine reads GVAR_PLAYER_REPUTATION — a documented unification to one source of truth).
/// </summary>
public static class GenericReputation
{
    /// <summary>Parse genrep.txt: skip blank/'#' lines, split each on space/tab/comma, take
    /// (threshold, messageId); return sorted DESCENDING by threshold (genericReputationCompare).</summary>
    public static IReadOnlyList<ReputationEntry> Parse(string text)
    {
        var list = new List<ReputationEntry>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] tok = line.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length >= 2 && int.TryParse(tok[0], out int threshold) && int.TryParse(tok[1], out int messageId))
                list.Add(new ReputationEntry(threshold, messageId));
        }
        list.Sort((a, b) => b.Threshold.CompareTo(a.Threshold)); // descending
        return list;
    }

    /// <summary>The title message id for a reputation value: the highest-threshold entry whose threshold
    /// the value meets (entries descending), or -1 when the value is below every threshold (no title) —
    /// character_editor.cc:5509.</summary>
    public static int TitleFor(int value, IReadOnlyList<ReputationEntry> entries)
    {
        foreach (ReputationEntry e in entries)
            if (value >= e.Threshold)
                return e.MessageId;
        return -1;
    }
}
