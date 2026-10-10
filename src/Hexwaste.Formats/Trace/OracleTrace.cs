namespace Hexwaste.Formats.Trace;

/// <summary>Process-wide oracle trace sink (inactive unless Open was called by --trace). Line
/// formats are byte-identical to the fo2ce patch's oracle.cc oracleTrace* functions.</summary>
public static class OracleTrace
{
    private static TextWriter? _writer;

    public static bool Active => _writer is not null;
    public static Func<uint>? Clock { get; set; }

    public static void Open(TextWriter writer, string header)
    {
        _writer = writer;
        _writer.WriteLine($"# {header}");
    }

    public static void Close()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
    }

    private static void Line(string body)
    {
        if (_writer is null)
            return;
        _writer.Write("t=");
        _writer.Write(Clock?.Invoke() ?? 0u);
        _writer.Write(' ');
        _writer.WriteLine(body);
    }

    public static void Rng(int min, int max, int result, string site) => Line($"RNG {min}..{max} -> {result} site={site}");
    public static void Proc(int selfId, string programName, string procName) => Line($"PROC {programName.ToLowerInvariant()} {procName} self=obj#{selfId}");
    public static void Gvar(int index, int oldValue, int newValue) => Line($"GVAR {index} {oldValue}->{newValue}");
    public static void Mvar(int index, int oldValue, int newValue) => Line($"MVAR {index} {oldValue}->{newValue}");
    public static void Lvar(int sid, int index, int oldValue, int newValue) => Line($"LVAR sid=0x{sid:X8} {index} {oldValue}->{newValue}");
    public static void Turn(int objId, int ap) => Line($"TURN obj#{objId} ap={ap}");
    public static void Attack(int attackerId, int defenderId, int chance, bool hit, int damage, int defenderFlags) =>
        Line($"ATTACK obj#{attackerId}->obj#{defenderId} chance={chance} hit={(hit ? 1 : 0)} dmg={damage} dflags=0x{defenderFlags:X}");
    public static void Move(int objId, int fromTile, int toTile)
    {
        if (fromTile != toTile)
            Line($"MOVE obj#{objId} {fromTile}->{toTile}");
    }
    public static void Spawn(int objId, int pid, int tile) => Line($"SPAWN obj#{objId} pid=0x{pid:X8} tile={tile}");
}
