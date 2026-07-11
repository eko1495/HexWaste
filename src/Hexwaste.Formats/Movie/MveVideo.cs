namespace Hexwaste.Formats.Movie;

/// <summary>
/// The Interplay MVE video decoder (P133), ported faithfully from fallout2-ce
/// src/movie_lib.cc _nfPkDecomp (:1404) + the opcode dispatch (case 17 :792). Decodes the
/// per-8x8-block stream into an 8-bit indexed frame using the decode-map nibbles (opcode
/// 0x0F) as per-block operations and the video-data stream (0x11) as their operands.
///
/// This is a LITERAL port: a single byte buffer holds the two ping-pong surfaces back to
/// back (cur at <see cref="_curBase"/>, prv at <see cref="_prvBase"/>), and <c>dest</c> is a
/// mutating index exactly like fo2ce's <c>dest</c> pointer, so every opcode's byte
/// consumption and dest arithmetic transcribes 1:1 (including the mid-block column-split
/// sub-modes 8/2, 8/3, 10/2, 10/3 that jump dest by ±4). Validated pixel-exact against
/// ffmpeg's interplay_video decoder.
///
/// Drive it by feeding the demuxed opcodes (<see cref="Step"/>); <see cref="FramePresented"/>
/// flips true on a SendBuffer (0x07) so the caller can grab <see cref="CurrentIndexed"/> /
/// <see cref="BlitRgba"/>.
/// </summary>
public sealed class MveVideo
{
    private const int Bpp = 1; // Fallout MVEs are 8-bit paletted (nfBufAlloc a3 = 1)

    private byte[] _buf = [];
    private int _curBase, _prvBase;
    private int _nfWidth, _nfHeight;
    private int _d6B3D00, _d6B3CEC; // 8*bpp*nfWidth, 7*bpp*nfWidth (movie_lib.cc:1206)
    private readonly int[] _rowOffset = new int[256]; // dword_51F018
    private ReadOnlyMemory<byte> _decodeMap;

    private readonly byte[] _map1 = new byte[512];
    private readonly uint[] _map2 = new uint[256];

    public int Width => _nfWidth;
    public int Height => _nfHeight;
    public MvePalette Palette { get; } = new();
    public bool FramePresented { get; private set; }

    /// <summary>The current decoded 8-bit indexed frame (a fresh copy of the cur surface).</summary>
    public byte[] CurrentIndexed
        => _nfWidth == 0 ? [] : _buf.AsSpan(_curBase, _nfWidth * _nfHeight).ToArray();

    /// <summary>Feed one demuxed opcode. Returns after processing; check
    /// <see cref="FramePresented"/> (reset each Step) to know when a frame is ready.</summary>
    public void Step(MveOpcode op)
    {
        FramePresented = false;
        switch ((MveOp)op.Type)
        {
            case MveOp.InitVideoBuffers:
                InitBuffers(op.Data.Span);
                break;
            case MveOp.SetPalette:
                Palette.SetPalette(op.Data.Span);
                break;
            case MveOp.SetPaletteCompressed:
                Palette.SetPaletteCompressed(op.Data.Span);
                break;
            case MveOp.SetDecodingMap:
                _decodeMap = op.Data; // remembered until the next VideoData
                break;
            case MveOp.VideoData:
                DecodeVideoData(op);
                break;
            case MveOp.SendBuffer:
                FramePresented = _nfWidth > 0;
                break;
        }
    }

    private void InitBuffers(ReadOnlySpan<byte> d)
    {
        // init_video_buffers (ver ≥ 2): [u16 wBlocks][u16 hBlocks]... nfBufAlloc :1192.
        int wBlocks = U16(d, 0), hBlocks = U16(d, 2);
        _nfWidth = 8 * wBlocks;
        _nfHeight = 8 * hBlocks * Bpp;
        int size = _nfWidth * _nfHeight;
        _buf = new byte[2 * size];
        _curBase = 0;
        _prvBase = size;
        _d6B3D00 = 8 * Bpp * _nfWidth;
        _d6B3CEC = 7 * Bpp * _nfWidth;
        // _nfPkConfig (:1374): dword_51F018[0..127] = i*W, [128..255] = (i-256)*W.
        int v = 0;
        for (int i = 0; i < 128; i++) { _rowOffset[i] = v; v += _nfWidth; }
        v = -128 * _nfWidth;
        for (int i = 128; i < 256; i++) { _rowOffset[i] = v; v += _nfWidth; }
    }

