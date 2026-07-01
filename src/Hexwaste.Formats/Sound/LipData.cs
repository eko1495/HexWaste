using System.Buffers.Binary;

namespace Hexwaste.Formats.Sound;

/// <summary>
/// A parsed <c>.lip</c> lip-sync file (talking-head phoneme timing), ported from fallout2-ce src/lips.cc
/// lipsLoad() (the v2 branch) + lipsTicker(). The file is BIG-ENDIAN (fo2ce fileReadInt32 byte-swaps,
/// db.cc:321). Layout (v2): a 44-byte header of 8 int32 (version, field_4, flags, field_10, field_1C,
/// phonemeCount, field_28, markerCount) + an 8-byte file_name + a 4-byte tag; then <c>phonemeCount</c>
/// phoneme bytes; then <c>markerCount</c> markers of {int32 marker; int32 position}. Verified byte-for-byte
/// against the real ELDER\AELD1.LIP (628 = 44 + 64 + 65×8).
/// </summary>
public sealed class LipData
{
    /// <summary>Phoneme index → head-FRM frame, ported verbatim from fallout2-ce src/game_dialog.cc:320
    /// (_head_phoneme_lookup[PHONEME_COUNT]). The head draws frame <c>PhonemeFrame[phoneme]</c>.</summary>
    public static readonly int[] PhonemeFrame =
    [
        0, 3, 1, 1, 3, 1, 1, 1, 7, 8, 7, 3, 1, 8, 1, 7, 7, 6, 6, 2, 2,
        2, 2, 4, 4, 5, 5, 2, 2, 2, 2, 2, 6, 2, 2, 5, 8, 2, 2, 2, 2, 8,
    ];

    /// <summary>The head phoneme-animation FRM ids, ported from fallout2-ce src/art.h:40-42
    /// (HEAD_ANIMATION_GOOD/NEUTRAL/BAD_PHONEMES) — chosen by the reply's classified reaction.</summary>
    public const int AnimGoodPhonemes = 9;
    public const int AnimNeutralPhonemes = 10;
    public const int AnimBadPhonemes = 11;

    /// <summary>The head frame for a phoneme index (bounds-guarded).</summary>
    public static int FrameForPhoneme(int phoneme) =>
        phoneme >= 0 && phoneme < PhonemeFrame.Length ? PhonemeFrame[phoneme] : 0;

    /// <summary>Phoneme index per step (each &lt; 42 = PHONEME_COUNT, lips.h:10).</summary>
    public IReadOnlyList<byte> Phonemes { get; }

    /// <summary>(marker, sample-position) pairs; marker[0] is {0|1, 0}.</summary>
    public IReadOnlyList<(int Marker, int Position)> Markers { get; }

    /// <summary>The speech sample rate (field_4, e.g. 22528) — converts elapsed ms → sample position.</summary>
    public int SampleRate { get; }

    private LipData(byte[] phonemes, (int, int)[] markers, int sampleRate)
    {
        Phonemes = phonemes;
        Markers = markers;
        SampleRate = sampleRate;
    }

    /// <summary>Parse a v2 <c>.lip</c> file. Throws <see cref="InvalidDataException"/> on a bad version/size.</summary>
    public static LipData Parse(byte[] data)
    {
        if (data.Length < 44)
            throw new InvalidDataException($".lip too small ({data.Length} bytes).");
        int Int32At(int off) => BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(off, 4));

        int version = Int32At(0);
        if (version != 2)
            throw new InvalidDataException($".lip version {version} unsupported (only v2).");
        int sampleRate = Int32At(1 * 4);    // field_4 — the speech sample rate (e.g. 22528)
        int phonemeCount = Int32At(5 * 4);  // field_24 — the 6th int32
        int markerCount = Int32At(7 * 4);   // field_2C — the 8th int32
        int phonemesOffset = 44;            // header: 8 int32 (32) + file_name[8] + tag[4]
        int markersOffset = phonemesOffset + phonemeCount;
        if (phonemeCount < 0 || markerCount < 0 || markersOffset + markerCount * 8 > data.Length)
            throw new InvalidDataException(".lip phoneme/marker counts exceed file size.");

        var phonemes = new byte[phonemeCount];
        Array.Copy(data, phonemesOffset, phonemes, 0, phonemeCount);

        var markers = new (int, int)[markerCount];
        for (int i = 0; i < markerCount; i++)
        {
            int o = markersOffset + i * 8;
            markers[i] = (Int32At(o), Int32At(o + 4));
        }
        return new LipData(phonemes, markers, sampleRate);
    }

    /// <summary>The phoneme index active at a given decoded-sound sample position — ported from
    /// lipsTicker(): the phoneme of the largest marker whose position is before <paramref name="samplePos"/>.
    /// Returns 0 (neutral/closed) before the first marker.</summary>
    public int PhonemeAt(int samplePos)
    {
        if (Phonemes.Count == 0)
            return 0;
        int idx = 0;
        while (idx + 1 < Markers.Count && idx + 1 < Phonemes.Count && samplePos > Markers[idx].Position)
            idx++;
        return Phonemes[idx];
    }
}
