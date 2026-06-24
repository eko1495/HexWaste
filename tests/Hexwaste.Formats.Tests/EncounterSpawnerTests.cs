using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Hex;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

public class EncounterSpawnerTests
{
    // An interior tile (row 100, col 100); NOT on a grid edge, where TileInDirection
    // can't step and a formation would collapse onto one hex.
    private const int DudeTile = 20100;

    private sealed record Scenario(EncounterResult Result, WorldmapFile World, ICombatRng Rng);

    // Build a one-table world whose single entry points at the given [Encounter: ...]
    // group block. A seq seeds a SeqRng (else MinRng = always the low end).
    private static Scenario Setup(string groups, string enc = "Enc:(4-4) GRP AMBUSH Player", params int[] seq)
    {
        string w = $"""
            [Encounter Table 0]
            lookup_name=T
            enc_00=Chance:100%,{enc}

            {groups}
            """;
        WorldmapFile world = WorldmapFile.Parse(w);
        EncounterTable table = world.Table("T")!;
        return new Scenario(new EncounterResult(table, table.Entries[0]), world,
            seq.Length > 0 ? new SeqRng(seq) : new MinRng());
    }

    private static IReadOnlyList<SpawnInstruction> Plan(Scenario s, int partyCount = 1,
        IReadOnlyList<int>? startTiles = null, Func<int, bool>? isBlocked = null,
        Func<int, int, bool>? reachable = null, Func<int, int>? getGlobal = null,
        GameDifficulty difficulty = GameDifficulty.Normal) =>
        EncounterSpawner.Plan(s.Result, s.World, s.Rng, DudeTile, dudePerception: 5, partyCount,
            startTiles ?? [DudeTile], isBlocked ?? (_ => false), reachable ?? ((_, _) => true),
            getGlobal, difficulty: difficulty);

    [Fact]
    public void RatioAndSingleMembersScaleWithGroupSize()
    {
        // critterCount = Between(4,4) = 4. type_00 ratio:50% → 50*4/100 = 2; the
        // ratio-less type_01 is a SINGLE leader → exactly 1. Total 3.
        IReadOnlyList<SpawnInstruction> plan = Plan(Setup("""
            [Encounter: GRP]
            type_00=ratio:50%, pid:100, Script:618, Item:(2-2)200(wielded)
            type_01=pid:101
            position=huddle, spacing:1
            """));

        Assert.Equal(3, plan.Count);
        Assert.Equal(2, plan.Count(s => s.Pid == 100));
        Assert.Single(plan, s => s.Pid == 101);
        SpawnInstruction grunt = plan.First(s => s.Pid == 100);
        Assert.Equal(617, grunt.ScriptIndex);                       // Script:618 → N-1
        Assert.Equal(new SpawnItem(200, 2, Wielded: true, Worn: false), Assert.Single(grunt.Items));
        Assert.Equal(-1, plan.First(s => s.Pid == 101).ScriptIndex); // no Script: → unbound
        Assert.All(plan, s => Assert.False(s.Dead));
    }

    [Fact]
    public void FightingEntryPutsItsSubGroupsOnDistinctTeams()
    {
        // "GRP_A AND GRP_B FIGHTING Player" → group A on team 1, group B on team 2, so the
        // factions brawl (phase-16 M3); a plain AMBUSH keeps everyone on the one team.
        IReadOnlyList<SpawnInstruction> fight = Plan(Setup("""
            [Encounter: GRP_A]
            type_00=ratio:100%, pid:100
            position=huddle, spacing:1

            [Encounter: GRP_B]
            type_00=ratio:100%, pid:200
            position=huddle, spacing:1
            """, "Enc:(2-2) GRP_A AND (2-2) GRP_B FIGHTING Player"));
        Assert.All(fight.Where(s => s.Pid == 100), s => Assert.Equal(1, s.Team));
        Assert.All(fight.Where(s => s.Pid == 200), s => Assert.Equal(2, s.Team));
        Assert.Contains(fight, s => s.Team == 1);
        Assert.Contains(fight, s => s.Team == 2);
    }

