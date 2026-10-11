using System.Globalization;

namespace Hexwaste.Formats.Trace;

public abstract record ScenarioAction
{
    public sealed record Wait(int Ms) : ScenarioAction;
    public sealed record EndTurn(int Count) : ScenarioAction;
}

/// <summary>The oracle scenario file shared with the fo2ce trace patch (tools/fo2ce-trace,
/// oracle.cc oracleScenarioBegin): `seed N` first, then `wait MS` / `end-turn [N]`; '#' comments.
/// Parsed exactly like the patch: the command is the first whitespace-separated token (`%31s`), the
/// argument is the leading integer of the second token (`%*s %d`: optional sign + digits, rest and
/// further tokens ignored); `wait` needs an argument &gt;= 0; `end-turn` without a parsable count
/// means 1. Malformed input throws <see cref="FormatException"/> (the patch exits 6).</summary>
public sealed record OracleScenario(int Seed, IReadOnlyList<ScenarioAction> Actions)
{
    public static OracleScenario Parse(string text)
    {
        int? seed = null;
        var actions = new List<ScenarioAction>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Split('#')[0];
            string[] p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0)
                continue;
            int? arg = p.Length >= 2 ? ScanInt(p[1]) : null;
            if (seed is null)
            {
                if (p[0] != "seed" || arg is null)
                    throw new FormatException($"scenario must start with seed <int>, got '{line.Trim()}'");
                seed = arg;
                continue;
            }
            switch (p[0])
            {
                case "wait" when arg is >= 0:
                    actions.Add(new ScenarioAction.Wait(arg.Value));
                    break;
                case "end-turn":
                    actions.Add(new ScenarioAction.EndTurn(arg ?? 1));
                    break;
                default:
                    throw new FormatException($"bad scenario command '{line.Trim()}'");
            }
        }
        return new OracleScenario(seed ?? throw new FormatException("scenario has no seed"), actions);
    }

    /// <summary>sscanf "%d" on one token: the leading [+-]?digits prefix, or null when there is none.</summary>
    private static int? ScanInt(string token)
    {
        int end = token.Length > 0 && (token[0] == '+' || token[0] == '-') ? 1 : 0;
        int digitsStart = end;
        while (end < token.Length && char.IsAsciiDigit(token[end]))
            end++;
        if (end == digitsStart)
            return null;
        return int.TryParse(token.AsSpan(0, end), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v)
            ? v
            : throw new FormatException($"scenario number out of range: '{token}'");
    }
}
