using System.IO.Compression;

namespace FalloutPoc.Formats.Dat2;

/// <summary>
/// Read-only Fallout 2 DAT2 archive (master.dat, critter.dat, patch000.dat).
///
/// Format ported from fallout2-ce src/dfile.cc dbaseOpen():
/// - Footer (last 8 bytes): [int32 entriesDataSize][int32 dbaseDataSize], little-endian.
/// - Entries table starts at fileSize - entriesDataSize - 8: [int32 entriesLength][entries...].
/// - Data section starts at fileSize - dbaseDataSize; entry DataOffset is relative to it
///   (this allows arbitrary data at the beginning of the .DAT file).
/// - Compressed entries are zlib streams (inflateInit, i.e. with zlib header — ZLibStream, not raw deflate).
/// - Entry paths use '\' separators and are matched case-insensitively (bsearch with stricmp).
/// </summary>
public sealed class Dat2Archive : IDisposable
{
    private readonly Dictionary<string, Dat2Entry> _byPath;
    private bool _disposed;

    public string Path { get; }
    public IReadOnlyList<Dat2Entry> Entries { get; }

    /// <summary>Offset of the data section: fileSize - dbaseDataSize.</summary>
    public long DataSectionOffset { get; }

    private Dat2Archive(string path, List<Dat2Entry> entries, long dataSectionOffset)
    {
        Path = path;
        Entries = entries;
        DataSectionOffset = dataSectionOffset;
        _byPath = new Dictionary<string, Dat2Entry>(entries.Count, StringComparer.OrdinalIgnoreCase);
        foreach (Dat2Entry entry in entries)
            _byPath[entry.Path] = entry;
    }

    public static Dat2Archive Open(string path)
    {
        using FileStream stream = File.OpenRead(path);
        long fileSize = stream.Length;
        if (fileSize < 8)
            throw new InvalidDataException($"'{path}' is too small to be a DAT2 archive.");

        using var reader = new BinaryReader(stream); // BinaryReader is little-endian, matching x86 fread

        // ported from fallout2-ce src/dfile.cc dbaseOpen(): footer = two 32-bit ints
        stream.Seek(fileSize - 8, SeekOrigin.Begin);
        int entriesDataSize = reader.ReadInt32();
        int dbaseDataSize = reader.ReadInt32();

        long entriesTableOffset = fileSize - entriesDataSize - 8;
        if (entriesTableOffset < 0 || dbaseDataSize > fileSize)
            throw new InvalidDataException($"'{path}' has an invalid DAT2 footer.");

        stream.Seek(entriesTableOffset, SeekOrigin.Begin);
        int entriesLength = reader.ReadInt32();
        if (entriesLength < 0)
            throw new InvalidDataException($"'{path}' has a negative DAT2 entry count.");

        var entries = new List<Dat2Entry>(entriesLength);
        for (int i = 0; i < entriesLength; i++)
        {
            int pathLength = reader.ReadInt32();
            if (pathLength < 0 || pathLength > 4096)
                throw new InvalidDataException($"'{path}' entry {i} has an invalid path length {pathLength}.");

            // Entry paths are single-byte chars (cp1252-ish); game paths are plain ASCII.
            string entryPath = System.Text.Encoding.Latin1.GetString(reader.ReadBytes(pathLength));
            byte compressed = reader.ReadByte();
            int uncompressedSize = reader.ReadInt32();
            int dataSize = reader.ReadInt32();
            int dataOffset = reader.ReadInt32();

            entries.Add(new Dat2Entry(entryPath, compressed == 1, uncompressedSize, dataSize, dataOffset));
        }

        return new Dat2Archive(path, entries, fileSize - dbaseDataSize);
    }

    /// <summary>Normalizes '/' to '\' — DAT2 entry paths always use backslashes.</summary>
    public static string NormalizePath(string virtualPath) => virtualPath.Replace('/', '\\');

    public bool Contains(string virtualPath) =>
        _byPath.ContainsKey(NormalizePath(virtualPath));

    public Dat2Entry? FindEntry(string virtualPath) =>
        _byPath.TryGetValue(NormalizePath(virtualPath), out Dat2Entry? entry) ? entry : null;

    /// <summary>
    /// Opens a streaming, read-only view of an entry's uncompressed contents.
    /// Each call opens its own file handle (like fallout2-ce, where every DFile has its own FILE*),
    /// so concurrent reads are safe.
    /// </summary>
    public Stream OpenRead(string virtualPath) =>
        OpenRead(FindEntry(virtualPath)
            ?? throw new FileNotFoundException($"'{virtualPath}' not found in '{Path}'.", virtualPath));

    public Stream OpenRead(Dat2Entry entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var file = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var section = new SectionStream(file, DataSectionOffset + entry.DataOffset, entry.DataSize, ownsParent: true);
        return entry.Compressed
            ? new ZLibStream(section, CompressionMode.Decompress)
            : section;
    }

    public byte[] ReadAllBytes(string virtualPath)
    {
        Dat2Entry entry = FindEntry(virtualPath)
            ?? throw new FileNotFoundException($"'{virtualPath}' not found in '{Path}'.", virtualPath);
        using Stream stream = OpenRead(entry);
        byte[] buffer = new byte[entry.UncompressedSize];
        stream.ReadExactly(buffer);
        return buffer;
    }

    public void Dispose() => _disposed = true;
}
