using Hexwaste.Formats;

namespace Hexwaste.Formats.Tests;

/// <summary>P130 (gap batch B): the Preferences model — discrete cycling wraps, continuous
/// sliders clamp, defaults match preferences.cc preferencesSetDefaults (:489).</summary>
public class GamePreferencesTests
{
    [Fact]
    public void DefaultsMatchTheEngine()
    {
        var p = new GamePreferences();
        Assert.Equal(1, p.Get("game_difficulty"));   // NORMAL
        Assert.Equal(1, p.Get("combat_difficulty")); // NORMAL
        Assert.Equal(3, p.Get("violence_level"));    // MAXIMUM_BLOOD (position 3 of 4)
        Assert.Equal(0, p.Get("running"));           // NORMAL (not always-run)
        Assert.Equal(22281, p.Get("master_volume"));
    }

    [Fact]
    public void DiscreteAdjustWrapsWithinPositions()
    {
        var p = new GamePreferences();
        int vlIndex = System.Array.FindIndex(GamePreferences.Settings, s => s.Key == "violence_level");
        Assert.Equal(4, GamePreferences.Settings[vlIndex].Positions); // None/Minimal/Normal/Max

        Assert.Equal(0, p.Adjust(vlIndex, +1)); // 3 -> wrap to 0
        Assert.Equal(3, p.Adjust(vlIndex, -1)); // 0 -> wrap to 3
        Assert.Equal(2, p.Adjust(vlIndex, -1)); // 3 -> 2
    }

    [Fact]
    public void ContinuousSliderClampsAndTakesFractions()
    {
        var p = new GamePreferences();
        int mv = System.Array.FindIndex(GamePreferences.Settings, s => s.Key == "master_volume");

        p.SetFraction(mv, 0.5);
        Assert.Equal(16384, p.Get("master_volume"), tolerance: 8); // half of 32767
        p.SetFraction(mv, 2.0);
        Assert.Equal(32767, p.Get("master_volume")); // clamped to max
        p.SetFraction(mv, -1.0);
        Assert.Equal(0, p.Get("master_volume"));      // clamped to min
    }

    [Fact]
    public void ResetDefaultsRestoresEverything()
    {
        var p = new GamePreferences();
        int gd = System.Array.FindIndex(GamePreferences.Settings, s => s.Key == "game_difficulty");
        p.Adjust(gd, +1); // 1 -> 2 (Hard)
        Assert.Equal(2, p.Get("game_difficulty"));

        p.ResetDefaults();
        Assert.Equal(1, p.Get("game_difficulty"));
    }

    [Fact]
    public void BackedSettingsAreTheBehaviorallyWiredOnes()
    {
        // The 8 settings that drive real Hexwaste behavior (difficulty x2, violence, running,
        // 4 volumes) — the rest render/persist but are documented no-ops.
        string[] backed = [.. GamePreferences.Settings.Where(s => s.Backed).Select(s => s.Key)];
        Assert.Equal(
            ["game_difficulty", "combat_difficulty", "violence_level", "running",
             "master_volume", "music_volume", "sndfx_volume", "speech_volume"],
            backed);
    }
}
