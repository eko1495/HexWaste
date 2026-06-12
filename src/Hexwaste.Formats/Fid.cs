namespace Hexwaste.Formats;

/// <summary>Object types shared by FIDs and PIDs (fallout2-ce src/obj_types.h).</summary>
public enum ObjectType
{
    Item = 0,
    Critter = 1,
    Scenery = 2,
    Wall = 3,
    Tile = 4,
    Misc = 5,
    Interface = 6,
    Inventory = 7,
    Head = 8,
    Background = 9,
    Skilldex = 10,
}

/// <summary>
/// FID (art frame id) and PID (prototype id) bit helpers,
/// ported from fallout2-ce src/obj_types.h, src/animation.h and src/art.cc.
/// </summary>
public static class Fid
{
    // #define FID_TYPE(value) ((value) & 0xF000000) >> 24
    public static ObjectType Type(int fid) => (ObjectType)((fid & 0xF000000) >> 24);

    // #define PID_TYPE(value) (value) >> 24  (arithmetic shift, like C++)
    public static int PidType(int pid) => pid >> 24;

    public static int PidIndex(int pid) => pid & 0xFFFFFF;

    // #define FID_ANIM_TYPE(value) ((value) & 0xFF0000) >> 16
    public static int AnimType(int fid) => (fid & 0xFF0000) >> 16;

    public static int Index(int fid) => fid & 0xFFF;

    public static int Rotation(int fid) => (fid & 0x70000000) >> 28;

    public static int WeaponCode(int fid) => (fid & 0xF000) >> 12;

    /// <summary>ported from fallout2-ce src/art.cc buildFidInternal().</summary>
    public static int Build(ObjectType objectType, int frmId, int animType = 0, int weaponCode = 0, int rotation = 0) =>
        ((rotation << 28) & 0x70000000) | ((int)objectType << 24) | ((animType << 16) & 0xFF0000)
        | ((weaponCode << 12) & 0xF000) | (frmId & 0xFFF);

    // ported from fallout2-ce src/proto_types.h
    public const int FirstExitGridPid = 0x5000010;
    public const int LastExitGridPid = 0x5000017;

    public static bool IsExitGridPid(int pid) => pid is >= FirstExitGridPid and <= LastExitGridPid;
}
