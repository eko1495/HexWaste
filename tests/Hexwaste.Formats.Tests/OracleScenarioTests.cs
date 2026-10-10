using Hexwaste.Formats.Trace;

namespace Hexwaste.Formats.Tests;

public class OracleScenarioTests
{
    [Fact]
    public void ParsesSeedWaitAndEndTurn()
    {
        OracleScenario s = OracleScenario.Parse("# c\nseed 12345\nwait 5000\nend-turn 5\nend-turn\n");
        Assert.Equal(12345, s.Seed);
        Assert.Equal<ScenarioAction>(
            [new ScenarioAction.Wait(5000), new ScenarioAction.EndTurn(5), new ScenarioAction.EndTurn(1)],
            s.Actions);
    }

    [Fact]
    public void RejectsMissingSeed() =>
        Assert.Throws<FormatException>(() => OracleScenario.Parse("wait 10\n"));

    [Fact]
    public void RejectsUnknownCommand() =>
        Assert.Throws<FormatException>(() => OracleScenario.Parse("seed 1\nattack 5\n"));
}
