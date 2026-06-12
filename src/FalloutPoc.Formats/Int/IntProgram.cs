using System.Buffers.Binary;

namespace FalloutPoc.Formats.Int;

/// <summary>
/// One procedure record of a compiled script, ported from fallout2-ce
/// src/interpreter.h struct Procedure (24 bytes on disk, big-endian).
/// </summary>
/// <param name="Name">Resolved from <c>nameOffset</c> into the identifiers block.</param>
/// <param name="Flags">ProcedureFlags: 1=timed 2=conditional 4=imported 8=exported 16=critical.</param>
/// <param name="Time">Trigger time for timed procedures.</param>
/// <param name="ConditionOffset">Bytecode offset of the condition for conditional procedures.</param>
/// <param name="BodyOffset">Absolute file offset of the procedure body bytecode.</param>
/// <param name="ArgumentCount">Number of declared arguments.</param>
public sealed record IntProcedure(
    string Name,
    int Flags,
    int Time,
    int ConditionOffset,
    int BodyOffset,
    int ArgumentCount)
{
    /// <summary>ported from fallout2-ce src/interpreter.h PROCEDURE_FLAG_IMPORTED.</summary>
    public bool IsImported => (Flags & 0x04) != 0;

    /// <summary>ported from fallout2-ce src/interpreter.h PROCEDURE_FLAG_CRITICAL.</summary>
    public bool IsCritical => (Flags & 0x10) != 0;
}

/// <summary>
/// A parsed Fallout 2 .int script, ported from fallout2-ce src/interpreter.cc
/// programCreateByPath(). Layout (all big-endian):
/// 42 bytes of stub bytecode (the engine starts interpreting at offset 0; the
/// stub jumps to the global-variable init prologue and hosts the fixed return
/// addresses 18/20/24/28/32 used by the procedure call convention), then at
/// 0x2A an int32 procedure count followed by 24-byte procedure records, the
/// identifiers block ([int32 size] then [u16 len][NUL-terminated name]
/// entries), the static strings block in the same shape (size 0xFFFFFFFF when
/// absent), and bytecode to EOF. Instruction pointers index the whole file.
/// </summary>
public sealed class IntProgram
{
    private const int ProcedureTableOffset = 42;
    private const int ProcedureRecordSize = 24;

    private readonly int _identifiersOffset;
    private readonly int _staticStringsOffset;

    /// <summary>The entire script file; bytecode addresses index into it.</summary>
    public byte[] Data { get; }

    public IReadOnlyList<IntProcedure> Procedures { get; }

    private IntProgram(byte[] data, IntProcedure[] procedures, int identifiersOffset, int staticStringsOffset)
    {
        Data = data;
        Procedures = procedures;
        _identifiersOffset = identifiersOffset;
        _staticStringsOffset = staticStringsOffset;
    }

    public static IntProgram Load(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Load(memory.ToArray());
    }

    public static IntProgram Load(byte[] data)
    {
        if (data.Length < ProcedureTableOffset + 4)
            throw new InvalidDataException("Script file too small to contain a procedure table.");

        int procedureCount = ReadInt32(data, ProcedureTableOffset);
        if (procedureCount < 0 || ProcedureTableOffset + 4 + (long)procedureCount * ProcedureRecordSize > data.Length)
            throw new InvalidDataException($"Implausible procedure count {procedureCount}.");

        // ported from programCreateByPath(): identifiers = data + 42 + 4 + 24 * count,
        // staticStrings = identifiers + int32(identifiers) + 4 (i.e. the
        // identifier block terminator; static string offsets are read at
        // staticStrings + 4 + offset).
        int identifiersOffset = ProcedureTableOffset + 4 + procedureCount * ProcedureRecordSize;
        int staticStringsOffset = identifiersOffset + ReadInt32(data, identifiersOffset) + 4;

        var procedures = new IntProcedure[procedureCount];
        for (int i = 0; i < procedureCount; i++)
        {
            int recordOffset = ProcedureTableOffset + 4 + i * ProcedureRecordSize;
            int nameOffset = ReadInt32(data, recordOffset);
            procedures[i] = new IntProcedure(
                Name: ReadCString(data, identifiersOffset + nameOffset),
                Flags: ReadInt32(data, recordOffset + 4),
                Time: ReadInt32(data, recordOffset + 8),
                ConditionOffset: ReadInt32(data, recordOffset + 12),
                BodyOffset: ReadInt32(data, recordOffset + 16),
                ArgumentCount: ReadInt32(data, recordOffset + 20));
        }

        return new IntProgram(data, procedures, identifiersOffset, staticStringsOffset);
    }

    /// <summary>
    /// Index of the procedure with the given name (case-insensitive) or -1,
    /// ported from fallout2-ce src/interpreter.cc programFindProcedure().
    /// </summary>
    public int FindProcedure(string name)
    {
        for (int i = 0; i < Procedures.Count; i++)
        {
            if (string.Equals(Procedures[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    /// <summary>ported from fallout2-ce src/interpreter.cc programGetIdentifier().</summary>
    public string GetIdentifier(int offset) => ReadCString(Data, _identifiersOffset + offset);

    /// <summary>
    /// Static string for a 0x9001-tagged value, ported from fallout2-ce
    /// src/interpreter.cc programGetString() (staticStrings + 4 + offset).
    /// </summary>
    public string GetStaticString(int offset)
    {
        int position = _staticStringsOffset + 4 + offset;
        if (position < 0 || position >= Data.Length)
            throw new InvalidDataException($"Static string offset {offset} is out of range.");
        return ReadCString(Data, position);
    }

    internal short ReadCode16(int offset)
    {
        if (offset < 0 || offset + 2 > Data.Length)
            throw new InvalidDataException($"Instruction pointer {offset} is out of range.");
        return BinaryPrimitives.ReadInt16BigEndian(Data.AsSpan(offset, 2));
    }

    internal int ReadCode32(int offset)
    {
        if (offset < 0 || offset + 4 > Data.Length)
            throw new InvalidDataException($"Operand at {offset} is out of range.");
        return ReadInt32(Data, offset);
    }

    private static int ReadInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset, 4));

    private static string ReadCString(byte[] data, int offset)
    {
        int end = Array.IndexOf(data, (byte)0, offset);
        if (end < 0)
            end = data.Length;
        return System.Text.Encoding.Latin1.GetString(data, offset, end - offset);
    }
}
