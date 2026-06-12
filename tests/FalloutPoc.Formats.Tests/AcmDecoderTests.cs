using System.Buffers.Binary;
using FalloutPoc.Formats;
using FalloutPoc.Formats.Sound;

namespace FalloutPoc.Formats.Tests;

public class AcmDecoderTests
{
    // Two small real sfx: a button click and a screen-door sound.
    private const string ButtonSfx = @"sound\sfx\butin1.acm";
    private const string DoorSfx = @"sound\sfx\sodoorsa.acm";

    [GameDataFact]
    public void DecodesRealSfxWithExpectedFormat()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        AcmAudio audio = AcmDecoder.Decode(vfs.ReadAllBytes(ButtonSfx));

        Assert.Equal(22050, audio.SampleRate);
        Assert.True(audio.Channels >= 1, $"channels {audio.Channels}");
        Assert.True(audio.Samples.Length > 1000, $"only {audio.Samples.Length} samples");

        // Sanity-check signal energy: real audio is neither silence nor DC.
        double rms = Math.Sqrt(audio.Samples.Average(s => (double)s * s));
        Assert.True(rms > 10.0, $"RMS {rms:F1} too low - decode produced near-silence");
        Assert.True(rms < short.MaxValue, $"RMS {rms:F1} implausibly high");
    }

    [GameDataFact]
    public void DecodingIsDeterministic()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        byte[] data = vfs.ReadAllBytes(DoorSfx);

        long first = Checksum(AcmDecoder.Decode(data).Samples);
        using var stream = new MemoryStream(data);
        long second = Checksum(AcmDecoder.Decode(stream).Samples);

        Assert.Equal(first, second);
    }

    [GameDataFact]
    public void SampleCountMatchesHeaderDeclaredTotal()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        foreach (string path in new[] { ButtonSfx, DoorSfx })
        {
            byte[] data = vfs.ReadAllBytes(path);
            // ACM header: bytes 0-3 magic/version, bytes 4-7 total 16-bit
            // sample count (little-endian).
            int declared = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
            AcmAudio audio = AcmDecoder.Decode(data);
            Assert.Equal(declared, audio.Samples.Length);
        }
    }

    [Fact]
    public void WrongSignatureThrows()
    {
        // Valid files start with 97 28 03 01 (24-bit magic 0x32897 + version 1).
        byte[] badMagic = new byte[64];
        badMagic[0] = 0x96; // corrupt first magic byte
        badMagic[1] = 0x28;
        badMagic[2] = 0x03;
        badMagic[3] = 0x01;
        Assert.Throws<InvalidDataException>(() => AcmDecoder.Decode(badMagic));

        byte[] badVersion = new byte[64];
        badVersion[0] = 0x97;
        badVersion[1] = 0x28;
        badVersion[2] = 0x03;
        badVersion[3] = 0x02; // unsupported version
        Assert.Throws<InvalidDataException>(() => AcmDecoder.Decode(badVersion));
    }

    private static long Checksum(short[] samples)
    {
        long hash = 17;
        foreach (short sample in samples)
            hash = unchecked(hash * 31 + sample);
        return hash;
    }
}
