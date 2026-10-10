namespace Hexwaste.Formats.Trace;

public abstract record ScenarioAction
{
    public sealed record Wait(int Ms) : ScenarioAction;
    public sealed record EndTurn(int Count) : ScenarioAction;
}

/// <summary>The oracle scenario file shared with the fo2ce trace patch (tools/fo2ce-trace,
/// oracle.cc oracleRunScenario): `seed N` first, then `wait MS` / `end-turn [N]`; '#' comments.</summary>
public sealed record OracleScenario(int Seed, IReadOnlyList<ScenarioAction> Actions)
{
    public static OracleScenario Parse(string text)
    {
        int? seed = null;
        var actions = new List<ScenarioAction>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Split('#')[0].Trim();
            if (line.Length == 0)
                continue;
            string[] p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (p[0])
            {
                case "seed" when p.Length == 2 && seed is null:
                    seed = int.Parse(p[1]);
                    break;
                case var _ when seed is null:
                    throw new FormatException($"scenario must start with seed, got '{line}'");
                case "wait" when p.Length == 2:
                    actions.Add(new ScenarioAction.Wait(int.Parse(p[1])));
                    break;
                case "end-turn":
                    actions.Add(new ScenarioAction.EndTurn(p.Length == 2 ? int.Parse(p[1]) : 1));
                    break;
                default:
                    throw new FormatException($"unknown scenario command '{line}'");
            }
        }
        return new OracleScenario(seed ?? throw new FormatException("scenario has no seed"), actions);
    }
}
