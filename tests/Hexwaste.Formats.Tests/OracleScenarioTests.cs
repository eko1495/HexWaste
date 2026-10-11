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

// Parity with the fo2ce trace patch's parser (oracle.cc oracleScenarioBegin: `%31s` command, `%*s %d`
// argument — any whitespace separates, the argument is sscanf's leading-integer prefix, extra tokens are
// ignored, `wait` needs n >= 0, `end-turn` without a parsable count means 1).
public class OracleScenarioFo2ceParityTests
{
    [Fact]
    public void SplitsOnAnyWhitespace()
    {
        OracleScenario s = OracleScenario.Parse("seed\t7\r\n  wait\t\t 30 \r\nend-turn\t2\r\n");
        Assert.Equal(7, s.Seed);
        Assert.Equal<ScenarioAction>([new ScenarioAction.Wait(30), new ScenarioAction.EndTurn(2)], s.Actions);
    }

    [Fact]
    public void RejectsNegativeWait() =>
        Assert.Throws<FormatException>(() => OracleScenario.Parse("seed 1\nwait -10\n"));

    [Fact]
    public void RejectsWaitWithoutNumber() =>
        Assert.Throws<FormatException>(() => OracleScenario.Parse("seed 1\nwait x\n"));

    [Fact]
    public void RejectsSeedWithoutNumber() =>
        Assert.Throws<FormatException>(() => OracleScenario.Parse("seed abc\nwait 10\n"));

    [Fact]
    public void EndTurnWithUnparsableCountMeansOne() =>
        Assert.Equal<ScenarioAction>([new ScenarioAction.EndTurn(1)], OracleScenario.Parse("seed 1\nend-turn x\n").Actions);

    [Fact]
    public void ArgumentIsTheLeadingIntegerPrefixAndExtraTokensAreIgnored()
    {
        OracleScenario s = OracleScenario.Parse("seed -5 junk\nwait 25ms\nend-turn +3 more\n");
        Assert.Equal(-5, s.Seed);
        Assert.Equal<ScenarioAction>([new ScenarioAction.Wait(25), new ScenarioAction.EndTurn(3)], s.Actions);
    }

    [Fact]
    public void RejectsSecondSeed() =>
        Assert.Throws<FormatException>(() => OracleScenario.Parse("seed 1\nseed 2\n"));

    [Fact]
    public void SeedOnlyScenarioHasNoActions() =>
        Assert.Empty(OracleScenario.Parse("seed 1\n").Actions);
}
