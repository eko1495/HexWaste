namespace FalloutPoc.Formats.Art;

/// <summary>
/// Resolves FIDs to FRM virtual paths using the per-type art lists
/// (art\tiles\tiles.lst etc.), ported from fallout2-ce src/art.cc
/// artBuildFilePath()/artReadList(): path = art\&lt;typeDir&gt;\&lt;lst line (fid &amp; 0xFFF), 0-based&gt;.
/// Critters/heads use composed names (anim codes) and are out of scope for this PoC.
/// </summary>
public sealed class ArtIndex(GameFileSystem vfs)
{
    // ported from fallout2-ce src/art.cc gArtListDescriptions
    private static readonly string[] TypeDirs =
        ["items", "critters", "scenery", "walls", "tiles", "misc", "intrface", "inven"];

    private readonly Dictionary<int, string[]> _lists = [];

    public string GetFrmPath(int fid)
    {
        var type = Fid.Type(fid);
        if (type is ObjectType.Critter or ObjectType.Head)
            throw new NotSupportedException($"FID 0x{fid:X8}: {type} art is out of scope for this PoC.");

        int typeIndex = (int)type;
        if (typeIndex < 0 || typeIndex >= TypeDirs.Length)
            throw new InvalidDataException($"FID 0x{fid:X8} has unsupported type {typeIndex}.");

        string[] list = GetList(typeIndex);
        int index = Fid.Index(fid);
        if (index >= list.Length)
            throw new InvalidDataException(
                $"FID 0x{fid:X8} index {index} is out of range of {TypeDirs[typeIndex]}.lst ({list.Length} lines).");

        return $@"art\{TypeDirs[typeIndex]}\{list[index]}";
    }

    private string[] GetList(int typeIndex)
    {
        if (_lists.TryGetValue(typeIndex, out string[]? cached))
            return cached;

        string dir = TypeDirs[typeIndex];
        using Stream stream = vfs.OpenRead($@"art\{dir}\{dir}.lst");
        using var reader = new StreamReader(stream);

        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            int cut = line.IndexOfAny([' ', '\t', ',']);
            lines.Add((cut >= 0 ? line[..cut] : line).Trim());
        }

        string[] result = [.. lines];
        _lists[typeIndex] = result;
        return result;
    }
}
