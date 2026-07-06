namespace Hexwaste.Formats.Sound;

/// <summary>
/// Positional sound-effect attenuation, ported from fallout2-ce
/// src/game_sound.cc:1272 _gsound_compute_relative_volume: a sound anchored to an
/// on-screen object plays at full volume; an off-screen one fades with the hex
/// distance from the dude, scaled by his Perception — full inside PE hexes, a
/// linear drop between PE and 2·PE, then a constant floor of roughly one third
/// (0x2AAA/0x7FFF). (P121 backlog item: sfx distance attenuation.)
/// </summary>
public static class SfxVolume
{
    private const int Max = 0x7FFF;      // full relative volume
    private const int Slope = 0x5554;    // the linear fade span (Max − floor)
    private const int Floor = 0x2AAA;    // beyond 2·PE

    /// <summary>The 0..1 gain for a sound at <paramref name="distanceHexes"/> from the dude
    /// whose Perception is <paramref name="perception"/>. <paramref name="onScreen"/> mirrors
    /// the engine's iso-window rect-intersection test (:1297) — visible objects are never
    /// attenuated regardless of distance.</summary>
    public static float RelativeGain(bool onScreen, int distanceHexes, int perception)
    {
        if (onScreen)
            return 1f;
        int pe = Math.Max(1, perception);
        int volume = distanceHexes <= pe ? Max
            : distanceHexes < 2 * pe ? Max - Slope * (distanceHexes - pe) / pe
            : Floor;
        return volume / (float)Max;
    }
}
