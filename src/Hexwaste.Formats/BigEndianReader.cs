using System.Buffers.Binary;

namespace Hexwaste.Formats;

/// <summary>
/// Big-endian primitive reader. MAP, FRM and PRO files store multi-byte values
/// big-endian — fallout2-ce src/db.cc fileReadInt32() byte-swaps every read.
/// </summary>
public sealed class BigEndianReader(Stream stream)
{
    private readonly byte[] _buffer = new byte[4];

    public Stream BaseStream { get; } = stream;

    public int ReadInt32()
    {
        BaseStream.ReadExactly(_buffer, 0, 4);
        return BinaryPrimitives.ReadInt32BigEndian(_buffer);
    }

    public uint ReadUInt32() => (uint)ReadInt32();

    public short ReadInt16()
    {
        BaseStream.ReadExactly(_buffer, 0, 2);
        return BinaryPrimitives.ReadInt16BigEndian(_buffer);
    }

    public byte ReadByte()
    {
        BaseStream.ReadExactly(_buffer, 0, 1);
        return _buffer[0];
    }

    public byte[] ReadBytes(int count)
    {
        byte[] result = new byte[count];
        BaseStream.ReadExactly(result);
        return result;
    }

    public int[] ReadInt32Array(int count)
    {
        int[] result = new int[count];
        for (int i = 0; i < count; i++)
            result[i] = ReadInt32();
        return result;
    }

    public void Skip(int byteCount)
    {
        if (BaseStream.CanSeek)
            BaseStream.Seek(byteCount, SeekOrigin.Current);
        else
            for (int i = 0; i < byteCount; i++)
                ReadByte();
    }
}
