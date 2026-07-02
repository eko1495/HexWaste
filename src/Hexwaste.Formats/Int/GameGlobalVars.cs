namespace Hexwaste.Formats.Int;

/// <summary>
/// Parses the new-game global-variable seed values from <c>data\vault13.gam</c>, ported from fallout2-ce
/// src/game.cc <c>globalVarsRead("data\\vault13.gam", "GAME_GLOBAL_VARS:")</c> (0x443B5C). After the
/// <c>GAME_GLOBAL_VARS:</c> header, every non-blank / non-<c>//</c> line is one variable IN ORDER (the
/// list index is positional, matching the trailing <c>// (N)</c> markers); the value is the integer after
/// <c>=</c> (sscanf %d), or 0 if absent. In the base game 684 of 696 globals seed 0; only 12 are non-zero
/// (e.g. GVAR_TOWN_REP_ARROYO[47]:=50, GVAR_FIND_VIC[619]:=1), so seeding is a small, sparse correction.
/// </summary>
public static class GameGlobalVars
{
    /// <summary>The positional seed values from the GAME_GLOBAL_VARS section (index i = the i-th var).</summary>
    public static IReadOnlyList<int> Parse(string text, string section = "GAME_GLOBAL_VARS:")
    {
        // P114: `section` selects the block — "GAME_GLOBAL_VARS:" (vault13.gam) or "MAP_GLOBAL_VARS:"
        // (a per-map .gam, map.cc:945). Same positional line-parse for both (game.cc:1044).
        var values = new List<int>();
        bool inSection = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (!inSection)
            {
                if (line.StartsWith(section, StringComparison.Ordinal))
                    inSection = true;
                continue;
            }
            if (line.Length == 0)                                   // engine: skip blank (line[0]=='\n')
                continue;
            if (line.Length >= 2 && line[0] == '/' && line[1] == '/') // engine: skip a "//" comment at col 0
                continue;
            int semicolon = line.IndexOf(';');                      // strip ';' + any inline comment after it
            if (semicolon >= 0)
                line = line[..semicolon];
            int eq = line.IndexOf('=');                             // value = sscanf %d after '='
            values.Add(eq >= 0 ? ParseLeadingInt(line.AsSpan(eq + 1)) : 0);
        }
        return values;
    }

    /// <summary>sscanf("%d") semantics: skip leading whitespace, read an optional sign + digits.</summary>
    private static int ParseLeadingInt(ReadOnlySpan<char> s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        int start = i;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        return int.TryParse(s[start..i], out int v) ? v : 0;
    }
}