    [Fact]
    public void AmbushEntryKeepsEveryMemberOnTheSameTeam()
    {
        IReadOnlyList<SpawnInstruction> plan = Plan(Setup("""
            [Encounter: GRP]
            type_00=ratio:100%, pid:100
            position=huddle, spacing:1
            """, "Enc:(3-3) GRP AMBUSH Player"));
        Assert.NotEmpty(plan);
        Assert.All(plan, s => Assert.Equal(1, s.Team));
    }

    [Fact]
    public void RatioClampsToAtLeastOne()
    {
        // 25% of 1 = 0 → clamped to 1; the SINGLE leader adds 1. Total 2.
        Assert.Equal(2, Plan(Setup("""
            [Encounter: GRP]
            type_00=ratio:25%, pid:100
            type_01=pid:101
            position=huddle
            """, "Enc:(1-1) GRP AMBUSH Player")).Count);
    }

    [Fact]
    public void PartyOverTwoAddsTwoCritters()
    {
        // Enc:(1-1) → 1, +2 for a real party → 3; ratio:100% spawns all 3.
        const string g = """
            [Encounter: GRP]
            type_00=ratio:100%, pid:100
            position=huddle
            """;
        Assert.Equal(3, Plan(Setup(g, "Enc:(1-1) GRP AMBUSH Player"), partyCount: 4).Count);
        Assert.Single(Plan(Setup(g, "Enc:(1-1) GRP AMBUSH Player"), partyCount: 2)); // ≤2 → no bonus
    }

    [Fact]
    public void HardDifficultyAddsTwoToTheGroup()
    {
        // worldmap.cc:3702 HARD: critterCount += 2. Enc:(2-2) → 2, +2 = 4 (vs 2 at Normal).
        const string g = """
            [Encounter: GRP]
            type_00=ratio:100%, pid:100
            position=Surrounding
            """;
        const string enc = "Enc:(2-2) GRP AMBUSH Player";
        Assert.Equal(2, Plan(Setup(g, enc)).Count);                                   // Normal control
        Assert.Equal(4, Plan(Setup(g, enc), difficulty: GameDifficulty.Hard).Count);  // +2
    }

    [Fact]
    public void EasyDifficultySubtractsTwoAboveTheMinimum()
    {
        // worldmap.cc:3696 EASY: critterCount -= 2 (the roll 6 exceeds min+2, so it actually
        // drops). Enc:(4-8), SeqRng→6 → Normal 6, Easy 4.
        const string g = """
            [Encounter: GRP]
            type_00=ratio:100%, pid:50
            position=Surrounding
            """;
        const string enc = "Enc:(4-8) GRP AMBUSH Player";
        Assert.Equal(6, Plan(Setup(g, enc, 6)).Count);                                   // Normal control
        Assert.Equal(4, Plan(Setup(g, enc, 6), difficulty: GameDifficulty.Easy).Count);  // −2
    }

    [Fact]
    public void EasyDifficultyFloorsAtTheEntryMinimum()
    {
        // worldmap.cc:3697 — a fixed Enc:(N-N) entry can't drop below N (count−2 < min → min),
        // so Easy is a no-op on the common fixed-size entry. Enc:(4-4): Easy still 4.
        const string g = """
            [Encounter: GRP]
            type_00=ratio:100%, pid:50
            position=Surrounding
            """;
        const string enc = "Enc:(4-4) GRP AMBUSH Player";
        Assert.Equal(4, Plan(Setup(g, enc), difficulty: GameDifficulty.Easy).Count); // floored, == Normal
    }

    [Fact]
    public void DeadMemberIsMarkedAsCorpse()
    {
        Assert.True(Assert.Single(Plan(Setup("""
            [Encounter: GRP]
            type_00=Dead, ratio:100%, pid:100
            position=huddle
            """, "Enc:(1-1) GRP AMBUSH Player"))).Dead);
    }

    [Fact]
    public void PidlessMemberSpawnsNothing()
    {
        // Special1's type_00=ratio:0% carries no pid → our parse yields pid 0 → skip.
        Assert.Empty(Plan(Setup("""
            [Encounter: GRP]
            type_00=ratio:0%
            position=huddle
            """, "Enc:(1-1) GRP AMBUSH Player")));
    }

