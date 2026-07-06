using Hexwaste.Formats;
using Hexwaste.Formats.Art;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Pal;
using Hexwaste.Formats.Proto;
using Hexwaste.Formats.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Hexwaste.Viewer;

// Worldmap travel + map transitions: exit-grid/stairs transitions, the animated party-dot leg
// (TravelTo/StepAnimatedTravel), encounter engage/avoid + arrival, the Outdoorsman detect. The
// travel-state fields move with it (concern-local). Pure move from ViewerGame.cs.
public sealed partial class ViewerGame
{
    /// <summary>
    /// Exit grid / stairs / ladder travel, mirroring fallout2-ce
    /// src/proto_instance.cc useStairs()/useLadder*(): map &gt; 0 loads another
    /// map via maps.txt; otherwise it's a teleport within the current map.
    /// </summary>
    private void ApplyTransition(MapDestination destination)
    {
        if (destination.Map > 0)
        {
            string? mapFile = _mapList.GetMapFileName(destination.Map);
            if (mapFile is null)
            {
                Console.WriteLine($"unknown destination map index {destination.Map}");
                return;
            }

            Console.WriteLine($"travelling to {mapFile} (tile {destination.Tile}, elevation {destination.Elevation})");
            LoadMap(mapFile, destination);
            return;
        }

        if (destination.Map < 0)
        {
            _worldmapOpen = true;
            // Phase-16 M2: if we're leaving a transient encounter map with a leg still in
            // progress, auto-resume travel toward the original destination instead of
            // forcing a worldmap re-click (the engine's isWalking).
            if (_currentMapTransient && _travelDestination is { } resumeDest)
            {
                _resumeTravelDest = resumeDest;
                Console.WriteLine($"travel-resume: left encounter map -> continuing to {resumeDest.Name}");
            }
            else
            {
                Log("You head out to the wasteland.");
            }
            return;
        }

        // Same-map teleport (stairs/ladders with map == 0).
        if (_dude is null)
            return;
        _dude.Stop();
        _solidObjects[_elevation].Remove(_dude.Dude);
        _elevation = destination.Elevation is >= 0 and < MapFile.ElevationCount
            && _map.Elevations[destination.Elevation] is not null
            ? destination.Elevation
            : _elevation;
        _dude.Dude.HexTile = destination.Tile;
        InsertSorted(_solidObjects[_elevation], _dude.Dude);
        RebuildBlockedTiles(_dude.Dude);
        _camera.SetCenter(destination.Tile);
        _camera.PanX = 0;
        _camera.PanY = 0;
        _baseTitle = $"Hexwaste viewer — {_map.Header.Name} (elevation {_elevation})";
        Window.Title = _baseTitle;
    }

