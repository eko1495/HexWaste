namespace Hexwaste.Formats.Art;

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
        if (type is ObjectType.Head)
            throw new NotSupportedException($"FID 0x{fid:X8}: {type} art is out of scope for this PoC.");
        if (type is ObjectType.Critter)
            return GetCritterFrmPath(fid);

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

    /// <summary>Finds a critter's index in critters.lst by base name (e.g. "hmwarr"), or -1.</summary>
    public int FindCritterIndex(string baseName)
    {
        string[] list = GetList((int)ObjectType.Critter);
        return Array.FindIndex(list, n => string.Equals(n, baseName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The critters.lst base name for a critter FID (e.g. "hfprim"), or null.
    /// The 2nd char encodes gender ('m'/'f') — used for gender-correct sfx.</summary>
    public string? CritterBaseName(int fid)
    {
        string[] list = GetList((int)ObjectType.Critter);
        int index = Fid.Index(fid);
        return index >= 0 && index < list.Length ? list[index] : null;
    }

    /// <summary>
    /// Critter FRM names are composed: base name from critters.lst + a
    /// two-character animation/weapon code + ".frm" (or ".fr0".." for
    /// per-rotation files when the FID carries a rotation, used only by some
    /// knockdown/death art). ported from fallout2-ce src/art.cc artBuildFilePath().
    /// </summary>
    private string GetCritterFrmPath(int fid)
    {
        string[] list = GetList((int)ObjectType.Critter);
        int index = Fid.Index(fid);
        if (index >= list.Length)
            throw new InvalidDataException(
                $"FID 0x{fid:X8} index {index} is out of range of critters.lst ({list.Length} lines).");

        (char weaponChar, char animChar) = GetAnimationCode(Fid.AnimType(fid), Fid.WeaponCode(fid));

        int rotation = Fid.Rotation(fid);
        string extension = rotation != 0 ? $".fr{(char)(rotation + 47)}" : ".frm";
        return $@"art\critters\{list[index]}{weaponChar}{animChar}{extension}";
    }

    /// <summary>ported from fallout2-ce src/art.cc _art_get_code().</summary>
    public static (char WeaponChar, char AnimChar) GetAnimationCode(int animation, int weaponType)
    {
        const int animWalk = 1;
        const int animDodge = 13;
        const int animThrow = 18;
        const int animProneToStanding = 36;
        const int animBackToStanding = 37;
        const int animTakeOut = 38;
        const int animFireContinuous = 47;
        const int firstKnockdownAndDeath = 20;
        const int firstSfDeath = 48;
        const int animCalledShotPic = 64;

        if (animation is >= animTakeOut and <= animFireContinuous)
        {
            if (weaponType == 0)
                throw new InvalidDataException($"animation {animation} requires a weapon code.");
            return ((char)('d' + weaponType - 1), (char)('c' + animation - animTakeOut));
        }

        return animation switch
        {
            animProneToStanding => ('c', 'h'),
            animBackToStanding => ('c', 'j'),
            animCalledShotPic => ('n', 'a'),
            >= firstSfDeath => ('r', (char)('a' + animation - firstSfDeath)),
            >= firstKnockdownAndDeath => ('b', (char)('a' + animation - firstKnockdownAndDeath)),
            animThrow => weaponType switch
            {
                1 => ('d', 'm'), // knife
                4 => ('g', 'm'), // spear
                _ => ('a', 's'), // rock/grenade
            },
            animDodge => weaponType <= 0
                ? ('a', 'n')
                : ((char)('d' + weaponType - 1), 'e'),
            <= animWalk when weaponType > 0 => ((char)('d' + weaponType - 1), (char)('a' + animation)),
            _ => ('a', (char)('a' + animation)),
        };
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
            // ported from fallout2-ce src/art.cc artReadList(): names end at " ,;\r\t\n"
            int cut = line.IndexOfAny([' ', '\t', ',', ';']);
            lines.Add((cut >= 0 ? line[..cut] : line).Trim());
        }

        string[] result = [.. lines];
        _lists[typeIndex] = result;
        return result;
    }
}