    [Fact]
    public void SurroundingFacesEachSpawnTowardTheDude()
    {
        IReadOnlyList<SpawnInstruction> plan = Plan(Setup("""
            [Encounter: RING]
            type_00=ratio:100%, pid:50
            position=Surrounding
            """, "Enc:(3-3) RING AMBUSH Player"));

        Assert.Equal(3, plan.Count);
        Assert.All(plan, s => Assert.Equal(HexGrid.RotationTo(s.Tile, DudeTile), s.Rotation));
    }

    [Fact]
    public void AllTilesBlockedAndUnreachableSkipsTheCritter()
    {
        // Nothing is placeable → the 25-retry loop gives up → no instruction.
        Assert.Empty(Plan(Setup("""
            [Encounter: GRP]
            type_00=ratio:100%, pid:100
            position=huddle
            """, "Enc:(1-1) GRP AMBUSH Player"), isBlocked: _ => true, reachable: (_, _) => false));
    }

    [Fact]
    public void ItemQuantityRollsWithinRange()
    {
        // Item:(2-5): Between(2,5) draws rng.Next(2,6); SeqRng yields 4. The group-size
        // roll Between(1,1) is short-circuited (no draw), so the first draw is the item.
        SpawnItem item = Assert.Single(Assert.Single(Plan(Setup("""
            [Encounter: GRP]
            type_00=ratio:100%, pid:100, Item:(2-5)200
            position=huddle
            """, "Enc:(1-1) GRP AMBUSH Player", 4))).Items);
        Assert.Equal(new SpawnItem(200, 4, Wielded: false, Worn: false), item);
    }

    [Fact]
    public void WedgeAnchorsOnARandomStartPoint()
    {
        const int start = 18100; // interior, so the 2nd wedge critter can step off it
        IReadOnlyList<SpawnInstruction> plan = Plan(Setup("""
            [Encounter: GRP]
            type_00=ratio:100%, pid:100
            position=wedge, spacing:2
            """, "Enc:(2-2) GRP AMBUSH Player"), startTiles: [start]);
        // First wedge critter lands on the chosen start point (callCount==0 path).
        Assert.Equal(2, plan.Count);
        Assert.Equal(start, plan[0].Tile);
    }

    [Fact]
    public void SpawnedTilesNeverOverlap()
    {
        // The placed-tile guard means no two critters share a hex even when the
        // formation geometry would otherwise repeat one.
        IReadOnlyList<SpawnInstruction> plan = Plan(Setup("""
            [Encounter: RING]
            type_00=ratio:100%, pid:50
            position=Surrounding
            """, "Enc:(6-6) RING AMBUSH Player"));
        Assert.Equal(plan.Count, plan.Select(s => s.Tile).Distinct().Count());
    }

    [Fact]
    public void HuddleCollisionForcesSkips()
    {
        // spacing:0 makes every huddle step land back on the anchor (TileInDirection
        // by 0 = same hex), so the placed-dedup must skip all but the first critter —
        // proving the guard actually rejects genuine collisions, not just spreads.
        IReadOnlyList<SpawnInstruction> plan = Plan(Setup("""
            [Encounter: GRP]
            type_00=ratio:100%, pid:100
            position=huddle, spacing:0
            """, "Enc:(4-4) GRP AMBUSH Player"));
        Assert.Single(plan);
    }

    [Fact]
    public void WedgeStepsByRotOffsetAndSpacingFromTheAnchor()
    {
        // Pin the wedge stepping geometry: critter 1 sits on the start point, critter 2
        // steps `spacing` hexes in (rotOffset[0]=1 + tileDirs[0]) — re-derived via the
        // same HexGrid helper so the test fails if the formula wiring changes.
        const int start = 18100, spacing = 2;
        IReadOnlyList<SpawnInstruction> plan = Plan(Setup("""
            [Encounter: GRP]
            type_00=ratio:100%, pid:100
            position=wedge, spacing:2
            """, "Enc:(2-2) GRP AMBUSH Player"), startTiles: [start]);

        Assert.Equal(2, plan.Count);
        Assert.Equal(start, plan[0].Tile);
        int dir0 = HexGrid.RotationTo(start, DudeTile);
        Assert.Equal(HexGrid.TileInDirection(start, (1 + dir0) % 6, spacing), plan[1].Tile);
    }