    /// <summary>Travels to a worldmap area: first usable entrance, resolved via maps.txt lookup names.</summary>
    private void TravelTo(WorldArea area)
    {
        // Phase-10 M3: roll for encounters along the way. The pure walk + roll + map
        // pick lives in Formats.Map.WorldmapTravel.ResolveLeg (#14); the viewer only
        // does the I/O (advance the real clock, load the map). If the wasteland bites,
        // the encounter map loads instead of the town — re-clicking the destination
        // resumes travel from the encounter spot (the engine's isWalking auto-resume is
        // a documented v1 simplification). The very first travel of a game (no worldPos
        // yet) skips the roll and just arrives.
        bool rolled = _worldPosX >= 0 && _worldPosY >= 0;
        if (rolled)
        {
            _wmRng ??= new Formats.Combat.SystemCombatRng(RngSeed ?? Environment.TickCount);
            int getGlobal(int g) => _scriptHost?.GlobalVars.GetValueOrDefault(g, 0) ?? 0;
            Formats.Combat.CritterState? dudeStats = _dude is not null ? GetCritterState(_dude.Dude) : null;
            int luck = dudeStats?.Stat(Formats.Combat.CritterStat.Luck) ?? 5;
            int outdoorsman = _dude is not null ? PartyBestOutdoorsman() : 0;

            // Phase-17 M2: live play ANIMATES the leg — Update drains TravelLeg.Step() over
            // wall-time so a party dot crosses the worldmap (terrain-paced). Headless runs
            // (the goldens) drain the WHOLE leg synchronously, byte-identical (same RNG).
            if (_animateTravel)
            {
                _activeTravel = (new Formats.Map.TravelLeg(Worldmap, _cities.Areas, _mapList,
                    _worldPosX, _worldPosY, area.WorldX, area.WorldY, _clock.Ticks, _wmRng,
                    getGlobal, _dudeLevel, luck, outdoorsman, Difficulty, WorldFog, _scriptHost?.Car), area);
                _travelStepAccumMs = 0;
                _worldmapOpen = true;
                return;
            }

            Formats.Map.WorldmapTravel.LegOutcome leg = Formats.Map.WorldmapTravel.ResolveLeg(
                Worldmap, _cities.Areas, _mapList, _worldPosX, _worldPosY, area.WorldX, area.WorldY,
                _clock.Ticks, _wmRng, getGlobal, _dudeLevel, luck, outdoorsman, Difficulty, WorldFog, _scriptHost?.Car);

            _clock.Ticks += Formats.Map.WorldmapTravel.PathfinderTicks(leg.ClockTicksAdded,
                DudePerkRank(Formats.Perks.PerkId.Pathfinder)); // P79: Pathfinder shaves rank×25% off travel time
            _worldPosX = leg.FinalWorldX;
            _worldPosY = leg.FinalWorldY;
            if (leg.Encounter is { } r)
            {
                HandleLegEncounter(r, leg.EncounterMap!, area);
                return;
            }
            if (leg.OutOfGas) // the car ran dry mid-leg → strand the party (worldmap.cc:3054-3079)
            {
                DropCarOutOfGas();
                return;
            }
        }

        ArriveAt(area, rolled);
    }

    /// <summary>Advance the animated worldmap dot (phase-17 M2): accumulate wall-time and run
    /// one <see cref="Formats.Map.TravelLeg.Step"/> per travel tick. P120: the terrain cadence
    /// lives inside the leg now — a mountain tick may not move the dot but still costs the
    /// flat 30 game-minutes (worldmap.cc walk loop: wmGameTimeIncrement(18000) per iteration
    /// whether or not wmPartyWalkingStep advanced). On an encounter or arrival the leg ends
    /// and the shared handlers run. Paused while an avoid prompt is up; no-op otherwise.</summary>
    private void StepAnimatedTravel(double elapsedMs)
    {
        if (_activeTravel is null || _encounterPrompt is not null)
            return;

        _travelStepAccumMs += elapsedMs;
        while (_travelStepAccumMs >= TravelTickMs && _activeTravel is { } active)
        {
            _travelStepAccumMs -= TravelTickMs;
            _carDotFrame++; // P122: the driving Highwayman animates per tick (worldmap.cc:3047)
            Formats.Map.TravelStep s = active.Leg.Step();
            _clock.Ticks += Formats.Map.WorldmapTravel.PathfinderTicks( // P79 Pathfinder
                Formats.Map.WorldmapTravel.TicksPerStep, DudePerkRank(Formats.Perks.PerkId.Pathfinder));
            _worldPosX = s.X;
            _worldPosY = s.Y;
            if (s.Encounter is { } r)
            {
                _activeTravel = null;
                HandleLegEncounter(r, s.EncounterMap!, active.Dest);
                return;
            }
            if (s.OutOfGas) // the car ran dry mid-leg (worldmap.cc:3054)
            {
                _activeTravel = null;
                DropCarOutOfGas();
                return;
            }
            if (s.Arrived)
            {
                _activeTravel = null;
                ArriveAt(active.Dest, rolled: true);
                return;
            }
        }
    }

