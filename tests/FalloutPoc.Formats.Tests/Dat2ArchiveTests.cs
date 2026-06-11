using System.IO.Compression;
using FalloutPoc.Formats.Dat2;

namespace FalloutPoc.Formats.Tests;

/// <summary>
/// Directory-parsing tests against a synthetic DAT2 archive built in memory,
/// following the layout in fallout2-ce src/dfile.cc dbaseOpen().
/// </summary>
public class Dat2ArchiveTests : IDisposable
{
    private readonly string _datPath = Path.Combine(Path.GetTempPath(), $"poc-test-{Guid.NewGuid():N}.dat");

    private static readonly byte[] StoredContent = "stored entry content"u8.ToArray();
    private static readonly byte[] CompressedContent =
        Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();

    public Dat2ArchiveTests() => File.WriteAllBytes(_datPath, BuildSyntheticDat());

    public void Dispose() => File.Delete(_datPath);

    private static byte[] BuildSyntheticDat()
    {
        // Data section: stored entry first, zlib-compressed entry second.
        byte[] compressedBlob = ZlibCompress(CompressedContent);

        using var dat = new MemoryStream();
        using var writer = new BinaryWriter(dat);

        // Arbitrary prefix garbage — dbaseOpen() allows data before the dbase
        // content (dataOffset = fileSize - dbaseDataSize).
        byte[] prefix = "GARBAGE!"u8.ToArray();
        writer.Write(prefix);

        long dataSectionStart = dat.Position;
        writer.Write(StoredContent);
        writer.Write(compressedBlob);

        // Entries table: [int32 count] then per-entry records, sorted by path.
        long entriesTableStart = dat.Position;
        writer.Write(2);
        WriteEntry(writer, @"art\tiles\big.bin", compressed: true, CompressedContent.Length,
            compressedBlob.Length, StoredContent.Length);
        WriteEntry(writer, @"text\stored.txt", compressed: false, StoredContent.Length,
            StoredContent.Length, 0);

        // Footer: [entriesDataSize][dbaseDataSize].
        int entriesDataSize = (int)(dat.Position - entriesTableStart);
        int dbaseDataSize = (int)(dat.Position - dataSectionStart) + 8;
        writer.Write(entriesDataSize);
        writer.Write(dbaseDataSize);

        return dat.ToArray();
    }

    private static void WriteEntry(BinaryWriter writer, string path, bool compressed,
        int uncompressedSize, int dataSize, int dataOffset)
    {
        byte[] pathBytes = System.Text.Encoding.Latin1.GetBytes(path);
        writer.Write(pathBytes.Length);
        writer.Write(pathBytes);
        writer.Write((byte)(compressed ? 1 : 0));
        writer.Write(uncompressedSize);
        writer.Write(dataSize);
        writer.Write(dataOffset);
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionMode.Compress, leaveOpen: true))
            zlib.Write(data);
        return output.ToArray();
    }

    [Fact]
    public void ParsesDirectory()
    {
        using var archive = Dat2Archive.Open(_datPath);

        Assert.Equal(2, archive.Entries.Count);

        Dat2Entry compressed = archive.Entries[0];
        Assert.Equal(@"art\tiles\big.bin", compressed.Path);
        Assert.True(compressed.Compressed);
        Assert.Equal(CompressedContent.Length, compressed.UncompressedSize);

        Dat2Entry stored = archive.Entries[1];
        Assert.Equal(@"text\stored.txt", stored.Path);
        Assert.False(stored.Compressed);
        Assert.Equal(StoredContent.Length, stored.UncompressedSize);
        Assert.Equal(StoredContent.Length, stored.DataSize);
    }

    [Fact]
    public void DataSectionOffsetSkipsArbitraryPrefix()
    {
        using var archive = Dat2Archive.Open(_datPath);
        Assert.Equal("GARBAGE!"u8.Length, archive.DataSectionOffset);
    }

    [Fact]
    public void ExtractsStoredEntry()
    {
        using var archive = Dat2Archive.Open(_datPath);
        Assert.Equal(StoredContent, archive.ReadAllBytes(@"text\stored.txt"));
    }

    [Fact]
    public void ExtractsCompressedEntry()
    {
        using var archive = Dat2Archive.Open(_datPath);
        Assert.Equal(CompressedContent, archive.ReadAllBytes(@"art\tiles\big.bin"));
    }

    [Fact]
    public void LookupIsCaseInsensitiveAndAcceptsForwardSlashes()
    {
        using var archive = Dat2Archive.Open(_datPath);
        Assert.True(archive.Contains(@"TEXT\STORED.TXT"));
        Assert.True(archive.Contains("art/tiles/BIG.bin"));
        Assert.NotNull(archive.FindEntry("text/stored.txt"));
        Assert.Null(archive.FindEntry(@"text\missing.txt"));
    }

    [Fact]
    public void OpenReadIsStreamingAndIndependent()
    {
        using var archive = Dat2Archive.Open(_datPath);
        using Stream a = archive.OpenRead(@"art\tiles\big.bin");
        using Stream b = archive.OpenRead(@"text\stored.txt");

        // Interleaved reads from two handles must not corrupt each other.
        byte[] first = new byte[100];
        a.ReadExactly(first);
        byte[] storedAll = new byte[StoredContent.Length];
        b.ReadExactly(storedAll);
        byte[] restOfA = new byte[CompressedContent.Length - 100];
        a.ReadExactly(restOfA);

        Assert.Equal(StoredContent, storedAll);
        Assert.Equal(CompressedContent, first.Concat(restOfA).ToArray());
    }

    [Fact]
    public void MissingEntryThrowsFileNotFound()
    {
        using var archive = Dat2Archive.Open(_datPath);
        Assert.Throws<FileNotFoundException>(() => archive.ReadAllBytes("no/such/file"));
    }
}

public class Dat2RealGameDataTests
{
    [GameDataFact]
    public void OpensMasterDat()
    {
        string masterDat = Path.Combine(GameData.RequiredDir, "master.dat");
        using var archive = Dat2Archive.Open(masterDat);
        Assert.True(archive.Entries.Count > 1000, $"expected >1000 entries, got {archive.Entries.Count}");
    }

    [GameDataFact]
    public void MasterDatContainsColorPalAndArtemple()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        Assert.True(vfs.Exists("color.pal"), "color.pal not found");
        Assert.True(vfs.Exists(@"maps\artemple.map"), "maps\\artemple.map not found");
    }

    [GameDataFact]
    public void ColorPalIs768BytePalettePlusTables()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        byte[] pal = vfs.ReadAllBytes("color.pal");
        // color.pal = 768 bytes of RGB + conversion tables; must be at least 768.
        Assert.True(pal.Length >= 768, $"color.pal is only {pal.Length} bytes");
        // Palette components are 6-bit (0..63); index 0..767 must respect that
        // (except the 0xFF padding GOG files sometimes have — value <64 or ==255).
        Assert.All(pal.Take(768), b => Assert.True(b < 64 || b == 255, $"palette byte {b} out of 6-bit range"));
    }
}
