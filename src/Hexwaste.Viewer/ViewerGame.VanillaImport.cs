using Hexwaste.Formats.Map;
using Hexwaste.Formats.Save.Vanilla;

namespace Hexwaste.Viewer;

/// <summary>Trace oracle (M1): start from a vanilla fallout2-ce SAVE.DAT slot instead of a pristine
/// map. Mirrors what loadsave.cc lsgLoadGameInSlot() (:1699) leaves in memory for the state the M1
/// fixtures touch, in the engine's handler order (_PrepLoad clock → gvars → _SlotMap2Game map load with
/// map_enter → _obj_load_dude → critter/perks/pc stats/traits → party → queue). A thin adapter over the
/// JSON LoadGame()'s own downstream calls (LoadMap, the sheet/inventory fields, the timer/event queues).
/// Read-only: Hexwaste never writes vanilla saves.</summary>
public sealed partial class ViewerGame
{
    /// <summary>--load-vanilla-save &lt;slot dir&gt;: SAVE.DAT + the gzip'd &lt;MAP&gt;.SAV of one slot.</summary>
    public string? VanillaSaveDir { get; set; }

    // The stale dude script sid LoadMap's critter-list rebuild keeps (vanilla import only; -1 otherwise).
    private int _vanillaStaleDudeSid = -1;

    private void LoadVanillaSaveAtStartup()
    {
        string dir = VanillaSaveDir!;
        var ctx = VanillaSaveContext.FromGameData(_vfs, _protos);
        VanillaSave save;
        using (Stream s = SlotFiles.OpenMaybeGzip(Path.Combine(dir, "SAVE.DAT")))
            save = SaveDatReader.Read(s, ctx);
        ApplyVanillaSave(save, dir);
    }

