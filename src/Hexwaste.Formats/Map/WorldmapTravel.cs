using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Map;

/// <summary>
/// The pure decision layer for a worldmap travel leg (phase-10 M3), lifted out of
/// <c>ViewerGame</c> (#14, mirroring the phase-9 <see cref="CombatEngine"/> seam:
/// the decision logic lives here, all I/O — loading the map, advancing the real
/// clock, the worldmap screen — stays with the caller). Engine-free: every
/// dependency is a <c>Hexwaste.Formats</c> type, so the whole leg is unit-testable
/// under a deterministic <see cref="ICombatRng"/>.
///
/// Ported from fallout2-ce src/worldmap.cc (the per-pixel walk + the encounter roll
/// per step + the known-area suppression) — the same chain the live <c>TravelTo</c>
/// and the <c>--travel-from</c> demo drive.
/// </summary>
public static class WorldmapTravel
{
    /// <summary>30 game-minutes per worldmap pixel-step (worldmap.cc travel cost).</summary>
    public const int TicksPerStep = 18000;

    /// <summary>The squared radius (in worldmap pixels) of a city's "you're basically
    /// there" circle — the engine never rolls an encounter inside it (worldmap.cc:3340).</summary>
    private const int KnownAreaRadiusSq = 12 * 12;

    /// <summary>The outcome of one travel leg. <see cref="Encounter"/> non-null = the
    /// wasteland bit: load <see cref="EncounterMap"/> as a transient map with that group.
    /// Null = a clean arrival at the destination. <see cref="ClockTicksAdded"/> is the
    /// per-step travel time the caller applies to the real clock.</summary>
    public sealed record LegOutcome(
        int FinalWorldX,
        int FinalWorldY,
        long ClockTicksAdded,
        EncounterResult? Encounter,
        string? EncounterMap);

    /// <summary>
    /// Walk the Bresenham line from (<paramref name="startX"/>,<paramref name="startY"/>)
    /// to (<paramref name="destX"/>,<paramref name="destY"/>), rolling an encounter per
    /// pixel-step. On the first hit, resolves the encounter's transient map and returns
    /// it with the encounter group; on a clean arrival returns a null encounter. The
    /// caller supplies the leg's starting clock ticks so each step's hour/day matches
    /// the live clock the engine would have advanced.
    /// </summary>
    public static LegOutcome ResolveLeg(
        WorldmapFile worldmap, IReadOnlyList<WorldArea> areas, MapList mapList,
        int startX, int startY, int destX, int destY, long startClockTicks,
        ICombatRng rng, Func<int, int> getGlobal,
        int dudeLevel, int luck, int outdoorsman, GameDifficulty difficulty)
    {
        var enc = new WorldEncounters(worldmap, rng, startX, startY);

        int x = startX, y = startY;
        int dx = Math.Abs(destX - x), dy = Math.Abs(destY - y);
        int sx = x < destX ? 1 : -1, sy = y < destY ? 1 : -1, err = dx - dy;
        long ticksAdded = 0;

        for (int guard = 0; (x != destX || y != destY) && guard < 4000; guard++)
        {
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
            ticksAdded += TicksPerStep;
            if (IsNearKnownArea(areas, x, y)) // known-area suppression (worldmap.cc:3340-3343)
                continue;

            long nowTicks = startClockTicks + ticksAdded;
            EncounterResult? r = enc.Roll(x, y, GameClock.HourAt(nowTicks), getGlobal,
                dudeLevel, GameClock.DayAt(nowTicks), luck, outdoorsman, difficulty);
            if (r is not null)
                return new LegOutcome(x, y, ticksAdded, r, ResolveEncounterMap(mapList, r, rng));
        }

        return new LegOutcome(destX, destY, ticksAdded, null, null);
    }

    /// <summary>True when a worldmap pixel sits on/near a known city circle — the engine
    /// never rolls an encounter there (worldmap.cc:3340-3343). Suppresses ambushes on a
    /// town's doorstep.</summary>
    public static bool IsNearKnownArea(IReadOnlyList<WorldArea> areas, int worldX, int worldY)
    {
        foreach (WorldArea a in areas)
        {
            if (a.Entrances.Count == 0)
                continue;
            int dx = a.WorldX - worldX, dy = a.WorldY - worldY;
            if (dx * dx + dy * dy <= KnownAreaRadiusSq)
                return true;
        }
        return false;
    }

    /// <summary>Pick the encounter's transient map: the entry's <c>Map</c> override, else
    /// a random map from the table's pool, falling back to <c>desert1.map</c> — only ever
    /// a <c>saved=No</c> map (phase-10 M3, wmRndEncounterPick map selection, simplified).</summary>
    public static string ResolveEncounterMap(MapList mapList, EncounterResult enc, ICombatRng rng)
    {
        string? Resolve(string lookup)
        {
            int idx = mapList.FindByLookupName(lookup);
            string? file = idx >= 0 ? mapList.GetMapFileName(idx) : null;
            return file is not null && mapList.IsTransient(file) ? file : null;
        }

        if (enc.Entry.Map is { Length: > 0 } m && Resolve(m) is { } mapped)
            return mapped;
        if (enc.Table.Maps.Count > 0)
            // one shuffled pass so a non-transient/unresolvable entry doesn't loop forever
            foreach (string lookup in enc.Table.Maps.OrderBy(_ => rng.Next(0, enc.Table.Maps.Count)))
                if (Resolve(lookup) is { } file)
                    return file;
        return "desert1.map"; // guaranteed transient fallback
    }
}
