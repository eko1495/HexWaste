namespace FalloutPoc.Formats.Dat2;

/// <summary>
/// A single file entry in a DAT2 archive directory.
/// Layout ported from fallout2-ce src/dfile.h DBaseEntry / src/dfile.cc dbaseOpen():
/// [int32 pathLength][path bytes][byte compressed][int32 uncompressedSize][int32 dataSize][int32 dataOffset]
/// </summary>
public sealed record Dat2Entry(
    string Path,
    bool Compressed,
    int UncompressedSize,
    int DataSize,
    int DataOffset);