    /// <summary>The Highwayman car ran out of fuel mid-leg: dismount and strand the party on the
    /// "Car: Desert" out-of-gas map. ported from fallout2-ce src/worldmap.cc:3054-3079 (CITY_CAR_OUT_OF_GAS
    /// → cardesrt). The car keeps its parked area so it can be re-fuelled + re-boarded (content-gated).</summary>
    private void DropCarOutOfGas()
    {
        if (_scriptHost is not null)
            _scriptHost.Car.InCar = false;
        _worldmapOpen = false;
        Log("The car sputters and dies — out of fuel.");
        Console.WriteLine($"car-outofgas: x={_worldPosX} y={_worldPosY} map=cardesrt");
        int idx = _mapList.FindByLookupName("Car Out of Gas");
        string? mapFile = idx >= 0 ? _mapList.GetMapFileName(idx) : "cardesrt.map";
        LoadMap(mapFile ?? "cardesrt.map", null);
    }

    /// <summary>Handle an encounter that fired mid-leg (shared by the synchronous resolve
    /// and the animated step): a detected encounter grants (100-detect) XP then offers the
    /// yes/no avoid (live = the overlay; headless = _autoEncounterAnswer); engaging loads
    /// the transient map, avoiding resumes travel toward <paramref name="area"/>.</summary>
    private void HandleLegEncounter(Formats.Map.EncounterResult r, string encounterMap, WorldArea area)
    {
        string? name = EncounterName(r);
        if (r.Detected) // worldmap.cc:3475
        {
            if (r.AvoidXp > 0)
                AwardXp(r.AvoidXp);
            Console.WriteLine($"encounter detected: {r.Entry.Spawns.FirstOrDefault()?.Group ?? "?"}"
                + $" name=\"{name ?? "?"}\" avoidXp={r.AvoidXp} -> {encounterMap}");
            if (_autoEncounterAnswer is not { } answer)
            {
                _encounterPrompt = (r, encounterMap, name, area);
                _worldmapOpen = true; // keep the worldmap up under the prompt overlay
                Log($"You spot {name ?? "trouble"} ahead. Encounter it? (Y/N)");
                return;
            }
            if (!answer)
            {
                Log($"You avoid {name ?? "the encounter"} and travel on.");
                Console.WriteLine($"encounter avoided: continuing to area{area.Index}");
                TravelTo(area); // resume the leg from the encounter point
                return;
            }
            // engage → fall through to load the encounter map
        }

        _travelDestination = area; // remember the leg target so it auto-resumes (P16-M2)
        EngageEncounter(r, encounterMap, name);
    }

    /// <summary>Arrive at a worldmap area: resolve its entrance, advance the clock (the
    /// flat estimate only on the very first roll-less travel), record the worldmap
    /// whereabouts, and load the town map.</summary>
    private void ArriveAt(WorldArea area, bool rolled)
    {
        // ported behavior from fallout2-ce src/worldmap.cc
        // wmAreaFindFirstValidMap(): first enabled entrance, else force the first.
        AreaEntrance entrance = area.Entrances.FirstOrDefault(e => e.StartsOn) ?? area.Entrances.First();

        int mapIndex = _mapList.FindByLookupName(entrance.MapLookupName);
        string? mapFile = mapIndex >= 0 ? _mapList.GetMapFileName(mapIndex) : null;
        if (mapFile is null)
        {
            Console.Error.WriteLine($"area '{area.Name}': cannot resolve map '{entrance.MapLookupName}'");
            return;
        }

        _worldmapOpen = false;
        // ResolveLeg already advanced the clock per walk-loop tick across the whole leg
        // (P120: terrain-paced — mountain ticks cost time without moving); only the first
        // travel of a game (no prior worldPos → no roll) needs the flat estimate, else the
        // clock double-counts the trip.
        if (!rolled)
            _clock.AdvanceHours(8);
        // Record the dude's worldmap whereabouts so a save round-trips it
        // (phase-10 M2); a reload drops you back on the worldmap here.
        _currentAreaId = area.Index;
        if (_scriptHost?.Car.InCar == true) // park the car here so car_current_town(30) reports it (worldmap.cc:3162)
            _scriptHost.Car.CurrentAreaId = area.Index;
        _worldPosX = area.WorldX;
        _worldPosY = area.WorldY;
        WorldFog.MarkRadiusVisited(area.WorldX, area.WorldY); // reveal the destination (P22; covers the roll-less first travel that has no leg)
        _travelDestination = null; // clean arrival — the leg is over, nothing to auto-resume
        Console.WriteLine($"travelling to {area.Name} -> {mapFile}");
        LoadMap(mapFile, new MapDestination(mapIndex, entrance.Tile, entrance.Elevation, entrance.Rotation));
        Log($"You arrive at {area.Name}.");
    }

