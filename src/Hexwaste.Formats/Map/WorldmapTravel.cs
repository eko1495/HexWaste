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
/// Ported from fallout2-ce src/worldmap.cc (the walk-loop tick: terrain-gated pixel
/// advance + flat game time + the encounter roll, plus the known-area suppression) —
/// the same chain the live <c>TravelTo</c> and the <c>--travel-from</c> demo drive.
/// </summary>
public static class WorldmapTravel
{
    /// <summary>30 game-minutes per walk-loop tick (wmGameTimeIncrement(18000), worldmap.cc:3103 —
    /// per LOOP ITERATION whether or not the terrain cadence advanced a pixel; P120).</summary>
    public const int TicksPerStep = 18000;

    /// <summary>worldmap.cc:4179 wmGameTimeIncrement: the Pathfinder perk shaves rank×25% off travel time.
    /// Integer form (the engine keeps a sub-tick fractional remainder; we round per call — documented, and
    /// TicksPerStep 18000 is large enough that the rounding loss is negligible). Rank 0 = unchanged → the
    /// travel goldens stay byte-identical. P79.</summary>
    public static long PathfinderTicks(long ticks, int rank) =>
        rank <= 0 ? ticks : Math.Max(0, ticks - ticks * rank / 4);

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
        string? EncounterMap,
        bool OutOfGas = false);

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
        WorldmapFog? fog = null, CarState? car = null)
    {
        // Phase-17 M0: the whole-leg walk is now a DRAIN of the stepwise TravelLeg — one
        // Step() == one old loop iteration, in the same RNG draw order, so this stays
        // byte-identical while the viewer can also drive Step() per frame for the dot.
        // The optional fog reveals subtiles along the path (phase-22) — pure position math,
        // no RNG, so passing it never perturbs the encounter stream.
        var leg = new TravelLeg(worldmap, areas, mapList, startX, startY, destX, destY,
            startClockTicks, rng, getGlobal, dudeLevel, luck, outdoorsman, difficulty, fog, car);
        while (true)
        {
            TravelStep s = leg.Step();
            if (s.Encounter is not null)
                return new LegOutcome(s.X, s.Y, leg.TicksAdded, s.Encounter, s.EncounterMap, s.OutOfGas);
            if (s.OutOfGas) // the car ran dry mid-leg (worldmap.cc:3054) — stop here, the caller drops the party
                return new LegOutcome(s.X, s.Y, leg.TicksAdded, null, null, true);
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

    /// <summary>Pick the encounter's map: the entry's <c>Map</c> override, else a random map from the
    /// table's pool, falling back to <c>desert1.map</c> (phase-10 M3, wmRndEncounterPick map selection).
    /// ported from fallout2-ce src/worldmap.cc:3640 wmRndEncounterPick — the map is <c>encounterTableEntry
    /// -&gt;map</c> UNCONDITIONALLY; <c>saved=</c> never gates the choice. P101 (bucket 3): the earlier
    /// <c>IsTransient</c> filter wrongly rejected the 6 saved=Yes SPECIAL-encounter maps (crashed whale,
    /// Cafe of Broken Dreams, …), silently degrading them to random terrain — dropped.</summary>
    public static string ResolveEncounterMap(MapList mapList, EncounterResult enc, ICombatRng rng)
    {
        string? Resolve(string lookup)
        {
            int idx = mapList.FindByLookupName(lookup);
            return idx >= 0 ? mapList.GetMapFileName(idx) : null;
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
    string? EncounterMap, bool Arrived, bool OutOfGas = false);

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
    private readonly CarState? _car;
    private readonly int _carStride;
    private readonly WorldmapFile _worldmap;
    // P120 terrain travel-time: the per-pixel pacing lives INSIDE the leg now — one Step() is
    // one fo2ce walk-loop tick, and the pixel advance is cadence-gated per terrain difficulty
    // (wmPartyWalkingStep, worldmap.cc:4312). fo2ce's _terrainCounter is a static that runs
    // across the whole session; ours restarts per leg (documented — desert is unaffected, a
    // mountain leg can differ by at most one skip-tick of phase).
    private readonly TerrainCadence _cadence = new();
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
        WorldmapFog? fog = null, CarState? car = null)
    {
        _car = car;
        // Driving covers several pixels per step (worldmap.cc:3025-3051): base foot 1 + 3 in-car + blower(1)
        // + Reno upgrade(1) + super-car(3). Foot / no-car keeps stride 1 → the encounter stream is unchanged.
        _carStride = car?.InCar == true
            ? 4 + (getGlobal(CarState.GvarBlower) != 0 ? 1 : 0)
                + (getGlobal(CarState.GvarRenoUpgrade) != 0 ? 1 : 0)
                + (getGlobal(CarState.GvarSuperCar) != 0 ? 3 : 0)
            : 1;
        _worldmap = worldmap;
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

    /// <summary>Advance ONE walk-loop tick toward the destination (worldmap.cc:3025-3110):
    /// the pixel advance is cadence-gated by the current subtile's terrain difficulty
    /// (wmPartyWalkingStep :4312 — a mountain tick may move nothing), then the clock gains a
    /// flat 18000 ticks and ONE encounter roll runs at the resulting pixel (the roll's own
    /// Δ3 movement anchor makes an unmoved-tick roll a free no-op, :3331). So hard terrain
    /// costs MORE GAME TIME per pixel — 4/(5−difficulty)× — while encounter chance stays
    /// distance-anchored (P120; desert difficulty 1 advances every tick, unchanged).
    /// No-ops once the leg has arrived (or the 4000-tick guard trips).</summary>
    public TravelStep Step()
    {
        if (Arrived || _guard >= 4000)
            return new TravelStep(_x, _y, null, null, true);

        _guard++;
        // A car covers up to _carStride pixels per tick but rolls/ticks/burns exactly ONCE
        // (worldmap.cc:3025-3083); each stride unit is its own wmPartyWalkingStep, so each
        // gets its own cadence check. On foot _carStride == 1.
        for (int i = 0; i < _carStride && !Arrived; i++)
            if (_cadence.Tick(_worldmap.TerrainTravelDifficultyAt(_x, _y)))
                AdvanceOnePixel();
        TicksAdded += WorldmapTravel.TicksPerStep;

        bool outOfGas = false;
        if (_car?.InCar == true)
        {
            _car.UseGas(100, _getGlobal); // wmCarUseGas(100) per driving step (worldmap.cc:3052)
            outOfGas = _car.IsOutOfGas;    // ran dry → the caller drops the party (worldmap.cc:3054)
        }

        if (!WorldmapTravel.IsNearKnownArea(_areas, _x, _y)) // worldmap.cc:3340-3343
        {
            long nowTicks = _startClockTicks + TicksAdded;
            EncounterResult? r = _enc.Roll(_x, _y, GameClock.HourAt(nowTicks), _getGlobal,
                _dudeLevel, GameClock.DayAt(nowTicks), _luck, _outdoorsman, _difficulty);
            if (r is not null)
                return new TravelStep(_x, _y, r,
                    WorldmapTravel.ResolveEncounterMap(_mapList, r, _rng), Arrived, outOfGas);
        }
        return new TravelStep(_x, _y, null, null, Arrived, outOfGas);
    }

    /// <summary>One Bresenham pixel toward the destination (+ fog reveal). The car calls this several times
    /// per Step; foot calls it once.</summary>
    private void AdvanceOnePixel()
    {
        int e2 = 2 * _err;
        if (e2 > -_dy) { _err -= _dy; _x += _sx; }
        if (e2 < _dx) { _err += _dx; _y += _sy; }
        _fog?.MarkRadiusVisited(_x, _y); // reveal the new pixel's neighbourhood (phase-22)
    }
}

/// <summary>The worldmap party's per-tick pacing, ported from fallout2-ce wmPartyWalkingStep
/// (_terrainCounter cycles 1..4, advancing one pixel only when
/// <c>_terrainCounter / terrainDifficulty >= 1</c>). Higher terrain difficulty = fewer ticks
/// advance = slower over mountains (1/2/3/4 -> 4/3/2/1 of every 4 ticks step). The
/// counter is continuous across the journey, NOT reset per pixel (the engine's static
/// _terrainCounter starts at 1). P120: this now runs INSIDE <see cref="TravelLeg.Step"/> —
/// each Step is one walk-loop tick costing flat game time, so terrain difficulty directly
/// scales travel time per pixel (it was viewer-side dot animation only from phase-17 M1).</summary>
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
