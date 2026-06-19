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
}