    /// <summary>P125: enter a townmap entrance — load its map at the entrance's
    /// (elevation, tile, rotation), exactly like the wmTownMapFunc pick
    /// (worldmap.cc:5800-5802 → mapSetEnteringLocation). The clock/worldPos are already
    /// settled (we're standing at the town); this only chooses WHERE to walk in.</summary>
    private void EnterTownmapEntrance(WorldArea area, int entranceIndex)
    {
        if (entranceIndex < 0 || entranceIndex >= area.Entrances.Count)
            return;
        AreaEntrance entrance = area.Entrances[entranceIndex];
        int mapIndex = _mapList.FindByLookupName(entrance.MapLookupName);
        string? mapFile = mapIndex >= 0 ? _mapList.GetMapFileName(mapIndex) : null;
        if (mapFile is null)
        {
            Console.Error.WriteLine($"townmap: area {area.Index} entrance {entranceIndex}"
                + $" cannot resolve map '{entrance.MapLookupName}'");
            return;
        }
        if (_worldmapScreen is not null)
            _worldmapScreen.TownmapArea = null;
        _worldmapOpen = false;
        Console.WriteLine($"townmap-enter: area={area.Index} entrance={entranceIndex} -> {mapFile}");
        LoadMap(mapFile, new MapDestination(mapIndex, entrance.Tile, entrance.Elevation, entrance.Rotation));
    }

    /// <summary>Pre-answer for a detected encounter in headless runs (phase-16 M1):
    /// true = engage, false = avoid. Null in live play → the interactive Y/N prompt.</summary>
    private bool? _autoEncounterAnswer;

    /// <summary>The destination of an in-progress travel leg (phase-16 M2, the engine's
    /// isWalking target): set when an engaged encounter interrupts the leg, cleared on a
    /// clean arrival. Leaving the encounter map back to the worldmap auto-resumes toward it.</summary>
    private WorldArea? _travelDestination;
    /// <summary>Deferred auto-resume: set when leaving a transient map mid-leg, consumed
    /// at the top of the next Update to continue travel without a re-click (phase-16 M2).</summary>
    private WorldArea? _resumeTravelDest;

    /// <summary>Animate worldmap travel as a moving dot (phase-17 M2). True in live play;
    /// the headless harness travel actions set it false so the goldens drain the whole leg
    /// synchronously (byte-identical RNG).</summary>
    private bool _animateTravel = true;
    /// <summary>The in-flight animated leg + its destination; null = not travelling. Update
    /// drains <see cref="Formats.Map.TravelLeg.Step"/> over wall-time (phase-17 M2).</summary>
    private (Formats.Map.TravelLeg Leg, WorldArea Dest)? _activeTravel;

    // P122: the worldmap Highwayman monitor — wmcarmve.frm (interface 433) frames cycle per
    // travel tick while driving (worldmap.cc:3047); wmscreen (363) frames the movie, and the
    // fuel bar drains alongside (wmInterfaceRefreshCarFuel).
    private int _carDotFrame;

