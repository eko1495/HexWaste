using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

public class WorldEncountersTests
{
    // Tile 0, subtile [2,2] (x∈[100,150), y∈[100,150)) → a Forced (100%) cell
    // pointing at TestTable; enc_01 is level-gated.
    private const string World = """
        [Data]
        Forced=100%
        None=0%

        [Tile 0]
        encounter_difficulty=0
        2_2=Desert,No_Fill,Forced,Forced,Forced,TestTable

        [Encounter Table 6]
        lookup_name=TestTable
        enc_00=Chance:50%,Enc:(2-4) ARRO_Rats AMBUSH Player
        enc_01=Chance:50%,Enc:(1-2) DEN_Slavers AMBUSH Player, If(Player(Level) > 5)
        """;

    private static WorldmapFile World0() => WorldmapFile.Parse(World);

    [Fact]
    public void DeltaGateBlocksStepsUnderThreeInEitherAxis()
    {
        var enc = new WorldEncounters(World0(), new MinRng(), startX: 0, startY: 0);
        // dy = 2 < 3 → gated, no roll even on a 100% cell.
        Assert.Null(enc.Roll(110, 2, 1200, _ => 0, playerLevel: 1, daysPlayed: 0));
    }

    [Fact]
    public void ForcedEncounterPicksTheFirstEligibleEntry()
    {
        var enc = new WorldEncounters(World0(), new MinRng(), 0, 0);
        // Level 1: enc_01 (Player(Level) > 5) is filtered out → enc_00 ARRO_Rats.
        EncounterResult r = enc.Roll(110, 110, 1200, _ => 0, playerLevel: 1, daysPlayed: 0)!;
        Assert.Equal("ARRO_Rats", r.Entry.Spawns.Single().Group);
        Assert.Equal("AMBUSH", r.Entry.Situation);
    }

    [Fact]
    public void LevelConditionEnablesTheGatedEntry()
    {
        // occurrence roll 0 (<100 → fires), pick roll 75 over total 100 → second entry.
        var enc = new WorldEncounters(World0(), new SequenceRng(0, 75), 0, 0);
        EncounterResult r = enc.Roll(110, 110, 1200, _ => 0, playerLevel: 7, daysPlayed: 0)!;
        Assert.Equal("DEN_Slavers", r.Entry.Spawns.Single().Group);
    }

    [Fact]
    public void GlobalConditionGatesAnEntry()
    {
        const string w = """
            [Data]
            Forced=100%
            [Tile 0]
            2_2=Desert,_,Forced,Forced,Forced,T
            [Encounter Table 0]
            lookup_name=T
            enc_00=Chance:50%,Enc:(1-1) GROUP_A AMBUSH Player, If(Global(5) > 0)
            """;
        WorldmapFile world = WorldmapFile.Parse(w);

        // Global(5)=0 → no candidate → no encounter even on a forced cell.
        Assert.Null(new WorldEncounters(world, new MinRng(), 0, 0).Roll(110, 110, 1200, _ => 0, 1, 0));
        // Global(5)=1 → candidate passes.
        EncounterResult r = new WorldEncounters(world, new MinRng(), 0, 0).Roll(110, 110, 1200, g => g == 5 ? 1 : 0, 1, 0)!;
        Assert.Equal("GROUP_A", r.Entry.Spawns.Single().Group);
    }

    [Fact]
    public void OneShotCounterFiltersAfterUse()
    {
        const string w = """
            [Data]
            Forced=100%
            [Tile 0]
            2_2=Desert,_,Forced,Forced,Forced,T
            [Encounter Table 0]
            lookup_name=T
            enc_00=Chance:50%,Counter:1,Enc:(1-1) ONE_SHOT AMBUSH Player
            """;
        var enc = new WorldEncounters(WorldmapFile.Parse(w), new MinRng(), 0, 0);

        Assert.Equal("ONE_SHOT", enc.Roll(110, 110, 1200, _ => 0, 1, 0)!.Entry.Spawns.Single().Group);
        // Counter spent (now 0) → filtered → no candidate → null.
        Assert.Null(enc.Roll(220, 220, 1200, _ => 0, 1, 0));
    }

    [Fact]
    public void DeterministicUnderSeed()
    {
        EncounterResult? Run() =>
            new WorldEncounters(World0(), new SystemCombatRng(123), 0, 0)
                .Roll(110, 110, 1200, _ => 0, playerLevel: 9, daysPlayed: 0);
        Assert.Equal(Run()?.Entry.Spawns.Single().Group, Run()?.Entry.Spawns.Single().Group);
    }

    private sealed class MinRng : ICombatRng
    {
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
    }

    private sealed class SequenceRng(params int[] values) : ICombatRng
    {
        private int _i;
        public int Next(int minInclusive, int maxExclusive) =>
            Math.Clamp(values[Math.Min(_i++, values.Length - 1)], minInclusive, maxExclusive - 1);
    }
}
