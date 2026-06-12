using FalloutPoc.Formats.Hex;

namespace FalloutPoc.Formats.Light;

/// <summary>
/// What the light spread needs to know about an object occupying a tile.
/// Mirrors the fields _obj_adjust_light() reads off each Object at a hex:
/// OBJECT_LIGHT_THRU (0x20000000), FID_TYPE == OBJ_TYPE_WALL, OBJECT_FLAT
/// (0x08), and proto->wall.extendedFlags for walls.
/// </summary>
/// <param name="LightThru">OBJECT_LIGHT_THRU flag (0x20000000) is set.</param>
/// <param name="IsWall">FID_TYPE(fid) == OBJ_TYPE_WALL.</param>
/// <param name="IsFlat">OBJECT_FLAT flag (0x08) is set.</param>
/// <param name="WallExtendedFlags">proto->wall.extendedFlags for walls (0 otherwise).</param>
public readonly record struct LightBlocker(
    bool LightThru,
    bool IsWall,
    bool IsFlat,
    int WallExtendedFlags);

/// <summary>
/// Static per-tile light intensity for one elevation, ported from
/// fallout2-ce src/light.cc (gTileIntensity) and the object light spread in
/// src/object.cc _obj_adjust_light(). Object data is supplied through a
/// callback so this stays engine-free.
/// </summary>
public sealed class LightGrid
{
    /// <summary>ported from fallout2-ce src/light.h LIGHT_INTENSITY_MIN.</summary>
    public const int IntensityMin = 65536 / 4;

    /// <summary>ported from fallout2-ce src/light.h LIGHT_INTENSITY_MAX.</summary>
    public const int IntensityMax = 65536;

    /// <summary>ported from fallout2-ce src/light.cc lightResetTileIntensity().</summary>
    public const int DefaultIntensity = 655;

    // Rotations, ported from fallout2-ce src/obj_types.h Rotation.
    private const int RotationNE = 0;
    private const int RotationE = 1;
    private const int RotationSE = 2;
    private const int RotationSW = 3;
    private const int RotationW = 4;
    private const int RotationNW = 5;

    /// <summary>
    /// Ambient light level; ported from fallout2-ce src/light.cc
    /// gAmbientIntensity (initialized to LIGHT_INTENSITY_MAX).
    /// </summary>
    public int Ambient { get; set; } = IntensityMax;

    private readonly int[] _tiles = new int[HexGrid.Size];

    // ported from fallout2-ce src/object.cc _light_distance (0x5196DC):
    // ring distance of each of the 36 per-rotation wedge slots.
    private static readonly int[] LightDistance =
    [
        1, 2, 3, 4, 5, 6, 7, 8,
        2, 3, 4, 5, 6, 7, 8,
        3, 4, 5, 6, 7, 8,
        4, 5, 6, 7, 8,
        5, 6, 7, 8,
        6, 7, 8,
        7, 8,
        8,
    ];

    // ported from fallout2-ce src/object.cc _light_offsets, filled by
    // _obj_light_table_init(); indexed [tile parity][rotation][slot].
    private static readonly int[][][] LightOffsets = BuildLightOffsets();

    public LightGrid() => Reset();

    /// <summary>ported from fallout2-ce src/light.cc lightResetTileIntensity().</summary>
    public void Reset() => Array.Fill(_tiles, DefaultIntensity);

    /// <summary>
    /// ported from fallout2-ce src/light.cc lightGetTileIntensity() (clamp to
    /// LIGHT_INTENSITY_MAX) combined with the ambient floor applied at its
    /// call sites, e.g. src/tile.cc tileRenderFloorsInRect() / src/object.cc
    /// _obj_render_pre_roof(): max(ambient, lightGetTileIntensity(...)).
    /// </summary>
    public int GetTileIntensity(int tile)
    {
        if (!HexGrid.IsValid(tile))
            return 0;

        return Math.Min(Math.Max(_tiles[tile], Ambient), IntensityMax);
    }

