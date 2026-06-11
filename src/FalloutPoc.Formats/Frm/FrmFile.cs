using System.Buffers.Binary;

namespace FalloutPoc.Formats.Frm;

/// <summary>One frame of an FRM: 8-bit palette-indexed pixels, row-major.</summary>
public sealed class FrmFrame
{
    public required short Width { get; init; }
    public required short Height { get; init; }

    /// <summary>Shift relative to the previous frame in the animation (offsets accumulate).</summary>
    public required short OffsetX { get; init; }
    public required short OffsetY { get; init; }

    /// <summary>Width*Height palette indices; index 0 is transparent.</summary>
    public required byte[] Pixels { get; init; }
}

/// <summary>
/// FRM sprite file. Format ported from fallout2-ce src/art.cc artReadHeader() /
/// artReadFrameData(). All multi-byte values are BIG-endian (fileReadInt32 in
/// src/db.cc byte-swaps).
///
/// Layout:
///   int32 version; int16 framesPerSecond; int16 actionFrame; int16 frameCount;
///   int16 xOffsets[6]; int16 yOffsets[6];   // per-rotation centering shift
///   int32 dataOffsets[6];                   // per-rotation offset into frame data area
///   int32 dataSize;
///   frame data area (starts at byte 62): per direction, frameCount frames of
///   [int16 w][int16 h][int32 size][int16 x][int16 y][size bytes of pixels].
/// Directions with equal dataOffsets share the same frame data.
/// </summary>
public sealed class FrmFile
{
    public const int RotationCount = 6;
    private const int HeaderSize = 62;

    public required int Version { get; init; }
    public required short FramesPerSecondRaw { get; init; }
    public required short ActionFrame { get; init; }
    public required short FrameCount { get; init; }
    public required short[] RotationOffsetsX { get; init; }
    public required short[] RotationOffsetsY { get; init; }

    /// <summary>[rotation][frame]. Rotations sharing data reference the same array instance.</summary>
    public required FrmFrame[][] Directions { get; init; }

    /// <summary>ported from fallout2-ce src/art.cc artGetFramesPerSecond(): 0 means 10.</summary>
    public int FramesPerSecond => FramesPerSecondRaw == 0 ? 10 : FramesPerSecondRaw;

    public FrmFrame GetFrame(int frame, int rotation = 0) => Directions[rotation][frame];

    public static FrmFile Load(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Load(buffer.ToArray());
    }

    public static FrmFile Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException($"FRM data is only {data.Length} bytes, header needs {HeaderSize}.");

        int version = BinaryPrimitives.ReadInt32BigEndian(data);
        short fps = BinaryPrimitives.ReadInt16BigEndian(data[4..]);
        short actionFrame = BinaryPrimitives.ReadInt16BigEndian(data[6..]);
        short frameCount = BinaryPrimitives.ReadInt16BigEndian(data[8..]);
        if (frameCount < 0)
            throw new InvalidDataException($"FRM has negative frame count {frameCount}.");

        short[] xOffsets = new short[RotationCount];
        short[] yOffsets = new short[RotationCount];
        int[] dataOffsets = new int[RotationCount];
        for (int i = 0; i < RotationCount; i++)
            xOffsets[i] = BinaryPrimitives.ReadInt16BigEndian(data[(10 + i * 2)..]);
        for (int i = 0; i < RotationCount; i++)
            yOffsets[i] = BinaryPrimitives.ReadInt16BigEndian(data[(22 + i * 2)..]);
        for (int i = 0; i < RotationCount; i++)
            dataOffsets[i] = BinaryPrimitives.ReadInt32BigEndian(data[(34 + i * 4)..]);

        var directions = new FrmFrame[RotationCount][];
        for (int rotation = 0; rotation < RotationCount; rotation++)
        {
            // ported from fallout2-ce src/art.cc artRead(): equal consecutive
            // dataOffsets mean the direction reuses the previous one's frames.
            if (rotation > 0 && dataOffsets[rotation] == dataOffsets[rotation - 1])
            {
                directions[rotation] = directions[rotation - 1];
                continue;
            }

            var frames = new FrmFrame[frameCount];
            int offset = HeaderSize + dataOffsets[rotation];
            for (int frame = 0; frame < frameCount; frame++)
            {
                short width = BinaryPrimitives.ReadInt16BigEndian(data[offset..]);
                short height = BinaryPrimitives.ReadInt16BigEndian(data[(offset + 2)..]);
                int size = BinaryPrimitives.ReadInt32BigEndian(data[(offset + 4)..]);
                short x = BinaryPrimitives.ReadInt16BigEndian(data[(offset + 8)..]);
                short y = BinaryPrimitives.ReadInt16BigEndian(data[(offset + 10)..]);

                if (size != width * height)
                    throw new InvalidDataException(
                        $"FRM frame {frame} rotation {rotation}: size {size} != {width}x{height}.");

                frames[frame] = new FrmFrame
                {
                    Width = width,
                    Height = height,
                    OffsetX = x,
                    OffsetY = y,
                    Pixels = data.Slice(offset + 12, size).ToArray(),
                };
                offset += 12 + size;
            }

            directions[rotation] = frames;
        }

        return new FrmFile
        {
            Version = version,
            FramesPerSecondRaw = fps,
            ActionFrame = actionFrame,
            FrameCount = frameCount,
            RotationOffsetsX = xOffsets,
            RotationOffsetsY = yOffsets,
            Directions = directions,
        };
    }
}