    private void DecodeVideoData(MveOpcode op)
    {
        if (_nfWidth == 0 || _decodeMap.IsEmpty)
            return;
        ReadOnlySpan<byte> d = op.Data.Span;
        // 14-byte header (7 shorts): [seq][?][x][y][w][h][flags]; bit0 of flags = swap.
        int x = U16(d, 4), y = U16(d, 6), w = U16(d, 8), h = U16(d, 10), flags = U16(d, 12);
        if ((flags & 0x01) != 0)
            (_curBase, _prvBase) = (_prvBase, _curBase); // movieSwapSurfaces (:800)
        NfPkDecomp(_decodeMap.Span, d[14..], x, y, w, h);
    }

    /// <summary>Faithful port of _nfPkDecomp. <paramref name="map"/> is the decode-map nibbles
    /// (2 per byte); <paramref name="a2"/> is the operand stream. a3/a4 = x/y block pos,
    /// a5/a6 = width/height in blocks.</summary>
    private void NfPkDecomp(ReadOnlySpan<byte> map, ReadOnlySpan<byte> a2, int a3, int a4, int a5, int a6)
    {
        int nfWidth = _nfWidth;
        int d6B401B = 8 * a3;
        int d6B4017 = 8 * a5;
        int d6B401F = 8 * a4 * Bpp;
        int var8 = _d6B3D00 - d6B4017;
        int var10 = _d6B3CEC - 8;
        int v10Base = _prvBase - _curBase; // nf_buf_prv - nf_buf_cur

        int dest = _curBase;
        if (a3 != 0 || a4 != 0)
            dest = _curBase + d6B401B + nfWidth * d6B401F;

        int a1 = 0; // map read cursor
        int si = 0; // a2 (stream) cursor
        Span<int> nibbles = stackalloc int[2];

        while (a6-- != 0)
        {
            int v49 = a5 >> 1;
            while (v49-- != 0)
            {
                int v8 = map[a1++];
                nibbles[0] = v8 & 0xF;
                nibbles[1] = v8 >> 4;
                for (int j = 0; j < 2; j++)
                {
                    int v7 = nibbles[j];
                    switch (v7)
                    {
                        case 1:
                            dest += 8;
                            break;

                        case 0:
                        case 2:
                        case 3:
                        case 4:
                        case 5:
                        {
                            int v10;
                            switch (v7)
                            {
                                case 0:
                                    v10 = v10Base;
                                    break;
                                case 2:
                                case 3:
                                {
                                    int v11 = MveTables.Word51F618[a2[si++]];
                                    if (v7 == 3)
                                        v11 = ((-(v11 & 0xFF)) & 0xFF) | (((-(v11 >> 8)) & 0xFF) << 8);
                                    v10 = SignExtend8(v11) + _rowOffset[v11 >> 8];
                                    break;
                                }
                                default: // 4, 5
                                {
                                    int v13;
                                    if (v7 == 4) { v13 = MveTables.Word51F418[a2[si++]]; }
                                    else { v13 = a2[si] | (a2[si + 1] << 8); si += 2; }
                                    v10 = SignExtend8(v13) + _rowOffset[v13 >> 8] + v10Base;
                                    break;
                                }
                            }

                            for (int i = 0; i < 8; i++)
                            {
                                PutU32(dest, ReadU32(dest + v10));
                                PutU32(dest + 4, ReadU32(dest + v10 + 4));
                                dest += nfWidth;
                            }
                            dest -= nfWidth;
                            dest -= var10;
                            break;
                        }

                        case 6:
                            nibbles[0] += 2;
                            while (nibbles[0]-- != 0)
                            {
                                dest += 16;
                                if (v49-- != 0)
                                    continue;
                                dest += var8;
                                a6--;
                                v49 = (a5 >> 1) - 1;
                            }
                            break;

                        case 7: Op7(a2, ref si, ref dest, nfWidth, var10); break;
                        case 8: Op8(a2, ref si, ref dest, nfWidth, var10); break;
                        case 9: Op9(a2, ref si, ref dest, nfWidth, var10); break;
                        case 10: Op10(a2, ref si, ref dest, nfWidth, var10); break;

                        case 11: // 64 literal bytes (:2211)
                            for (int i = 0; i < 8; i++)
                            {
                                PutU32(dest, ReadStream32(a2, si + i * 8));
                                PutU32(dest + 4, ReadStream32(a2, si + i * 8 + 4));
                                dest += nfWidth;
                            }
                            dest -= nfWidth;
                            si += 64;
                            dest -= var10;
                            break;

                        case 12: // 16 bytes → each 2x2 (:2227)
                            for (int i = 0; i < 4; i++)
                            {
                                int b0 = a2[si + i * 4 + 0]; uint value1 = (uint)(b0 | (b0 << 8));
                                int b1 = a2[si + i * 4 + 1]; value1 |= (uint)((b1 << 16) | (b1 << 24));
                                int b2 = a2[si + i * 4 + 2]; uint value2 = (uint)(b2 | (b2 << 8));
                                int b3 = a2[si + i * 4 + 3]; value2 |= (uint)((b3 << 16) | (b3 << 24));
                                PutU32(dest, value1); PutU32(dest + 4, value2);
                                PutU32(dest + nfWidth, value1); PutU32(dest + nfWidth + 4, value2);
                                dest += nfWidth * 2;
                            }
                            dest -= nfWidth;
                            si += 16;
                            dest -= var10;
                            break;

                        case 13: // 4 bytes → each 4x4 (:2259)
                        {
                            int b0 = a2[si + 0]; uint v1 = (uint)(b0 | (b0 << 8) | (b0 << 16) | (b0 << 24));
                            int b1 = a2[si + 1]; uint v2 = (uint)(b1 | (b1 << 8) | (b1 << 16) | (b1 << 24));
                            for (int i = 0; i < 2; i++)
                            {
                                PutU32(dest, v1); PutU32(dest + 4, v2);
                                PutU32(dest + nfWidth, v1); PutU32(dest + nfWidth + 4, v2);
                                dest += nfWidth * 2;
                            }
                            int b2 = a2[si + 2]; v1 = (uint)(b2 | (b2 << 8) | (b2 << 16) | (b2 << 24));
                            int b3 = a2[si + 3]; v2 = (uint)(b3 | (b3 << 8) | (b3 << 16) | (b3 << 24));
                            for (int i = 0; i < 2; i++)
                            {
                                PutU32(dest, v1); PutU32(dest + 4, v2);
                                PutU32(dest + nfWidth, v1); PutU32(dest + nfWidth + 4, v2);
                                dest += nfWidth * 2;
                            }
                            dest -= nfWidth;
                            si += 4;
                            dest -= var10;
                            break;
                        }

                        case 14:
                        case 15: // solid (14) / 2-color row dither (15) (:2301)
                        {
                            uint value1, value2;
                            if (v7 == 14)
                            {
                                int b = a2[si++];
                                value1 = (uint)(b | (b << 8) | (b << 16) | (b << 24));
                                value2 = value1;
                            }
                            else
                            {
                                int b = a2[si] | (a2[si + 1] << 8); si += 2;
                                value1 = (uint)(b | (b << 16));
                                value2 = (value1 << 8) | (value1 >> (32 - 8));
                            }
                            for (int i = 0; i < 4; i++)
                            {
                                PutU32(dest, value1); PutU32(dest + 4, value1);
                                dest += nfWidth;
                                PutU32(dest, value2); PutU32(dest + 4, value2);
                                dest += nfWidth;
                            }
                            dest -= nfWidth;
                            dest -= var10;
                            break;
                        }
                    }
                }
            }
            dest += var8;
        }
    }

