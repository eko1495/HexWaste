namespace Hexwaste.Formats.Endgame;

/// <summary>The reason a death ending is shown, ported from fallout2-ce src/endgame.h
/// (ENDGAME_DEATH_ENDING_REASON_*).</summary>
public enum EndgameDeathReason { Death = 0, Timeout = 2 }

/// <summary>
/// One death-ending narration record from <c>data\enddeath.txt</c>, ported from fallout2-ce
/// src/endgame.cc endgameDeathEndingInit(). Format:
/// <c>gvar, value, worldAreaKnown, worldAreaNotKnown, minLevel, %, narrator</c>. A record is eligible iff
/// (gvar == -1 or getGlobalVar(gvar) &lt; value), the required area is known, the forbidden area is not
/// known, and the PC level ≥ minLevel; one is then chosen by <c>%</c>-weighted random.
/// </summary>
public readonly record struct EndgameDeathEnding(
    int Gvar, int Value, int WorldAreaKnown, int WorldAreaNotKnown, int MinLevel, int Percentage, string VoiceOverBaseName);

/// <summary>Parser + faithful selector for <c>data\enddeath.txt</c>.
/// ported from fallout2-ce src/endgame.cc endgameDeathEndingInit()/endgameSetupDeathEnding()/endgameDeathEndingValidate().</summary>
public static class EndgameDeathEndingFile
{
    private static readonly char[] Delims = [' ', '\t', ','];

    /// <summary>GVAR_MODOC_SHITTY_DEATH — a non-zero value forces the "you smell like shit" Modoc death
    /// (record index 12) with no random roll (endgame.cc:1134).</summary>
    public const int GvarModocShittyDeath = 491;

    /// <summary>Record index the Modoc-shitty-death special forces (endgame.cc:1134).</summary>
    public const int ModocDeathIndex = 12;

    /// <summary>The default death narration when the table is empty/unreadable
    /// (endgame.cc:1004 strcpy "narrator\\nar_5").</summary>
    public const string DefaultFileName = @"narrator\nar_5";

    public static IReadOnlyList<EndgameDeathEnding> Parse(byte[] data)
    {
        string text = System.Text.Encoding.ASCII.GetString(data).Replace("\r", "");
        var records = new List<EndgameDeathEnding>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimStart();
            if (line.Length == 0 || line[0] == '#')
                continue;
            string[] t = line.Split(Delims, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 7)
                continue;
            records.Add(new EndgameDeathEnding(
                Atoi(t[0]), Atoi(t[1]), Atoi(t[2]), Atoi(t[3]), Atoi(t[4]), Atoi(t[5]), t[6].TrimEnd()));
        }
        return records;
    }

    /// <summary>Pick the death-narration file (e.g. <c>narrator\nar_5</c>) for a game-over.
    /// ported from fallout2-ce src/endgame.cc endgameSetupDeathEnding() + endgameDeathEndingValidate().
    /// <paramref name="getGlobalVar"/>/<paramref name="areaKnown"/> read the live GVAR store + worldmap-known
    /// state; <paramref name="randomBetween"/> mirrors random.cc randomBetween(min, max) (inclusive).</summary>
    public static string Select(
        IReadOnlyList<EndgameDeathEnding> records, EndgameDeathReason reason,
        Func<int, int> getGlobalVar, Func<int, bool> areaKnown, int pcLevel, Func<int, int, int> randomBetween)
    {
        if (records.Count == 0)
            return DefaultFileName;

        // endgameDeathEndingValidate: mark each record eligible + sum eligible percentages.
        var enabled = new bool[records.Count];
        int percentage = 0;
        for (int i = 0; i < records.Count; i++)
        {
            EndgameDeathEnding e = records[i];
            if (e.Gvar != -1 && getGlobalVar(e.Gvar) >= e.Value)
                continue;
            if (e.WorldAreaKnown != -1 && !areaKnown(e.WorldAreaKnown))
                continue;
            if (e.WorldAreaNotKnown != -1 && areaKnown(e.WorldAreaNotKnown))
                continue;
            if (pcLevel < e.MinLevel)
                continue;
            enabled[i] = true;
            percentage += e.Percentage;
        }

        int selectedEnding = 0;
        bool special = reason == EndgameDeathReason.Death
            && getGlobalVar(GvarModocShittyDeath) != 0;
        if (special)
        {
            selectedEnding = ModocDeathIndex;
        }
        else
        {
            // Weighted walk: selectedEnding counts ENABLED records skipped (the engine's quirk — it then
            // indexes the FULL array by that count, endgame.cc:1140-1153).
            int chance = randomBetween(0, percentage);
            int accum = 0;
            for (int i = 0; i < records.Count; i++)
            {
                if (!enabled[i])
                    continue;
                accum += records[i].Percentage;
                if (accum >= chance)
                    break;
                selectedEnding++;
            }
        }

        if (selectedEnding < 0 || selectedEnding >= records.Count)
            return DefaultFileName;
        return @"narrator\" + records[selectedEnding].VoiceOverBaseName;
    }

    /// <summary>C <c>atoi</c> semantics (see EndgameEndingFile.Atoi).</summary>
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
