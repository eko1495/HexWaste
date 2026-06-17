namespace Hexwaste.Formats.Map;

/// <summary>A karma-title row from data\karmavar.txt: the GVAR whose non-zero value earns the title,
/// plus the FRM/name/description message ids.</summary>
public readonly record struct KarmaEntry(int Gvar, int ArtNum, int NameMessageId, int DescriptionMessageId);

/// <summary>
/// Earned karma titles (Champion, Berserker, Childkiller, the 9 GVAR_KARMA_* …), ported from
/// fallout2-ce src/character_editor.cc <c>karmaInit</c> (6978) + the active-title scan (5537). Each row of
/// <c>data\karmavar.txt</c> binds a GVAR to a title; the title is "earned" while that GVAR is non-zero —
/// 100% script-driven (no engine auto-award), so inert until content (or the harness) sets a karma GVAR.
/// The <c>GVAR_PLAYER_REPUTATION</c> (gvar 0) row is the generic reputation handled separately (B-M1).
/// </summary>
public static class KarmaTitles
{
    /// <summary>Parse karmavar.txt: skip blank/'#' lines, split on space/tab/comma, take
    /// (gvar, art_num, name, description) message ids (karmaInit, all <c>atoi</c>).</summary>
    public static IReadOnlyList<KarmaEntry> Parse(string text)
    {
        var list = new List<KarmaEntry>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] tok = line.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length >= 4
                && int.TryParse(tok[0], out int gvar) && int.TryParse(tok[1], out int art)
                && int.TryParse(tok[2], out int name) && int.TryParse(tok[3], out int desc))
                list.Add(new KarmaEntry(gvar, art, name, desc));
        }
        return list;
    }

    /// <summary>The currently-earned titles: rows whose GVAR is non-zero (character_editor.cc:5537),
    /// excluding the generic-reputation row (gvar 0, which is a value not a title).</summary>
    public static IEnumerable<KarmaEntry> Active(IReadOnlyList<KarmaEntry> entries, Func<int, int> globalVar) =>
        entries.Where(e => e.Gvar != 0 && globalVar(e.Gvar) != 0);
}
