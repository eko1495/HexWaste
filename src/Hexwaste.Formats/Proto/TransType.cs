namespace Hexwaste.Formats.Proto;

/// <summary>
/// An object's translucency class, decoded from the proto flag bits 0xFC000 (P23). Ported from
/// fallout2-ce src/object.cc objectCreateInternal() (~:943): the engine sets exactly one
/// OBJECT_TRANS_* flag from the proto, then src/object.cc _obj_render_object() (~:5067) picks
/// the per-type blend table. <see cref="None"/> covers both "no trans flag" and OBJECT_TRANS_NONE
/// (0x8000) — the latter is the explicit "render opaque, never fade near the dude" flag, NOT a
/// translucent effect (it falls to the engine's normal opaque blit).
/// </summary>
public enum TransType
{
    None,   // opaque (no flag, or OBJECT_TRANS_NONE 0x8000)
    Wall,   // OBJECT_TRANS_WALL   0x10000
    Glass,  // OBJECT_TRANS_GLASS  0x20000
    Steam,  // OBJECT_TRANS_STEAM  0x40000
    Energy, // OBJECT_TRANS_ENERGY 0x80000
    Red,    // OBJECT_TRANS_RED    0x4000
}

public static class Translucency
{
    // OBJECT_TRANS_* bit values (fallout2-ce src/obj_types.h:72-77).
    private const int TransRed = 0x4000, TransNone = 0x8000, TransWall = 0x10000;
    private const int TransGlass = 0x20000, TransSteam = 0x40000, TransEnergy = 0x80000;

    /// <summary>The TRANS_* mask (OBJECT_FLAG_0xFC000) — all six trans bits.</summary>
    public const int Mask = TransRed | TransNone | TransWall | TransGlass | TransSteam | TransEnergy;

    /// <summary>Decode a proto/object flags word to its translucency class, mirroring the engine's
    /// priority (object.cc:943): TRANS_NONE wins (→ opaque), else wall/glass/steam/energy/red.</summary>
    public static TransType FromFlags(int flags) =>
        (flags & TransNone) != 0 ? TransType.None
        : (flags & TransWall) != 0 ? TransType.Wall
        : (flags & TransGlass) != 0 ? TransType.Glass
        : (flags & TransSteam) != 0 ? TransType.Steam
        : (flags & TransEnergy) != 0 ? TransType.Energy
        : (flags & TransRed) != 0 ? TransType.Red
        : TransType.None;
}
