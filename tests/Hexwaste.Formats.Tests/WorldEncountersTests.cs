using System.Text;
using Hexwaste.Formats;
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
    public void SpentOneShotCounterSurvivesExportImport()
    {
        // The felt effect of P10-M2: a one-shot encounter consumed before a save
        // stays consumed after the reload (export the counter, re-parse pristine,
        // import) — the entry is filtered out on the reloaded world.
        const string w = """
            [Data]
            Forced=100%
            [Tile 0]
            2_2=Desert,_,Forced,Forced,Forced,T
            [Encounter Table 0]
            lookup_name=T
            enc_00=Chance:50%,Counter:1,Enc:(1-1) ONE_SHOT AMBUSH Player
            """;
        WorldmapFile wm = WorldmapFile.Parse(w);
        Assert.Equal("ONE_SHOT",
            new WorldEncounters(wm, new MinRng(), 0, 0).Roll(110, 110, 1200, _ => 0, 1, 0)!
                .Entry.Spawns.Single().Group);

        Dictionary<string, int[]> saved = wm.ExportCounters();
        Assert.Equal([0], saved["T"]); // spent

        WorldmapFile reload = WorldmapFile.Parse(w);
        reload.ImportCounters(saved);
        // Counter restored to 0 → filtered → no candidate → no encounter.
        Assert.Null(new WorldEncounters(reload, new MinRng(), 0, 0).Roll(110, 110, 1200, _ => 0, 1, 0));
    }

    [Fact]
    public void MultiUseCounterDecrementsThroughPickAndPersistsEachCycle()
    {
        // A Counter:2 one-shot driven through real Roll/Pick (not a direct field
        // write): it fires twice (2->1->0), survives export/import as a STILL-LIVE
        // value each cycle, and is filtered on the third roll. Synthetic — shipping
        // worldmap.txt carries only Counter:1 — but it locks the decrement-through-
        // Pick + multi-cycle persistence path against regression.
        const string w = """
            [Data]
            Forced=100%
            [Tile 0]
            2_2=Desert,_,Forced,Forced,Forced,T
            [Encounter Table 0]
            lookup_name=T
            enc_00=Chance:50%,Counter:2,Enc:(1-1) TWICE AMBUSH Player
            """;
        string? Fire(WorldmapFile m) =>
            new WorldEncounters(m, new MinRng(), 0, 0).Roll(110, 110, 1200, _ => 0, 1, 0)
                ?.Entry.Spawns.Single().Group;

        WorldmapFile wm = WorldmapFile.Parse(w);
        Assert.Equal("TWICE", Fire(wm));              // cycle 1: 2 -> 1
        Assert.Equal([1], wm.ExportCounters()["T"]);  // still live, persisted as 1

        WorldmapFile r2 = WorldmapFile.Parse(w);
        r2.ImportCounters(wm.ExportCounters());
        Assert.Equal(1, r2.Table("T")!.Entries[0].Counter);
        Assert.Equal("TWICE", Fire(r2));              // cycle 2: 1 -> 0
        Assert.Equal([0], r2.ExportCounters()["T"]);  // now spent

        WorldmapFile r3 = WorldmapFile.Parse(w);
        r3.ImportCounters(r2.ExportCounters());
        Assert.Null(Fire(r3));                        // cycle 3: filtered, no encounter
    }

    [Fact]
    public void HighOutdoorsmanDetectsAndAvoidsTheEncounter()
    {
        // A forced cell still rolls a group, but a high Outdoorsman steers around it
        // (phase-10 #12). MinRng: the avoid roll (1) < detect (min(outdoorsman,95)).
        EncounterResult? Run(int outdoorsman) =>
            new WorldEncounters(World0(), new MinRng(), 0, 0)
                .Roll(110, 110, 1200, _ => 0, playerLevel: 1, daysPlayed: 0, outdoorsman: outdoorsman);
        Assert.NotNull(Run(0));   // no skill → walks into it
        Assert.Null(Run(100));    // capped to 95 → detected → avoided
    }

    [Fact]
    public void LuckShiftsTheWeightedPick()
    {
        const string w = """
            [Data]
            Forced=100%
            [Tile 0]
            2_2=Desert,_,Forced,Forced,Forced,T
            [Encounter Table 0]
            lookup_name=T
            enc_00=Chance:50%,Enc:(1-1) A AMBUSH Player
            enc_01=Chance:50%,Enc:(1-1) B AMBUSH Player
            """;
        // occ 0 (fires), pick 48, avoid-roll 1. Luck 5 → 48 → A; Luck 10 → 53 → B.
        string? Run(int luck) =>
            new WorldEncounters(WorldmapFile.Parse(w), new SequenceRng(0, 48, 1), 0, 0)
                .Roll(110, 110, 1200, _ => 0, 1, 0, luck: luck)?.Entry.Spawns.Single().Group;
        Assert.Equal("A", Run(5));
        Assert.Equal("B", Run(10));
    }

    [Fact]
    public void HardDifficultyRaisesEncounterFrequency()
    {
        const string w = """
            [Data]
            Test=30%
            [Tile 0]
            2_2=Desert,_,Test,Test,Test,T
            [Encounter Table 0]
            lookup_name=T
            enc_00=Chance:100%,Enc:(1-1) A AMBUSH Player
            """;
        // occ roll 31: Normal freq 30 (31≥30 → none); Hard freq 30+30/15=32 (31<32 → fires).
        EncounterResult? Run(GameDifficulty d) =>
            new WorldEncounters(WorldmapFile.Parse(w), new SequenceRng(31, 0, 1), 0, 0)
                .Roll(110, 110, 1200, _ => 0, 1, 0, difficulty: d);
        Assert.Null(Run(GameDifficulty.Normal));
        Assert.NotNull(Run(GameDifficulty.Hard));
    }

    [Fact]
    public void DeterministicUnderSeed()
    {
        EncounterResult? Run() =>
            new WorldEncounters(World0(), new SystemCombatRng(123), 0, 0)
                .Roll(110, 110, 1200, _ => 0, playerLevel: 9, daysPlayed: 0);
        Assert.Equal(Run()?.Entry.Spawns.Single().Group, Run()?.Entry.Spawns.Single().Group);
    }

    [GameDataFact]
    public void RealWorldmapWalkIsDeterministicAndFiresEncounters()
    {
        // Mirrors the viewer's --encounter-walk: a Bresenham diagonal across the
        // Arroyo tiles, +30 game-min per step, seeded. The roll chain is pure, so
        // this is the canonical regression test (no GraphicsDevice needed).
        List<string> Walk()
        {
            using var vfs = GameFileSystem.Open(GameData.RequiredDir);
            var world = WorldmapFile.Parse(Encoding.Latin1.GetString(vfs.ReadAllBytes(@"data\worldmap.txt")));
            var clock = new GameClock();
            var enc = new WorldEncounters(world, new SystemCombatRng(7), 80, 40);
            int x = 80, y = 40, dx = 610, dy = 250, sx = 1, sy = 1, err = dx - dy;
            var groups = new List<string>();
            for (int s = 0; s < 150 && (x != 690 || y != 290); s++)
            {
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }
                clock.Ticks += 18000;
                if (enc.Roll(x, y, clock.Hour, _ => 0, playerLevel: 1, daysPlayed: clock.Day) is { } r)
                    groups.Add(r.Entry.Spawns.FirstOrDefault()?.Group ?? "?");
            }
            return groups;
        }

        List<string> a = Walk();
        List<string> b = Walk();
        Assert.NotEmpty(a);              // the wasteland bites
        Assert.Equal(a, b);             // deterministic under the seed
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
