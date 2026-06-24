using System.Text;
using Hexwaste.Formats;
using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

public class AiPacketTests
{
    private const string Sample = """
        [Generic Guards]
        packet_num=12
        min_to_hit=20
        min_hp=4
        max_dist=10
        distance=on_your_own
        disposition=defensive

        [Thugs]
        packet_num=13
        min_to_hit=40
        min_hp=10
        max_dist=10

        [Animals]
        packet_num=8
        min_to_hit=0
        min_hp=0
        """;

    [Fact]
    public void ParsesPacketsByNumberWithM1Fields()
    {
        AiPacketTable table = AiPacketTable.Parse(Sample);
        Assert.Equal(3, table.Count);

        AiPacket guard = table.Get(12)!;
        Assert.Equal("Generic Guards", guard.Name);
        Assert.Equal(20, guard.MinToHit);
        Assert.Equal(4, guard.MinHp);
        Assert.Equal(10, guard.MaxDist);
        Assert.Equal("on_your_own", guard.Distance);

        Assert.Equal(40, table.Get(13)!.MinToHit);
        Assert.Equal(0, table.Get(8)!.MinHp);
        Assert.Null(table.Get(999)); // unknown packet
    }

    [Fact]
    public void IgnoresCommentsAndBlankLinesAndStripsInlineComments()
    {
        const string text = """
            ; a comment
            [Pkt]
            packet_num=5
            min_to_hit=33 ; inline note
            min_hp=7
            """;
        AiPacket p = AiPacketTable.Parse(text).Get(5)!;
        Assert.Equal(33, p.MinToHit);
        Assert.Equal(7, p.MinHp);
    }

    [Fact]
    public void ParsesHurtTooMuchKeywordListIntoDamMask()
    {
        // ported keyword->mask: "crippled" = legs+arms (0x3C, NOT blind), "blind" = 0x40.
        const string text = """
            [Both]
            packet_num=1
            hurt_too_much=crippled, blind

            [Arms]
            packet_num=2
            hurt_too_much=crippled_arms

            [None]
            packet_num=3
            min_hp=5
            """;
        AiPacketTable table = AiPacketTable.Parse(text);
        Assert.Equal(CriticalTables.DamCripLimbs | CriticalTables.DamBlind, table.Get(1)!.HurtTooMuch); // 0x7C
        Assert.Equal(CriticalTables.DamCripArmAny, table.Get(2)!.HurtTooMuch); // 0x30
        Assert.Equal(0, table.Get(3)!.HurtTooMuch); // absent → never flee on hurt
    }

    [GameDataFact]
    public void RealAiTxtParsesAllPacketsWithKnownSliceValues()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        AiPacketTable table = AiPacketTable.Parse(Encoding.Latin1.GetString(vfs.ReadAllBytes(@"data\ai.txt")));

        // 187 [Section]s in the shipped ai.txt (verified in phase-9 research).
        Assert.Equal(187, table.Count);

        // Slice humanoid packets from the research report's table.
        Assert.Equal(20, table.Get(12)!.MinToHit); // Generic Guards
        Assert.Equal(4, table.Get(12)!.MinHp);
        Assert.Equal(40, table.Get(13)!.MinToHit); // Thugs
        Assert.Equal(34, table.Get(14)!.MinToHit); // Peasants

        // P34-M2: the real hurt_too_much masks (read from the shipped ai.txt).
        Assert.Equal(CriticalTables.DamBlind, table.Get(8)!.HurtTooMuch);                          // Animals: "blind"
        Assert.Equal(CriticalTables.DamCripLimbs | CriticalTables.DamBlind, table.Get(14)!.HurtTooMuch); // Peasants: "crippled, blind"
        Assert.Equal(CriticalTables.DamBlind, table.Get(33)!.HurtTooMuch);                         // Den slave coward: "blind"

        // P42: the real chem_use modes. The two golden-fight enemies — Animals(8, scorpion) and
        // Peasants(14) — have NO chem_use → 0 clean → never heal → the combat goldens stay byte-identical.
        Assert.Equal(0, table.Get(8)!.ChemUse);   // Animals: clean (absent)
        Assert.Equal(0, table.Get(14)!.ChemUse);  // Peasants: clean (absent)
        Assert.Equal(2, table.Get(12)!.ChemUse);  // Generic Guards: stims_when_hurt_lots
        Assert.Equal(4, table.Get(50)!.ChemUse);  // anytime

