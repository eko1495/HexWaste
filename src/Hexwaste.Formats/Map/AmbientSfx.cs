namespace Hexwaste.Formats.Map;

/// <summary>
/// The worldmap/map ambient sound-effect picker, ported from fallout2-ce src/worldmap.cc.
/// A per-map weighted list (maps.txt ambient_sfx=) is rolled chance-proportionally; bird sounds
/// are remapped to cricket sounds at night.
/// </summary>
public static class AmbientSfx
{
    /// <summary>
    /// ported from fallout2-ce src/worldmap.cc wmSfxRollNextIdx(): a chance-weighted pick.
    /// <paramref name="rollZeroToTotal"/> receives the summed chance and returns a value in [0, total]
    /// (the engine's inclusive randomBetween(0, totalChances)). Returns -1 for an empty/zero-weight list.
    /// </summary>
    public static int RollIndex(IReadOnlyList<(string Name, int Chance)> entries, Func<int, int> rollZeroToTotal)
    {
        int total = 0;
        foreach ((_, int chance) in entries)
            total += chance;
        if (total <= 0)
            return -1;

        int roll = rollZeroToTotal(total);
        for (int i = 0; i < entries.Count; i++)
        {
            if (roll >= entries[i].Chance)
            {
                roll -= entries[i].Chance;
                continue;
            }
            return i;
        }
        return -1;
    }

    /// <summary>
    /// ported from fallout2-ce src/worldmap.cc wmSfxIdxName(): at night (hhmm hour ≤ 600 or ≥ 1800)
    /// the two bird ambients become cricket sounds; everything else is unchanged.
    /// </summary>
    public static string RemapBirdForNight(string name, int hourHhmm)
    {
        bool night = hourHhmm <= 600 || hourHhmm >= 1800;
        if (!night)
            return name;
        return name switch
        {
            "brdchir1" => "cricket",
            "brdchirp" => "cricket1",
            _ => name,
        };
    }
}
