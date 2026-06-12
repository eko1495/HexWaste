using System.Buffers.Binary;

namespace Hexwaste.Formats.Sound;

/// <summary>Decoded Interplay ACM audio: interleaved signed 16-bit samples.</summary>
public sealed record AcmAudio(short[] Samples, int Channels, int SampleRate);

/// <summary>
/// Interplay ACM audio decoder, ported from fallout2-ce src/sound_decoder.cc
/// (soundDecoderInit / soundDecoderDecode and the ReadBand_* / untransform_*
/// helpers). The original is a pull-based streaming decoder fed by a read
/// callback; this port buffers the whole file and decodes it in one pass, but
/// the algorithm — bit reader, scale table, band fillers and the in-place
/// inverse subband transform — is kept faithful to the original, including its
/// odd byte-level accesses, so output matches the engine bit for bit.
///
/// File header (all values little-endian via the bit reader):
///   24 bits magic 0x32897, 8 bits version (must be 1) — i.e. the file starts
///   with bytes 97 28 03 01; 32 bits total 16-bit sample count; 16 bits
///   channels; 16 bits sample rate; 4 bits levels; 12 bits samples-per-subband.
/// </summary>
public static class AcmDecoder
{
    /// <summary>Decodes a complete ACM file held in memory.</summary>
    public static AcmAudio Decode(byte[] acmData)
    {
        ArgumentNullException.ThrowIfNull(acmData);
        return new Decoder(acmData).Decode();
    }