    // ---- color-pattern opcodes 7-10 (2/4/8/16-color block fills) ----
    // Each expands operand bytes into map1 selector indices via the R-tables, sets the
    // packed pixel values in map2, emits the block, and advances dest to the next block
    // (+8 net) via fo2ce's own dest arithmetic. Transcribed 1:1 from movie_lib.cc.

    private void Op7(ReadOnlySpan<byte> a2, ref int si, ref int dest, int nfWidth, int var10)
    {
        if (a2[si] > a2[si + 1])
        {
            // 7/1 (:1519)
            for (int i = 0; i < 2; i++)
            {
                Exp(MveTables.R0053[a2[si + 2 + i] & 0xF], i * 8);
                Exp(MveTables.R0053[a2[si + 2 + i] >> 4], i * 8 + 4);
            }
            _map2[0xC1] = (uint)((a2[si + 1] << 8) | a2[si + 1]);
            _map2[0xC3] = (uint)((a2[si] << 8) | a2[si]);
            for (int i = 0; i < 4; i++)
            {
                uint p0 = (_map2[_map1[i * 4]] << 16) | _map2[_map1[i * 4 + 1]];
                uint p1 = (_map2[_map1[i * 4 + 2]] << 16) | _map2[_map1[i * 4 + 3]];
                PutU32(dest, p0);
                PutU32(dest + nfWidth, p0);
                PutU32(dest + 4, p1);
                PutU32(dest + nfWidth + 4, p1);
                dest += nfWidth * 2;
            }
            dest -= nfWidth;
            si += 4;
            dest -= var10;
        }
        else
        {
            // 7/2 (:1562)
            for (int i = 0; i < 8; i++)
                Exp(MveTables.R0004[a2[si + 2 + i]], i * 4);
            _map2[0xC1] = (uint)((a2[si + 1] << 8) | a2[si]);
            _map2[0xC3] = (uint)((a2[si] << 8) | a2[si]);
            _map2[0xC2] = (uint)((a2[si] << 8) | a2[si + 1]);
            _map2[0xC5] = (uint)((a2[si + 1] << 8) | a2[si + 1]);
            for (int i = 0; i < 8; i++)
            {
                PutU32(dest, (_map2[_map1[i * 4]] << 16) | _map2[_map1[i * 4 + 1]]);
                PutU32(dest + 4, (_map2[_map1[i * 4 + 2]] << 16) | _map2[_map1[i * 4 + 3]]);
                dest += nfWidth;
            }
            dest -= nfWidth;
            si += 10;
            dest -= var10;
        }
    }