    /// <summary>Draw the in-car monitor box (worldmap.cc:6179-6199): the car movie + the screen
    /// overlay + the fuel bar, anchored to the viewport's lower-right (the chrome's spot is
    /// window-fixed; Hexwaste has no worldmap chrome yet — documented). No-op on foot / no art.</summary>
    private void DrawWorldmapCarBox()
    {
        if (_scriptHost?.Car is not { InCar: true } car)
            return;
        Texture2D? movie;
        try
        {
            int fid = Fid.Build(ObjectType.Interface, 433);
            int frames = _frmCache.FrameCount(fid);
            movie = _frmCache.GetTexture(fid, frames > 0 ? _carDotFrame % frames : 0);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            return;
        }

        // The engine's offsets: overlay (499,330), movie (514,336), fuel bar (500,339) —
        // i.e. movie at overlay+(15,6), bar at overlay+(1,9), inside the 640x480 window.
        Viewport vp = GraphicsDevice.Viewport;
        Texture2D? overlay = InterfaceFrm(363); // wmscreen
        int boxW = overlay?.Width ?? movie.Width + 15;
        int boxH = overlay?.Height ?? movie.Height + 12;
        int ox = vp.Width - boxW - 12, oy = vp.Height - boxH - 12;
        _spriteBatch.Draw(movie, new Vector2(ox + 15, oy + 6), Color.White);
        if (overlay is not null)
            _spriteBatch.Draw(overlay, new Vector2(ox, oy), Color.White);
        // The fuel bar: a green column that drains with the tank (wmInterfaceRefreshCarFuel —
        // height 70 · fuel/CAR_FUEL_MAX at (500,339) → overlay-local (1,9)).
        _panelPixel ??= CreatePixel();
        int barH = (int)(70L * Math.Clamp(car.Fuel, 0, Formats.CarState.FuelMax) / Formats.CarState.FuelMax);
        if (barH > 0)
            _spriteBatch.Draw(_panelPixel, new Rectangle(ox + 1, oy + 9 + (70 - barH), 2, barH),
                new Color(0, 196, 0));
    }
    private double _travelStepAccumMs;
    private const double TravelTickMs = 30; // wall-time per cadence tick (the dot's base pace)

    /// <summary>A detected encounter awaiting the player's avoid choice in live play
    /// (phase-16 M1): the result, its transient map, display name, and the leg's
    /// destination (to resume travel on avoid). Null = no prompt up.</summary>
    private (Formats.Map.EncounterResult Enc, string MapFile, string? Name, WorldArea Dest)? _encounterPrompt;

    /// <summary>Engage a worldmap encounter: spawn the group on its transient map
    /// (phase-10 M3 path; the banner names it via worldmap.msg, phase-16 M0).</summary>
    private void EngageEncounter(Formats.Map.EncounterResult r, string mapFile, string? name)
    {
        _pendingEncounter = r;
        _worldmapOpen = false;
        Console.WriteLine($"encounter while travelling: {r.Entry.Spawns.FirstOrDefault()?.Group ?? "?"}"
            + $" name=\"{name ?? "?"}\" table={r.Table.Index} entry={r.Entry.EntryIndex} -> {mapFile}");
        Log(name is not null
            ? $"{(r.Entry.Situation == "AMBUSH" ? "Ambush! " : "")}{name}"
            : "Ambush! The wasteland bites.");
        LoadMap(mapFile, null, transient: true);
    }

    /// <summary>The worldmap RNG — persisted across travel legs so successive rolls
    /// differ; seeded off --rng-seed for golden transcripts, else wall-clock for a
    /// fresh wasteland each playthrough (phase-10 M3).</summary>
    private Formats.Combat.ICombatRng? _wmRng;

    /// <summary>The best Outdoorsman skill across the dude + companions (party_get_best_
    /// skill_value), feeding the encounter detect-and-avoid roll (phase-10 #12). 17 =
    /// SKILL_OUTDOORSMAN.</summary>
    private int? _forceOutdoorsman; // phase-16 M1 test override (force the detect path)
    private int PartyBestOutdoorsman()
    {
        if (_forceOutdoorsman is { } forced)
            return forced;
        int best = (_dude is not null ? GetCritterState(_dude.Dude)?.SkillValue(17) : 0) ?? 0;
        foreach (MapObject m in _scriptHost?.PartyMembers ?? [])
            best = Math.Max(best, GetCritterState(m)?.SkillValue(17) ?? 0);
        return best;
    }


    /// <summary>Queues the transition when the dude steps onto an exit grid.</summary>
    private void CheckExitGridAt(int tile)
    {
        MapObject? exitGrid = _flatObjects[_elevation]
            .FirstOrDefault(o => o.HexTile == tile && Fid.IsExitGridPid(o.Pid) && o.Destination is not null);
        if (exitGrid?.Destination is { } destination)
            _pendingTransition = destination;
    }
}
