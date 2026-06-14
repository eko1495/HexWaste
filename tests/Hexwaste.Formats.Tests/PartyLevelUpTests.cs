using System.Text;
using Hexwaste.Formats;
using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Party;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Companion level-up foundation (#13): the party.txt parser + the
/// _partyMemberIncLevels decision math, ported from fallout2-ce
/// src/party_member.cc. Pure Formats logic — no viewer wiring yet (no shippable
/// map recruits a party.txt companion; the mechanic lights up when one does).
/// </summary>
public class PartyLevelUpTests
{
    // A trimmed party.txt: a real leveller (Sulik-shaped), a "never levels" member
    // (level_up_every=0, pids=-1), and the count header that must be skipped.
    private const string Sample = """
        [Party Members]
        count=2

        [Party Member 0]   ; Player -- Not actually used!
        party_member_pid=16777216
        area_attack_mode=always, sometimes
        level_minimum=0
        level_up_every=0
        level_pids=-1

        [Party Member 4]   ; pMSulik_PID
        party_member_pid=16777313
        area_attack_mode=always, sometimes, be_careful
        level_minimum=6
        level_up_every=3
        level_pids=16777526,16777527,16777528,16777529,16777530,16777531
        """;

    [Fact]
    public void ParsesLevelTablesAndSkipsTheCountHeader()
    {
        PartyTable table = PartyTable.Parse(Sample);

        Assert.Equal(2, table.Members.Count); // only "[Party Member N]" sections, not "[Party Members]"

        PartyMemberDescription sulik = table.ForPid(16777313)!;
        Assert.Equal(4, sulik.SectionIndex);
        Assert.Equal(6, sulik.LevelMinimum);
        Assert.Equal(3, sulik.LevelUpEvery);
        Assert.Equal(new[] { 16777526, 16777527, 16777528, 16777529, 16777530, 16777531 }, sulik.LevelPids);

        PartyMemberDescription player = table.ForPid(16777216)!;
        Assert.Equal(0, player.LevelUpEvery); // never levels
        Assert.Null(table.ForPid(0x1000005)); // a Radscorpion is not a party member
    }

    [Fact]
    public void LevelPidsAreCappedAtMaxLevel()
    {
        // 8 pids declared, only the first 6 (PARTY_MEMBER_MAX_LEVEL) are kept.
        PartyTable table = PartyTable.Parse("""
            [Party Member 1]
            party_member_pid=100
            level_up_every=2
            level_pids=1 2 3 4 5 6 7 8
            """);
        Assert.Equal(PartyTable.MaxLevel, table.ForPid(100)!.LevelPids.Count);
    }

    private static PartyMemberDescription Desc(int every, int min, params int[] pids) =>
        new(SectionIndex: 1, Pid: 100, LevelMinimum: min, LevelUpEvery: every, LevelPids: pids);

    [Fact]
    public void BelowLevelMinimumNeverAdvancesAndDrawsNoRoll()
    {
        var desc = Desc(every: 3, min: 6, 101, 102);
        var state = new PartyLevelUpState();

        Assert.Null(PartyLevelUp.IncLevel(desc, state, pcLevel: 5, new ThrowingRng()));
        Assert.Equal(0, state.NumLevelUps); // the min gate is checked before NumLevelUps++
        Assert.Equal(0, state.Level);
    }

    [Fact]
    public void LevelModZeroAdvancesUnconditionallyWithoutARoll()
    {
        // level_up_every=1 → every numLevelUps has levelMod 0 → guaranteed advance, no roll.
        var desc = Desc(every: 1, min: 0, 101, 102);
        var state = new PartyLevelUpState();

        Assert.Equal(101, PartyLevelUp.IncLevel(desc, state, pcLevel: 1, new ThrowingRng()));
        Assert.Equal(1, state.Level);
        Assert.Equal(0, state.IsEarly); // levelMod==0 is not an "early" advance
    }

    [Fact]
    public void InvertedRollFailsToAdvanceWhenTheDieExceedsTheThreshold()
    {
        // numLevelUps=1, levelMod=1, threshold = 100*1/3 = 33. The engine does NOT
        // advance when randomBetween(0,100) > threshold (party_member.cc:1528).
        var desc = Desc(every: 3, min: 0, 101, 102);
        var state = new PartyLevelUpState();

        Assert.Null(PartyLevelUp.IncLevel(desc, state, pcLevel: 1, new FixedRng(34))); // 34 > 33 → no advance
        Assert.Equal(0, state.Level);
        Assert.Equal(1, state.NumLevelUps);

        // Next PC level: numLevelUps=2, levelMod=2, threshold=66; a low die advances early.
        Assert.Equal(101, PartyLevelUp.IncLevel(desc, state, pcLevel: 2, new FixedRng(10))); // 10 ≤ 66 → advance
        Assert.Equal(1, state.Level);
        Assert.Equal(1, state.IsEarly); // an early (levelMod!=0) advance sets the skip flag
    }

    [Fact]
    public void AnEarlyAdvanceSkipsUntilTheNextCycleBoundary()
    {
        var desc = Desc(every: 3, min: 0, 101, 102, 103);
        var state = new PartyLevelUpState { Level = 1, NumLevelUps = 1, IsEarly = 1 };

        // numLevelUps=2 → levelMod=2 != 0 → skip (no advance, no roll), isEarly stays set.
        Assert.Null(PartyLevelUp.IncLevel(desc, state, pcLevel: 9, new ThrowingRng()));
        Assert.Equal(1, state.IsEarly);
        // numLevelUps=3 → levelMod=0 → clears isEarly, still no advance this call.
        Assert.Null(PartyLevelUp.IncLevel(desc, state, pcLevel: 10, new ThrowingRng()));
        Assert.Equal(0, state.IsEarly);
        Assert.Equal(1, state.Level);
    }

    [Fact]
    public void AdvancesStopAtTheLastStageAndApplyEveryPidInOrder()
    {
        // every=1 → guaranteed advance each call; 3 stages → 3 advances then capped.
        var desc = Desc(every: 1, min: 0, 101, 102, 103);
        var state = new PartyLevelUpState();
        var rng = new ThrowingRng(); // levelMod==0 path never rolls

        Assert.Equal(101, PartyLevelUp.IncLevel(desc, state, 1, rng));
        Assert.Equal(102, PartyLevelUp.IncLevel(desc, state, 2, rng));
        Assert.Equal(103, PartyLevelUp.IncLevel(desc, state, 3, rng));
        Assert.Null(PartyLevelUp.IncLevel(desc, state, 4, rng)); // Level == LevelPids.Count → capped
        Assert.Equal(3, state.Level);
    }

    [Fact]
    public void AFullSulikCycleAppliesAllSixStageProtosInOrder()
    {
        // every=3 with the early roll always succeeding (die 0): the member advances
        // once per cycle (at levelMod==1), so 6 stages take 16 PC level-ups.
        var desc = Desc(every: 3, min: 6, 201, 202, 203, 204, 205, 206);
        var state = new PartyLevelUpState();
        var applied = new List<int>();

        for (int pc = 6; pc <= 6 + 20; pc++)
            if (PartyLevelUp.IncLevel(desc, state, pc, new FixedRng(0)) is { } stagePid)
                applied.Add(stagePid);

        Assert.Equal(new[] { 201, 202, 203, 204, 205, 206 }, applied);
        Assert.Equal(6, state.Level);
    }

    [GameDataFact]
    public void RealPartyTxtCarriesTheKnownLevelTables()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        PartyTable table = PartyTable.Parse(Encoding.Latin1.GetString(vfs.ReadAllBytes(@"data\party.txt")));

        // Sulik (section 4): level_minimum 6, every 3, 6 upgrade protos.
        PartyMemberDescription sulik = table.ForPid(16777313)!;
        Assert.Equal(4, sulik.SectionIndex);
        Assert.Equal(6, sulik.LevelMinimum);
        Assert.Equal(3, sulik.LevelUpEvery);
        Assert.Equal(6, sulik.LevelPids.Count);

        // The Player pseudo-member never levels.
        Assert.Equal(0, table.ForPid(16777216)!.LevelUpEvery);
        // No shippable critter PID we test with is a party member (the honesty check).
        Assert.Null(table.ForPid(0x1000005)); // Radscorpion
    }

    private sealed class FixedRng(int value) : ICombatRng
    {
        public int Next(int minInclusive, int maxExclusive) => Math.Clamp(value, minInclusive, maxExclusive - 1);
    }

    private sealed class ThrowingRng : ICombatRng
    {
        public int Next(int minInclusive, int maxExclusive) =>
            throw new InvalidOperationException("RNG must not be drawn on the levelMod==0 / gated paths");
    }
}