    private void Op8(ReadOnlySpan<byte> a2, ref int si, ref int dest, int nfWidth, int var10)
    {
        if (a2[si] > a2[si + 1])
        {
            if (a2[si + 6] > a2[si + 7])
            {
                // 8/1 (:1597)
                for (int i = 0; i < 4; i++) Exp(MveTables.R0004[a2[si + 2 + i]], i * 4);
                for (int i = 0; i < 4; i++) Exp(MveTables.R0004[a2[si + 8 + i]], 16 + i * 4);
                Set2Color(a2, si);
                for (int i = 0; i < 4; i++)
                {
                    PutU32(dest, (_map2[_map1[i * 4]] << 16) | _map2[_map1[i * 4 + 1]]);
                    PutU32(dest + 4, (_map2[_map1[i * 4 + 2]] << 16) | _map2[_map1[i * 4 + 3]]);
                    dest += nfWidth;
                }
                Set2Color(a2, si + 6);
                for (int i = 0; i < 4; i++)
                {
                    PutU32(dest, (_map2[_map1[16 + i * 4]] << 16) | _map2[_map1[16 + i * 4 + 1]]);
                    PutU32(dest + 4, (_map2[_map1[16 + i * 4 + 2]] << 16) | _map2[_map1[16 + i * 4 + 3]]);
                    dest += nfWidth;
                }
                dest -= nfWidth;
                si += 12;
                dest -= var10;
            }
            else
            {
                // 8/2 (:1647) — left/right column split
                for (int i = 0; i < 4; i++) Exp(MveTables.R0004[a2[si + 2 + i]], i * 4);
                for (int i = 0; i < 4; i++) Exp(MveTables.R0004[a2[si + 8 + i]], 16 + i * 4);
                Set2Color(a2, si);
                for (int i = 0; i < 4; i++)
                {
                    PutU32(dest, (_map2[_map1[i * 4]] << 16) | _map2[_map1[i * 4 + 1]]);
                    dest += nfWidth;
                    PutU32(dest, (_map2[_map1[i * 4 + 2]] << 16) | _map2[_map1[i * 4 + 3]]);
                    dest += nfWidth;
                }
                dest -= nfWidth * 8 - 4;
                Set2Color(a2, si + 6);
                for (int i = 0; i < 4; i++)
                {
                    PutU32(dest, (_map2[_map1[16 + i * 4]] << 16) | _map2[_map1[16 + i * 4 + 1]]);
                    dest += nfWidth;
                    PutU32(dest, (_map2[_map1[16 + i * 4 + 2]] << 16) | _map2[_map1[16 + i * 4 + 3]]);
                    dest += nfWidth;
                }
                dest -= nfWidth;
                si += 12;
                dest -= 4;
                dest -= var10;
            }
        }
        else
        {
            // 8/3 (:1705) — four 8x2 bands
            for (int i = 0; i < 2; i++) Exp(MveTables.R0004[a2[si + 2 + i]], i * 4);
            for (int i = 0; i < 2; i++) Exp(MveTables.R0004[a2[si + 6 + i]], 8 + i * 4);
            for (int i = 0; i < 2; i++) Exp(MveTables.R0004[a2[si + 10 + i]], 16 + i * 4);
            for (int i = 0; i < 2; i++) Exp(MveTables.R0004[a2[si + 14 + i]], 24 + i * 4);
            Set2Color(a2, si);
            for (int i = 0; i < 2; i++)
            {
                PutU32(dest, (_map2[_map1[i * 4]] << 16) | _map2[_map1[i * 4 + 1]]);
                dest += nfWidth;
                PutU32(dest, (_map2[_map1[i * 4 + 2]] << 16) | _map2[_map1[i * 4 + 3]]);
                dest += nfWidth;
            }
            Set2Color(a2, si + 4);
            for (int i = 0; i < 2; i++)
            {
                PutU32(dest, (_map2[_map1[8 + i * 4]] << 16) | _map2[_map1[8 + i * 4 + 1]]);
                dest += nfWidth;
                PutU32(dest, (_map2[_map1[8 + i * 4 + 2]] << 16) | _map2[_map1[8 + i * 4 + 3]]);
                dest += nfWidth;
            }
            dest -= nfWidth * 8 - 4;
            Set2Color(a2, si + 8);
            for (int i = 0; i < 2; i++)
            {
                PutU32(dest, (_map2[_map1[16 + i * 4]] << 16) | _map2[_map1[16 + i * 4 + 1]]);
                dest += nfWidth;
                PutU32(dest, (_map2[_map1[16 + i * 4 + 2]] << 16) | _map2[_map1[16 + i * 4 + 3]]);
                dest += nfWidth;
            }
            Set2Color(a2, si + 12);
            for (int i = 0; i < 2; i++)
            {
                PutU32(dest, (_map2[_map1[24 + i * 4]] << 16) | _map2[_map1[24 + i * 4 + 1]]);
                dest += nfWidth;
                PutU32(dest, (_map2[_map1[24 + i * 4 + 2]] << 16) | _map2[_map1[24 + i * 4 + 3]]);
                dest += nfWidth;
            }
            dest -= nfWidth;
            si += 16;
            dest -= 4;
            dest -= var10;
        }
    }

