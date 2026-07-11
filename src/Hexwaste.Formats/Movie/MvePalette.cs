namespace Hexwaste.Formats.Movie;

/// <summary>
/// The MVE 256-entry RGBA palette, updated by SetPalette (0x0C) / SetPaletteCompressed
/// (0x0D) opcodes — ported from fallout2-ce src/movie_lib.cc palLoadPalette (case 12 :780).
/// Entries are 6-bit RGB (0..63); expanded to 8-bit as <c>(c &lt;&lt; 2) | (c &gt;&gt; 4)</c>
/// (the standard 6→8 scaling, matching ffmpeg's interplay palette). (P133.)
/// </summary>
public sealed class MvePalette
{
    // RGBA (4 bytes/entry, A=255) — ready to blit as a color palette.
    private readonly byte[] _rgba = new byte[256 * 4];

    public ReadOnlySpan<byte> Rgba => _rgba;

    /// <summary>The R/G/B (each 0..255) of a palette index.</summary>
    public (byte R, byte G, byte B) Color(int index) =>
        (_rgba[index * 4], _rgba[index * 4 + 1], _rgba[index * 4 + 2]);

    /// <summary>0x0C set_palette: [u16 start][u16 count] then count*3 6-bit RGB (data offset 4).</summary>
    public void SetPalette(ReadOnlySpan<byte> data)
    {
        int start = data[0] | (data[1] << 8);
        int count = data[2] | (data[3] << 8);
        int src = 4;
        for (int i = 0; i < count && start + i < 256; i++)
            SetEntry(start + i, data[src++], data[src++], data[src++]);
    }

    /// <summary>0x0D set_palette_compressed: a 32-byte bitmask, then 3 6-bit RGB per set bit
    /// (movie_lib.cc palSetCompressed).</summary>
    public void SetPaletteCompressed(ReadOnlySpan<byte> data)
    {
        int src = 32;
        for (int i = 0; i < 256; i++)
            if ((data[i >> 3] & (1 << (i & 7))) != 0 && src + 2 < data.Length)
                SetEntry(i, data[src++], data[src++], data[src++]);
    }

    private void SetEntry(int i, byte r6, byte g6, byte b6)
    {
        _rgba[i * 4] = Expand(r6);
        _rgba[i * 4 + 1] = Expand(g6);
        _rgba[i * 4 + 2] = Expand(b6);
        _rgba[i * 4 + 3] = 255;
    }

    // 6-bit (0..63) → 8-bit.
    private static byte Expand(byte c) => (byte)((c << 2) | (c >> 4));
}
