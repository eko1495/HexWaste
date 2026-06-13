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
    }
}
