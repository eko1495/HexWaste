namespace Hexwaste.Formats;

/// <summary>
/// One quest from <c>data/quests.txt</c>. ported from fallout2-ce src/pipboy.cc QuestDescription:
/// a quest shows in the Pip-Boy Archives once its global var reaches <see cref="DisplayThreshold"/>
/// and reads as completed once it reaches <see cref="CompletedThreshold"/>. <see cref="Location"/> is a
/// map.msg id (the town name header); <see cref="Description"/> is a quests.msg id (the quest line).
/// </summary>
public sealed record Quest(int Location, int Description, int Gvar, int DisplayThreshold, int CompletedThreshold);

/// <summary>
/// Parses <c>data/quests.txt</c> — comma-separated rows of
/// <c>location, description, gvar, displayThreshold, completedThreshold</c>, '#'/'//' comments and
/// blank lines skipped. ported from fallout2-ce src/pipboy.cc questInit().
/// </summary>
public static class QuestLog
{
    public static IReadOnlyList<Quest> Parse(Stream stream)
    {
        var quests = new List<Quest>();
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } raw)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || line.StartsWith("//"))
                continue;

            string[] parts = line.Split(',');
            if (parts.Length < 5)
                continue;
            if (int.TryParse(parts[0].Trim(), out int location)
                && int.TryParse(parts[1].Trim(), out int description)
                && int.TryParse(parts[2].Trim(), out int gvar)
                && int.TryParse(parts[3].Trim(), out int display)
                && int.TryParse(parts[4].Trim(), out int completed))
            {
                quests.Add(new Quest(location, description, gvar, display, completed));
            }
        }
        return quests;
    }
}
