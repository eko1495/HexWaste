namespace Hexwaste.Formats.Movie;

/// <summary>The audio stream format from an MVE InitAudio (0x03) opcode, ported from
/// fallout2-ce src/movie_lib.cc case 3 (:685): flags at offset 2 (bit0 stereo, bit1 16-bit,
/// bit2 compressed when the opcode version ≥ 1), sample rate at offset 4.</summary>
public readonly record struct MveAudioFormat(int SampleRate, bool Stereo, bool Bits16, bool Compressed)
{
    public static MveAudioFormat Parse(MveOpcode initAudio)
    {
        ReadOnlySpan<byte> d = initAudio.Data.Span;
        int flags = U16(d, 2);
        return new MveAudioFormat(
            SampleRate: U16(d, 4),
            Stereo: (flags & 0x01) != 0,
            Bits16: (flags & 0x02) != 0,
            Compressed: initAudio.Version >= 1 && (flags & 0x04) != 0);
    }

    internal static int U16(ReadOnlySpan<byte> d, int o) => d[o] | (d[o + 1] << 8);
}

/// <summary>
/// Demuxes an MVE's audio: walks the opcode stream, decodes each AudioData (0x08) frame —
/// Interplay DPCM (<see cref="InterplayDpcm"/>) or raw PCM16 — and expands each AudioSilence
/// (0x09) to zeros, concatenating one continuous little-endian PCM16 buffer for the whole
/// movie. The AudioData/Silence payload is [u16 seq][u16 streamMask][u16 uncompressedLen]
/// then the frame; only stream 0 (mask bit 0) is taken (Fallout's movies are single-stream).
/// (P132.)
/// </summary>
public static class MveAudio
{
    public sealed record Track(MveAudioFormat Format, byte[] Pcm16);

    /// <summary>Decode the movie's audio, or null when it carries no audio stream.</summary>
    public static Track? Decode(MveFile mve)
    {
        MveAudioFormat? format = null;
        var pcm = new List<byte>();
        foreach (MveOpcode op in mve.Opcodes)
        {
            switch ((MveOp)op.Type)
            {
                case MveOp.InitAudio:
                    format = MveAudioFormat.Parse(op);
                    break;

                case MveOp.AudioData when format is { } fmt:
                {
                    ReadOnlySpan<byte> d = op.Data.Span;
                    if (d.Length < 6 || (MveAudioFormat.U16(d, 2) & 0x01) == 0)
                        break; // not stream 0
                    ReadOnlySpan<byte> frame = d[6..];
                    pcm.AddRange(fmt.Compressed
                        ? InterplayDpcm.DecodeFrame(frame, fmt.Stereo)
                        : frame.ToArray());
                    break;
                }

                case MveOp.AudioSilence when format is not null:
                {
                    ReadOnlySpan<byte> d = op.Data.Span;
                    if (d.Length >= 6 && (MveAudioFormat.U16(d, 2) & 0x01) != 0)
                        pcm.AddRange(new byte[MveAudioFormat.U16(d, 4)]); // uncompressedLen zeros
                    break;
                }
            }
        }
        return format is { } f ? new Track(f, [.. pcm]) : null;
    }
}