    /// <summary>
    /// ported from fallout2-ce src/object.cc _obj_adjust_light(), increase
    /// path only (a2 == 0), without the dirty-rect bookkeeping.
    /// <paramref name="blockersAt"/> must yield the non-hidden objects at the
    /// given hex on the light source's elevation, in object-list order.
    /// </summary>
    public void AddObjectLight(int tile, int lightDistance, int lightIntensity,
        Func<int, IEnumerable<LightBlocker>> blockersAt)
    {
        if (lightIntensity <= 0)
            return;

        if (!HexGrid.IsValid(tile))
            return;

        // The source tile gets the full intensity before clamping.
        IncreaseTileIntensity(tile, lightIntensity);

        if (lightDistance > 8)
            lightDistance = 8;

        if (lightIntensity > 65536)
            lightIntensity = 65536;

        int[][] offsets = LightOffsets[tile & 1];
        int v7 = (lightIntensity - 655) / (lightDistance + 1);
        Span<int> v28 = stackalloc int[36];
        v28[0] = lightIntensity - v7;
        v28[1] = v28[0] - v7;
        v28[8] = v28[0] - v7;
        v28[2] = v28[0] - v7 - v7;
        v28[9] = v28[2];
        v28[15] = v28[0] - v7 - v7;
        v28[3] = v28[2] - v7;
        v28[10] = v28[2] - v7;
        v28[16] = v28[2] - v7;
        v28[21] = v28[2] - v7;
        v28[4] = v28[2] - v7 - v7;
        v28[11] = v28[4];
        v28[17] = v28[2] - v7 - v7;
        v28[22] = v28[2] - v7 - v7;
        v28[26] = v28[2] - v7 - v7;
        v28[5] = v28[4] - v7;
        v28[12] = v28[4] - v7;
        v28[18] = v28[4] - v7;
        v28[23] = v28[4] - v7;
        v28[27] = v28[4] - v7;
        v28[30] = v28[4] - v7;
        v28[6] = v28[4] - v7 - v7;
        v28[13] = v28[6];
        v28[19] = v28[4] - v7 - v7;
        v28[24] = v28[4] - v7 - v7;
        v28[28] = v28[4] - v7 - v7;
        v28[31] = v28[4] - v7 - v7;
        v28[33] = v28[4] - v7 - v7;
        v28[7] = v28[6] - v7;
        v28[14] = v28[6] - v7;
        v28[20] = v28[6] - v7;
        v28[25] = v28[6] - v7;
        v28[29] = v28[6] - v7;
        v28[32] = v28[6] - v7;
        v28[34] = v28[6] - v7;
        v28[35] = v28[6] - v7;

        // _light_blocked: 0/1 per [rotation][slot]. The original is a static
        // global, but every slot read for a given index belongs to an inner
        // ring already written during this call, so a fresh array is
        // equivalent.
        int[,] blocked = new int[HexGrid.RotationCount, 36];

        for (int index = 0; index < 36; index++)
        {
            if (lightDistance >= LightDistance[index])
            {
                for (int rotation = 0; rotation < HexGrid.RotationCount; rotation++)
                {
                    int nextRotation = (rotation + 1) % HexGrid.RotationCount;
                    int v14 = ComputeBlocked(blocked, index, rotation, nextRotation);

                    if (v14 == 0)
                    {
                        int target = tile + offsets[rotation][index];
                        if (HexGrid.IsValid(target))
                        {
                            bool v12 = true;

                            foreach (LightBlocker blocker in blockersAt(target))
                            {
                                v14 = blocker.LightThru ? 0 : 1;

                                if (blocker.IsWall)
                                {
                                    if (!blocker.IsFlat)
                                    {
                                        int extendedFlags = blocker.WallExtendedFlags;
                                        if ((extendedFlags & 0x8000000) != 0 || (extendedFlags & 0x40000000) != 0)
                                        {
                                            if (rotation != RotationW
                                                && rotation != RotationNW
                                                && (rotation != RotationNE || index >= 8)
                                                && (rotation != RotationSW || index <= 15))
                                            {
                                                v12 = false;
                                            }
                                        }
                                        else if ((extendedFlags & 0x10000000) != 0)
                                        {
                                            if (rotation != RotationNE && rotation != RotationNW)
                                            {
                                                v12 = false;
                                            }
                                        }
                                        else if ((extendedFlags & 0x20000000) != 0)
                                        {
                                            if (rotation != RotationNE
                                                && rotation != RotationE
                                                && rotation != RotationW
                                                && rotation != RotationNW
                                                && (rotation != RotationSW || index <= 15))
                                            {
                                                v12 = false;
                                            }
                                        }
                                        else
                                        {
                                            if (rotation != RotationNE
                                                && rotation != RotationE
                                                && (rotation != RotationNW || index <= 7))
                                            {
                                                v12 = false;
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    if (v14 != 0 && rotation >= RotationE && rotation <= RotationSW)
                                    {
                                        v12 = false;
                                    }
                                }

                                if (v14 != 0)
                                {
                                    break;
                                }
                            }

                            if (v12)
                            {
                                IncreaseTileIntensity(target, v28[index]);
                            }
                        }
                    }

                    blocked[rotation, index] = v14;
                }
            }
        }
    }

    /// <summary>ported from fallout2-ce src/light.cc lightIncreaseTileIntensity().</summary>
    private void IncreaseTileIntensity(int tile, int intensity)
    {
        if (!HexGrid.IsValid(tile))
            return;

        _tiles[tile] += intensity;
    }

    /// <summary>
    /// ported from fallout2-ce src/object.cc _obj_adjust_light(): the 36-case
    /// occlusion switch, transcribed verbatim (the edx/ebx/esi/edi/eax
    /// register sequences operate on 0/1 values, so &amp; and | are boolean
    /// AND/OR).
    /// </summary>
    private static int ComputeBlocked(int[,] blocked, int index, int rotation, int nextRotation)
    {
        int v14;
        int eax;
        int edx;
        int ebx;
        int esi;
        int edi;
        switch (index)
        {
case 0:
                v14 = 0;
                break;
            case 1:
                v14 = blocked[rotation, 0];
                break;
            case 2:
                v14 = blocked[rotation, 1];
                break;
            case 3:
                v14 = blocked[rotation, 2];
                break;
            case 4:
                v14 = blocked[rotation, 3];
                break;
            case 5:
                v14 = blocked[rotation, 4];
                break;
            case 6:
                v14 = blocked[rotation, 5];
                break;
            case 7:
                v14 = blocked[rotation, 6];
                break;
            case 8:
                v14 = blocked[rotation, 0] & blocked[nextRotation, 0];
                break;
            case 9:
                v14 = blocked[rotation, 1] & blocked[rotation, 8];
                break;
            case 10:
                v14 = blocked[rotation, 2] & blocked[rotation, 9];
                break;
            case 11:
                v14 = blocked[rotation, 3] & blocked[rotation, 10];
                break;
            case 12:
                v14 = blocked[rotation, 4] & blocked[rotation, 11];
                break;
            case 13:
                v14 = blocked[rotation, 5] & blocked[rotation, 12];
                break;
            case 14:
                v14 = blocked[rotation, 6] & blocked[rotation, 13];
                break;
            case 15:
                v14 = blocked[rotation, 8] & blocked[nextRotation, 1];
                break;
            case 16:
                v14 = blocked[rotation, 8] | (blocked[rotation, 9] & blocked[rotation, 15]);
                break;
            case 17:
                edx = blocked[rotation, 9];
                edx |= blocked[rotation, 10];
                ebx = blocked[rotation, 8];
                esi = blocked[rotation, 16];
                ebx &= edx;
                edx &= esi;
                edi = blocked[rotation, 15];
                ebx |= edx;
                edx = blocked[rotation, 10];
                eax = blocked[rotation, 9];
                edx |= edi;
                eax &= edx;
                v14 = ebx | eax;
                break;
            case 18:
                edx = blocked[rotation, 0];
                ebx = blocked[rotation, 9];
                esi = blocked[rotation, 10];
                edx |= ebx;
                edi = blocked[rotation, 11];
                edx |= esi;
                ebx = blocked[rotation, 17];
                edx |= edi;
                ebx &= edx;
                edx = esi;
                esi = blocked[rotation, 16];
                edi = blocked[rotation, 9];
                edx &= esi;
                edx |= edi;
                edx |= ebx;
                v14 = edx;
                break;
            case 19:
                edx = blocked[rotation, 17];
                edi = blocked[rotation, 18];
                ebx = blocked[rotation, 11];
                edx |= edi;
                esi = blocked[rotation, 10];
                ebx &= edx;
                edx = blocked[rotation, 9];
                edx |= esi;
                ebx |= edx;
                edx = blocked[rotation, 12];
                edx &= edi;
                ebx |= edx;
                v14 = ebx;
                break;
            case 20:
                edx = blocked[rotation, 2];
                esi = blocked[rotation, 11];
                edi = blocked[rotation, 12];
                ebx = blocked[rotation, 8];
                edx |= esi;
                esi = blocked[rotation, 9];
                edx |= edi;
                edi = blocked[rotation, 10];
                ebx &= edx;
                edx &= esi;
                esi = blocked[rotation, 17];
                ebx |= edx;
                edx = blocked[rotation, 16];
                ebx |= edi;
                edi = blocked[rotation, 18];
                edx |= esi;
                esi = blocked[rotation, 19];
                edx |= edi;
                eax = blocked[rotation, 11];
                edx |= esi;
                eax &= edx;
                ebx |= eax;
                v14 = ebx;
                break;
            case 21:
                v14 = (blocked[rotation, 8] & blocked[nextRotation, 1])
                    | (blocked[rotation, 15] & blocked[nextRotation, 2]);
                break;
            case 22:
                edx = blocked[nextRotation, 1];
                ebx = blocked[rotation, 15];
                esi = blocked[rotation, 21];
                edx |= ebx;
                ebx = blocked[rotation, 8];
                edx |= esi;
                ebx &= edx;
                edx = blocked[rotation, 9];
                edi = esi;
                edx |= esi;
                esi = blocked[rotation, 15];
                edx &= esi;
                ebx |= edx;
                edx = esi;
                esi = blocked[rotation, 16];
                edx |= edi;
                edx &= esi;
                ebx |= edx;
                v14 = ebx;
                break;
            case 23:
                edx = blocked[rotation, 3];
                ebx = blocked[rotation, 16];
                esi = blocked[rotation, 15];
                ebx |= edx;
                edx = blocked[rotation, 9];
                edx &= esi;
                edi = blocked[rotation, 22];
                ebx |= edx;
                edx = blocked[rotation, 17];
                edx &= edi;
                ebx |= edx;
                v14 = ebx;
                break;
            case 24:
                edx = blocked[rotation, 0];
                edi = blocked[rotation, 9];
                ebx = blocked[rotation, 10];
                edx |= edi;
                esi = blocked[rotation, 17];
                edx |= ebx;
                edi = blocked[rotation, 18];
                edx |= esi;
                ebx = blocked[rotation, 16];
                edx |= edi;
                esi = blocked[rotation, 16];
                ebx &= edx;
                edx = blocked[rotation, 15];
                edi = blocked[rotation, 23];
                edx |= esi;
                esi = blocked[rotation, 9];
                edx |= edi;
                edi = blocked[rotation, 8];
                edx &= esi;
                edx |= edi;
                esi = blocked[rotation, 22];
                ebx |= edx;
                edx = blocked[rotation, 15];
                edi = blocked[rotation, 23];
                edx |= esi;
                esi = blocked[rotation, 17];
                edx |= edi;
                edx &= esi;
                ebx |= edx;
                edx = blocked[rotation, 18];
                edx &= edi;
                ebx |= edx;
                v14 = ebx;
                break;
            case 25:
                edx = blocked[rotation, 8];
                edi = blocked[rotation, 15];
                ebx = blocked[rotation, 16];
                edx |= edi;
                esi = blocked[rotation, 23];
                edx |= ebx;
                edi = blocked[rotation, 24];
                edx |= esi;
                ebx = blocked[rotation, 9];
                edx |= edi;
                esi = blocked[rotation, 1];
                ebx &= edx;
                edx = blocked[rotation, 8];
                edx &= esi;
                edi = blocked[rotation, 16];
                ebx |= edx;
                edx = blocked[rotation, 8];
                esi = blocked[rotation, 17];
                edx |= edi;
                edi = blocked[rotation, 24];
                esi |= edx;
                esi |= edi;
                esi &= blocked[rotation, 10];
                edi = blocked[rotation, 23];
                ebx |= esi;
                esi = blocked[rotation, 17];
                edx |= edi;
                ebx |= esi;
                esi = blocked[rotation, 24];
                edi = blocked[rotation, 18];
                edx |= esi;
                edx &= edi;
                esi = blocked[rotation, 19];
                ebx |= edx;
                edx = blocked[rotation, 0];
                eax = blocked[rotation, 24];
                edx |= esi;
                eax &= edx;
                ebx |= eax;
                v14 = ebx;
                break;
            case 26:
                ebx = blocked[rotation, 8];
                esi = blocked[nextRotation, 1];
                edi = blocked[nextRotation, 2];
                esi &= ebx;
                ebx = blocked[rotation, 15];
                ebx &= edi;
                eax = blocked[rotation, 21];
                ebx |= esi;
                eax &= blocked[nextRotation, 3];
                ebx |= eax;
                v14 = ebx;
                break;
            case 27:
                edx = blocked[nextRotation, 0];
                edi = blocked[rotation, 15];
                esi = blocked[rotation, 21];
                edx |= edi;
                edi = blocked[rotation, 26];
                edx |= esi;
                esi = blocked[rotation, 22];
                edx |= edi;
                edi = blocked[nextRotation, 1];
                esi &= edx;
                edx = blocked[rotation, 8];
                ebx = blocked[rotation, 15];
                edx &= edi;
                edx |= ebx;
                edi = blocked[rotation, 16];
                esi |= edx;
                edx = blocked[rotation, 8];
                eax = blocked[rotation, 21];
                edx |= edi;
                eax &= edx;
                esi |= eax;
                v14 = esi;
                break;
            case 28:
                ebx = blocked[rotation, 9];
                edi = blocked[rotation, 16];
                esi = blocked[rotation, 23];
                edx = blocked[nextRotation, 0];
                ebx |= edi;
                edi = blocked[rotation, 15];
                ebx |= esi;
                esi = blocked[rotation, 8];
                ebx &= edi;
                edi = blocked[rotation, 21];
                ebx |= esi;
                esi = blocked[rotation, 22];
                edx |= edi;
                edi = blocked[rotation, 27];
                edx |= esi;
                esi = blocked[rotation, 16];
                edx |= edi;
                edx &= esi;
                edi = blocked[rotation, 17];
                ebx |= edx;
                edx = blocked[rotation, 9];
                esi = blocked[rotation, 23];
                edx |= edi;
                edi = blocked[rotation, 22];
                edx |= esi;
                edx &= edi;
                ebx |= edx;
                edx = esi;
                edx &= blocked[rotation, 27];
                ebx |= edx;
                v14 = ebx;
                break;
            case 29:
                edx = blocked[rotation, 8];
                edi = blocked[rotation, 16];
                ebx = blocked[rotation, 23];
                edx |= edi;
                esi = blocked[rotation, 15];
                ebx |= edx;
                edx = blocked[rotation, 9];
                edx &= esi;
                edi = blocked[rotation, 22];
                ebx |= edx;
                edx = blocked[rotation, 17];
                edx &= edi;
                esi = blocked[rotation, 28];
                ebx |= edx;
                edx = blocked[rotation, 24];
                edx &= esi;
                ebx |= edx;
                v14 = ebx;
                break;
            case 30:
                ebx = blocked[rotation, 8];
                esi = blocked[nextRotation, 1];
                edi = blocked[nextRotation, 2];
                esi &= ebx;
                ebx = blocked[rotation, 15];
                ebx &= edi;
                edi = blocked[nextRotation, 3];
                esi |= ebx;
                ebx = blocked[rotation, 21];
                ebx &= edi;
                eax = blocked[rotation, 26];
                ebx |= esi;
                eax &= blocked[nextRotation, 4];
                ebx |= eax;
                v14 = ebx;
                break;
            case 31:
                edx = blocked[rotation, 8];
                esi = blocked[nextRotation, 1];
                edi = blocked[rotation, 15];
                edx &= esi;
                ebx = blocked[rotation, 21];
                edx |= edi;
                esi = blocked[rotation, 22];
                ebx |= edx;
                edx = blocked[rotation, 8];
                edi = blocked[rotation, 27];
                edx |= esi;
                esi = blocked[rotation, 26];
                edx |= edi;
                edx &= esi;
                ebx |= edx;
                edx = edi;
                edx &= blocked[rotation, 30];
                ebx |= edx;
                v14 = ebx;
                break;
            case 32:
                ebx = blocked[rotation, 8];
                edi = blocked[rotation, 9];
                esi = blocked[rotation, 16];
                ebx |= edi;
                edi = blocked[rotation, 23];
                ebx |= esi;
                esi = blocked[rotation, 28];
                ebx |= edi;
                ebx |= esi;
                esi = blocked[rotation, 15];
                esi &= ebx;
                edx = blocked[rotation, 8];
                edx &= blocked[nextRotation, 1];
                ebx = blocked[rotation, 16];
                esi |= edx;
                edx = blocked[rotation, 8];
                edx |= ebx;
                ebx = blocked[rotation, 28];
                edi = blocked[rotation, 21];
                ebx |= edx;
                ebx &= edi;
                edi = blocked[rotation, 23];
                ebx |= esi;
                esi = blocked[rotation, 22];
                edx |= edi;
                ebx |= esi;
                esi = blocked[rotation, 28];
                edi = blocked[rotation, 27];
                edx |= esi;
                edx &= edi;
                esi = blocked[rotation, 31];
                ebx |= edx;
                edx = blocked[rotation, 0];
                edi = blocked[rotation, 28];
                edx |= esi;
                edx &= edi;
                ebx |= edx;
                v14 = ebx;
                break;
            case 33:
                esi = blocked[rotation, 8];
                edi = blocked[nextRotation, 1];
                ebx = blocked[rotation, 15];
                esi &= edi;
                ebx &= blocked[nextRotation, 2];
                edi = blocked[nextRotation, 3];
                esi |= ebx;
                ebx = blocked[rotation, 21];
                ebx &= edi;
                edi = blocked[nextRotation, 4];
                esi |= ebx;
                ebx = blocked[rotation, 26];
                ebx &= edi;
                eax = blocked[rotation, 30];
                ebx |= esi;
                eax &= blocked[nextRotation, 5];
                ebx |= eax;
                v14 = ebx;
                break;
            case 34:
                edx = blocked[nextRotation, 2];
                edi = blocked[rotation, 26];
                ebx = blocked[rotation, 30];
                edx |= edi;
                esi = blocked[rotation, 15];
                edx |= ebx;
                ebx = blocked[rotation, 8];
                edi = blocked[rotation, 21];
                ebx &= edx;
                edx &= esi;
                esi = blocked[rotation, 22];
                ebx |= edx;
                edx = blocked[rotation, 16];
                ebx |= edi;
                edi = blocked[rotation, 27];
                edx |= esi;
                esi = blocked[rotation, 31];
                edx |= edi;
                eax = blocked[rotation, 26];
                edx |= esi;
                eax &= edx;
                ebx |= eax;
                v14 = ebx;
                break;
            case 35:
                ebx = blocked[rotation, 8];
                esi = blocked[nextRotation, 1];
                edi = blocked[nextRotation, 2];
                esi &= ebx;
                ebx = blocked[rotation, 15];
                ebx &= edi;
                edi = blocked[nextRotation, 3];
                esi |= ebx;
                ebx = blocked[rotation, 21];
                ebx &= edi;
                edi = blocked[nextRotation, 4];
                esi |= ebx;
                ebx = blocked[rotation, 26];
                ebx &= edi;
                edi = blocked[nextRotation, 5];
                esi |= ebx;
                ebx = blocked[rotation, 30];
                ebx &= edi;
                eax = blocked[rotation, 33];
                ebx |= esi;
                eax &= blocked[nextRotation, 6];
                ebx |= eax;
                v14 = ebx;
                break;
            default:
                throw new InvalidOperationException("Should be unreachable");
        }

        return v14;
    }

    /// <summary>
    /// ported from fallout2-ce src/object.cc _obj_light_table_init(): for each
    /// tile parity and rotation, the 36 tile-number offsets of the wedge
    /// (8 + 7 + ... + 1 slots at ring distance 1..8). The original builds the
    /// table from gCenterTile; offsets only depend on tile parity, so any
    /// interior base tile of the right parity produces the same values.
    /// </summary>
    private static int[][][] BuildLightOffsets()
    {
        int[][][] offsets = new int[2][][];
        int centerTile = HexGrid.Width * (HexGrid.Height / 2) + HexGrid.Width / 2;

        for (int s = 0; s < 2; s++)
        {
            int v4 = centerTile + s;
            int parity = v4 & 1;
            offsets[parity] = new int[HexGrid.RotationCount][];
            for (int i = 0; i < HexGrid.RotationCount; i++)
            {
                int v15 = 8;
                int[] p = new int[36];
                offsets[parity][i] = p;
                int slot = 0;
                for (int j = 0; j < 8; j++)
                {
                    int tile = HexGrid.TileInDirection(v4, (i + 1) % HexGrid.RotationCount, j);

                    for (int m = 0; m < v15; m++)
                    {
                        p[slot++] = HexGrid.TileInDirection(tile, i, m + 1) - v4;
                    }

                    v15--;
                }
            }
        }

        return offsets;
    }
}