        // P43: the real best_weapon prefs (gBestWeaponKeys index). Generic Guards(12) = ranged_over_melee(3),
        // a real driver (denbus/kladwtwn pkt12 NPCs carry a backup); Animals(8, scorpion) absent → -1 →
        // the default ordering, but scorpions are non-biped + carry no weapons → never switch → goldens hold.
        Assert.Equal(3, table.Get(12)!.BestWeapon);  // Generic Guards: ranged_over_melee
        Assert.Equal(-1, table.Get(8)!.BestWeapon);  // Animals: absent
        Assert.Equal(-1, table.Get(13)!.BestWeapon); // Thugs: absent
    }

    [Theory]
    [InlineData("clean", 0)]
    [InlineData("stims_when_hurt_little", 1)]
    [InlineData("stims_when_hurt_lots", 2)]
    [InlineData("sometimes", 3)]
    [InlineData("anytime", 4)]
    [InlineData("always", 5)]
    [InlineData("", 0)]
    [InlineData("nonsense", 0)]
    public void ChemUseParsesFromTheGChemUseKeys(string value, int expected)
    {
        AiPacketTable t = AiPacketTable.Parse($"[P]\npacket_num=1\nchem_use={value}\n");
        Assert.Equal(expected, t.Get(1)!.ChemUse);
    }

    [Fact]
    public void ChemUseDefaultsToCleanWhenAbsent()
    {
        AiPacketTable t = AiPacketTable.Parse("[P]\npacket_num=1\nmin_hp=5\n");
        Assert.Equal(0, t.Get(1)!.ChemUse);
    }

    [Theory]
    [InlineData("no_pref", 0)]
    [InlineData("melee", 1)]
    [InlineData("melee_over_ranged", 2)]
    [InlineData("ranged_over_melee", 3)]
    [InlineData("ranged", 4)]
    [InlineData("unarmed", 5)]
    [InlineData("unarmed_over_thrown", 6)]
    [InlineData("random", 7)]
    [InlineData("never", -1)]   // a run_away_mode key, not a best_weapon key → unmatched → -1
    [InlineData("", -1)]
    public void BestWeaponParsesFromTheGBestWeaponKeys(string value, int expected)
    {
        AiPacketTable t = AiPacketTable.Parse($"[P]\npacket_num=1\nbest_weapon={value}\n");
        Assert.Equal(expected, t.Get(1)!.BestWeapon);
    }

    [Fact]
    public void BestWeaponDefaultsToMinusOneWhenAbsent()
    {
        AiPacketTable t = AiPacketTable.Parse("[P]\npacket_num=1\nmin_hp=5\n");
        Assert.Equal(-1, t.Get(1)!.BestWeapon);
    }

    // --- P72-M3 taunt fields + CombatTaunt.Pick ---------------------------

    [Fact]
    public void TauntFieldsParseFromAiTxt()
    {
        AiPacketTable t = AiPacketTable.Parse(
            "[Addict]\npacket_num=179\nchance=15\ncolor=58\nattack_start=2040\nattack_end=2059\n"
            + "run_start=2000\nrun_end=2019\n");
        AiPacket p = t.Get(179)!;
        Assert.Equal(15, p.Chance);
        Assert.Equal(58, p.TauntColor);
        Assert.Equal((2040, 2059), (p.AttackStart, p.AttackEnd));
        Assert.Equal((2000, 2019), (p.RunStart, p.RunEnd));
    }

    private sealed class SeqRng(params int[] v) : ICombatRng
    {
        private int _i;
        public int Next(int lo, int hi) => Math.Clamp(v[Math.Min(_i++, v.Length - 1)], lo, hi - 1);
    }

    [Fact]
    public void TauntPickSkipsWhenChanceIsZero()
    {
        var p = new AiPacket(8, "Scorpion", 0, 0, 0, "", "", Chance: 0,
            AttackStart: 50140, AttackEnd: 50159);
        // chance 0 short-circuits with no roll → no taunt (the Scorpion packet).
        Assert.Equal(-1, CombatTaunt.Pick(p, CombatTaunt.Type.Attack, new SeqRng(1, 50150)));
    }

    [Fact]
    public void TauntPickRollsTheChanceThenPicksAMessageInRange()
    {
        var p = new AiPacket(179, "Addict", 0, 0, 0, "", "", Chance: 15,
            AttackStart: 2040, AttackEnd: 2059, RunStart: 2000, RunEnd: 2019);
        // roll 10 ≤ 15 → taunt; next draw 2045 → the message id (inclusive range).
        Assert.Equal(2045, CombatTaunt.Pick(p, CombatTaunt.Type.Attack, new SeqRng(10, 2045)));
        // roll 16 > 15 → skip (only the chance draw consumed).
        Assert.Equal(-1, CombatTaunt.Pick(p, CombatTaunt.Type.Attack, new SeqRng(16)));
        // run range picks from its own bounds.
        Assert.Equal(2005, CombatTaunt.Pick(p, CombatTaunt.Type.Run, new SeqRng(1, 2005)));
    }

    [Fact]
    public void TauntPickReturnsMinusOneWhenRangeIsEmpty()
    {
        var p = new AiPacket(1, "P", 0, 0, 0, "", "", Chance: 100, AttackStart: 100, AttackEnd: 50);
        Assert.Equal(-1, CombatTaunt.Pick(p, CombatTaunt.Type.Attack, new SeqRng(1)));
    }

    // --- P75-M4 called_freq parse + AiCalledShot.Pick ---------------------

    [Fact]
    public void CalledFreqParsesFromAiTxt()
    {
        AiPacketTable t = AiPacketTable.Parse("[Khan]\npacket_num=5\ncalled_freq=10\n");
        Assert.Equal(10, t.Get(5)!.CalledFreq);
        Assert.Equal(0, AiPacketTable.Parse("[P]\npacket_num=1\n").Get(1)!.CalledFreq); // absent → 0
    }

    [Fact]
    public void CalledShotPicksALocationWhenTheRollFires()
    {
        // freq 1 → always fires; INT 7 >= 5; location draw 6 (eyes); to-hit there clears min_to_hit.
        Assert.Equal(6, AiCalledShot.Pick(1, 7, canAim: true, minToHit: 0, new SeqRng(1, 6), _ => 95));
    }

    [Fact]
    public void CalledShotSkipsWhenItCannotFire()
    {
        Assert.Equal(CriticalTables.LocationUncalled, AiCalledShot.Pick(0, 7, true, 0, new SeqRng(1), _ => 95)); // freq 0
        Assert.Equal(CriticalTables.LocationUncalled, AiCalledShot.Pick(5, 7, canAim: false, 0, new SeqRng(1), _ => 95)); // can't aim
        Assert.Equal(CriticalTables.LocationUncalled, AiCalledShot.Pick(10, 7, true, 0, new SeqRng(5), _ => 95)); // roll≠1
        Assert.Equal(CriticalTables.LocationUncalled, AiCalledShot.Pick(10, 3, true, 0, new SeqRng(1), _ => 95)); // INT 3 < 5
    }

    [Fact]
    public void CalledShotRevertsWhenToHitBelowMinToHit()
    {
        // fires + picks eyes, but the to-hit there (10) < min_to_hit (40) → revert to uncalled.
        Assert.Equal(CriticalTables.LocationUncalled, AiCalledShot.Pick(1, 7, true, minToHit: 40, new SeqRng(1, 6), _ => 10));
    }

    // --- P76-M1 area_attack_mode parse + AiBurstMode.ShouldBurst ----------

    [Theory]
    [InlineData("always", AreaAttack.Always)]
    [InlineData("sometimes", AreaAttack.Sometimes)]
    [InlineData("be_careful", AreaAttack.BeCareful)]
    [InlineData("be_sure", AreaAttack.BeSure)]
    [InlineData("be_absolutely_sure", AreaAttack.BeAbsolutelySure)]
    [InlineData("no_pref", AreaAttack.Never)]   // unrecognised mode → no burst
    public void AreaAttackModeParses(string s, AreaAttack expected) =>
        Assert.Equal(expected, AiBurstMode.Parse(s));

    [Fact]
    public void AreaAttackModeAbsentIsTheDefaultBranch() => Assert.Null(AiBurstMode.Parse(""));

    private static AiPacket Burst(string mode, int freq) =>
        new(1, "x", 0, 0, 0, "", "", AreaAttackMode: mode, SecondaryFreq: freq);

    [Fact]
    public void ShouldBurstHonorsTheModeAndFreq()
    {
        Assert.True(AiBurstMode.ShouldBurst(Burst("always", 0), 5, 5, 50, new SeqRng(1)));      // always
        Assert.False(AiBurstMode.ShouldBurst(Burst("no_pref", 0), 5, 5, 99, new SeqRng(1)));    // never
        Assert.True(AiBurstMode.ShouldBurst(Burst("be_careful", 0), 5, 5, 60, new SeqRng(1)));  // to-hit 60 ≥ 50
        Assert.False(AiBurstMode.ShouldBurst(Burst("be_careful", 0), 5, 5, 40, new SeqRng(1))); // 40 < 50
        Assert.True(AiBurstMode.ShouldBurst(Burst("sometimes", 5), 5, 5, 50, new SeqRng(1)));   // freq roll == 1
        Assert.False(AiBurstMode.ShouldBurst(Burst("sometimes", 5), 5, 5, 50, new SeqRng(2)));  // roll 2 ≠ 1
    }

    [Fact]
    public void ShouldBurstDefaultBranchGatesOnIntAndDistance()
    {
        // area_attack_mode absent → int<6 OR dist<10 then the 1/freq roll.
        Assert.True(AiBurstMode.ShouldBurst(Burst("", 1), 5, 20, 50, new SeqRng(1)));  // int 5 < 6 → freq 1 fires
        Assert.True(AiBurstMode.ShouldBurst(Burst("", 1), 9, 5, 50, new SeqRng(1)));   // dist 5 < 10 → fires
        Assert.False(AiBurstMode.ShouldBurst(Burst("", 1), 9, 20, 50, new SeqRng(1))); // int 9, dist 20 → no
    }
}
