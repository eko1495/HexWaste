namespace FalloutPoc.Formats.Pal;

/// <summary>
/// Animated palette color cycling, ported from fallout2-ce src/cycle.cc.
/// Five fixed color tables rotate in place over reserved palette ranges
/// (slime 229..232, monitors 233..237, slow fire 238..242, fast fire 243..247,
/// shoreline 248..253) plus the alarm "bobber" at index 254. Each group ticks
/// at its original period; table values are 6-bit (the engine shifts the
/// 8-bit literals right by 2 at init).
/// </summary>
public sealed class PaletteCycler
{
    // ported from fallout2-ce src/cycle.cc kSlowCyclePeriod etc.
    private const double SlowPeriodMs = 1000.0 / 5;
    private const double MediumPeriodMs = 1000.0 / 7;
    private const double FastPeriodMs = 1000.0 / 10;
    private const double VeryFastPeriodMs = 1000.0 / 30;

    // Tables ported from fallout2-ce src/cycle.cc (8-bit RGB literals),
    // pre-shifted to 6-bit like colorCycleInit() does.
    private static readonly byte[] Slime = Shift([
        0, 108, 0, 11, 115, 7, 27, 123, 15, 43, 131, 27]);

    private static readonly byte[] Shoreline = Shift([
        83, 63, 43, 75, 59, 43, 67, 55, 39, 63, 51, 39, 55, 47, 35, 51, 43, 35]);

    private static readonly byte[] FireSlow = Shift([
        255, 0, 0, 215, 0, 0, 147, 43, 11, 255, 119, 0, 255, 59, 0]);

    private static readonly byte[] FireFast = Shift([
        71, 0, 0, 123, 0, 0, 179, 0, 0, 123, 0, 0, 71, 0, 0]);

    private static readonly byte[] Monitors = Shift([
        107, 107, 111, 99, 103, 127, 87, 107, 143, 0, 147, 163, 107, 187, 255]);

    private static byte[] Shift(byte[] values) => [.. values.Select(v => (byte)(v >> 2))];

    private double _slowTimer;
    private double _mediumTimer;
    private double _fastTimer;
    private double _veryFastTimer;

    private int _slimeStart;
    private int _shorelineStart;
    private int _fireSlowStart;
    private int _fireFastStart;
    private int _monitorsStart;
    private byte _bobberRed;
    private sbyte _bobberDiff = -4;

    public Palette Palette { get; }

    public PaletteCycler(Palette palette)
    {
        Palette = palette;

        // The engine's first ticker call fires every group at once (its
        // last-tick stamps start at 0), filling the cycling ranges that
        // color.pal stores as unmapped placeholders. The slow period is the
        // longest, so advancing by it makes every group due, like that first
        // call — frame one already shows fire.
        Update(SlowPeriodMs);
    }

    /// <summary>
    /// Advances cycling clocks by <paramref name="elapsedMs"/> and rotates due
    /// groups, writing into the palette's raw 6-bit data.
    /// Returns true when the palette changed. ported from fallout2-ce
    /// src/cycle.cc colorCycleTicker().
    /// </summary>
    public bool Update(double elapsedMs)
    {
        bool changed = false;
        byte[] raw = Palette.Raw;

        _slowTimer += elapsedMs;
        if (_slowTimer >= SlowPeriodMs)
        {
            _slowTimer = 0;
            changed = true;

            WriteRotated(raw, 229, Slime, _slimeStart);
            _slimeStart -= 3;
            if (_slimeStart < 0)
                _slimeStart = 9;

            WriteRotated(raw, 248, Shoreline, _shorelineStart);
            _shorelineStart -= 3;
            if (_shorelineStart < 0)
                _shorelineStart = 15;

            WriteRotated(raw, 238, FireSlow, _fireSlowStart);
            _fireSlowStart -= 3;
            if (_fireSlowStart < 0)
                _fireSlowStart = 12;
        }

        _mediumTimer += elapsedMs;
        if (_mediumTimer >= MediumPeriodMs)
        {
            _mediumTimer = 0;
            changed = true;

            WriteRotated(raw, 243, FireFast, _fireFastStart);
            _fireFastStart -= 3;
            if (_fireFastStart < 0)
                _fireFastStart = 12;
        }

        _fastTimer += elapsedMs;
        if (_fastTimer >= FastPeriodMs)
        {
            _fastTimer = 0;
            changed = true;

            WriteRotated(raw, 233, Monitors, _monitorsStart);
            _monitorsStart -= 3;
            if (_monitorsStart < 0)
                _monitorsStart = 12;
        }

        _veryFastTimer += elapsedMs;
        if (_veryFastTimer >= VeryFastPeriodMs)
        {
            _veryFastTimer = 0;
            changed = true;

            if (_bobberRed == 0 || _bobberRed == 60)
                _bobberDiff = (sbyte)-_bobberDiff;
            _bobberRed = (byte)(_bobberRed + _bobberDiff);

            raw[254 * 3] = _bobberRed;
            raw[254 * 3 + 1] = 0;
            raw[254 * 3 + 2] = 0;
        }

        return changed;
    }

    /// <summary>Writes table[start..] then table[..start] at the palette offset.</summary>
    private static void WriteRotated(byte[] raw, int paletteIndex, byte[] table, int start)
    {
        int offset = paletteIndex * 3;
        for (int i = start; i < table.Length; i++)
            raw[offset++] = table[i];
        for (int i = 0; i < start; i++)
            raw[offset++] = table[i];
    }
}
