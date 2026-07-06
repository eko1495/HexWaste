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

// Persistence: the per-map delta snapshot/replay (CaptureMapDelta / ApplyDelta* / RebuildObject) and the
// versioned JSON SaveGame/LoadGame round-trip. Pure move from ViewerGame.cs (the 10-slot picker UI lives
// in ViewerGame.Panels.cs; fields stay central).
public sealed partial class ViewerGame
{
    /// <summary>Snapshots the current map's player-visible changes — every
    /// door's open/locked state, pristine objects gone from the world (by
    /// ordinal), created objects still in it, full container contents, MVARs
    /// — so revisits and saves replay them over pristine + map_enter(0).</summary>
    private void CaptureMapDelta()
    {
        // A transient (saved=No) encounter map is never remembered — it regenerates
        // pristine every visit (phase-10 M0/M3). This is the single _visitedMaps writer,
        // so guarding it here closes BOTH the map-exit path and the F5/save path (which
        // calls this directly, bypassing LoadMap's guard #2) — otherwise saving mid-
        // encounter wrote a phantom delta that replayed the spawned critters on load.
        if (_currentMapTransient)
            return;

        var delta = new SaveState.MapDelta
        {
            MapVars = [.. _map.GlobalVariables], SnapshotDay = _clock.Day,
            SeenTiles = [.. _seenTiles], // P71: persist the automap fog (the explored-tile set)
        };

        var present = new HashSet<MapObject>();
        foreach (MapElevation? elev in _map.Elevations)
            if (elev is not null)
                present.UnionWith(elev.Objects);

        // Party members AND live dismissed bodies travel via state.Party /
        // state.DismissedCompanions, OUTSIDE map deltas. The map-exit path pulls them
        // first (→ not present → taken), but an F5 save calls this directly. So mark a
        // still-on-map managed critter's pristine ordinal TAKEN too, and skip them in
        // the Created/Moved/Container loops — otherwise a companion recruited in place
        // (then F5'd before leaving) is restored twice on load.
        var managed = new HashSet<MapObject>(_scriptHost?.PartyMembers ?? []);
        managed.UnionWith(_dismissedCompanions.Keys);

        for (int ordinal = 0; ordinal < _ordinalObjects.Length; ordinal++)
        {
            MapObject o = _ordinalObjects[ordinal];
            if (!present.Contains(o) || managed.Contains(o))
                delta.TakenOrdinals.Add(ordinal);
        }

        for (int elevation = 0; elevation < MapFile.ElevationCount; elevation++)
        {
            foreach (MapObject obj in _map.Elevations[elevation]?.Objects ?? [])
            {
                // Party members are injected after the ordinal build (so they're not in
                // _objectOrdinals) and travel OUTSIDE map deltas via state.Party — exclude
                // them like the dude, else an F5 save (no ExtractPartyFromMap first)
                // captures each companion as a Created object and load duplicates them.
                if (!_objectOrdinals.ContainsKey(obj) && obj != _dude?.Dude && !managed.Contains(obj))
                    delta.Created.Add(new SaveState.CreatedObject(
                        obj.Pid, obj.HexTile, elevation, Math.Max(obj.StackCount, 1)));
                if (IsDoor(obj))
                    delta.Doors.Add(new SaveState.SavedDoor(
                        obj.HexTile, obj.Pid, _openDoors.Contains(obj), obj.IsLockedState));
            }
        }

        // Position drift (wandering NPCs, script moves) — V2.
        var elevationOf = new Dictionary<MapObject, int>();
        for (int elevation = 0; elevation < MapFile.ElevationCount; elevation++)
            foreach (MapObject obj in _map.Elevations[elevation]?.Objects ?? [])
                elevationOf[obj] = elevation;
        for (int ordinal = 0; ordinal < _ordinalObjects.Length; ordinal++)
        {
            MapObject obj = _ordinalObjects[ordinal];
            if (managed.Contains(obj) || !elevationOf.TryGetValue(obj, out int currentElevation))
                continue; // party/dismissed are taken above, not drifted
            (int tile, int rotation, int elevation0) = _pristinePositions[ordinal];
            if (obj.HexTile != tile || obj.Rotation != rotation || currentElevation != elevation0)
                delta.MovedOrdinals.Add(new SaveState.MovedObject(
                    ordinal, obj.HexTile, currentElevation, obj.Rotation));
        }

        // Snapshot containers that hold something now OR were script-stocked
        // at map_enter — an empty snapshot is what keeps looted ones looted.
        // Corpses count as containers (their loot must not resurrect).
        foreach ((MapObject obj, int ordinal) in _objectOrdinals)
        {
            if (!present.Contains(obj) || managed.Contains(obj))
                continue; // party/dismissed carry their own inventory outside the delta
            if (obj.IsDead && Fid.PidType(obj.Pid) == (int)ObjectType.Critter)
                delta.DeadOrdinals.Add(ordinal);
            if (obj.Inventory.Count > 0 || _stockedOrdinals.Contains(ordinal)
                || (obj.IsDead && Fid.PidType(obj.Pid) == (int)ObjectType.Critter))
                delta.ContainerInventories[ordinal] =
                    [.. obj.Inventory.Select(i => new SaveState.SavedItem(i.Pid, Math.Max(i.StackCount, 1),
                        i.Flags & (MapObject.FlagInLeftHand | MapObject.FlagInRightHand | MapObject.FlagWorn), i.AmmoQuantity, i.AmmoTypePid))];
        }

        _visitedMaps[_map.Header.Name] = delta;
    }