    private void Op9(ReadOnlySpan<byte> a2, ref int si, ref int dest, int nfWidth, int var10)
    {
        if (a2[si] > a2[si + 1])
        {
            if (a2[si + 2] > a2[si + 3])
            {
                // 9/1 (:1814) — 4-color, rows doubled vertically
                for (int i = 0; i < 8; i++) ExpFwd(MveTables.R0063[a2[si + 4 + i]], i * 4);
                Set4Color(a2, si);
                for (int i = 0; i < 4; i++)
                {
                    uint p0 = Pack4(i * 8 + 0, i * 8 + 1, i * 8 + 2, i * 8 + 3);
                    uint p1 = Pack4(i * 8 + 4, i * 8 + 5, i * 8 + 6, i * 8 + 7);
                    PutU32(dest, p0); PutU32(dest + 4, p1);
                    PutU32(dest + nfWidth, p0); PutU32(dest + nfWidth + 4, p1);
                    dest += nfWidth * 2;
                }
                dest -= nfWidth;
                si += 12;
                dest -= var10;
            }
            else
            {
                // 9/2 (:1852) — 4-color, cols doubled horizontally
                for (int i = 0; i < 8; i++) ExpRev(MveTables.R0063[a2[si + 4 + i]], i * 4);
                Set4Color(a2, si);
                for (int i = 0; i < 8; i++)
                {
                    PutU32(dest, PackH(i * 4 + 0, i * 4 + 1));
                    PutU32(dest + 4, PackH(i * 4 + 2, i * 4 + 3));
                    dest += nfWidth;
                }
                dest -= nfWidth;
                si += 12;
                dest -= var10;
            }
        }
        else
        {
            if (a2[si + 2] > a2[si + 3])
            {
                // 9/3 (:1888) — 4-color, both doubled
                for (int i = 0; i < 4; i++) ExpRev(MveTables.R0063[a2[si + 4 + i]], i * 4);
                Set4Color(a2, si);
                for (int i = 0; i < 4; i++)
                {
                    uint p0 = PackH(i * 4 + 0, i * 4 + 1);
                    uint p1 = PackH(i * 4 + 2, i * 4 + 3);
                    PutU32(dest, p0); PutU32(dest + 4, p1);
                    dest += nfWidth;
                    PutU32(dest, p0); PutU32(dest + 4, p1);
                    dest += nfWidth;
                }
                dest -= nfWidth;
                si += 8;
                dest -= var10;
            }
            else
            {
                // 9/4 (:1928) — full 4-color 8x8
                for (int i = 0; i < 16; i++) ExpFwd(MveTables.R0063[a2[si + 4 + i]], i * 4);
                Set4Color(a2, si);
                for (int i = 0; i < 8; i++)
                {
                    PutU32(dest, Pack4(i * 8 + 0, i * 8 + 1, i * 8 + 2, i * 8 + 3));
                    PutU32(dest + 4, Pack4(i * 8 + 4, i * 8 + 5, i * 8 + 6, i * 8 + 7));
                    dest += nfWidth;
                }
                dest -= nfWidth;
                si += 20;
                dest -= var10;
            }
        }
    }