    [Fact]
    public void SurroundingRingsAtThePerceptionDistance()
    {
        // MinRng: distance = max(0, -2 + Perception(5)) = 3; rDist = Between(0, 3/2)=0,
        // so the spawn sits exactly 3 hexes from the dude (the ring radius), proving the
        // distance reads Perception (not the dead group-level Distance) and draws it.
        IReadOnlyList<SpawnInstruction> plan = Plan(Setup("""
            [Encounter: RING]
            type_00=ratio:100%, pid:50
            position=Surrounding, distance:9
            """, "Enc:(1-1) RING AMBUSH Player"));
        Assert.Equal(3, HexGrid.Distance(DudeTile, Assert.Single(plan).Tile)); // 3, not the group distance:9
    }

    [Fact]
    public void MemberIfConditionGatesTheSpawn()
    {
        // type_01 only spawns when Global(5) > 0 (phase-10 #7); type_00 is unconditional.
        const string g = """
            [Encounter: GRP]
            type_00=ratio:100%, pid:100
            type_01=ratio:100%, pid:101, If(Global(5) > 0)
            position=huddle
            """;
        const string enc = "Enc:(1-1) GRP AMBUSH Player";

        // Global(5)=0 → the gated member is skipped (only pid 100).
        Assert.DoesNotContain(Plan(Setup(g, enc), getGlobal: _ => 0), s => s.Pid == 101);
        // Global(5)=1 → it spawns.
        Assert.Contains(Plan(Setup(g, enc), getGlobal: x => x == 5 ? 1 : 0), s => s.Pid == 101);
    }

    [Fact]
    public void PerMemberDistanceOverridesThePerceptionRing()
    {
        // A member Distance pins the surrounding ring radius (2), instead of the
        // Perception±2 default (which MinRng would make 3) — phase-10 #7.
        IReadOnlyList<SpawnInstruction> plan = Plan(Setup("""
            [Encounter: RING]
            type_00=ratio:100%, pid:50, Distance:2
            position=Surrounding
            """, "Enc:(1-1) RING AMBUSH Player"));
        Assert.Equal(2, HexGrid.Distance(DudeTile, Assert.Single(plan).Tile));
    }

    [Fact]
    public void DeterministicUnderSeed()
    {
        const string g = """
            [Encounter: RING]
            type_00=ratio:100%, pid:50, Item:(1-4)200
            position=Surrounding
            """;
        // Project to a string key — SpawnInstruction.Items is an IReadOnlyList, which
        // breaks record value-equality, so compare the meaningful fields explicitly.
        static IEnumerable<string> Keys(IReadOnlyList<SpawnInstruction> plan) => plan.Select(s =>
            $"{s.Pid}/{s.ScriptIndex}/{s.Tile}/{s.Rotation}/{s.Dead}/"
            + string.Join(",", s.Items.Select(i => $"{i.Pid}x{i.Count}{(i.Wielded ? "w" : "")}{(i.Worn ? "a" : "")}")));
        IReadOnlyList<SpawnInstruction> Run() =>
            Plan(Setup(g, "Enc:(2-6) RING AMBUSH Player") with { Rng = new SystemCombatRng(99) });
        Assert.Equal(Keys(Run()), Keys(Run()));
    }

    private sealed class MinRng : ICombatRng
    {
        public int Next(int minInclusive, int maxExclusive) => minInclusive;
    }

    private sealed class SeqRng(params int[] values) : ICombatRng
    {
        private int _i;
        public int Next(int minInclusive, int maxExclusive) =>
            Math.Clamp(values[Math.Min(_i++, values.Length - 1)], minInclusive, maxExclusive - 1);
    }
}
