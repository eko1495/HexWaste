namespace Hexwaste.Formats.Art;

/// <summary>
/// Resolves FIDs to FRM virtual paths using the per-type art lists
/// (art\tiles\tiles.lst etc.), ported from fallout2-ce src/art.cc
/// artBuildFilePath()/artReadList(): path = art\&lt;typeDir&gt;\&lt;lst line (fid &amp; 0xFFF), 0-based&gt;.
/// Critters/heads use composed names (anim codes) and are out of scope for this PoC.
/// </summary>
public sealed class ArtIndex(GameFileSystem vfs)
{
    // ported from fallout2-ce src/art.cc gArtListDescriptions (indexed by ObjectType — heads=8 added P87)
    private static readonly string[] TypeDirs =
        ["items", "critters", "scenery", "walls", "tiles", "misc", "intrface", "inven", "heads", "backgrnd", "skilldex"];

    // ported from fallout2-ce src/art.cc _head1/_head2: the per-head-anim suffix chars. v4 = FID_ANIM_TYPE
    // indexes both — _head1 = the emotion (g/n/b), _head2 = the kind (v transition / f fidget / n/g/b
    // neutral-pose / p phoneme-talk). e.g. anim 4 → 'n','f' = neutral fidget (ELDERNF<n>); anim 10 → 'n','p'
    // = neutral talk (ELDERNP).
    private const string Head1 = "gggnnnbbbgnb";
    private const string Head2 = "vfngfbnfvppp";

    private readonly Dictionary<int, string[]> _lists = [];

    public string GetFrmPath(int fid)
    {
        var type = Fid.Type(fid);
        if (type is ObjectType.Head)
            return GetHeadFrmPath(fid); // P87: talking-head dialog art
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

    /// <summary>
    /// Talking-head FRM names are composed (like critters): the heads.lst base name + an emotion char
    /// (_head1) + a kind char (_head2), with a trailing fidget number for the 'f' kind.
    /// ported from fallout2-ce src/art.cc artBuildFilePath() (the type==OBJ_TYPE_HEAD branch): v4 =
    /// FID_ANIM_TYPE selects the suffix pair; v5 = the weapon-code nibble = the fidget number.
    /// </summary>
    private string GetHeadFrmPath(int fid)
    {
        string[] list = GetList((int)ObjectType.Head);
        int index = Fid.Index(fid);
        if (index < 0 || index >= list.Length)
            throw new InvalidDataException(
                $"FID 0x{fid:X8} index {index} is out of range of heads.lst ({list.Length} lines).");

        int v4 = Fid.AnimType(fid);
        if (v4 < 0 || v4 >= Head1.Length)
            throw new InvalidDataException($"FID 0x{fid:X8} has unsupported head animation {v4}.");

        char c1 = Head1[v4], c2 = Head2[v4];
        int fidget = Fid.WeaponCode(fid); // the 'f' (fidget) variants carry a 1-based index suffix
        return c2 == 'f'
            ? $@"art\heads\{list[index]}{c1}f{fidget}.frm"
            : $@"art\heads\{list[index]}{c1}{c2}.frm";
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