    private void Op10(ReadOnlySpan<byte> a2, ref int si, ref int dest, int nfWidth, int var10)
    {
        if (a2[si] > a2[si + 1])
        {
            if (a2[si + 12] > a2[si + 13])
            {
                // 10/1 (:1966) — top 4 rows colorset A, bottom 4 colorset B
                for (int i = 0; i < 8; i++) ExpFwd(MveTables.R0063[a2[si + 4 + i]], i * 4);
                for (int i = 0; i < 8; i++) ExpFwd(MveTables.R0063[a2[si + 16 + i]], 32 + i * 4);
                Set4Color(a2, si);
                for (int i = 0; i < 4; i++)
                {
                    PutU32(dest, Pack4(i * 8 + 0, i * 8 + 1, i * 8 + 2, i * 8 + 3));
                    PutU32(dest + 4, Pack4(i * 8 + 4, i * 8 + 5, i * 8 + 6, i * 8 + 7));
                    dest += nfWidth;
                }
                Set4Color(a2, si + 12);
                for (int i = 0; i < 4; i++)
                {
                    PutU32(dest, Pack4(32 + i * 8 + 0, 32 + i * 8 + 1, 32 + i * 8 + 2, 32 + i * 8 + 3));
                    PutU32(dest + 4, Pack4(32 + i * 8 + 4, 32 + i * 8 + 5, 32 + i * 8 + 6, 32 + i * 8 + 7));
                    dest += nfWidth;
                }
                dest -= nfWidth;
                si += 24;
                dest -= var10;
            }
            else
            {
                // 10/2 (:2023) — left cols colorset A, right cols colorset B
                for (int i = 0; i < 8; i++) ExpFwd(MveTables.R0063[a2[si + 4 + i]], i * 4);
                for (int i = 0; i < 8; i++) ExpFwd(MveTables.R0063[a2[si + 16 + i]], 32 + i * 4);
                Set4Color(a2, si);
                for (int i = 0; i < 4; i++)
                {
                    PutU32(dest, Pack4(i * 8 + 0, i * 8 + 1, i * 8 + 2, i * 8 + 3));
                    dest += nfWidth;
                    PutU32(dest, Pack4(i * 8 + 4, i * 8 + 5, i * 8 + 6, i * 8 + 7));
                    dest += nfWidth;
                }
                dest -= nfWidth * 8 - 4;
                Set4Color(a2, si + 12);
                for (int i = 0; i < 4; i++)
                {
                    PutU32(dest, Pack4(32 + i * 8 + 0, 32 + i * 8 + 1, 32 + i * 8 + 2, 32 + i * 8 + 3));
                    dest += nfWidth;
                    PutU32(dest, Pack4(32 + i * 8 + 4, 32 + i * 8 + 5, 32 + i * 8 + 6, 32 + i * 8 + 7));
                    dest += nfWidth;
                }
                dest -= nfWidth;
                si += 24;
                dest -= 4;
                dest -= var10;
            }
        }
        else
        {
            // 10/3 (:2091) — four 8x2 bands, each its own colorset
            for (int i = 0; i < 4; i++) ExpFwd(MveTables.R0063[a2[si + 4 + i]], i * 4);
            for (int i = 0; i < 4; i++) ExpFwd(MveTables.R0063[a2[si + 12 + i]], 16 + i * 4);
            for (int i = 0; i < 4; i++) ExpFwd(MveTables.R0063[a2[si + 20 + i]], 32 + i * 4);
            for (int i = 0; i < 4; i++) ExpFwd(MveTables.R0063[a2[si + 28 + i]], 48 + i * 4);
            Set4Color(a2, si);
            for (int i = 0; i < 2; i++)
            {
                PutU32(dest, Pack4(i * 8 + 0, i * 8 + 1, i * 8 + 2, i * 8 + 3));
                dest += nfWidth;
                PutU32(dest, Pack4(i * 8 + 4, i * 8 + 5, i * 8 + 6, i * 8 + 7));
                dest += nfWidth;
            }
            Set4Color(a2, si + 8);
            for (int i = 0; i < 2; i++)
            {
                PutU32(dest, Pack4(16 + i * 8 + 0, 16 + i * 8 + 1, 16 + i * 8 + 2, 16 + i * 8 + 3));
                dest += nfWidth;
                PutU32(dest, Pack4(16 + i * 8 + 4, 16 + i * 8 + 5, 16 + i * 8 + 6, 16 + i * 8 + 7));
                dest += nfWidth;
            }
            dest -= nfWidth * 8 - 4;
            Set4Color(a2, si + 16);
            for (int i = 0; i < 2; i++)
            {
                PutU32(dest, Pack4(32 + i * 8 + 0, 32 + i * 8 + 1, 32 + i * 8 + 2, 32 + i * 8 + 3));
                dest += nfWidth;
                PutU32(dest, Pack4(32 + i * 8 + 4, 32 + i * 8 + 5, 32 + i * 8 + 6, 32 + i * 8 + 7));
                dest += nfWidth;
            }
            Set4Color(a2, si + 24);
            for (int i = 0; i < 2; i++)
            {
                PutU32(dest, Pack4(48 + i * 8 + 0, 48 + i * 8 + 1, 48 + i * 8 + 2, 48 + i * 8 + 3));
                dest += nfWidth;
                PutU32(dest, Pack4(48 + i * 8 + 4, 48 + i * 8 + 5, 48 + i * 8 + 6, 48 + i * 8 + 7));
                dest += nfWidth;
            }
            dest -= nfWidth;
            si += 32;
            dest -= 4;
            dest -= var10;
        }
    }

