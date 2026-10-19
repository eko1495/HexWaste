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
    /// ported from fallout2-ce src/game_sound.cc ambientSoundEffectEventProcess(): the draws of one ambient
    /// event, in fo2ce's order. <paramref name="storedIdx"/> is the event's data (null = no data, as for the
    /// map-load start and a loaded .SAV event: roll now). The delay is always drawn; the next index and the
    /// reschedule only for a non-empty list. In combat nothing plays (draws unchanged).
    /// <paramref name="between"/> is the engine's inclusive randomBetween(min, max).
    /// Returns DelayTicks -1 when the chain stops.
    /// </summary>
    public static (int PlayIdx, int? NextIdx, int DelayTicks) RunEvent(
        IReadOnlyList<(string Name, int Chance)> entries, int? storedIdx, bool inCombat, Func<int, int, int> between)
    {
        int idx = storedIdx ?? (entries.Count > 0 ? RollIndex(entries, total => between(0, total)) : -1);
        int delay = 10 * between(15, 20);
        int? next = null;
        int delayTicks = -1;
        if (entries.Count > 0)
        {
            next = RollIndex(entries, total => between(0, total));
            delayTicks = delay;
        }
        if (inCombat)
            idx = -1;
        return (idx, next, delayTicks);
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
