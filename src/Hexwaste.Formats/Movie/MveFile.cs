namespace Hexwaste.Formats.Movie;

/// <summary>One MVE opcode inside a chunk: [u16 length][u8 type][u8 version] + payload.</summary>
public readonly record struct MveOpcode(byte Type, byte Version, ReadOnlyMemory<byte> Data);

/// <summary>The MVE opcode types (Interplay MVE format). Only the ones Hexwaste consumes are
/// named; the rest pass through as their numeric type.</summary>
public enum MveOp : byte
{
    EndOfStream = 0x00,
    EndOfChunk = 0x01,
    CreateTimer = 0x02,
    InitAudio = 0x03,
    StartStopAudio = 0x04,
    InitVideoBuffers = 0x05,
    SendBuffer = 0x07,        // present the decoded frame
    AudioData = 0x08,         // a compressed/raw audio frame
    AudioSilence = 0x09,
    InitVideoMode = 0x0A,     // width/height
    SetPalette = 0x0C,
    SetPaletteCompressed = 0x0D,
    SetDecodingMap = 0x0F,    // the per-block codec map (video, session 2)
    VideoData = 0x11,         // the delta-coded frame (video, session 2)
}

/// <summary>
/// The MVE container demuxer (P132), ported from the Interplay MVE format (fallout2-ce
/// src/movie_lib.cc _MVE_rmStepMovie / _MVE_rmPrepMovie). Layout (all little-endian):
///   "Interplay MVE File\x1A\x00" (20-byte magic)
///   three preamble shorts (0x001A, 0x0100, 0x1133)
///   then a stream of CHUNKS: [u16 length][u16 type] + payload,
///   each payload a stream of OPCODES: [u16 length][u8 type][u8 version] + data.
/// This session exposes the demux + the audio path; the video opcodes (SetDecodingMap,
/// VideoData, palettes) are surfaced for the session-2 codec.
/// </summary>
public sealed class MveFile
{
    private static readonly byte[] Magic = System.Text.Encoding.ASCII.GetBytes("Interplay MVE File\x1A\0");

    private readonly byte[] _data;

    /// <summary>Every opcode across every chunk, in stream order (the player steps them).</summary>
    public IReadOnlyList<MveOpcode> Opcodes { get; }

    private MveFile(byte[] data, List<MveOpcode> opcodes)
    {
        _data = data;
        Opcodes = opcodes;
    }

    public static bool HasMagic(byte[] data) =>
        data.Length >= Magic.Length && data.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    public static MveFile Parse(byte[] data)
    {
        if (!HasMagic(data))
            throw new InvalidDataException("not an Interplay MVE file (bad magic).");

        int off = Magic.Length + 6; // magic + the three preamble shorts
        var opcodes = new List<MveOpcode>();
        while (off + 4 <= data.Length)
        {
            int chunkLen = ReadU16(data, off);
            off += 4; // [u16 length][u16 type]
            int chunkEnd = Math.Min(off + chunkLen, data.Length);
            while (off + 4 <= chunkEnd)
            {
                int opLen = ReadU16(data, off);
                byte type = data[off + 2];
                byte version = data[off + 3];
                off += 4;
                if (off + opLen > data.Length)
                    break; // truncated
                opcodes.Add(new MveOpcode(type, version, data.AsMemory(off, opLen)));
                off += opLen;
            }
            off = chunkEnd;
        }
        return new MveFile(data, opcodes);
    }

    private static int ReadU16(byte[] d, int o) => d[o] | (d[o + 1] << 8);
}