    // map1 expansion: forward byte order (:1600 style) — used by 7/8 (R0004) and 9/10 (R0063).
    private void Exp(uint value1, int at)
    {
        _map1[at] = (byte)value1;
        _map1[at + 1] = (byte)(value1 >> 8);
        _map1[at + 2] = (byte)(value1 >> 16);
        _map1[at + 3] = (byte)(value1 >> 24);
    }

    private void ExpFwd(uint value1, int at) => Exp(value1, at);

    // reversed byte order (:1856 style) — 9/2, 9/3.
    private void ExpRev(uint value1, int at)
    {
        _map1[at + 3] = (byte)value1;
        _map1[at + 2] = (byte)(value1 >> 8);
        _map1[at + 1] = (byte)(value1 >> 16);
        _map1[at] = (byte)(value1 >> 24);
    }

    // 2-color map2 setup (:1616 / 1666) with the color pair at a2[off], a2[off+1].
    private void Set2Color(ReadOnlySpan<byte> a2, int off)
    {
        _map2[0xC1] = (uint)((a2[off + 1] << 8) | a2[off]);
        _map2[0xC3] = (uint)((a2[off] << 8) | a2[off]);
        _map2[0xC2] = (uint)((a2[off] << 8) | a2[off + 1]);
        _map2[0xC5] = (uint)((a2[off + 1] << 8) | a2[off + 1]);
    }

