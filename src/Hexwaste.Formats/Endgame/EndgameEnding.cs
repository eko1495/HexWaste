namespace Hexwaste.Formats.Endgame;

/// <summary>
/// One victory-ending slide record from <c>data\endgame.txt</c>, ported from fallout2-ce
/// src/endgame.cc endgameEndingInit(). A slide plays iff the game global var <see cref="Gvar"/>
/// currently equals <see cref="Value"/> (endgame.cc:217, strict <c>==</c>); slides play in file
/// order and ALL matching slides play. <see cref="ArtNum"/> is the 0-based line in
/// <c>art\intrface\intrface.lst</c> of the slide FRM (327 == DP.FRM, the panning desert scene).
/// <see cref="VoiceOverBaseName"/> is the narrator base name (no path/ext): the voice-over is
/// <c>sound\speech\narrator\&lt;base&gt;.acm</c>, the subtitles <c>text\&lt;lang&gt;\cuts\&lt;base&gt;.txt</c>.
/// <see cref="Direction"/> pans the desert image (-1 right-to-left, 1 left-to-right); only used for art 327.
/// </summary>
public readonly record struct EndgameEnding(int Gvar, int Value, int ArtNum, string VoiceOverBaseName, int Direction);

/// <summary>Parser for <c>data\endgame.txt</c>. ported from fallout2-ce src/endgame.cc endgameEndingInit().</summary>
public static class EndgameEndingFile
{
    private static readonly char[] Delims = [' ', '\t', ','];

    /// <summary>Parse the victory-ending slide table. Skips blank lines and lines whose first
    /// non-space character is <c>#</c> (a comment); a line needs at least the 4 mandatory fields
    /// (gvar, value, art_num, name). The optional 5th <c>direction</c> field defaults to 1 when absent;
    /// when a trailing inline <c>#</c> comment follows the name, the 5th token is <c>"#"</c> and (like the
    /// engine's <c>atoi</c>) parses to 0 — a faithful quirk of endgame.cc.</summary>
    public static IReadOnlyList<EndgameEnding> Parse(byte[] data)
    {
        string text = System.Text.Encoding.ASCII.GetString(data).Replace("\r", "");
        var records = new List<EndgameEnding>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimStart();
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] t = line.Split(Delims, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 4)
                continue;
            records.Add(new EndgameEnding(
                Atoi(t[0]), Atoi(t[1]), Atoi(t[2]),
                t[3].TrimEnd(),                        // endgame.cc:948 single trailing-space strip (strtok already trims delims)
                t.Length >= 5 ? Atoi(t[4]) : 1));      // endgame.cc:956 direction defaults to 1 when absent
        }
        return records;
    }

    /// <summary>C <c>atoi</c> semantics: skip leading whitespace, optional sign, consume leading digits;
    /// a non-numeric token (e.g. an inline <c>"#"</c> comment marker) yields 0.</summary>
    private static int Atoi(string s)
    {
        int i = 0, n = s.Length;
        while (i < n && char.IsWhiteSpace(s[i])) i++;
        int sign = 1;
        if (i < n && (s[i] == '+' || s[i] == '-')) { if (s[i] == '-') sign = -1; i++; }
        long v = 0;
        bool any = false;
        while (i < n && s[i] >= '0' && s[i] <= '9') { v = v * 10 + (s[i] - '0'); i++; any = true; }
        return any ? (int)(sign * v) : 0;
    }
}