    private void ApplyVanillaSave(VanillaSave save, string slotDir)
    {
        if (save.Combat.Active)
            throw new NotSupportedException("vanilla import: in-combat saves are M2");
        // partyMembersLoad (party_member.cc) re-links ids to objects already on the loaded map; the JSON
        // load's party path rebuilds members from protos instead, so there is nothing equivalent to reuse.
        if (save.PartyMemberIds.Any(id => id != save.Dude.Id))
            throw new NotSupportedException("vanilla import: party members are M2");

        // 1. _PrepLoad: game time (loadsave.cc:2416), then the gvars (scriptsLoadGameGlobalVars) — both
        //    are in place before the map's map_enter runs. Same writes as LoadGame().
        _clock.Ticks = save.Header.GameTime;
        _lastAmbientHour = -1;
        if (_scriptHost is not null)
        {
            _scriptHost.GlobalVars.Clear();
            for (int i = 0; i < save.GlobalVars.Length; i++)
                _scriptHost.GlobalVars[i] = save.GlobalVars[i];
            // LVARs come from the .SAV's own local-vars block (ScriptHost.GetLocalVarSlice seeds every
            // slice from map.LocalVariables at the record's offset) — so drop any cached slices first.
            _scriptHost.ClearAllLocalVars();
            _scriptHost.ClearScriptVms();
        }

        // 2. _SlotMap2Game → mapLoadSaved (map.cc:1054): the slot's in-game-saved map, NOT the pristine
        //    .MAP. Its header has flags & 1, so LoadMap skips the .gam MVAR override (map.cc:922-938) and
        //    map_enter runs with firstRun = 0 (RunMapEnter's flags default; no delta is registered).
        string savPath = Directory.GetFiles(slotDir)
            .FirstOrDefault(f => Path.GetFileName(f).Equals(save.Header.MapFileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException(
                $"vanilla import: {slotDir} has no {save.Header.MapFileName} (the map SAVE.DAT names)");
        MapFile saved;
        using (Stream s = SlotFiles.OpenMaybeGzip(savPath))
            saved = MapFile.Load(s, _protos);
        // The engine stamps the slot name (ARTEMPLE.SAV) into the header; Hexwaste keys its per-map state
        // (LVAR slices, run-once cache, visited deltas) by the pristine name (ARTEMPLE.MAP) — normalize.
        var map = new MapFile
        {
            Header = saved.Header with { Name = Path.ChangeExtension(saved.Header.Name, ".MAP") },
            GlobalVariables = saved.GlobalVariables,
            LocalVariables = saved.LocalVariables,
            Elevations = saved.Elevations,
        };
        foreach ((int sid, MapScriptRecord record) in saved.ScriptsBySid)
            map.ScriptsBySid[sid] = record;
        map.SpatialScripts.AddRange(saved.SpatialScripts);
        map.CritterSids.AddRange(saved.CritterSids);
        // Oracle F6: the .SAV keeps the dude's previous-session script record (obj_dude, owner = the dude)
        // in the critter section, so fo2ce runs obj_dude twice per round after a save load (findings F6).
        // fo2ce runs it as its own Script struct; Hexwaste runs the dude's obj_dude (owner.Sid = DudeSid).
        var heldSids = saved.Elevations.Where(e => e is not null).SelectMany(e => e!.Objects)
            .Select(o => o.Sid).ToHashSet();
        _vanillaStaleDudeSid = saved.CritterSids.FirstOrDefault(sid => !heldSids.Contains(sid)
            && saved.ScriptsBySid.TryGetValue(sid, out MapScriptRecord r) && r.ScriptListIndex == 0, -1); // 0 = obj_dude, as in BindDudeScript

        string mapName = Path.ChangeExtension(save.Header.MapFileName, ".map").ToLowerInvariant();
        _visitedMaps.Clear();
        ResetParty();
        _isLoadingGame = true; // engine _loadingGame gate (kill_critter_type), as in LoadGame()
        try
        {
            // The dude spawns straight onto his saved tile/rotation (_obj_load_dude re-places him after
            // map_enter; an explicit spawn also keeps override_map_start from moving him, P113).
            LoadMap(mapName, new MapDestination(0, save.Dude.HexTile, save.Header.Elevation, save.Dude.Rotation),
                captureOutgoing: false, transient: false, preloadedMap: map);
        }
        finally { _isLoadingGame = false; _vanillaStaleDudeSid = -1; }
        if (_dude is null)
            throw new InvalidDataException($"vanilla import: map {mapName} loaded without a dude");

        // 3. critterLoad / perksLoad / _pc_load_data / traitsLoad: the sheet, rebuilt like LoadGame()'s
        //    saved-base-stats branch. Vanilla bonusStats already hold level-up HP and the worn armor's
        //    AC/DT/DR (inventory.cc _adjust_ac writes them into the bonus block), so they are taken
        //    verbatim — no HpPerLevel replay and no ApplyArmorBonus.
        _activeCharacter = "custom";
        _dudeGcd = new Formats.Combat.GcdFile
        {
            Stats = new Formats.Proto.CritterProtoStats(0, 0, save.Critter.Flags,
                [.. save.Critter.BaseStats], [.. save.Critter.BonusStats], [.. save.Critter.Skills],
                save.Critter.BodyType, save.Critter.Experience, save.Critter.KillType, save.Critter.DamageType),
            Name = save.Header.CharacterName,
            TaggedSkills = [.. save.TaggedSkills],
            Traits = [.. save.Traits],
        };
        if (_scriptHost is not null)
            _scriptHost.DudeTraits = _dudeGcd.Traits;
        _dudeLevel = Math.Max(save.PcStats.Level, 1);
        _dudeXp = save.PcStats.Experience;
        _unspentSkillPoints = save.PcStats.UnspentSkillPoints;
        _dudeReputation = save.PcStats.Reputation;
        _dudeKarma = save.PcStats.Karma;
        _dudePerkRanks = new int[Formats.Perks.PerkTable.Count];
        int[] perks = save.PerksByDescription[0]; // description 0 = the dude (perk.cc perksLoad)
        Array.Copy(perks, _dudePerkRanks, Math.Min(perks.Length, _dudePerkRanks.Length));
        _killsByType = new int[19];
        Array.Copy(save.Kills, _killsByType, Math.Min(save.Kills.Length, _killsByType.Length));
        // dudeEnableState: DUDE_STATE_SNEAKING (0) is bit 0 of the dude proto's critter flags; critterLoad
        // restores _sneak_working beside it (critter.cc:203).
        _sneak.FlagSet = (save.Critter.Flags & 1) != 0;
        _sneak.Working = save.Critter.SneakWorking != 0;
        _activeHand = MapObject.FlagInRightHand;
        Array.Clear(_drugBonus);       // vanilla folds active drug/withdrawal effects into the stat
        Array.Clear(_withdrawalBonus); // blocks above; only their pending events (queue) are separate
        _pendingDrugEvents.Clear();
        _pendingWithdrawalEvents.Clear();
        _npcDrugBonus.Clear();
        _radEvents.Clear();

        // 4. _obj_load_dude (object.cc:3629): the saved dude object — id, fid, hp, rad/poison, combat
        //    data, inventory (items keep their saved ids) — then tileSetCenter(saved center tile).
        MapObject dude = _dude.Dude;
        MapObject from = save.Dude;
        dude.Id = from.Id;
        dude.Fid = from.Fid;
        dude.Frame = from.Frame;
        dude.CurrentHp = from.CurrentHp;
        dude.Radiation = from.Radiation;
        dude.Poison = from.Poison;
        dude.CombatResults = from.CombatResults;
        dude.DamageLastTurn = from.DamageLastTurn;
        dude.Maneuver = from.Maneuver;
        dude.Team = from.Team;
        dude.AiPacket = from.AiPacket;
        _dudeInventory.Clear(); // aliased to dude.Inventory by SpawnDude
        _dudeInventory.AddRange(from.Inventory);
        if (GetCritterState(dude) is { } sheet)
            _combat.SetDudeAp(sheet.MaxActionPoints);
        _hudDisplayedHp = _hudDisplayedAc = -1;
        SchedulePoison();
        _camera.SetCenter(save.DudeCenterTile);

        // Worldmap whereabouts only (the rest of the worldmap section is ignored for M1).
        _worldPosX = save.Worldmap.PosX;
        _worldPosY = save.Worldmap.PosY;
        _currentAreaId = save.Worldmap.CurrentArea;

        // 5. queueLoad (queue.cc:84): map each saved event onto the Hexwaste queue that models it.
        //    Tie order differs: fo2ce queueLoad re-inserts the pre-existing (map_enter-created) events
        //    AFTER saved events of equal time (queue.cc:171-184), so saved events win same-tick ties;
        //    Hexwaste appends the imported timers behind them. Recorded as finding F10
        //    (docs/research-notes/oracle-m1-findings-2026-10.md).
        int imported = ImportVanillaQueue(save.Queue, save.Dude.Id);

        int objects = _map.Elevations.Sum(e => e?.Objects.Count ?? 0);
        Console.Error.WriteLine($"vanilla-import: map={mapName} ({save.Header.MapFileName}) elev={_elevation} "
            + $"dude id={dude.Id} tile={dude.HexTile} rot={dude.Rotation} hp={dude.CurrentHp} center={save.DudeCenterTile} "
            + $"objects={objects} gvars={_scriptHost?.GlobalVars.Count ?? 0} mvars={_map.GlobalVariables.Length} "
            + $"lvars={_map.LocalVariables.Length} time={_clock.Ticks} L{_dudeLevel} xp={_dudeXp} "
            + $"inv={_dudeInventory.Count} queue={imported}/{save.Queue.Length}");
    }

    /// <summary>Maps vanilla queue events onto Hexwaste's event queues by absolute time. Events no
    /// Hexwaste queue models are reported (one stderr line each) and dropped — any consequence shows up
    /// as a trace divergence. Returns the number imported.</summary>
    private int ImportVanillaQueue(IReadOnlyList<QueueEventSave> queue, int dudeId)
    {
        int imported = 0;
        foreach (QueueEventSave e in queue)
        {
            bool dudeOwned = e.OwnerId == dudeId;
            switch (e.Type)
            {
                case 3 when _scriptHost is not null: // EVENT_TYPE_SCRIPT: data = { sid, fixedParam }
                {
                    // scriptEventProcess runs the event's SID (not its owner, whose id can repeat on map
                    // objects) — bind it to the object carrying that script.
                    int sid = e.Data[0];
                    MapObject? owner = _map.Elevations.Where(el => el is not null)
                        .SelectMany(el => el!.Objects).FirstOrDefault(o => o.Sid == sid);
                    if (owner is null)
                    {
                        Console.Error.WriteLine($"vanilla-import: skipped queue event type 3 (no object with sid 0x{sid:X8})");
                        continue;
                    }
                    _scriptHost.AddTimer(_map, owner, (int)Math.Max(0, e.Time - _clock.Ticks), e.Data[1]);
                    break;
                }
                case 12: // EVENT_TYPE_MAP_UPDATE_EVENT: the next map_update heartbeat lands on the saved time
                    // (it precedes the load-time +600 one, which mapUpdateEventProcess then clears).
                    _mapUpdateClockMs = Math.Max(0, MapUpdateIntervalMs - (e.Time - _clock.Ticks) * 100.0);
                    break;
                case 0 when dudeOwned: // EVENT_TYPE_DRUG: data = { stats[3], modifiers[3] } (item.cc:2886)
                    _pendingDrugEvents.Add((e.Time, null, e.Data[0..3], e.Data[3..6]));
                    break;
                case 2 when dudeOwned: // EVENT_TYPE_WITHDRAWAL: data = { field_0 (onset), pid, perk }
                    _pendingWithdrawalEvents.Add((e.Time, e.Data[0] != 0, e.Data[1], e.Data[2]));
                    break;
                case 5 when dudeOwned: // EVENT_TYPE_POISON: the next poison tick
                    _dudePoisonNextTick = e.Time;
                    break;
                case 6 when dudeOwned: // EVENT_TYPE_RADIATION: data = { radiationLevel, isHealing }
                    _radEvents.Add((e.Time, e.Data[0], e.Data[1] != 0));
                    break;
                default:
                    Console.Error.WriteLine($"vanilla-import: skipped queue event type {e.Type} (t={e.Time} owner={e.OwnerId})");
                    continue;
            }
            imported++;
        }
        return imported;
    }
}
