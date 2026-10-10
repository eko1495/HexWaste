namespace Hexwaste.Formats.Trace;

public sealed record TraceDivergence(int LineNumber, string? Expected, string? Actual, IReadOnlyList<string> Context);

/// <summary>Lockstep first-divergence comparison for oracle traces. '#' header lines are skipped;
/// the trailing <c>site=</c> field is context only and never compared.</summary>
public static class TraceComparer
{
    public static string StripSite(string line)
    {
        int i = line.IndexOf(" site=", StringComparison.Ordinal);
        return i < 0 ? line : line[..i];
    }

    public static TraceDivergence? FirstDivergence(IReadOnlyList<string> expected, IReadOnlyList<string> actual, int contextLines = 20)
    {
        List<(int Line, string Text)> e = Body(expected), a = Body(actual);
        int n = Math.Max(e.Count, a.Count);
        for (int i = 0; i < n; i++)
        {
            string? ex = i < e.Count ? e[i].Text : null;
            string? ac = i < a.Count ? a[i].Text : null;
            if (ex is not null && ac is not null && StripSite(ex) == StripSite(ac))
                continue;
            int lineNumber = i < e.Count ? e[i].Line : (i < a.Count ? a[i].Line : i + 1);
            var context = e.Skip(Math.Max(0, i - contextLines)).Take(Math.Min(i, contextLines)).Select(x => x.Text).ToList();
            return new TraceDivergence(lineNumber, ex, ac, context);
        }
        return null;
    }

    private static List<(int, string)> Body(IReadOnlyList<string> lines)
    {
        var body = new List<(int, string)>();
        for (int i = 0; i < lines.Count; i++)
            if (!lines[i].StartsWith('#'))
                body.Add((i + 1, lines[i]));
        return body;
    }
}
