namespace Hexwaste.Formats;

/// <summary>
/// The Preferences screen model, ported from fallout2-ce src/preferences.cc
/// gPreferenceDescriptions (:370) + preferencesSetDefaults (:489). Each entry is a slider
/// with a fixed number of discrete positions (2/3/4) or a continuous range; the viewer
/// renders them on the authentic PREFSCRN window and mutates <see cref="Values"/>.
///
/// Only a subset drives Hexwaste behavior — game/combat difficulty, violence level (gates
/// the gore death art), always-run, and the four volumes; the rest render, cycle, and
/// persist for the session but are documented no-ops (there is no combat-speed, text-delay,
/// brightness, or mouse-sensitivity system). Session-scoped: Hexwaste has no config file,
/// so preferences reset each launch (a documented divergence from the engine's fallout2.cfg).
/// </summary>
public sealed class GamePreferences
{
    /// <summary>One setting: a preferences.msg TITLE id, the per-position VALUE label ids
    /// (preferences.msg), and — for a continuous slider — a null Labels with a
    /// [<see cref="Min"/>,<see cref="Max"/>] range instead of discrete positions.</summary>
    public sealed record Setting(string Key, int TitleMsg, int[] ValueMsgs, int Default,
        bool Backed, int Min = 0, int Max = 0)
    {
        public bool Continuous => ValueMsgs.Length == 0;
        public int Positions => ValueMsgs.Length;
    }

    // preferences.cc gPreferenceDescriptions order (the three window columns), with the
    // preferences.msg TITLE ids (the section headings — col1 101-105, col2 106-111, col3
    // 112-119, drawn at :1023-1037) and the per-position VALUE-label ids (from the
    // descriptor's labelIds arrays, :371-389). Backed = wired to real Hexwaste behavior.
    public static readonly Setting[] Settings =
    [
        new("game_difficulty",    101, [203, 204, 205],      Default: 1, Backed: true),   // Easy/Normal/Hard
        new("combat_difficulty",  102, [206, 204, 208],      Default: 1, Backed: true),   // Wimpy/Normal/Rough
        new("violence_level",     103, [214, 215, 204, 216], Default: 3, Backed: true),   // None/Minimal/Normal/Max Blood
        new("target_highlight",   104, [202, 201, 213],      Default: 1, Backed: false),  // Off/On/Toggle
        new("combat_looks",       105, [202, 201],           Default: 0, Backed: false),
        new("combat_messages",    106, [211, 212],           Default: 1, Backed: false),  // Verbose/Brief
        new("combat_taunts",      107, [202, 201],           Default: 1, Backed: false),
        new("language_filter",    108, [202, 201],           Default: 0, Backed: false),
        new("running",            109, [209, 219],           Default: 0, Backed: true),   // Normal/Always
        new("subtitles",          110, [202, 201],           Default: 0, Backed: false),
        new("item_highlight",     111, [202, 201],           Default: 1, Backed: false),
        new("combat_speed",       112, [], Default: 0, Backed: false, Min: 0, Max: 50),
        new("text_base_delay",    113, [], Default: 3, Backed: false, Min: 1, Max: 6),
        new("master_volume",      114, [], Default: 22281, Backed: true, Min: 0, Max: 32767),
        new("music_volume",       115, [], Default: 22281, Backed: true, Min: 0, Max: 32767),
        new("sndfx_volume",       116, [], Default: 22281, Backed: true, Min: 0, Max: 32767),
        new("speech_volume",      117, [], Default: 22281, Backed: true, Min: 0, Max: 32767),
        new("brightness",         118, [], Default: 0, Backed: false, Min: 0, Max: 100),
        new("mouse_sensitivity",  119, [], Default: 0, Backed: false, Min: 0, Max: 100),
    ];

    private readonly int[] _values = [.. Settings.Select(s => s.Default)];

    public IReadOnlyList<int> Values => _values;

    public int Get(string key) => _values[IndexOf(key)];

    /// <summary>Seed a setting from the live game state (difficulty/volumes live outside
    /// the model), clamped to the setting's range/positions.</summary>
    public void Set(string key, int value) => SetIndex(IndexOf(key), value);

    public void SetIndex(int index, int value)
    {
        Setting s = Settings[index];
        _values[index] = s.Continuous ? Math.Clamp(value, s.Min, s.Max)
            : Math.Clamp(value, 0, s.Positions - 1);
    }

    /// <summary>Cycle a discrete setting by <paramref name="delta"/> (wrapping), or step a
    /// continuous one by 1/16th of its range. Returns the new value.</summary>
    public int Adjust(int index, int delta)
    {
        Setting s = Settings[index];
        if (s.Continuous)
            _values[index] = Math.Clamp(_values[index] + delta * Math.Max(1, (s.Max - s.Min) / 16), s.Min, s.Max);
        else
            _values[index] = ((_values[index] + delta) % s.Positions + s.Positions) % s.Positions;
        return _values[index];
    }

    /// <summary>Set a continuous slider to a fraction 0..1 of its range (a click/drag on the
    /// track); no-op for discrete settings (they cycle via <see cref="Adjust"/>).</summary>
    public void SetFraction(int index, double fraction)
    {
        Setting s = Settings[index];
        if (s.Continuous)
            _values[index] = Math.Clamp(s.Min + (int)Math.Round(fraction * (s.Max - s.Min)), s.Min, s.Max);
    }

    /// <summary>Restore every setting to its engine default (the DEFAULT button).</summary>
    public void ResetDefaults()
    {
        for (int i = 0; i < Settings.Length; i++)
            _values[i] = Settings[i].Default;
    }

    private static int IndexOf(string key) => Array.FindIndex(Settings, s => s.Key == key);
}
