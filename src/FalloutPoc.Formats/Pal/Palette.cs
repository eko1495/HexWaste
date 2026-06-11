namespace FalloutPoc.Formats.Pal;

/// <summary>
/// Fallout 2 palette (color.pal): 256 RGB triples with 6-bit components.
/// Ported from fallout2-ce src/color.cc colorPaletteLoad(): components > 0x3F
/// mark unmapped slots and are zeroed. Display color = component * 4
/// (6-bit VGA DAC to 8-bit). Palette index 0 is transparent.
/// </summary>
public sealed class Palette
{
    public const int TransparentIndex = 0;

    /// <summary>Raw 6-bit components (768 bytes, R G B per index) — used by color cycling.</summary>
    public byte[] Raw { get; }

    private Palette(byte[] raw) => Raw = raw;

    public static Palette Load(Stream stream)
    {
        byte[] raw = new byte[768];
        stream.ReadExactly(raw);

        // ported from fallout2-ce src/color.cc colorPaletteLoad():
        // any component > 0x3F means the slot is unmapped -> zero the triple.
        for (int i = 0; i < 256; i++)
        {
            if (raw[i * 3] > 0x3F || raw[i * 3 + 1] > 0x3F || raw[i * 3 + 2] > 0x3F)
            {
                raw[i * 3] = 0;
                raw[i * 3 + 1] = 0;
                raw[i * 3 + 2] = 0;
            }
        }

        return new Palette(raw);
    }

    public static Palette Load(byte[] data)
    {
        using var stream = new MemoryStream(data);
        return Load(stream);
    }

    /// <summary>8-bit RGB for a palette index (6-bit value * 4, clamped).</summary>
    public (byte R, byte G, byte B) GetRgb(int index)
    {
        byte Scale(byte c) => (byte)Math.Min(c * 4, 255);
        return (Scale(Raw[index * 3]), Scale(Raw[index * 3 + 1]), Scale(Raw[index * 3 + 2]));
    }

    /// <summary>
    /// Palette as 256 RGBA pixels (index 0 fully transparent) — ready to upload
    /// as a 256x1 lookup texture for the palette-cycling shader.
    /// </summary>
    public byte[] ToRgba()
    {
        byte[] rgba = new byte[256 * 4];
        for (int i = 0; i < 256; i++)
        {
            (byte r, byte g, byte b) = GetRgb(i);
            rgba[i * 4] = r;
            rgba[i * 4 + 1] = g;
            rgba[i * 4 + 2] = b;
            rgba[i * 4 + 3] = (byte)(i == TransparentIndex ? 0 : 255);
        }
        return rgba;
    }
}