    // 4-color map2 setup (:1824) with the 4 colors at a2[off..off+3].
    private void Set4Color(ReadOnlySpan<byte> a2, int off)
    {
        _map2[0xC1] = a2[off + 2];
        _map2[0xC3] = a2[off + 0];
        _map2[0xC5] = a2[off + 3];
        _map2[0xC7] = a2[off + 1];
        _map2[0xE1] = a2[off + 2];
        _map2[0xE3] = a2[off + 0];
        _map2[0xE5] = a2[off + 3];
        _map2[0xE7] = a2[off + 1];
    }

    // 4-color pack (:1837): 4 single-byte pixels swizzled into one u32.
    private uint Pack4(int m0, int m1, int m2, int m3)
        => (_map2[_map1[m0]] << 16) | (_map2[_map1[m1]] << 24) | _map2[_map1[m2]] | (_map2[_map1[m3]] << 8);

    // horizontal-doubled pack (:1875): 2 pixels each written twice into one u32.
    private uint PackH(int m0, int m1)
        => (_map2[_map1[m0]] << 24) | (_map2[_map1[m0]] << 16) | (_map2[_map1[m1]] << 8) | _map2[_map1[m1]];

    /// <summary>Blit the current indexed frame to an RGBA buffer via the palette.</summary>
    public byte[] BlitRgba()
    {
        int n = _nfWidth * _nfHeight;
        var rgba = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            (byte r, byte g, byte b) = Palette.Color(_buf[_curBase + i]);
            rgba[i * 4] = r; rgba[i * 4 + 1] = g; rgba[i * 4 + 2] = b; rgba[i * 4 + 3] = 255;
        }
        return rgba;
    }

    private uint ReadU32(int i)
        => (uint)(_buf[i] | (_buf[i + 1] << 8) | (_buf[i + 2] << 16) | (_buf[i + 3] << 24));

    private static uint ReadStream32(ReadOnlySpan<byte> s, int i)
        => (uint)(s[i] | (s[i + 1] << 8) | (s[i + 2] << 16) | (s[i + 3] << 24));

    private void PutU32(int i, uint v)
    {
        _buf[i] = (byte)v;
        _buf[i + 1] = (byte)(v >> 8);
        _buf[i + 2] = (byte)(v >> 16);
        _buf[i + 3] = (byte)(v >> 24);
    }

    private static int SignExtend8(int v) => (sbyte)(v & 0xFF);
    private static int U16(ReadOnlySpan<byte> d, int o) => d[o] | (d[o + 1] << 8);
}
