namespace Hexwaste.Formats.Int;

/// <summary>
/// A static census scanner for <c>set_global_var</c> writes in .int bytecode (P124 quest-QA
/// sweep). File layout per fallout2-ce src/interpreter.cc (all big-endian): 42-byte header,
/// u32 procedureCount at 0x2A + 24-byte procedure records, the identifiers block
/// (u32 size + blob + 0xFFFFFFFF), the static-strings block (u32 size or 0xFFFFFFFF), then
/// code to EOF. Instructions are u16 words (valid iff the high byte has 0x80); only
/// OPCODE_PUSH (low bits 0x001) carries a 4-byte inline operand — 0xC001 = int push.
///
/// Two write tiers:
///  - CONST: the exact <c>[push int GVAR][push int VALUE][0x80C6]</c> triple — how the
///    compiler emits a literal <c>set_global_var(GVAR, N)</c> (opSetGlobalVar pops value
///    then variable, interpreter_extra.cc:1222, so the gvar is pushed first).
///  - TOUCHED: an upper bound for computed writes (counters like
///    <c>set_global_var(G, global_var(G) + 1)</c>) — the script pushes the gvar constant
///    somewhere AND performs at least one set_global_var. Includes read-only false
///    positives by design; the census labels it "dynamic/possible", never "verified".
/// </summary>
public static class GlobalWriteScan
{
    private const ushort SetGlobalVar = 0x80C6;
    private const ushort PushInt = 0xC001;

    public sealed record Result(
        IReadOnlyDictionary<int, SortedSet<int>> ConstWrites,
        IReadOnlySet<int> PushedInts,
        int SetGlobalCount);

    /// <summary>Scan one .int image; returns empty results (never throws) on a malformed file.</summary>
    public static Result Scan(byte[] data)
    {
        var constWrites = new Dictionary<int, SortedSet<int>>();
        var pushed = new HashSet<int>();
        int setCount = 0;
        try
        {
            int pc = CodeStart(data);
            // The rolling last-two-instruction window for the exact const-const-set triple.
            (bool IsIntPush, int Value) prev1 = default, prev2 = default;
            while (pc + 2 <= data.Length)
            {
                ushort word = ReadU16(data, pc);
                pc += 2;
                if ((word & 0x8000) == 0)
                {
                    prev2 = prev1 = default; // stream desync — drop the window, keep walking
                    continue;
                }
                if ((word & 0x3FF) == 0x001) // OPCODE_PUSH: 4-byte operand
                {
                    int operand = ReadI32(data, pc);
                    pc += 4;
                    bool isInt = word == PushInt;
                    if (isInt)
                        pushed.Add(operand);
                    prev2 = prev1;
                    prev1 = (isInt, operand);
                    continue;
                }
                if ((0x8000 | (word & 0x3FF)) == SetGlobalVar)
                {
                    setCount++;
                    if (prev1.IsIntPush && prev2.IsIntPush)
                        (constWrites.TryGetValue(prev2.Value, out SortedSet<int>? vals)
                            ? vals : constWrites[prev2.Value] = []).Add(prev1.Value);
                }
                prev2 = prev1;
                prev1 = default;
            }
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            // A truncated/odd file ends the scan with whatever was collected.
        }
        return new Result(constWrites, pushed, setCount);
    }

    /// <summary>Locate the code area (after header, procedures, identifiers and strings).</summary>
    private static int CodeStart(byte[] data)
    {
        int procCount = ReadI32(data, 42);
        int identBase = 42 + 4 + 24 * procCount;
        int identSize = ReadI32(data, identBase);
        int term = identBase + 4 + identSize;
        int strBase = term + 4;
        int strSize = ReadI32(data, strBase);
        return strSize < 0 ? strBase + 4 : strBase + 4 + strSize + 4;
    }

    private static ushort ReadU16(byte[] d, int o) => (ushort)((d[o] << 8) | d[o + 1]);

    private static int ReadI32(byte[] d, int o) =>
        (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];
}
