using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Translucency flag decode (P23): the proto flag bits 0xFC000 → a <see cref="TransType"/>,
/// ported from fallout2-ce src/object.cc objectCreateInternal() (~:943). TRANS_NONE (0x8000) is
/// the OPAQUE "never fade near the dude" flag, not a translucent effect → decodes to None.
/// </summary>
public class TranslucencyTests
{
    [Theory]
    [InlineData(0x00000, TransType.None)]   // no flag
    [InlineData(0x08000, TransType.None)]   // TRANS_NONE → opaque
    [InlineData(0x10000, TransType.Wall)]
    [InlineData(0x20000, TransType.Glass)]
    [InlineData(0x40000, TransType.Steam)]
    [InlineData(0x80000, TransType.Energy)]
    [InlineData(0x04000, TransType.Red)]
    public void FromFlagsDecodesEachBit(int flags, TransType expected) =>
        Assert.Equal(expected, Translucency.FromFlags(flags));

    [Fact]
    public void TransNoneWinsOverOtherTransBits()
    {
        // The engine's if/else (object.cc:943) tests TRANS_NONE first, so an object flagged
        // both NONE and GLASS renders opaque.
        Assert.Equal(TransType.None, Translucency.FromFlags(0x8000 | 0x20000));
    }

    [Fact]
    public void DecodePriorityMatchesEngineOrder()
    {
        // wall before glass before steam before energy before red (object.cc:946-955).
        Assert.Equal(TransType.Wall, Translucency.FromFlags(0x10000 | 0x20000 | 0x4000));
        Assert.Equal(TransType.Glass, Translucency.FromFlags(0x20000 | 0x40000));
        Assert.Equal(TransType.Energy, Translucency.FromFlags(0x80000 | 0x4000));
    }

    [Fact]
    public void IgnoresNonTransBits()
    {
        // OBJECT_NO_BLOCK (0x10) / MULTIHEX (0x800) and other flags don't affect translucency.
        Assert.Equal(TransType.None, Translucency.FromFlags(0x10 | 0x800));
        Assert.Equal(TransType.Glass, Translucency.FromFlags(0x20000 | 0x10 | 0x800));
    }

    [Fact]
    public void MaskCoversAllSixTransBits() =>
        Assert.Equal(0x4000 | 0x8000 | 0x10000 | 0x20000 | 0x40000 | 0x80000, Translucency.Mask);

    [Fact]
    public void ProtoInfoTranslucencyReflectsFlags()
    {
        var proto = new ProtoInfo(Pid: 0x100001D, MessageId: 0, Fid: 0, Flags: 0x40000,
            ExtendedFlags: 0, SubType: -1);
        Assert.Equal(TransType.Steam, proto.Translucency);
    }
}