    /// <summary>Pre-map_enter delta replay: MVARs scripts read, and removal of
    /// taken objects (their scripts must not run, like absent .SAV objects).</summary>
    private void ApplyDeltaBeforeScripts(SaveState.MapDelta delta)
    {
        for (int i = 0; i < delta.MapVars.Length && i < _map.GlobalVariables.Length; i++)
            _map.GlobalVariables[i] = delta.MapVars[i];

        // P71: restore the automap fog (the explored-tile set was cleared on map teardown).
        // SpawnDude's RevealAround then re-adds the spawn area on top, so a revisit shows
        // everywhere you'd been plus where you arrive.
        _seenTiles.UnionWith(delta.SeenTiles);

        foreach (int ordinal in delta.TakenOrdinals)
        {
            if (ordinal < 0 || ordinal >= _ordinalObjects.Length)
                continue;
            MapObject taken = _ordinalObjects[ordinal];
            foreach (MapElevation? elev in _map.Elevations)
                elev?.Objects.Remove(taken);
            foreach (List<MapObject> list in _flatObjects.Concat(_solidObjects))
                list.Remove(taken);
        }

        // Drifted objects settle into their saved spots BEFORE map_enter,
        // like objects loading from a .SAV.
        foreach (SaveState.MovedObject moved in delta.MovedOrdinals)
        {
            if (moved.Ordinal < 0 || moved.Ordinal >= _ordinalObjects.Length
                || moved.Elevation is < 0 or >= MapFile.ElevationCount
                || _map.Elevations[moved.Elevation] is not { } targetElev)
                continue;
            MapObject obj = _ordinalObjects[moved.Ordinal];

            foreach (List<MapObject> list in _flatObjects.Concat(_solidObjects))
                list.Remove(obj);
            foreach (MapElevation? elev in _map.Elevations)
                elev?.Objects.Remove(obj);

            obj.HexTile = moved.Tile;
            obj.Rotation = moved.Rotation;
            targetElev.Objects.Add(obj);
            if (!obj.IsHidden && Fid.Type(obj.Fid) is not ObjectType.Head && obj.HexTile >= 0)
                InsertSorted(obj.IsFlat ? _flatObjects[moved.Elevation] : _solidObjects[moved.Elevation], obj);
        }

        // Dead critters: scripts removed BEFORE map_enter (the engine nulls
        // the sid on death and .SAV reloads keep it — combat.cc:4876), so
        // their procs never run again.
        foreach (int ordinal in delta.DeadOrdinals)
        {
            if (ordinal < 0 || ordinal >= _ordinalObjects.Length)
                continue;
            MapObject dead = _ordinalObjects[ordinal];
            dead.Sid = -1;
            dead.CombatResults |= 0x80; // DAM_DEAD
            dead.CurrentHp = Math.Min(dead.CurrentHp, 0);
        }
    }

