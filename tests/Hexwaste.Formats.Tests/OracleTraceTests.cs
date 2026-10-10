using Hexwaste.Formats.Trace;

namespace Hexwaste.Formats.Tests;

// All OracleTrace-touching tests live in this one class: OracleTrace is static, and xUnit runs
// a class's tests sequentially, so they cannot race on the shared sink.
public class OracleTraceTests
{
    [Fact]
    public void WritesExactLineFormats()
    {
        var sw = new StringWriter();
        try
        {
            OracleTrace.Open(sw, "hdr");
            OracleTrace.Clock = () => 42u;
            OracleTrace.Gvar(7, 0, 1);
            OracleTrace.Lvar(-1, 3, 5, 6);
            OracleTrace.Attack(1, 2, 60, true, 9, 0x10);
            OracleTrace.Spawn(5, -2, 100);
            OracleTrace.Move(4, 9, 9);
        }
        finally
        {
            OracleTrace.Clock = null;
            OracleTrace.Close();
        }

        Assert.Equal(
            "# hdr\n" +
            "t=42 GVAR 7 0->1\n" +
            "t=42 LVAR sid=0xFFFFFFFF 3 5->6\n" +
            "t=42 ATTACK obj#1->obj#2 chance=60 hit=1 dmg=9 dflags=0x10\n" +
            "t=42 SPAWN obj#5 pid=0xFFFFFFFE tile=100\n",
            sw.ToString().Replace("\r\n", "\n"));
    }

    [Fact]
    public void InactiveAfterCloseAndWritesNothing()
    {
        var sw = new StringWriter();
        OracleTrace.Open(sw, "h");
        OracleTrace.Close();
        Assert.False(OracleTrace.Active);
        OracleTrace.Gvar(1, 0, 1);
        Assert.Equal("# h\n", sw.ToString().Replace("\r\n", "\n"));
    }
}
