using Hexwaste.Formats;
using Hexwaste.Formats.Sound;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Point-4/bucket-1 lip-sync: the .lip parser (fo2ce lips.cc lipsLoad v2 + lipsTicker). Behaviour is
/// covered by a synthetic fixture (no copyrighted content); the real ELDER\AELD1.LIP is asserted under a
/// GameDataFact (64 phonemes / 65 markers / marker[0] at position 0).
/// </summary>
public class LipDataTests
{
    // A minimal synthetic v2 .lip: 2 phonemes, 2 markers. Big-endian.
    private static byte[] SyntheticLip()
    {
        var b = new List<byte>();
        void I32(int v) // big-endian
        {
            b.Add((byte)(v >> 24)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 8)); b.Add((byte)v);
        }
        I32(2);   // version
        I32(0);   // field_4
        I32(0);   // flags
        I32(0);   // field_10
        I32(0);   // field_1C
        I32(2);   // field_24 = phonemeCount
        I32(0);   // field_28
        I32(2);   // field_2C = markerCount
        b.AddRange(new byte[8]); // file_name[8]
        b.AddRange(new byte[4]); // tag[4]  → header = 44 bytes
        b.Add(5); b.Add(9);      // 2 phonemes
        I32(1); I32(0);          // marker 0: {1, position 0}
        I32(0); I32(1000);       // marker 1: {0, position 1000}
        return [.. b];
    }

    [Fact]
    public void ParsesV2HeaderPhonemesAndMarkers()
    {
        var lip = LipData.Parse(SyntheticLip());
        Assert.Equal(2, lip.Phonemes.Count);
        Assert.Equal(2, lip.Markers.Count);
        Assert.Equal(5, lip.Phonemes[0]);
        Assert.Equal((1, 0), lip.Markers[0]);
        Assert.Equal((0, 1000), lip.Markers[1]);
    }

    [Fact]
    public void PhonemeAtWalksTheMarkerCursor()
    {
        var lip = LipData.Parse(SyntheticLip());
        Assert.Equal(5, lip.PhonemeAt(0));    // before marker 1 → phoneme[0]
        Assert.Equal(9, lip.PhonemeAt(2000)); // past marker 1's position → phoneme[1]
    }

    [Fact]
    public void RejectsNonV2()
    {
        var bytes = SyntheticLip();
        bytes[3] = 1; // version → 1
        Assert.Throws<InvalidDataException>(() => LipData.Parse(bytes));
    }

    [GameDataFact]
    public void RealElderLipParses()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        const string path = @"sound\speech\ELDER\AELD1.LIP";
        if (!vfs.Exists(path))
            return; // partial extraction — the synthetic fixture covers the parser
        var lip = LipData.Parse(vfs.ReadAllBytes(path));
        Assert.Equal(64, lip.Phonemes.Count);
        Assert.Equal(65, lip.Markers.Count);
        Assert.Equal(0, lip.Markers[0].Position);
        Assert.All(lip.Phonemes, p => Assert.True(p < 42)); // PHONEME_COUNT
    }
}