    /// <summary>Post-map_enter delta replay: door states, created objects,
    /// and container snapshots (overwriting whatever map_enter restocked).</summary>
    private void ApplyDeltaAfterScripts(SaveState.MapDelta delta)
    {
        foreach (SaveState.SavedDoor saved in delta.Doors)
        {
            MapObject? door = _solidObjects.SelectMany(list => list)
                .FirstOrDefault(o => o.HexTile == saved.HexTile && o.Pid == saved.Pid);
            if (door is null)
                continue;
            door.IsLockedState = saved.Locked;
            SetDoorState(door, saved.Open);
        }

        foreach (SaveState.CreatedObject created in delta.Created)
        {
            if (created.Elevation is < 0 or >= MapFile.ElevationCount
                || _map.Elevations[created.Elevation] is not { } elev
                || RebuildObject(created.Pid, created.Count) is not { } obj)
                continue;
            obj.HexTile = created.Tile;
            elev.Objects.Add(obj);
            if (!obj.IsHidden && Fid.Type(obj.Fid) is not ObjectType.Head && obj.HexTile >= 0)
                InsertSorted(obj.IsFlat ? _flatObjects[created.Elevation] : _solidObjects[created.Elevation], obj);
        }

        // Corpse conversion replay (no fall animation on revisit — the body
        // is long cold).
        foreach (int ordinal in delta.DeadOrdinals)
        {
            if (ordinal < 0 || ordinal >= _ordinalObjects.Length)
                continue;
            MapObject dead = _ordinalObjects[ordinal];
            if (Fid.AnimType(dead.Fid) == 0) // not yet converted
                ConvertToCorpse(dead, PickDeathAnim(dead));
        }

        // A script-stocked merchant container restocks from pristine data once
        // its snapshot is older than the window: skip the stale snapshot and
        // keep the fresh map_enter stock (the box's own caps + goods). World
        // loot (footlockers) is never script-stocked, so it always honors its
        // snapshot — a looted chest stays looted.
        int daysElapsed = delta.SnapshotDay > 0 ? _clock.Day - delta.SnapshotDay : 0;
        foreach ((int ordinal, List<SaveState.SavedItem> items) in delta.ContainerInventories)
        {
            if (ordinal < 0 || ordinal >= _ordinalObjects.Length)
                continue;
            if (daysElapsed >= RestockDays && _stockedOrdinals.Contains(ordinal))
            {
                Console.WriteLine($"restock: ordinal {ordinal} refreshed ({daysElapsed}d since snapshot)");
                continue;
            }

            MapObject container = _ordinalObjects[ordinal];
            container.Inventory.Clear();
            foreach (SaveState.SavedItem item in items)
            {
                if (RebuildObject(item.Pid, item.Count) is { } obj)
                {
                    obj.Flags |= item.Flags;
                    obj.AmmoQuantity = item.AmmoQuantity;
                    obj.AmmoTypePid = item.AmmoTypePid;
                    container.Inventory.Add(obj);
                }
            }
        }

        RebuildBlockedTiles(_dude?.Dude);
    }

    /// <summary>Reinstantiates a serialized object from its prototype (deltas
    /// keep only pid + count); null for unknown/broken pids.</summary>
    private MapObject? RebuildObject(int pid, int count)
    {
        try
        {
            ProtoInfo proto = _protos.Get(pid);
            var obj = new MapObject
            {
                Id = -4,
                HexTile = -1,
                X = 0,
                Y = 0,
                Frame = 0,
                Rotation = 0,
                Fid = proto.Fid,
                Flags = 0,
                Pid = pid,
                Sid = -1,
            };
            obj.StackCount = Math.Max(count, 1);
            if (proto.MiscCharges > 0)
                obj.AmmoQuantity = proto.MiscCharges; // created MISC items start full (proto.cc:765; P116, review H)
            return obj;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            Console.Error.WriteLine($"load: dropping unknown pid 0x{pid:X8}: {ex.Message}");
            return null;
        }
    }

    /// <summary>P116 (review "car trunk"): serialize the trunk storage — syncing any open trunk
    /// panel first — sparse (null when empty, old-save shaped).</summary>
    private List<Formats.SaveState.SavedItem>? SnapshotTrunk()
    {
        CommitTrunk();
        List<MapObject> items = _scriptHost?.Car.TrunkItems ?? [];
        return items.Count == 0 ? null
            : [.. items.Select(i => new Formats.SaveState.SavedItem(i.Pid, Math.Max(i.StackCount, 1),
                i.Flags & (MapObject.FlagInLeftHand | MapObject.FlagInRightHand | MapObject.FlagWorn),
                i.AmmoQuantity, i.AmmoTypePid))];
    }

