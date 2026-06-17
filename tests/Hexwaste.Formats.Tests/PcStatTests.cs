using Hexwaste.Formats.Int;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// The PC meta-stat index map (P31 B-M0), ported from stat_defs.h PcStat — the indices get_pc_stat reads.
/// Reputation (3) and karma (4) were stubbed to 0 before; locking the mapping guards the get_pc_stat seam
/// against an accidental reorder.
/// </summary>
public class PcStatTests
{
    [Fact]
    public void IndicesMatchTheEngineEnum()
    {
        Assert.Equal(0, PcStat.UnspentSkillPoints);
        Assert.Equal(1, PcStat.Level);
        Assert.Equal(2, PcStat.Experience);
        Assert.Equal(3, PcStat.Reputation);
        Assert.Equal(4, PcStat.Karma);
        Assert.Equal(5, PcStat.Count);
    }
}
