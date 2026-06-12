using System.Buffers.Binary;

namespace FalloutPoc.Formats.Text;

public sealed class AafGlyph
{
    public required short Width { get; init; }
    public required short Height { get; init; }

    /// <summary>Width*Height opacity levels (0 = transparent); row-major.</summary>
    public required byte[] Pixels { get; init; }
}

/// <summary>
/// Fallout's .aaf interface font, ported from fallout2-ce
/// src/font_manager.cc interfaceFontLoad()/interfaceFontDrawImpl().
/// Layout (big-endian): "AAFF" signature; int16 maxHeight, letterSpacing,
/// wordSpacing, lineSpacing; 256 glyph records (int16 width, int16 height,
/// int32 offset into the data block); then glyph data, 1 byte per pixel
/// (an opacity level used as a blend-table index by the original).
/// Glyphs render bottom-aligned to MaxHeight; the space character uses
/// WordSpacing as its width.
/// </summary>
public sealed class AafFont
{
    private const int Signature = 0x41414646; // "AAFF"
    private const int HeaderSize = 12 + 256 * 8;

    public required short MaxHeight { get; init; }
    public required short LetterSpacing { get; init; }
    public required short WordSpacing { get; init; }
    public required short LineSpacing { get; init; }
    public required AafGlyph[] Glyphs { get; init; }

    /// <summary>Highest opacity level present in any glyph — used to normalize to alpha.</summary>
    public required byte MaxLevel { get; init; }

    public static AafFont Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException($"AAF data is only {data.Length} bytes.");
        if (BinaryPrimitives.ReadInt32BigEndian(data) != Signature)
            throw new InvalidDataException("Not an AAF font (bad signature).");

        short maxHeight = BinaryPrimitives.ReadInt16BigEndian(data[4..]);
        short letterSpacing = BinaryPrimitives.ReadInt16BigEndian(data[6..]);
        short wordSpacing = BinaryPrimitives.ReadInt16BigEndian(data[8..]);
        short lineSpacing = BinaryPrimitives.ReadInt16BigEndian(data[10..]);

        var glyphs = new AafGlyph[256];
        byte maxLevel = 1;
        for (int i = 0; i < 256; i++)
        {
            int record = 12 + i * 8;
            short width = BinaryPrimitives.ReadInt16BigEndian(data[record..]);
            short height = BinaryPrimitives.ReadInt16BigEndian(data[(record + 2)..]);
            int offset = BinaryPrimitives.ReadInt32BigEndian(data[(record + 4)..]);

            byte[] pixels = data.Slice(HeaderSize + offset, width * height).ToArray();
            foreach (byte level in pixels)
                if (level > maxLevel)
                    maxLevel = level;

            glyphs[i] = new AafGlyph { Width = width, Height = height, Pixels = pixels };
        }

        return new AafFont
        {
            MaxHeight = maxHeight,
            LetterSpacing = letterSpacing,
            WordSpacing = wordSpacing,
            LineSpacing = lineSpacing,
            Glyphs = glyphs,
            MaxLevel = maxLevel,
        };
    }

    public int CharWidth(char ch) =>
        ch == ' ' ? WordSpacing : Glyphs[(byte)ch].Width;

    /// <summary>ported from interfaceFontGetStringWidthImpl(): letter spacing after every char.</summary>
    public int MeasureWidth(string text)
    {
        int width = 0;
        foreach (char ch in text)
            width += CharWidth(ch) + LetterSpacing;
        return width;
    }
}