    private void SaveGame()
    {
        CaptureMapDelta();
        SyncDismissedToRoster(); // fold the current map's live dismissed bodies into the roster
        var state = new SaveState
        {
            Version = SaveState.CurrentVersion,
            Map = _currentMapName,
            DudeTile = _dude?.Dude.HexTile ?? _map.Header.EnteringTile,
            DudeRotation = _dude?.Dude.Rotation ?? 0,
            DudeLevel = _dudeLevel,
            DudeXp = _dudeXp,
            DudeHp = _dude?.Dude.CurrentHp ?? -1,
            DudePoison = _dude is { Dude.Poison: > 0 } pd ? pd.Dude.Poison : null, // P35-M3 (sparse: null when not poisoned)
            // P37: persist the active drug bonus + pending wear-off kicks, sparse (null when no drug active).
            DrugBonus = _drugBonus.Any(b => b != 0) ? [.. _drugBonus] : null,
            PendingDrugs = _pendingDrugEvents.Count > 0
                ? [.. _pendingDrugEvents.Select(e => new SaveState.PendingDrug(e.FireTick, e.Stats, e.Amounts))]
                : null,
            // P38: the active withdrawal penalty + pending onset/recovery events (addiction GVARs ride GlobalVars).
            WithdrawalBonus = _withdrawalBonus.Any(b => b != 0) ? [.. _withdrawalBonus] : null,
            PendingWithdrawals = _pendingWithdrawalEvents.Count > 0
                ? [.. _pendingWithdrawalEvents.Select(e => new SaveState.PendingWithdrawal(e.FireTick, e.IsStart, e.Pid, e.Perk))]
                : null,
            KillsByType = _killsByType.Any(k => k != 0) ? [.. _killsByType] : null, // P38 (sparse: null when no kills)
            UnspentSkillPoints = _unspentSkillPoints,
            Character = _activeCharacter,
            DudeSkills = _dudeGcd is not null ? [.. _dudeGcd.Stats.Skills] : null,
            // Only persist perk ranks when something was taken (sparse; a fresh game saves null,
            // which loads as no perks — old-save compatible).
            DudePerkRanks = _dudePerkRanks.Any(r => r > 0) ? [.. _dudePerkRanks] : null,
            // P30 A-M2: persist the sneak state, sparse (null when not sneaking → old-save compatible).
            SneakFlag = _sneak.FlagSet ? true : null,
            SneakWorking = _sneak.Working ? true : null,
            // P31 B-M3: karma/reputation PC-stats, sparse (null at 0 → old-save compatible).
            DudeKarma = _dudeKarma != 0 ? _dudeKarma : null,
            DudeReputation = _dudeReputation != 0 ? _dudeReputation : null,
            DudeActiveHand = _activeHand != MapObject.FlagInRightHand ? _activeHand : null, // P81 (sparse: null = right)
            DudeBaseStats = _dudeGcd is not null ? [.. _dudeGcd.Stats.BaseStats] : null,
            DudeTaggedSkills = _dudeGcd is not null ? [.. _dudeGcd.TaggedSkills] : null,
            DudeName = _dudeGcd?.Name, // P121: the typed chargen name round-trips

            Elevation = _elevation,
            ClockTicks = _clock.Ticks,
            GlobalVars = new Dictionary<int, int>(_scriptHost?.GlobalVars ?? []),
            DudeInventory = [.. _dudeInventory.Select(i => new SaveState.SavedItem(i.Pid, i.StackCount, i.Flags & (MapObject.FlagInLeftHand | MapObject.FlagInRightHand | MapObject.FlagWorn), i.AmmoQuantity, i.AmmoTypePid))],
            VisitedMaps = new Dictionary<string, SaveState.MapDelta>(_visitedMaps),
            DismissedCompanions = _dismissedByMap.ToDictionary(kv => kv.Key, kv => new List<SaveState.DismissedCompanion>(kv.Value)),
            // Drop transient (saved=No) maps' LVAR slices: their sids are reallocated
            // fresh each visit, so saved slices would be orphaned dead weight (phase-10 M3).
            LocalVars = (_scriptHost?.ExportAllLocalVars() ?? [])
                .Where(kv => !_mapList.IsTransient(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value),
            Party = [.. (_scriptHost?.PartyMembers ?? []).Select(m => new SaveState.PartyMemberState(
                m.Pid, _partyScriptIndex.GetValueOrDefault(m, -1), m.CurrentHp, m.Team, m.AiPacket,
                m.Inventory.Select(i => new SaveState.SavedItem(i.Pid, Math.Max(i.StackCount, 1),
                    i.Flags & (MapObject.FlagInLeftHand | MapObject.FlagInRightHand | MapObject.FlagWorn),
                    i.AmmoQuantity, i.AmmoTypePid)).ToList(),
                Waiting: _waitingCompanions.Contains(m),
                OriginalTeam: _originalTeam.GetValueOrDefault(m, m.Team),
                LevelUpLevel: _companionLevelState.GetValueOrDefault(m)?.Level ?? 0,
                LevelUpNumLevelUps: _companionLevelState.GetValueOrDefault(m)?.NumLevelUps ?? 0,
                LevelUpIsEarly: _companionLevelState.GetValueOrDefault(m)?.IsEarly ?? 0,
                PerkRanks: _companionPerkRanks.GetValueOrDefault(m), // P29-M6 (null on the slice)
                Disposition: (int)CompanionSettings(m).Disposition, // P50 combat-control settings
                AttackWho: (int)CompanionSettings(m).AttackWho,
                Distance: (int)CompanionSettings(m).Distance,
                RunAway: (int)CompanionSettings(m).RunAway,
                ChemUse: (int)CompanionSettings(m).ChemUse,
                AreaAttack: (int)CompanionSettings(m).AreaAttack,
                WeaponPref: (int)CompanionSettings(m).WeaponPref))],
            WorldPosX = _worldPosX,
            WorldPosY = _worldPosY,
            CurrentAreaId = _currentAreaId,
            CarInCar = _scriptHost?.Car.InCar ?? false, // P100 (bucket 1): the Highwayman car state
            CarFuel = _scriptHost?.Car.Fuel ?? Formats.CarState.FuelMax,
            CarAreaId = _scriptHost?.Car.CurrentAreaId ?? -1,
            // P116 (review "car trunk"): sync any open trunk panel first, then persist the storage.
            TrunkItems = SnapshotTrunk(),
            TrunkMaxSize = _scriptHost is not null && _scriptHost.Car.TrunkMaxSize != 100
                ? _scriptHost.Car.TrunkMaxSize : null,
            TravelDestinationAreaId = _activeTravel?.Dest.Index ?? -1, // in-flight leg target (P17-M4)
            // _worldmap (not Worldmap): only export if worldmap.txt was actually
            // touched this session — never force-parse it just to save.
            EncounterCounters = _worldmap?.ExportCounters() ?? [],
            // _worldFog (not WorldFog): only export explored subtiles if the fog was
            // touched (any travel) — a fresh game saves an empty dict (P22).
            RevealedSubtiles = _worldFog?.Export() ?? [],
        };
        state.Save(SavePath);
        Log($"Game saved ({Path.GetFileName(SavePath)}).");
        Console.WriteLine($"saved: map={state.Map} tile={state.DudeTile} clock={state.ClockTicks} items={state.DudeInventory.Count} maps={state.VisitedMaps.Count} L{state.DudeLevel} xp={state.DudeXp} hp={state.DudeHp} worldPos=({state.WorldPosX},{state.WorldPosY}) area={state.CurrentAreaId} encCounters={state.EncounterCounters.Count}");
    }

    private void LoadGame()
    {
        SaveState? state = SaveState.Load(SavePath);
        if (state is null)
        {
            Log("No saved game found.");
            return;
        }

        // Ordinal-keyed deltas make cross-version saves silently corrupting —
        // refuse anything but an exact match.
        if (state.Version != SaveState.CurrentVersion)
        {
            Log($"Save is from an incompatible version ({state.Version}, need {SaveState.CurrentVersion}).");
            Console.WriteLine($"load refused: save version {state.Version} != {SaveState.CurrentVersion}");
            return;
        }

        _clock.Ticks = state.ClockTicks;
        _lastAmbientHour = -1;
        if (_scriptHost is not null)
        {
            _scriptHost.GlobalVars.Clear();
            foreach ((int key, int value) in state.GlobalVars)
                _scriptHost.GlobalVars[key] = value;

            // LVARs must be in place BEFORE map_enter runs on the restored
            // map — scripts gate their one-time work on them.
            _scriptHost.ClearAllLocalVars();
            _scriptHost.ClearScriptVms(); // stale in-memory VM globals must not leak into the loaded game
            foreach ((string mapName, Dictionary<int, int[]> slices) in state.LocalVars)
                _scriptHost.ImportLocalVars(mapName, slices);
        }

        _visitedMaps.Clear();
        foreach ((string mapName, SaveState.MapDelta delta) in state.VisitedMaps)
            _visitedMaps[mapName] = delta;

        // Worldmap whereabouts + consumed one-shot encounter counters (phase-10
        // M2). Drop the parsed worldmap so the restore starts from PRISTINE
        // counters, exactly like StartNewGame — ImportCounters is a sparse delta
        // (only changed tables), so without this reset a one-shot the abandoned
        // session spent would leak past an F9 reload into a save that left it
        // pristine. Nulling preserves the lazy parse: an empty (Count==0) save
        // leaves _worldmap unparsed; a non-empty one re-parses clean, then
        // applies only the saved deltas.
        _worldPosX = state.WorldPosX;
        _worldPosY = state.WorldPosY;
        _currentAreaId = state.CurrentAreaId;
        if (_scriptHost is not null) // P100 (bucket 1): restore the Highwayman car state
        {
            _scriptHost.Car.InCar = state.CarInCar;
            _scriptHost.Car.Fuel = state.CarFuel;
            _scriptHost.Car.CurrentAreaId = state.CarAreaId;
            // P116 (review "car trunk"): restore the trunk storage (null on old saves = empty).
            _scriptHost.Car.TrunkItems.Clear();
            foreach (SaveState.SavedItem item in state.TrunkItems ?? [])
                if (RebuildObject(item.Pid, item.Count) is { } obj)
                {
                    obj.AmmoQuantity = item.AmmoQuantity;
                    obj.AmmoTypePid = item.AmmoTypePid;
                    _scriptHost.Car.TrunkItems.Add(obj);
                }
            if (state.TrunkMaxSize is { } trunkMax)
                _scriptHost.SetTrunkMaxSize(trunkMax);
            _trunkObject = null; // rebuilt lazily against the restored list on next open
        }
        _worldmap = null;
        _worldFog = null; // re-create against the freshly parsed worldmap, then import the save
        if (state.EncounterCounters.Count > 0)
            Worldmap.ImportCounters(state.EncounterCounters);
        // Restore explored worldmap subtiles (P22 fog). Like the counters: a non-empty save
        // forces the lazy fog to materialise (against pristine Worldmap) and imports the deltas;
        // an empty save leaves it unmaterialised (a fresh all-UNKNOWN fog on first access).
        if (state.RevealedSubtiles.Count > 0)
            WorldFog.Import(state.RevealedSubtiles);

        // Mid-travel state (P17-M4): drop any stale in-flight leg (its Bresenham cursor is
        // meaningless after a reload) + a pending avoid prompt. If the save was taken mid-
        // walk, queue an auto-resume toward the saved destination (the P16-M2 machinery) —
        // a documented divergence from the engine's drop-stopped reload.
        _activeTravel = null;
        _encounterPrompt = null;
        _resumeTravelDest = state.TravelDestinationAreaId >= 0
            ? _cities.Areas.FirstOrDefault(a => a.Index == state.TravelDestinationAreaId)
            : null;

        ResetParty();

        // Dismissed companions (P10 #3): restore the per-map roster AFTER ResetParty
        // (which cleared it) and BEFORE LoadMap, so the loaded map's are injected.
        foreach ((string mapName, List<SaveState.DismissedCompanion> roster) in state.DismissedCompanions)
            _dismissedByMap[mapName] = roster;

        // captureOutgoing: false — the pre-load world must not leak into the
        // freshly imported VisitedMaps. transient: a saved=No map (a save taken
        // mid-encounter) reloads pristine — and per the documented rule we then drop
        // the player back on the worldmap at the saved worldPos, not mid-ambush.
        bool savedOnTransient = _mapList.IsTransient(state.Map);
        _isLoadingGame = true; // gate kill_critter_type during the restored map's script replay
        try
        {
            LoadMap(state.Map, new MapDestination(0, state.DudeTile, state.Elevation, state.DudeRotation),
                captureOutgoing: false, transient: savedOnTransient);
        }
        finally { _isLoadingGame = false; }
        if (savedOnTransient)
            _worldmapOpen = true;

        // Progression: rebuild the sheet from the saved base stats + tags +
        // skills (self-contained — works for created characters); fall back to
        // reloading the named premade for older saves. Then replay level HP.
        _activeCharacter = string.IsNullOrEmpty(state.Character) ? "player" : state.Character;
        _dudeLevel = Math.Max(state.DudeLevel, 1);
        _dudeXp = state.DudeXp;
        _unspentSkillPoints = state.UnspentSkillPoints;

        if (state.DudeBaseStats is { Length: 35 } savedBase)
        {
            _dudeGcd = new Formats.Combat.GcdFile
            {
                Stats = new Formats.Proto.CritterProtoStats(0, 0, 0,
                    [.. savedBase], new int[35], state.DudeSkills is { Length: 18 } s ? [.. s] : new int[18],
                    0, 0, 0, 0),
                Name = state.DudeName // P121: prefer the saved chargen name; old saves fall back
                    ?? (_activeCharacter == "custom" ? "Wanderer" : _dudeGcd?.Name ?? "Wanderer"),
                TaggedSkills = state.DudeTaggedSkills is { Length: 4 } t ? [.. t] : [-1, -1, -1, -1],
                Traits = [-1, -1],
            };
        }
        else
        {
            string sheetPath = $@"premade\{_activeCharacter}.gcd";
            if (_dudeGcd is not null && _vfs.Exists(sheetPath))
            {
                using Stream gcdStream = _vfs.OpenRead(sheetPath);
                _dudeGcd = Formats.Combat.GcdFile.Load(gcdStream);
            }
            if (_dudeGcd is not null && state.DudeSkills is { Length: 18 } savedSkills)
                Array.Copy(savedSkills, _dudeGcd.Stats.Skills, 18);
        }

        // Restore perk ranks (P28-M2); null/short save → no perks (inert). BEFORE the HP recompute so
        // the per-level Lifegiver bonus (P75-M3) can read the restored rank.
        _dudePerkRanks = new int[Formats.Perks.PerkTable.Count];
        if (state.DudePerkRanks is { } savedPerks)
            Array.Copy(savedPerks, _dudePerkRanks, Math.Min(savedPerks.Length, _dudePerkRanks.Length));

        if (_dudeGcd is not null)
        {
            int endurance = _dudeGcd.Stats.BaseStats[Formats.Combat.CritterStat.Endurance];
            // P75-M3: Lifegiver adds 4 HP/rank per level-up (stat.cc:771). The recompute reconstructs the
            // level-up HP from level, so it must include Lifegiver or a reload would lose it. DOCUMENTED
            // SIMPLIFICATION: it assumes the perk was held since level 1 (like the existing uniform-EN
            // assumption) — a mid-game pick over-applies a few HP on reload.
            int perLevel = Formats.Combat.Progression.HpPerLevel(endurance, DudePerkRank(Formats.Perks.PerkId.Lifegiver));
            _dudeGcd.Stats.BonusStats[Formats.Combat.CritterStat.MaximumHitPoints] += (_dudeLevel - 1) * perLevel;
        }

        // Restore the sneak state (P30 A-M2); null on a pre-P30 save → not sneaking.
        _sneak.FlagSet = state.SneakFlag ?? false;
        _sneak.Working = state.SneakWorking ?? false;

        // Restore karma/reputation (P31 B-M3); null on a pre-P31 save → 0.
        _dudeKarma = state.DudeKarma ?? 0;
        _dudeReputation = state.DudeReputation ?? 0;
        _activeHand = state.DudeActiveHand ?? MapObject.FlagInRightHand; // P81: null/old save → right hand
        if (_dude is not null)
        {
            _dude.Dude.CurrentHp = state.DudeHp > 0
                ? state.DudeHp
                : GetCritterState(_dude.Dude)?.MaxHp ?? _dude.Dude.CurrentHp;
            _dude.Dude.Poison = state.DudePoison ?? 0; // P35-M3: restore poison + re-derive the tick schedule
            SchedulePoison();
        }

        // Rebuild the dude's bag from prototypes; worn armor re-applies its
        // bonus stats over the freshly reloaded sheet.
        _dudeInventory.Clear();
        foreach (SaveState.SavedItem item in state.DudeInventory)
        {
            if (RebuildObject(item.Pid, item.Count) is { } obj)
            {
                obj.Flags |= item.Flags;
                obj.AmmoQuantity = item.AmmoQuantity;
                obj.AmmoTypePid = item.AmmoTypePid;
                _dudeInventory.Add(obj);
                if (obj.IsWorn)
                {
                    try
                    {
                        if (_protos.Get(obj.Pid).Armor is { } armor)
                            ApplyArmorBonus(armor, +1);
                    }
                    catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
                    {
                    }
                }
            }
        }

        // P37: restore the active drug bonus AFTER the base+armor sheet rebuild above (the drug
        // contribution is NOT in the base block, so re-apply it here or the pending wear-off would
        // drive the stat negative). Then restore the pending wear-off kicks (they fire on the clock).
        Array.Clear(_drugBonus);
        _pendingDrugEvents.Clear();
        if (state.DrugBonus is { } drugBonus && _dudeGcd is not null)
            for (int s = 0; s < 35 && s < drugBonus.Length; s++)
            {
                _drugBonus[s] = drugBonus[s];
                _dudeGcd.Stats.BonusStats[s] += drugBonus[s];
            }
        if (state.PendingDrugs is { } pending)
            foreach (SaveState.PendingDrug e in pending)
                _pendingDrugEvents.Add((e.FireTick, e.Stats, e.Amounts));

        // P38: restore the withdrawal penalty the same way (re-apply AFTER the sheet rebuild) + the
        // pending onset/recovery events. The addiction GVARs themselves ride GlobalVars (restored above).
        Array.Clear(_withdrawalBonus);
        _pendingWithdrawalEvents.Clear();
        if (state.WithdrawalBonus is { } wdBonus && _dudeGcd is not null)
            for (int s = 0; s < 35 && s < wdBonus.Length; s++)
            {
                _withdrawalBonus[s] = wdBonus[s];
                _dudeGcd.Stats.BonusStats[s] += wdBonus[s];
            }
        if (state.PendingWithdrawals is { } pendingWd)
            foreach (SaveState.PendingWithdrawal e in pendingWd)
                _pendingWithdrawalEvents.Add((e.FireTick, e.IsStart, e.Pid, e.Perk));

        // P38: restore the kill tally (sparse-null on a pre-P38 / no-kills save).
        _killsByType = new int[19];
        if (state.KillsByType is { } kills)
            Array.Copy(kills, _killsByType, Math.Min(kills.Length, _killsByType.Length));

        // Rebuild the companions and stand them next to the dude.
        if (_scriptHost is not null)
        {
            foreach (SaveState.PartyMemberState saved in state.Party)
            {
                if (RebuildObject(saved.Pid, 1) is not { } member)
                    continue;
                member.CurrentHp = saved.Hp;
                member.Team = saved.Team;
                member.AiPacket = saved.AiPacket;
                foreach (SaveState.SavedItem item in saved.Inventory)
                {
                    if (RebuildObject(item.Pid, item.Count) is { } obj)
                    {
                        obj.Flags |= item.Flags;
                        obj.AmmoQuantity = item.AmmoQuantity;
                        obj.AmmoTypePid = item.AmmoTypePid;
                        member.Inventory.Add(obj);
                    }
                }

                _scriptHost.PartyMembers.Add(member);
                if (saved.ScriptListIndex >= 0)
                    _partyScriptIndex[member] = saved.ScriptListIndex;
                // P29-M6: restore per-companion perk ranks (null/empty on the slice → nothing to do).
                if (saved.PerkRanks is { Length: > 0 })
                    _companionPerkRanks[member] = saved.PerkRanks;
                // P50: restore the combat-control disposition (old saves default to CompanionAi.Default
                // via the record's ctor defaults → SetCompanionAi clears it → byte-identical).
                SetCompanionAi(member, new Formats.Combat.CompanionAi(
                    (Formats.Combat.Disposition)saved.Disposition, (Formats.Combat.AttackWho)saved.AttackWho,
                    (Formats.Combat.Distance)saved.Distance, (Formats.Combat.RunAway)saved.RunAway,
                    (Formats.Combat.ChemUse)saved.ChemUse,
                    (Formats.Combat.AreaAttack)saved.AreaAttack, (Formats.Combat.WeaponPref)saved.WeaponPref));
                // Restore the companion control state (phase-10 #2): the "wait here"
                // flag and the pre-recruit team so a later dismiss restores it (not 0).
                if (saved.Waiting)
                    _waitingCompanions.Add(member);
                if (saved.OriginalTeam >= 0)
                    _originalTeam[member] = saved.OriginalTeam;

                // Restore the proto level-up bookkeeping (#10 M3) and re-apply the
                // stage proto as the stat override, so a levelled companion comes back
                // with the right stats (HP already restored from saved.Hp above).
                if (saved.LevelUpLevel > 0 || saved.LevelUpNumLevelUps > 0)
                {
                    _companionLevelState[member] = new Formats.Party.PartyLevelUpState
                    {
                        Level = saved.LevelUpLevel,
                        NumLevelUps = saved.LevelUpNumLevelUps,
                        IsEarly = saved.LevelUpIsEarly,
                    };
                    if (saved.LevelUpLevel > 0 && PartyTable()?.ForPid(saved.Pid) is { } desc
                        && saved.LevelUpLevel <= desc.LevelPids.Count)
                    {
                        try
                        {
                            if (_protos.Get(desc.LevelPids[saved.LevelUpLevel - 1]).Critter is { } stageStats)
                                _companionStatOverride[member] = stageStats;
                        }
                        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException) { }
                    }
                }
            }
            InjectPartyMembers();
        }

        Log("Game loaded.");
        Console.WriteLine($"loaded: map={state.Map} tile={state.DudeTile} clock={state.ClockTicks} items={_dudeInventory.Count} maps={_visitedMaps.Count} L{_dudeLevel} xp={_dudeXp} hp={_dude?.Dude.CurrentHp} worldPos=({_worldPosX},{_worldPosY}) area={_currentAreaId} encCounters={state.EncounterCounters.Count}");
    }
}