    /// <summary>Buffers the stream fully, then decodes it.</summary>
    public static AcmAudio Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Decode(buffer.ToArray());
    }

    private sealed class Decoder
    {
        // 0x6AD9E0 pack3_3, 0x6ADA00 pack5_3, 0x6AD960 pack11_2 — filled once
        // by init_pack_tables(); unfilled slots stay zero like the C statics.
        private static readonly byte[] Pack3_3 = new byte[32];
        private static readonly ushort[] Pack5_3 = new ushort[128];
        private static readonly byte[] Pack11_2 = new byte[128];

        // ported from init_pack_tables (0x4D3C78)
        static Decoder()
        {
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    for (int m = 0; m < 3; m++)
                        Pack3_3[i + j * 3 + m * 9] = (byte)(i + j * 4 + m * 16);

            for (int i = 0; i < 5; i++)
                for (int j = 0; j < 5; j++)
                    for (int m = 0; m < 5; m++)
                        Pack5_3[i + j * 5 + m * 25] = (ushort)(i + j * 8 + m * 64);

            for (int i = 0; i < 11; i++)
                for (int j = 0; j < 11; j++)
                    Pack11_2[i + j * 11] = (byte)(i + j * 16);
        }

        // _AudioDecoder_scale_tbl is 0x20000 bytes (0x10000 shorts) with
        // _AudioDecoder_scale0 pointing at its middle; Scale0 is that middle
        // index, so negative offsets in the original map to Scale0 - n here.
        private const int Scale0 = 0x8000;
        private readonly short[] _scale = new short[0x10000];

        private readonly byte[] _input;
        private int _pos;

        // Bit accumulator (SoundDecoder.hold / SoundDecoder.bits).
        private int _hold;
        private int _bits;

        private readonly int _levels;
        private readonly int _subbands;
        private readonly int _samplesPerSubband;
        private readonly int _totalSamples;
        private readonly int _blockSamplesPerSubband;
        private readonly int _blockTotalSamples;
        private readonly int _channels;
        private readonly int _rate;
        private readonly int _fileCnt;

        // The original addresses these buffers through int*, short* and
        // unsigned char* interchangeably, so they are kept as raw bytes with
        // explicit little-endian accessors; offsets below are byte offsets.
        private readonly byte[] _samples;
        private readonly byte[] _prevSamples;

        // Header parsing ported from soundDecoderInit (0x4D50A8).
        public Decoder(byte[] acmData)
        {
            _input = acmData;

            RequireBits(24);
            int magic = _hold;
            DropBits(24);
            if ((magic & 0xFFFFFF) != 0x32897)
                throw new InvalidDataException("Not an ACM file: bad signature (expected 24-bit magic 0x32897).");

            RequireBits(8);
            int version = _hold;
            DropBits(8);
            if (version != 1)
                throw new InvalidDataException($"Unsupported ACM version {version} (expected 1).");

            RequireBits(16);
            _fileCnt = _hold & 0xFFFF;
            DropBits(16);

            RequireBits(16);
            _fileCnt |= (_hold & 0xFFFF) << 16;
            DropBits(16);

            RequireBits(16);
            _channels = _hold & 0xFFFF;
            DropBits(16);

            RequireBits(16);
            _rate = _hold & 0xFFFF;
            DropBits(16);

            RequireBits(4);
            _levels = _hold & 0x0F;
            DropBits(4);

            RequireBits(12);
            _subbands = 1 << _levels;
            _samplesPerSubband = _hold & 0x0FFF;
            _totalSamples = _samplesPerSubband * _subbands;
            DropBits(12);

            int prevCount = _levels != 0 ? 3 * _subbands / 2 - 2 : 0;

            _blockSamplesPerSubband = 2048 / _subbands - 2;
            if (_blockSamplesPerSubband < 1)
                _blockSamplesPerSubband = 1;

            _blockTotalSamples = _blockSamplesPerSubband * _subbands;

            // fallout2-ce allocates sizeof(unsigned char*) (8 bytes on 64-bit)
            // per element; the a4 == 4 path of UntransformSubband touches up to
            // 8 bytes past the 4-byte-element area on the last subband, so the
            // same slack is kept here (contents are zeroed, matching memset).
            _prevSamples = new byte[8 * prevCount];
            _samples = new byte[4 * _totalSamples];
        }

        // Equivalent of repeatedly calling soundDecoderDecode (0x4D4FA0) until
        // file_cnt is exhausted, with soundDecoderFill inlined.
        public AcmAudio Decode()
        {
            var output = new short[_fileCnt];
            if (_fileCnt > 0 && _totalSamples <= 0)
                throw new InvalidDataException("ACM header declares samples but has an empty block size.");

            int outPos = 0;
            while (outPos < _fileCnt)
            {
                // CE: ReadBands handles Fmt31 (acts as decoder and transformer),
                // so bands are not untransformed again when it reports failure.
                if (ReadBands())
                    UntransformAll();

                int count = Math.Min(_totalSamples, _fileCnt - outPos);
                for (int i = 0; i < count; i++)
                {
                    int sample = GetI32(_samples, 4 * i);
                    output[outPos++] = unchecked((short)((sample >> _levels) & 0xFFFF));
                }
            }

            return new AcmAudio(output, _channels, _rate);
        }

        // ported from soundDecoderRequireBits; the original zero-fills its
        // 512-byte chunk when the read callback hits EOF, which is equivalent
        // to feeding zero bytes forever.
        private void RequireBits(int bits)
        {
            while (_bits < bits)
            {
                byte ch = _pos < _input.Length ? _input[_pos++] : (byte)0;
                _hold |= ch << _bits;
                _bits += 8;
            }
        }

        private void DropBits(int bits)
        {
            _hold >>= bits;
            _bits -= bits;
        }

        // ported from ReadBands (0x4D493C)
        private bool ReadBands()
        {
            RequireBits(4);
            int v9 = _hold & 0xF;
            DropBits(4);

            RequireBits(16);
            int v15 = _hold & 0xFFFF;
            DropBits(16);

            int v17 = 1 << v9;

            // The original writes through unsigned short*; values wrap to 16
            // bits on store and are read back sign-extended through short*.
            int v21 = 0;
            for (int i = 0; i < v17; i++)
            {
                _scale[Scale0 + i] = unchecked((short)v21);
                v21 += v15;
            }

            v21 = -v15;
            for (int i = 1; i <= v17; i++)
            {
                _scale[Scale0 - i] = unchecked((short)v21);
                v21 -= v15;
            }

            for (int index = 0; index < _subbands; index++)
            {
                RequireBits(5);
                int bits = _hold & 0x1F;
                DropBits(5);

                if (!ReadBand(index, bits))
                    return false;
            }

            return true;
        }

        // dispatch table _ReadBand_tbl (0x51E330)
        private bool ReadBand(int offset, int bits) => bits switch
        {
            0 => ReadBandFmt0(offset),
            >= 3 and <= 16 => ReadBandFmt3_16(offset, bits),
            17 => ReadBandFmt17(offset),
            18 => ReadBandFmt18(offset),
            19 => ReadBandFmt19(offset),
            20 => ReadBandFmt20(offset),
            21 => ReadBandFmt21(offset),
            22 => ReadBandFmt22(offset),
            23 => ReadBandFmt23(offset),
            24 => ReadBandFmt24(offset),
            26 => ReadBandFmt26(offset),
            27 => ReadBandFmt27(offset),
            29 => ReadBandFmt29(offset),
            31 => ReadBandFmt31(),
            _ => false, // ReadBand_Fail (formats 1, 2, 25, 28, 30)
        };

        // ported from ReadBand_Fmt0 (0x4D3DA0)
        private bool ReadBandFmt0(int offset)
        {
            int p = 4 * offset;
            for (int i = _samplesPerSubband; i != 0; i--)
            {
                SetI32(_samples, p, 0);
                p += 4 * _subbands;
            }
            return true;
        }

        // ported from ReadBand_Fmt3_16 (0x4D3DC8)
        private bool ReadBandFmt3_16(int offset, int bits)
        {
            // base += (int)(UINT_MAX << (bits - 1)) → scale0 - (1 << (bits - 1))
            int baseIdx = Scale0 + unchecked((int)(uint.MaxValue << (bits - 1)));
            int p = 4 * offset;
            int v14 = (1 << bits) - 1;

            for (int i = _samplesPerSubband; i != 0; i--)
            {
                RequireBits(bits);
                int value = _hold;
                DropBits(bits);

                SetI32(_samples, p, _scale[baseIdx + (v14 & value)]);
                p += 4 * _subbands;
            }
            return true;
        }

        // ported from ReadBand_Fmt17 (0x4D3E90)
        private bool ReadBandFmt17(int offset)
        {
            int p = 4 * offset;
            int i = _samplesPerSubband;
            while (i != 0)
            {
                RequireBits(3);

                int value = _hold & 0xFF;
                if ((value & 0x01) == 0)
                {
                    DropBits(1);

                    SetI32(_samples, p, 0);
                    p += 4 * _subbands;
                    if (--i == 0)
                        break;

                    SetI32(_samples, p, 0);
                    p += 4 * _subbands;
                    if (--i == 0)
                        break;
                }
                else if ((value & 0x02) == 0)
                {
                    DropBits(2);

                    SetI32(_samples, p, 0);
                    p += 4 * _subbands;
                    if (--i == 0)
                        break;
                }
                else
                {
                    DropBits(3);

                    SetI32(_samples, p, _scale[(value & 0x04) != 0 ? Scale0 + 1 : Scale0 - 1]);
                    p += 4 * _subbands;
                    i--;
                }
            }
            return true;
        }

        // ported from ReadBand_Fmt18 (0x4D3F98)
        private bool ReadBandFmt18(int offset)
        {
            int p = 4 * offset;
            int i = _samplesPerSubband;
            while (i != 0)
            {
                RequireBits(2);

                int value = _hold;
                if ((value & 0x01) == 0)
                {
                    DropBits(1);

                    SetI32(_samples, p, 0);
                    p += 4 * _subbands;
                    if (--i == 0)
                        return true;
                }
                else
                {
                    DropBits(2);

                    SetI32(_samples, p, _scale[(value & 0x02) != 0 ? Scale0 + 1 : Scale0 - 1]);
                    p += 4 * _subbands;
                    i--;
                }
            }
            return true;
        }

        // ported from ReadBand_Fmt19 (0x4D4068)
        private bool ReadBandFmt19(int offset)
        {
            int baseIdx = Scale0 - 1;
            int p = 4 * offset;
            int i = _samplesPerSubband;
            while (i != 0)
            {
                RequireBits(5);
                int value = _hold & 0x1F;
                DropBits(5);

                value = Pack3_3[value];

                SetI32(_samples, p, _scale[baseIdx + (value & 0x03)]);
                p += 4 * _subbands;
                if (--i == 0)
                    break;

                SetI32(_samples, p, _scale[baseIdx + ((value >> 2) & 0x03)]);
                p += 4 * _subbands;
                if (--i == 0)
                    break;

                SetI32(_samples, p, _scale[baseIdx + (value >> 4)]);
                p += 4 * _subbands;
                i--;
            }
            return true;
        }

        // ported from ReadBand_Fmt20 (0x4D4158)
        private bool ReadBandFmt20(int offset)
        {
            int p = 4 * offset;
            int i = _samplesPerSubband;
            while (i != 0)
            {
                RequireBits(4);

                int value = _hold & 0xFF;
                if ((value & 0x01) == 0)
                {
                    DropBits(1);

                    SetI32(_samples, p, 0);
                    p += 4 * _subbands;
                    if (--i == 0)
                        break;

                    SetI32(_samples, p, 0);
                    p += 4 * _subbands;
                    if (--i == 0)
                        break;
                }
                else if ((value & 0x02) == 0)
                {
                    DropBits(2);

                    SetI32(_samples, p, 0);
                    p += 4 * _subbands;
                    if (--i == 0)
                        break;
                }
                else
                {
                    DropBits(4);

                    int idx = (value & 0x08) != 0
                        ? ((value & 0x04) != 0 ? Scale0 + 2 : Scale0 + 1)
                        : ((value & 0x04) != 0 ? Scale0 - 1 : Scale0 - 2);
                    SetI32(_samples, p, _scale[idx]);
                    p += 4 * _subbands;
                    i--;
                }
            }
            return true;
        }

        // ported from ReadBand_Fmt21 (0x4D4254)
        private bool ReadBandFmt21(int offset)
        {
            int p = 4 * offset;
            int i = _samplesPerSubband;
            while (i != 0)
            {
                RequireBits(3);

                int value = _hold & 0xFF;
                if ((value & 0x01) == 0)
                {
                    DropBits(1);

                    SetI32(_samples, p, 0);
                    p += 4 * _subbands;
                    if (--i == 0)
                        break;
                }
                else
                {
                    DropBits(3);

                    int idx = (value & 0x04) != 0
                        ? ((value & 0x02) != 0 ? Scale0 + 2 : Scale0 + 1)
                        : ((value & 0x02) != 0 ? Scale0 - 1 : Scale0 - 2);
                    SetI32(_samples, p, _scale[idx]);
                    p += 4 * _subbands;
                    i--;
                }
            }
            return true;
        }

        // ported from ReadBand_Fmt22 (0x4D4338)
        private bool ReadBandFmt22(int offset)
        {
            int baseIdx = Scale0 - 2;
            int p = 4 * offset;
            int i = _samplesPerSubband;
            while (i != 0)
            {
                RequireBits(7);
                int value = _hold & 0x7F;
                DropBits(7);

                value = Pack5_3[value];

                SetI32(_samples, p, _scale[baseIdx + (value & 7)]);
                p += 4 * _subbands;
                if (--i == 0)
                    break;

                SetI32(_samples, p, _scale[baseIdx + ((value >> 3) & 7)]);
                p += 4 * _subbands;
                if (--i == 0)
                    break;

                SetI32(_samples, p, _scale[baseIdx + (value >> 6)]);
                p += 4 * _subbands;
                if (--i == 0)
                    break;
            }
            return true;
        }

        // ported from ReadBand_Fmt23 (0x4D4434)
        private bool ReadBandFmt23(int offset)
        {
            int p = 4 * offset;
            int i = _samplesPerSubband;
            while (i != 0)
            {
                RequireBits(5);

                int value = _hold;
                if ((value & 0x01) == 0)
                {
                    DropBits(1);

                    SetI32(_samples, p, 0);
                    p += 4 * _subbands;
                    if (--i == 0)
                        break;

                    SetI32(_samples, p, 0);
                    p += 4 * _subbands;
                    if (--i == 0)
                        break;
                }
                else if ((value & 0x02) == 0)
                {
                    DropBits(2);

                    SetI32(_samples, p, 0);
                    p += 4 * _subbands;
                    if (--i == 0)
                        break;
                }
                else if ((value & 0x04) == 0)
                {
                    DropBits(4);

                    SetI32(_samples, p, _scale[(value & 0x08) != 0 ? Scale0 + 1 : Scale0 - 1]);
                    p += 4 * _subbands;
                    if (--i == 0)
                        break;
                }
                else
                {
                    DropBits(5);

                    value >>= 3;
                    value &= 0x03;
                    if (value >= 2)
                        value += 3;

                    SetI32(_samples, p, _scale[Scale0 + value - 3]);
                    p += 4 * _subbands;
                    i--;
                }
            }
            return true;
        }

        // ported from ReadBand_Fmt24 (0x4D4584)
        private bool ReadBandFmt24(int offset)
        {
            int p = 4 * offset;
            int i = _samplesPerSubband;
            while (i != 0)
            {
                RequireBits(4);

                int value = _hold & 0xFF;
                if ((value & 0x01) == 0)
                {
                    DropBits(1);

                    SetI32(_samples, p, 0);
                    p += 4 * _subbands;
                    if (--i == 0)
                        break;
                }
                else if ((value & 0x02) == 0)
                {
                    DropBits(3);

                    SetI32(_samples, p, _scale[(value & 0x04) != 0 ? Scale0 + 1 : Scale0 - 1]);
                    p += 4 * _subbands;
                    if (--i == 0)
                        break;
                }
                else
                {
                    DropBits(4);

                    value >>= 2;
                    value &= 0x03;
                    if (value >= 2)
                        value += 3;

                    SetI32(_samples, p, _scale[Scale0 + value - 3]);
                    p += 4 * _subbands;
                    i--;
                }
            }
            return true;
        }

        // ported from ReadBand_Fmt26 (0x4D4698)
        private bool ReadBandFmt26(int offset)
        {
            int p = 4 * offset;
            int i = _samplesPerSubband;
            while (i != 0)
            {
                RequireBits(5);

                int value = _hold;
                if ((value & 0x01) == 0)
                {
                    DropBits(1);

                    SetI32(_samples, p, 0);
                    p += 4 * _subbands;
                    if (--i == 0)
                        break;

                    SetI32(_samples, p, 0);
                    p += 4 * _subbands;
                    if (--i == 0)
                        break;
                }
                else if ((value & 0x02) == 0)
                {
                    DropBits(2);

                    SetI32(_samples, p, 0);
                    p += 4 * _subbands;
                    if (--i == 0)
                        break;
                }
                else
                {
                    DropBits(5);

                    value >>= 2;
                    value &= 0x07;
                    if (value >= 4)
                        value += 1;

                    SetI32(_samples, p, _scale[Scale0 + value - 4]);
                    p += 4 * _subbands;
                    i--;
                }
            }
            return true;
        }

        // ported from ReadBand_Fmt27 (0x4D47A4)
        private bool ReadBandFmt27(int offset)
        {
            int p = 4 * offset;
            int i = _samplesPerSubband;
            while (i != 0)
            {
                RequireBits(4);

                int value = _hold;
                if ((value & 0x01) == 0)
                {
                    DropBits(1);

                    SetI32(_samples, p, 0);
                    p += 4 * _subbands;
                    if (--i == 0)
                        break;
                }
                else
                {
                    DropBits(4);

                    value >>= 1;
                    value &= 0x07;
                    if (value >= 4)
                        value += 1;

                    SetI32(_samples, p, _scale[Scale0 + value - 4]);
                    p += 4 * _subbands;
                    i--;
                }
            }
            return true;
        }

        // ported from ReadBand_Fmt29 (0x4D4870)
        private bool ReadBandFmt29(int offset)
        {
            int p = 4 * offset;
            int i = _samplesPerSubband;
            while (i != 0)
            {
                RequireBits(7);
                int value = _hold & 0x7F;
                DropBits(7);

                value = Pack11_2[value];

                SetI32(_samples, p, _scale[Scale0 + (value & 0x0F) - 5]);
                p += 4 * _subbands;
                if (--i == 0)
                    break;

                SetI32(_samples, p, _scale[Scale0 + (value >> 4) - 5]);
                p += 4 * _subbands;
                if (--i == 0)
                    break;
            }
            return true;
        }

        // ported from ReadBand_Fmt31; CE addition for some Russian
        // localizations — decodes and transforms in one go, then returns 0 so
        // ReadBands stops and untransform_all is skipped.
        private bool ReadBandFmt31()
        {
            int p = 0;
            for (int remaining = _totalSamples; remaining != 0; remaining--)
            {
                RequireBits(16);
                int value = _hold & 0xFFFF;
                DropBits(16);

                SetI32(_samples, p, (value << 16) >> (16 - _levels));
                p += 4;
            }
            return false;
        }

        // ported from untransform_subband0 (0x4D4ADC); a1 is a byte offset
        // into _prevSamples, a2 a byte offset into _samples. Each 4-byte prev
        // slot packs two 16-bit history values (low short + high short).
        private void UntransformSubband0(int a1, int a2, int a3, int a4)
        {
            if (a4 == 2)
            {
                // The original loops a3 times doing nothing here.
            }
            else if (a4 == 4)
            {
                int v31 = a3;
                int v9 = a2 + 4 * a3;
                int v10 = a2 + 4 * (a3 * 3);
                int v11 = a2 + 4 * (a3 * 2);

                while (v31 != 0)
                {
                    int v12 = GetI32(_prevSamples, a1) >> 16;

                    int v13 = GetI32(_samples, a2);
                    SetI32(_samples, a2, GetI16(_prevSamples, a1) + 2 * v12 + v13);

                    int v14 = GetI32(_samples, v9);
                    SetI32(_samples, v9, 2 * v13 - v12 - v14);

                    int v15 = GetI32(_samples, v11);
                    SetI32(_samples, v11, 2 * v14 + v15 + v13);

                    int v16 = GetI32(_samples, v10);
                    SetI32(_samples, v10, 2 * v15 - v14 - v16);

                    v10 += 4;
                    v11 += 4;
                    v9 += 4;

                    SetI16(_prevSamples, a1, unchecked((short)v15));
                    SetI16(_prevSamples, a1 + 2, unchecked((short)v16));

                    a1 += 4;
                    a2 += 4;

                    v31--;
                }
            }
            else
            {
                int v30 = a4 >> 1;
                int v32 = a3;
                while (v32 != 0)
                {
                    int v19 = a2;

                    // The original leaves v20/v22 uninitialized when v30 is
                    // odd; zero keeps the port deterministic.
                    int v20 = 0;
                    int v22 = 0;
                    if ((v30 & 0x01) == 0)
                    {
                        v20 = GetI16(_prevSamples, a1);
                        v22 = GetI32(_prevSamples, a1) >> 16;
                    }

                    int v23 = v30 >> 1;
                    while (--v23 != -1)
                    {
                        int v24 = GetI32(_samples, v19);
                        SetI32(_samples, v19, v24 + 2 * v22 + v20);
                        v19 += 4 * a3;

                        int v26 = GetI32(_samples, v19);
                        SetI32(_samples, v19, 2 * v24 - v22 - v26);
                        v19 += 4 * a3;

                        v20 = GetI32(_samples, v19);
                        SetI32(_samples, v19, v20 + 2 * v26 + v24);
                        v19 += 4 * a3;

                        v22 = GetI32(_samples, v19);
                        SetI32(_samples, v19, 2 * v20 - v26 - v22);
                        v19 += 4 * a3;
                    }

                    SetI16(_prevSamples, a1, unchecked((short)v20));
                    SetI16(_prevSamples, a1 + 2, unchecked((short)v22));

                    a1 += 4;
                    a2 += 4;
                    v32--;
                }
            }
        }

        // ported from untransform_subband (0x4D4D1C); a1/a2 are byte offsets
        // into _prevSamples/_samples. The a4 == 4 branch keeps the original's
        // decompiled pointer arithmetic verbatim: v26 is an int*, so v26 + 4
        // is +16 bytes, while v5/v6 stride in bytes and *v6 += ... is a
        // single-byte add.
        private void UntransformSubband(int a1, int a2, int a3, int a4)
        {
            int v26 = a1;
            int v25 = a2;

            if (a4 == 4)
            {
                int v4 = a2 + 4 * a3;
                int v5 = a2 + 3 * a3;
                int v6 = a2 + 2 * a3;

                while (a3-- != 0)
                {
                    int v7 = GetI32(_prevSamples, v26 + 16);
                    int v8 = GetI32(_samples, v25);
                    SetI32(_samples, v25, GetI32(_prevSamples, v26) + 2 * v7);

                    int v9 = GetI32(_samples, v4);
                    SetI32(_samples, v4, 2 * v8 - v7 - v9);

                    int v10 = GetI32(_samples, v6);
                    v5 += 4;
                    _samples[v6] = unchecked((byte)(_samples[v6] + 2 * v9 + v8));

                    int v11 = GetI32(_samples, v5 - 4);
                    v6 += 4;

                    SetI32(_samples, v5 - 4, 2 * v10 - v9 - v11);
                    v4 += 4;

                    SetI32(_prevSamples, v26, v10);
                    SetI32(_prevSamples, v26 + 16, v11);

                    v26 += 8;
                    v25 += 4;
                }
            }
            else
            {
                int v24 = a3;

                while (v24 != 0)
                {
                    int v13 = a4 >> 2;
                    int v14 = v25;
                    int v15 = GetI32(_prevSamples, v26);
                    int v16 = GetI32(_prevSamples, v26 + 4);

                    while (--v13 != -1)
                    {
                        int v17 = GetI32(_samples, v14);
                        SetI32(_samples, v14, v17 + 2 * v16 + v15);

                        int v18 = v14 + 4 * a3;
                        int v19 = GetI32(_samples, v18);
                        SetI32(_samples, v18, 2 * v17 - v16 - v19);

                        int v20 = v18 + 4 * a3;
                        v15 = GetI32(_samples, v20);
                        SetI32(_samples, v20, v15 + 2 * v19 + v17);

                        int v21 = v20 + 4 * a3;
                        v16 = GetI32(_samples, v21);
                        SetI32(_samples, v21, 2 * v15 - v19 - v16);

                        v14 = v21 + 4 * a3;
                    }

                    SetI32(_prevSamples, v26, v15);
                    SetI32(_prevSamples, v26 + 4, v16);

                    v26 += 8;
                    v25 += 4;

                    v24--;
                }
            }
        }

        // ported from untransform_all (0x4D4E80)
        private void UntransformAll()
        {
            if (_levels == 0)
                return;

            int ptr = 0;

            int v8 = _samplesPerSubband;
            while (v8 > 0)
            {
                int v3 = _subbands >> 1;
                int v4 = _blockSamplesPerSubband;
                if (v4 > v8)
                    v4 = v8;

                v4 *= 2;

                UntransformSubband0(0, ptr, v3, v4);

                int v5 = ptr;
                for (int v6 = 0; v6 < v4; v6++)
                {
                    SetI32(_samples, v5, GetI32(_samples, v5) + 1);
                    v5 += 4 * v3;
                }

                int j = 4 * v3;
                while (true)
                {
                    v3 >>= 1;
                    v4 *= 2;
                    if (v3 == 0)
                        break;
                    UntransformSubband(j, ptr, v3, v4);
                    j += 8 * v3;
                }

                ptr += _blockTotalSamples * 4;
                v8 -= _blockSamplesPerSubband;
            }
        }

        private static int GetI32(byte[] buffer, int offset) =>
            BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(offset));

        private static void SetI32(byte[] buffer, int offset, int value) =>
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), value);

        private static short GetI16(byte[] buffer, int offset) =>
            BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(offset));

        private static void SetI16(byte[] buffer, int offset, short value) =>
            BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset), value);
    }
}
