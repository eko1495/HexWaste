using System.Buffers.Binary;
using Hexwaste.Formats.Frm;
using Hexwaste.Formats.Pal;

namespace Hexwaste.Formats.Tests;

public class PaletteTests
{
    [Fact]
    public void ScalesSixBitComponentsByFour()
    {
        byte[] raw = new byte[768];
        raw[3] = 63; // index 1 = (63, 0, 0)
        var palette = Palette.Load(raw);
        Assert.Equal(((byte)252, (byte)0, (byte)0), palette.GetRgb(1));
    }

    [Fact]
    public void ZeroesUnmappedSlots()
    {
        byte[] raw = new byte[768];
        raw[3] = 255; // out-of-range component marks slot unmapped
        raw[4] = 10;
        var palette = Palette.Load(raw);
        Assert.Equal(((byte)0, (byte)0, (byte)0), palette.GetRgb(1));
    }

    [Fact]
    public void RgbaLookupHasTransparentIndexZero()
    {
        byte[] raw = new byte[768];
        var rgba = Palette.Load(raw).ToRgba();
        Assert.Equal(256 * 4, rgba.Length);
        Assert.Equal(0, rgba[3]); // alpha of index 0
        Assert.Equal(255, rgba[7]); // alpha of index 1
    }
}

public class FrmFileTests
{
    /// <summary>
    /// Builds a minimal big-endian FRM: 2 frames, direction 0 has its own data,
    /// directions 1-5 share it (equal dataOffsets).
    /// </summary>
    private static byte[] BuildSyntheticFrm()
    {
        var frames = new (short W, short H, short X, short Y)[] { (2, 2, 0, 0), (1, 3, -1, 5) };

        using var ms = new MemoryStream();
        void WriteI16(short v)
        {
            Span<byte> b = stackalloc byte[2];
            BinaryPrimitives.WriteInt16BigEndian(b, v);
            ms.Write(b);
        }
        void WriteI32(int v)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(b, v);
            ms.Write(b);
        }

        WriteI32(4); // version
        WriteI16(8); // fps
        WriteI16(1); // action frame
        WriteI16((short)frames.Length);
        for (int i = 0; i < 6; i++) WriteI16((short)(i * 10)); // xOffsets
        for (int i = 0; i < 6; i++) WriteI16((short)(-i)); // yOffsets
        for (int i = 0; i < 6; i++) WriteI32(0); // dataOffsets: all share direction 0 data

        int dataSize = frames.Sum(f => 12 + f.W * f.H);
        WriteI32(dataSize);

        byte pixel = 1;
        foreach ((short w, short h, short x, short y) in frames)
        {
            WriteI16(w);
            WriteI16(h);
            WriteI32(w * h);
            WriteI16(x);
            WriteI16(y);
            for (int i = 0; i < w * h; i++)
                ms.WriteByte(pixel++);
        }

        return ms.ToArray();
    }

    [Fact]
    public void ParsesHeaderAndFrames()
    {
        FrmFile frm = FrmFile.Load(BuildSyntheticFrm());

        Assert.Equal(4, frm.Version);
        Assert.Equal(8, frm.FramesPerSecond);
        Assert.Equal(1, frm.ActionFrame);
        Assert.Equal(2, frm.FrameCount);
        Assert.Equal(50, frm.RotationOffsetsX[5]);
        Assert.Equal(-5, frm.RotationOffsetsY[5]);

        FrmFrame first = frm.GetFrame(0);
        Assert.Equal(2, first.Width);
        Assert.Equal(2, first.Height);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, first.Pixels);

        FrmFrame second = frm.GetFrame(1);
        Assert.Equal(-1, second.OffsetX);
        Assert.Equal(5, second.OffsetY);
        Assert.Equal(3, second.Pixels.Length);
    }

    [Fact]
    public void EqualDataOffsetsShareFrameData()
    {
        FrmFile frm = FrmFile.Load(BuildSyntheticFrm());
        for (int rotation = 1; rotation < FrmFile.RotationCount; rotation++)
            Assert.Same(frm.Directions[0], frm.Directions[rotation]);
    }

    [Fact]
    public void ZeroFpsFallsBackToTen()
    {
        byte[] data = BuildSyntheticFrm();
        data[4] = 0;
        data[5] = 0; // zero out fps (big-endian int16 at offset 4)
        Assert.Equal(10, FrmFile.Load(data).FramesPerSecond);
    }
}

public class FrmRealGameDataTests
{
    [GameDataFact]
    public void FloorTileFrmIs80x36()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        // edg1000.frm is a generic floor tile; all Fallout floor tiles are 80x36.
        FrmFile frm = FrmFile.Load(vfs.ReadAllBytes(@"art\tiles\edg1000.frm"));
        FrmFrame frame = frm.GetFrame(0);
        Assert.Equal(80, frame.Width);
        Assert.Equal(36, frame.Height);
        Assert.Equal(80 * 36, frame.Pixels.Length);
    }

    [GameDataFact]
    public void RealColorPalLoadsWithSaneColors()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var palette = Palette.Load(vfs.ReadAllBytes("color.pal"));
        // At least half the palette should be mapped, non-black colors.
        int nonBlack = Enumerable.Range(0, 256).Count(i => palette.GetRgb(i) != ((byte)0, (byte)0, (byte)0));
        Assert.True(nonBlack > 128, $"only {nonBlack} non-black palette entries");
    }
}
