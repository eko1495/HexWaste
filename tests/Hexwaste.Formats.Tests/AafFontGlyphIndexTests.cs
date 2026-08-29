using Hexwaste.Formats.Text;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// AAF fonts are BYTE-indexed (256 glyph records) and the engine's own text is
/// single-byte, so it never meets a character above U+00FF — there is no reference
/// behaviour here to port. C# strings are UTF-16, though, and a plain (byte) cast
/// TRUNCATES: U+2014 '—' became 0x14, a control slot holding an arbitrary glyph,
/// which is what the HUD monitor was actually rendering. GlyphIndex maps anything
/// out of range to '?' instead, and BOTH the draw path and the measure path route
/// through it so wrapping cannot desynchronise from what is drawn.
/// </summary>
public class AafFontGlyphIndexTests
{
    [Fact]
    public void AnAsciiCharacterKeepsItsOwnSlot() =>
        Assert.Equal('A', AafFont.GlyphIndex('A'));

    [Fact]
    public void TheLastRepresentableByteKeepsItsOwnSlot() =>
        Assert.Equal(0xFF, AafFont.GlyphIndex((char)0xFF));

    [Fact]
    public void TheFirstOutOfRangeCharacterFallsBackToQuestionMark() =>
        Assert.Equal('?', AafFont.GlyphIndex((char)0x100));

    [Fact]
    public void AnEmDashNoLongerTruncatesIntoAControlSlot()
    {
        // The live defect: (byte)'—' == 0x14. Left unguarded, the monitor drew
        // whatever glyph slot 20 happens to hold.
        Assert.NotEqual(0x14, AafFont.GlyphIndex('—'));
        Assert.Equal('?', AafFont.GlyphIndex('—'));
    }

    [Fact]
    public void ARightArrowNoLongerTruncatesEither()
    {
        // (byte)'→' == 0x92 — the level-up skill line used this.
        Assert.Equal('?', AafFont.GlyphIndex('→'));
    }
}
