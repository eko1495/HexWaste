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
        int dudeLevel, int luck, int outdoorsman, GameDifficulty difficulty,
        WorldmapFog? fog = null)
    {
        // Phase-17 M0: the whole-leg walk is now a DRAIN of the stepwise TravelLeg — one
        // Step() == one old loop iteration, in the same RNG draw order, so this stays
        // byte-identical while the viewer can also drive Step() per frame for the dot.
        // The optional fog reveals subtiles along the path (phase-22) — pure position math,
        // no RNG, so passing it never perturbs the encounter stream.
        var leg = new TravelLeg(worldmap, areas, mapList, startX, startY, destX, destY,
            startClockTicks, rng, getGlobal, dudeLevel, luck, outdoorsman, difficulty, fog);
        while (true)
        {
            TravelStep s = leg.Step();
            if (s.Encounter is not null)
                return new LegOutcome(s.X, s.Y, leg.TicksAdded, s.Encounter, s.EncounterMap);
            if (s.Arrived)
                return new LegOutcome(s.X, s.Y, leg.TicksAdded, null, null);
        }
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

/// <summary>The outcome of a single <see cref="TravelLeg.Step"/>: the new pixel position,
/// the encounter that fired on it (null = none), its transient map, and whether the leg has
/// reached the destination.</summary>
public readonly record struct TravelStep(int X, int Y, EncounterResult? Encounter,
    string? EncounterMap, bool Arrived);

/// <summary>
/// A worldmap travel leg walked ONE Bresenham pixel-step at a time (phase-17 M0). Holds the
/// Bresenham cursor + the <see cref="WorldEncounters"/> instance (its Δ3 anchor) across steps,
/// so the viewer can drive <see cref="Step"/> per frame to animate the party dot while the
/// pure <see cref="WorldmapTravel.ResolveLeg"/> drains it in one go. Each Step() is exactly one
/// iteration of the old whole-leg loop, in the same RNG draw order — byte-identical.
/// </summary>
public sealed class TravelLeg
{
    private readonly WorldEncounters _enc;
    private readonly IReadOnlyList<WorldArea> _areas;
    private readonly MapList _mapList;
    private readonly ICombatRng _rng;
    private readonly Func<int, int> _getGlobal;
    private readonly WorldmapFog? _fog;
    private readonly int _destX, _destY, _dudeLevel, _luck, _outdoorsman;
    private readonly long _startClockTicks;
    private readonly GameDifficulty _difficulty;
    private readonly int _dx, _dy, _sx, _sy;
    private int _x, _y, _err, _guard;

    public int X => _x;
    public int Y => _y;
    /// <summary>Cumulative travel time across the steps taken so far (the caller adds it to
    /// the real clock).</summary>
    public long TicksAdded { get; private set; }
    public bool Arrived => _x == _destX && _y == _destY;

    public TravelLeg(
        WorldmapFile worldmap, IReadOnlyList<WorldArea> areas, MapList mapList,
        int startX, int startY, int destX, int destY, long startClockTicks,
        ICombatRng rng, Func<int, int> getGlobal,
        int dudeLevel, int luck, int outdoorsman, GameDifficulty difficulty,
        WorldmapFog? fog = null)
    {
        _enc = new WorldEncounters(worldmap, rng, startX, startY);
        _areas = areas;
        _mapList = mapList;
        _rng = rng;
        _getGlobal = getGlobal;
        _fog = fog;
        _destX = destX;
        _destY = destY;
        _dudeLevel = dudeLevel;
        _luck = luck;
        _outdoorsman = outdoorsman;
        _startClockTicks = startClockTicks;
        _difficulty = difficulty;
        _x = startX;
        _y = startY;
        _dx = Math.Abs(destX - startX);
        _dy = Math.Abs(destY - startY);
        _sx = startX < destX ? 1 : -1;
        _sy = startY < destY ? 1 : -1;
        _err = _dx - _dy;
        _fog?.MarkRadiusVisited(startX, startY); // reveal where the leg begins (phase-22)
    }

    /// <summary>Advance one pixel-step toward the destination, rolling an encounter on the
    /// new pixel (suppressed near a known city). Returns the step's outcome. No-ops once the
    /// leg has arrived (or the 4000-step guard trips).</summary>
    public TravelStep Step()
    {
        if (Arrived || _guard >= 4000)
            return new TravelStep(_x, _y, null, null, true);

        _guard++;
        int e2 = 2 * _err;
        if (e2 > -_dy) { _err -= _dy; _x += _sx; }
        if (e2 < _dx) { _err += _dx; _y += _sy; }
        TicksAdded += WorldmapTravel.TicksPerStep;
        _fog?.MarkRadiusVisited(_x, _y); // reveal the new pixel's neighbourhood (phase-22)

        if (!WorldmapTravel.IsNearKnownArea(_areas, _x, _y)) // worldmap.cc:3340-3343
        {
            long nowTicks = _startClockTicks + TicksAdded;
            EncounterResult? r = _enc.Roll(_x, _y, GameClock.HourAt(nowTicks), _getGlobal,
                _dudeLevel, GameClock.DayAt(nowTicks), _luck, _outdoorsman, _difficulty);
            if (r is not null)
                return new TravelStep(_x, _y, r,
                    WorldmapTravel.ResolveEncounterMap(_mapList, r, _rng), Arrived);
        }
        return new TravelStep(_x, _y, null, null, Arrived);
    }
}

/// <summary>The worldmap dot's per-pixel pacing, ported from fallout2-ce wmPartyWalkingStep
/// (_terrainCounter cycles 1..4, advancing the dot one pixel only when
/// <c>_terrainCounter / terrainDifficulty >= 1</c>). Higher terrain difficulty = fewer ticks
/// advance = a slower dot over mountains (1/2/3/4 -> 4/3/2/1 of every 4 ticks step). The
/// counter is continuous across the journey, NOT reset per pixel (the engine's static
/// _terrainCounter starts at 1). PURE pacing — it does NOT touch the game clock or the
/// encounter rolls, so animation speed is independent of encounter fidelity (phase-17 M1).</summary>
public sealed class TerrainCadence
{
    private int _counter = 1; // worldmap.cc:752 static _terrainCounter = 1

    /// <summary>Advance one wall-clock tick over terrain of the given difficulty; returns
    /// true when the dot should step one pixel this tick.</summary>
    public bool Tick(int terrainDifficulty)
    {
        if (++_counter > 4)
            _counter = 1;
        return _counter / Math.Max(1, terrainDifficulty) >= 1;
    }
}
