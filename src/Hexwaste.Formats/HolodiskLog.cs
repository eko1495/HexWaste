namespace Hexwaste.Formats;

/// <summary>
/// One holodisk from <c>data/holodisk.txt</c>. ported from fallout2-ce src/pipboy.cc HolodiskDescription:
/// a holodisk shows in the Pip-Boy Archives once its global var is non-zero. <see cref="Name"/> is a
/// pipboy.msg id (the list entry); <see cref="Description"/> is the pipboy.msg id of the first body line.
/// </summary>
public sealed record Holodisk(int Gvar, int Name, int Description);

/// <summary>
/// Parses <c>data/holodisk.txt</c> — whitespace/comma-separated rows of <c>gvar, name, description</c>,
/// '#'-comment and blank lines skipped. ported from fallout2-ce src/pipboy.cc holodiskInit().
/// </summary>
public static class HolodiskLog
{
    private static readonly char[] Delims = [' ', '\t', ','];

    public static IReadOnlyList<Holodisk> Parse(Stream stream)
    {
        var disks = new List<Holodisk>();
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } raw)
        {
            string line = raw.TrimStart();
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] parts = line.Split(Delims, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                continue;
            if (int.TryParse(parts[0], out int gvar)
                && int.TryParse(parts[1], out int name)
                && int.TryParse(parts[2], out int description))
            {
                disks.Add(new Holodisk(gvar, name, description));
            }
        }
        return disks;
    }
}
