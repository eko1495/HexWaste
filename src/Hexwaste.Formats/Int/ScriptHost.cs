using Hexwaste.Formats.Map;
using Hexwaste.Formats.Text;

namespace Hexwaste.Formats.Int;

/// <summary>
/// Runs object scripts in the micro INT VM with the engine's script-context
/// protocol (phase-4 M0): object handle table, source/target/dude context,
/// LVAR slices (lazily allocated zeroed per script like map.cc
/// _map_malloc_local_var — pristine maps store offset -1), MVARs into the
/// map's global block, and session-level GVARs. Any VM failure falls back to
/// non-scripted behavior — scripts are an enhancement, never a crash.
/// </summary>
public sealed class ScriptHost(GameFileSystem vfs, ScriptList scripts, Hexwaste.Formats.Proto.ProtoDatabase protos)
{
    private readonly Dictionary<string, IntProgram?> _programs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, MessageFile?> _dialogMessages = [];
    private readonly Dictionary<int, int> _globalVars = [];

    /// <summary>Lazily allocated LVAR slices per (map NAME, sid) — the engine
    /// appends zeroed slices to the map array on first access
    /// (scripts.cc:2805/2836). Keyed by the header map name so slices survive
    /// pristine reloads (in-session persistence) and dead MapFile instances
    /// are never pinned (the phase-5 measured leak).</summary>
    private readonly Dictionary<(string Map, int Sid), int[]> _localVarSlices = [];

    // Object handle table: scripts see objects as opaque ints; 0 = null.
    private readonly List<MapObject> _handles = [];
    private readonly Dictionary<MapObject, int> _handleByObject = [];

    public int HandleOf(MapObject? obj)
    {
        if (obj is null)
            return 0;
        if (_handleByObject.TryGetValue(obj, out int handle))
            return handle;
        _handles.Add(obj);
        handle = _handles.Count; // 1-based
        _handleByObject[obj] = handle;
        return handle;
    }

    public MapObject? ObjectOf(int handle) =>
        handle >= 1 && handle <= _handles.Count ? _handles[handle - 1] : null;

    /// <summary>Resolves object names for the VM (set by the host application).</summary>
    public Func<MapObject, string>? NameResolver { get; set; }

    /// <summary>Resolves whether a door/container is currently open (set by the host).</summary>
    public Func<MapObject, bool>? IsOpenResolver { get; set; }

    /// <summary>Applies a script-driven open/close (animate, unblock — set by the host).</summary>
    public Action<MapObject, bool>? OpenStateChanged { get; set; }

    /// <summary>Diagnostic sink for arity-stubbed externals.</summary>
    public Action<string>? OnStubbedExternal { get; set; }

    /// <summary>An object was placed on the map by a script (add to draw lists/blocking).</summary>
    public Action<MapObject, MapFile>? ObjectPlaced { get; set; }

    /// <summary>An object was removed from the map by a script.</summary>
    public Action<MapObject>? ObjectRemoved { get; set; }

    /// <summary>The prototype database (item icons, fids for created objects).</summary>
    public Hexwaste.Formats.Proto.ProtoDatabase Protos => protos;

    /// <summary>Game clock backing the game_time externals (host-provided).</summary>
    public Func<long>? ClockTicks { get; set; }

    /// <summary>maps.txt index of the current map (cur_map_index; host-provided).</summary>
    public Func<int>? CurrentMapIndexProvider { get; set; }

    /// <summary>Sink for messages produced outside interactive runs (timer float text).</summary>
    public Action<string>? OnScriptMessage { get; set; }

    /// <summary>A script requested a walk animation (animate_move_obj_to_tile).</summary>
    public Action<MapObject, int>? MoveRequested { get; set; }

    /// <summary>Stat-block override (the dude's gcd sheet); null falls back to
    /// the critter's prototype.</summary>
    public Func<MapObject, Proto.CritterProtoStats?>? StatsResolver { get; set; }

    /// <summary>A script attacked: (attacker = the script's self, target).
    /// The host starts/joins combat (opAttackComplex → scriptsRequestCombat).</summary>
    public Action<MapObject, MapObject>? AttackRequested { get; set; }

    /// <summary>anim_busy: is this object mid-animation (host animator)?</summary>
    public Func<MapObject, bool>? AnimBusyResolver { get; set; }

    /// <summary>give_exp_points: the host adds XP immediately (pcAddExperience).</summary>
    public Action<int>? ExpAwarded { get; set; }

    /// <summary>override_map_start: (tile, elevation, rotation) — the host
    /// repositions the dude + camera during map_enter.</summary>
    public Action<int, int, int>? MapStartOverridden { get; set; }

    /// <summary>play_gmovie: the host shows a caption card for the movie id.</summary>
    public Action<int>? MoviePlayed { get; set; }

    /// <summary>critter_damage: (victim, amount, bypassArmor) — the host
    /// applies HP loss and the death path.</summary>
    public Action<MapObject, int, bool>? CritterDamaged { get; set; }

    /// <summary>The party roster (engine party.cc list, minimum cut): scripts
    /// add/remove; party_member_obj answers by pid; the host carries members
    /// across maps.</summary>
    public List<MapObject> PartyMembers { get; } = [];

    /// <summary>A script recruited (true) or dismissed (false) this critter.</summary>
    public Action<MapObject, bool>? PartyChanged { get; set; }

    /// <summary>Runtime sid for a script-created object (engine scr_new): a
    /// fresh type-3 sid registered into the map's script table.</summary>
    public int AllocateSid(MapFile map, int scriptIndex)
    {
        int sid = 0x03000000 | 0x00800000; // synthetic range, clear of map sids
        while (map.ScriptsBySid.ContainsKey(sid))
            sid++;
        map.ScriptsBySid[sid] = new MapScriptRecord(scriptIndex, -1, 0);
        return sid;
    }

    /// <summary>
    /// Spatial triggers, ported from fallout2-ce scripts.cc
    /// scriptsExecSpatialProc(): exact built-tile match OR hex distance
    /// within radius, exact elevation. self = a lazily created hidden object
    /// at the trap tile; source = the mover. Disabled around first-run
    /// map_enter like _scr_SpatialsEnabled (map.cc:973).
    /// </summary>
    public bool SpatialsEnabled { get; set; } = true;

    private readonly Dictionary<(string Map, int Sid), MapObject> _spatialSelves = [];

    public void RunSpatialsAt(MapFile map, int tile, int elevation, MapObject mover)
    {
        if (!SpatialsEnabled || mover.IsHidden || mover.IsFlat || tile < 10)
            return;

        foreach (MapFile.SpatialScript spatial in map.SpatialScripts)
        {
            if (spatial.Elevation != elevation)
                continue;
            bool hit = spatial.Radius <= 0
                ? spatial.Tile == tile
                : Hex.HexGrid.Distance(spatial.Tile, tile) <= spatial.Radius;
            if (!hit)
                continue;

            if (!_spatialSelves.TryGetValue((map.Header.Name, spatial.Sid), out MapObject? self))
            {
                self = new MapObject
                {
                    Id = -5,
                    HexTile = spatial.Tile,
                    X = 0,
                    Y = 0,
                    Frame = 0,
                    Rotation = 0,
                    Fid = Fid.Build(ObjectType.Misc, 12),
                    Flags = 0x01, // hidden
                    Pid = 0x05000010,
                    Sid = spatial.Sid,
                };
                _spatialSelves[(map.Header.Name, spatial.Sid)] = self;
            }

            if (!map.ScriptsBySid.ContainsKey(spatial.Sid))
                map.ScriptsBySid[spatial.Sid] = new MapScriptRecord(spatial.ScriptListIndex, -1, 0);

            ScriptRunResult? result = RunProc(spatial.ScriptListIndex, map, spatial.Sid,
                map.ScriptsBySid[spatial.Sid], self, mover, 0, -1, ["spatial_p_proc"]);
            if (result is not null)
                foreach (string line in result.Messages)
                    OnScriptMessage?.Invoke(line);
        }
    }

    /// <summary>Cross-script external variables (export.cc) — one per session;
    /// shop scripts pass their stock boxes through these.</summary>
    public ExternalVariables ExternalVars { get; } = new();

    /// <summary>The dude's two selected traits (gcd), -1 = none.</summary>
    public int[] DudeTraits { get; set; } = [-1, -1];

    /// <summary>get_pc_stat values (1=level, 2=experience); host-provided.</summary>
    public Func<int, int>? PcStatProvider { get; set; }

    /// <summary>Rolls for do_check/statRoll (seedable by the host).</summary>
    public Random Rng { get; set; } = new();

    /// <summary>Effective stat, ported from fallout2-ce src/stat.cc
    /// critterGetStat(): base + bonus; pseudostats 35/36/37 read the instance.</summary>
    public int CritterStatValue(MapObject obj, int stat)
    {
        switch (stat)
        {
            case 35: // STAT_CURRENT_HIT_POINTS
                return obj.CurrentHp;
            case 36: // STAT_CURRENT_POISON_LEVEL
                return obj.Poison;
            case 37: // STAT_CURRENT_RADIATION_LEVEL
                return obj.Radiation;
        }

        if (stat is < 0 or > 34)
            return -1;

        Proto.CritterProtoStats? stats = StatsOf(obj);
        return stats is null ? -1 : stats.BaseStats[stat] + stats.BonusStats[stat];
    }

    internal Proto.CritterProtoStats? StatsOf(MapObject obj)
    {
        if (StatsResolver?.Invoke(obj) is { } overridden)
            return overridden;
        if (Fid.PidType(obj.Pid) != 1)
            return null;
        try
        {
            return protos.Get(obj.Pid).Critter;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            return null;
        }
    }

    // ---- script timer queue, ported from fallout2-ce queue.cc/scripts.cc:
    // absolute due time, sorted, stable FIFO for equal times. Delays are game
    // ticks at the ENGINE rate (10/s real time, 100 ms per tick) — independent
    // of any accelerated day/night clock. The engine drops all script timers
    // on map exit (_queue_leaving_map) — call ClearTimers() on transitions.

    private sealed record TimerEntry(double DueMs, MapFile Map, MapObject Owner, int Param);

    private readonly List<TimerEntry> _timers = [];
    private double _timerClockMs;

    public int PendingTimerCount => _timers.Count;

    public void AddTimer(MapFile map, MapObject owner, int delayTicks, int param)
    {
        double due = _timerClockMs + Math.Max(delayTicks, 0) * 100.0;
        int index = _timers.FindIndex(t => t.DueMs > due); // insert after equal times
        var entry = new TimerEntry(due, map, owner, param);
        if (index < 0)
            _timers.Add(entry);
        else
            _timers.Insert(index, entry);
    }

    public void RemoveTimers(MapObject owner, int? param = null) =>
        _timers.RemoveAll(t => t.Owner == owner && (param is null || t.Param == param));

    public void ClearTimers() => _timers.Clear();

    public const int MoneyPid = 41; // PROTO_ID_MONEY (proto_types.h:139)

    /// <summary>Caps in an inventory (item.cc itemGetTotalCaps, sans container recursion).</summary>
    public int CapsTotal(MapObject obj) =>
        obj.Inventory.Where(i => i.Pid == MoneyPid).Sum(i => i.StackCount);

    /// <summary>ported from fallout2-ce item.cc itemCapsAdjust(): -1 when
    /// removing more than the total; adding creates a money stack.</summary>
    public int CapsAdjust(MapObject obj, int amount)
    {
        if (amount >= 0)
        {
            if (amount == 0)
                return 0;
            if (obj.Inventory.FirstOrDefault(i => i.Pid == MoneyPid) is { } stack)
            {
                stack.StackCount += amount;
                return 0;
            }

            try
            {
                var money = new MapObject
                {
                    Id = -5,
                    HexTile = -1,
                    X = 0,
                    Y = 0,
                    Frame = 0,
                    Rotation = 0,
                    Fid = Protos.Get(MoneyPid).Fid,
                    Flags = 0,
                    Pid = MoneyPid,
                    Sid = -1,
                };
                money.StackCount = amount;
                obj.Inventory.Add(money);
                return 0;
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
            {
                Console.Error.WriteLine($"caps_adjust: {ex.Message}");
                return -1;
            }
        }

        int toRemove = -amount;
        if (CapsTotal(obj) < toRemove)
            return -1;

        foreach (MapObject stackEntry in obj.Inventory.Where(i => i.Pid == MoneyPid).ToList())
        {
            int take = Math.Min(stackEntry.StackCount, toRemove);
            stackEntry.StackCount -= take;
            toRemove -= take;
            if (stackEntry.StackCount <= 0)
                obj.Inventory.Remove(stackEntry);
            if (toRemove == 0)
                break;
        }

        return 0;
    }

    /// <summary>
    /// Advances the timer clock and runs due timed_event_p_procs. The caller
    /// gates this like the engine does (not during dialog/loot — scripts arm
    /// timers mid-conversation expecting them to fire after it closes).
    /// </summary>
    public void PumpTimers(double elapsedMs, MapObject? dude)
    {
        _timerClockMs += elapsedMs;
        while (_timers.Count > 0 && _timers[0].DueMs <= _timerClockMs)
        {
            TimerEntry entry = _timers[0];
            _timers.RemoveAt(0);
            ScriptRunResult? result = RunObjectProc(entry.Owner, entry.Map, dude,
                entry.Param, -1, "timed_event_p_proc");
            if (result is not null && OnScriptMessage is not null)
                foreach (string message in result.Messages)
                    OnScriptMessage(message);
        }
    }

    /// <summary>Session GVARs, exposed for save/load.</summary>
    public Dictionary<int, int> GlobalVars => _globalVars;

    /// <summary>
    /// Runs the object's description_p_proc (falling back to look_at_p_proc).
    /// Returns the display_msg lines when the script overrides the default
    /// description; null otherwise.
    /// </summary>
    public IReadOnlyList<string>? GetScriptedDescription(MapObject obj, MapFile map, MapObject? dude)
    {
        ScriptRunResult? result = RunObjectProc(obj, map, dude, "description_p_proc", "look_at_p_proc");
        return result is { Overridden: true, Messages.Count: > 0 } ? result.Messages : null;
    }

    public sealed record ScriptRunResult(bool Overridden, List<string> Messages);

    /// <summary>
    /// Runs the first procedure (by name) the object's script defines, with
    /// full context. Returns null when the object has no script / no such
    /// proc / the VM fails (soft fallback).
    /// </summary>
    public ScriptRunResult? RunObjectProc(MapObject obj, MapFile map, MapObject? dude,
        params string[] procedureNames) =>
        RunObjectProc(obj, map, dude, 0, -1, procedureNames);

    public ScriptRunResult? RunObjectProc(MapObject obj, MapFile map, MapObject? dude,
        int fixedParam, int actionBeingUsed, params string[] procedureNames)
    {
        if (obj.Sid == -1 || !map.ScriptsBySid.TryGetValue(obj.Sid, out MapScriptRecord? record))
            return null;

        return RunProc(record.ScriptListIndex, map, obj.Sid, record, obj, dude,
            fixedParam, actionBeingUsed, procedureNames);
    }

    /// <summary>
    /// use item ON object, ported from fallout2-ce proto_instance.cc:1245
    /// _obj_use_item_on(): the ITEM's use_obj_on_p_proc runs first (self =
    /// item, usedWith = target); unless it overrides, the TARGET's proc runs
    /// (self = target, usedWith = item). Returns the merged result, or null
    /// when neither side has a script.
    /// </summary>
    public ScriptRunResult? RunUseObjOn(MapObject item, MapObject target, MapFile map, MapObject? dude)
    {
        var messages = new List<string>();
        bool overridden = false;
        bool ranAny = false;

        if (item.Sid != -1 && map.ScriptsBySid.TryGetValue(item.Sid, out MapScriptRecord? itemRecord))
        {
            ScriptRunResult? result = RunProcWith(itemRecord.ScriptListIndex, map, item.Sid, itemRecord,
                self: item, dude, usedWith: target, "use_obj_on_p_proc");
            if (result is not null)
            {
                ranAny = true;
                messages.AddRange(result.Messages);
                overridden = result.Overridden;
            }
        }

        if (!overridden && target.Sid != -1 && map.ScriptsBySid.TryGetValue(target.Sid, out MapScriptRecord? targetRecord))
        {
            ScriptRunResult? result = RunProcWith(targetRecord.ScriptListIndex, map, target.Sid, targetRecord,
                self: target, dude, usedWith: item, "use_obj_on_p_proc");
            if (result is not null)
            {
                ranAny = true;
                messages.AddRange(result.Messages);
                overridden |= result.Overridden;
            }
        }

        return ranAny ? new ScriptRunResult(overridden, messages) : null;
    }

    private ScriptRunResult? RunProcWith(int scriptListIndex, MapFile map, int sid, MapScriptRecord record,
        MapObject self, MapObject? dude, MapObject usedWith, string procedureName)
    {
        string? path = scripts.GetScriptPath(scriptListIndex);
        if (path is null)
            return null;
        try
        {
            IntProgram? program = GetProgram(path);
            if (program is null)
                return null;
            var externals = new ScriptContext(this, map, sid, record, self, source: dude, dude: dude)
            {
                UsedWith = usedWith,
            };
            var vm = new IntVm(program, externals, OnStubbedExternal, ExternalVars);
            return vm.TryRunProcedure(procedureName)
                ? new ScriptRunResult(externals.Overridden, externals.Messages)
                : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or NotSupportedException)
        {
            Console.Error.WriteLine($"script {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Runs map-entry scripts like fallout2-ce map.cc:952-975 +
    /// scripts.cc scriptExecMapEnterScripts(): the MAP script first
    /// (scripts.lst index = header.ScriptIndex - 1) with fixedParam =
    /// first-run flag, then every scripted object's map_enter_p_proc.
    /// </summary>
    public void RunMapEnter(MapFile map, IEnumerable<MapObject> objects, MapObject? dude,
        bool? firstRunOverride = null)
    {
        int firstRun = firstRunOverride.HasValue
            ? (firstRunOverride.Value ? 1 : 0)
            : ((map.Header.Flags & 0x01) == 0 ? 1 : 0);
        _firstRunByMap[map.Header.Name] = firstRun == 1;

        if (map.Header.ScriptIndex > 0)
        {
            // The map script has no real owner object; synthesize one.
            var owner = new MapObject
            {
                Id = -2,
                HexTile = 1,
                X = 0,
                Y = 0,
                Frame = 0,
                Rotation = 0,
                Fid = Fid.Build(ObjectType.Misc, 12),
                Flags = 0,
                Pid = 0x05000010,
                Sid = -1,
            };
            var record = new MapScriptRecord(map.Header.ScriptIndex - 1, -1, 0);
            RunProc(record.ScriptListIndex, map, sid: -2, record, owner, dude,
                firstRun, -1, ["map_enter_p_proc"]);
        }

        // Snapshot: map_enter scripts create objects (container stocking),
        // mutating the underlying lists mid-iteration.
        foreach (MapObject obj in objects.ToList())
            RunObjectProc(obj, map, dude, firstRun, -1, "map_enter_p_proc");
    }

    /// <summary>Revisit tracking: metarule 14 FIRST_RUN consults this.</summary>
    private readonly Dictionary<string, bool> _firstRunByMap = [];

    internal bool IsFirstRun(MapFile map) =>
        _firstRunByMap.TryGetValue(map.Header.Name, out bool firstRun)
            ? firstRun
            : (map.Header.Flags & 0x01) == 0;

    private ScriptRunResult? RunProc(int scriptListIndex, MapFile map, int sid, MapScriptRecord record,
        MapObject self, MapObject? dude, int fixedParam, int actionBeingUsed, string[] procedureNames)
    {
        string? path = scripts.GetScriptPath(scriptListIndex);
        if (path is null)
            return null;

        try
        {
            IntProgram? program = GetProgram(path);
            if (program is null)
                return null;

            var externals = new ScriptContext(this, map, sid, record, self: self, source: dude, dude: dude)
            {
                FixedParamValue = fixedParam,
                ActionBeingUsedValue = actionBeingUsed,
            };
            var vm = new IntVm(program, externals, OnStubbedExternal, ExternalVars);
            foreach (string name in procedureNames)
            {
                if (vm.TryRunProcedure(name))
                    return new ScriptRunResult(externals.Overridden, externals.Messages);
            }

            return null;
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or NotSupportedException)
        {
            Console.Error.WriteLine($"script {path}: {ex.Message}");
            return null;
        }
    }

    private IntProgram? GetProgram(string path)
    {
        if (_programs.TryGetValue(path, out IntProgram? cached))
            return cached;
        IntProgram? program = vfs.Exists(path) ? IntProgram.Load(vfs.ReadAllBytes(path)) : null;
        _programs[path] = program;
        return program;
    }

    private string LookupMessage(int messageListId, int messageId)
    {
        if (!_dialogMessages.TryGetValue(messageListId, out MessageFile? messages))
        {
            string? path = scripts.GetDialogMessagePath(messageListId);
            messages = path is not null && vfs.Exists(path)
                ? LoadMessages(path)
                : null;
            _dialogMessages[messageListId] = messages;
        }

        return messages?.GetText(messageId) ?? "";
    }

    private MessageFile LoadMessages(string path)
    {
        using Stream stream = vfs.OpenRead(path);
        return MessageFile.Load(stream);
    }

    private int[] GetLocalVarSlice(MapFile map, int sid, MapScriptRecord record)
    {
        if (_localVarSlices.TryGetValue((map.Header.Name, sid), out int[]? slice))
            return slice;

        int count = record.LocalVarsCount > 0
            ? record.LocalVarsCount
            : scripts.GetLocalVarsCount(record.ScriptListIndex);
        slice = new int[Math.Max(count, 0)];

        // Saved maps (.SAV) carry real offsets into the serialized block —
        // seed the slice from it so saved state is honored when present.
        if (record.LocalVarsOffset >= 0)
        {
            for (int i = 0; i < slice.Length && record.LocalVarsOffset + i < map.LocalVariables.Length; i++)
                slice[i] = map.LocalVariables[record.LocalVarsOffset + i];
        }

        _localVarSlices[(map.Header.Name, sid)] = slice;
        return slice;
    }

    /// <summary>Clears the object handle table — call on map transitions
    /// (handles never outlive a VM run / dialog session).</summary>
    public void ResetHandles()
    {
        _handles.Clear();
        _handleByObject.Clear();
    }

    /// <summary>LVAR slices of one map, for save serialization.</summary>
    public Dictionary<int, int[]> ExportLocalVars(string mapName) =>
        _localVarSlices.Where(kv => kv.Key.Map == mapName)
            .ToDictionary(kv => kv.Key.Sid, kv => (int[])kv.Value.Clone());

    /// <summary>All maps' LVAR slices (save serialization).</summary>
    public Dictionary<string, Dictionary<int, int[]>> ExportAllLocalVars() =>
        _localVarSlices.GroupBy(kv => kv.Key.Map)
            .ToDictionary(g => g.Key, g => g.ToDictionary(kv => kv.Key.Sid, kv => (int[])kv.Value.Clone()));

    public void ImportLocalVars(string mapName, Dictionary<int, int[]> slices)
    {
        foreach ((int sid, int[] values) in slices)
            _localVarSlices[(mapName, sid)] = (int[])values.Clone();
    }

    public void ClearAllLocalVars() => _localVarSlices.Clear();

    /// <summary>
    /// A running conversation: the same VM + context persist across option
    /// picks (LVARs/program globals keep their state), exactly like
    /// game_dialog.cc _gdProcess: show reply, pick option, run its bound
    /// procedure (which repopulates reply+options), end when a procedure
    /// leaves zero options.
    /// </summary>
    public sealed class DialogSession
    {
        private readonly IntVm _vm;
        private readonly ScriptContext _context;

        public string NpcName { get; }
        public string Reply => _context.DialogReplyText;
        public IReadOnlyList<string> Options => _context.DialogOptions.Select(o => o.Text).ToList();
        public bool Active { get; private set; } = true;

        internal DialogSession(IntVm vm, ScriptContext context, string npcName)
        {
            _vm = vm;
            _context = context;
            NpcName = npcName;
        }

        /// <summary>A picked option called gdialog_barter: the host should open
        /// the trade window now; the queued reply is already in place.</summary>
        public bool TakeBarterRequest(out int modifier) => _context.TakeBarterRequest(out modifier);

        /// <summary>The shopkeeper's live stock container (see ScriptContext.StockBox).</summary>
        public MapObject? StockBox => _context.StockBox;

        /// <summary>Picks an option (0-based). Returns false when the dialog has ended.</summary>
        public bool Choose(int optionIndex)
        {
            if (!Active || optionIndex < 0 || optionIndex >= _context.DialogOptions.Count)
                return Active;

            int procedureIndex = _context.DialogOptions[optionIndex].ProcedureIndex;
            _context.ResetDialogRound();

            try
            {
                _vm.TryRunProcedureByIndex(procedureIndex);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                Console.Error.WriteLine($"dialog proc {procedureIndex}: {ex.Message}");
                Active = false;
                return false;
            }

            if (_context.DialogOptions.Count == 0 || _context.SessionEnded)
                Active = false;
            return Active;
        }

        /// <summary>Out-of-band lines produced this round (float_msg, display_msg, barter notice).</summary>
        public IReadOnlyList<string> SideMessages => _context.Messages;
    }

    /// <summary>
    /// Opens a conversation with a scripted object via its talk_p_proc.
    /// Returns null when the object has no dialog (floater-only NPCs put
    /// their lines in <paramref name="floaters"/>).
    /// </summary>
    public DialogSession? StartDialog(MapObject obj, MapFile map, MapObject? dude, out IReadOnlyList<string> floaters)
    {
        floaters = [];
        if (obj.Sid == -1 || !map.ScriptsBySid.TryGetValue(obj.Sid, out MapScriptRecord? record))
            return null;

        string? path = scripts.GetScriptPath(record.ScriptListIndex);
        if (path is null)
            return null;

        try
        {
            IntProgram? program = GetProgram(path);
            if (program is null)
                return null;

            var context = new ScriptContext(this, map, obj.Sid, record, self: obj, source: dude, dude: dude);
            var vm = new IntVm(program, context, OnStubbedExternal, ExternalVars);
            if (!vm.TryRunProcedure("talk_p_proc"))
                return null;

            floaters = context.Messages;
            if (context.DialogOptions.Count == 0)
                return null; // floater-only NPC — no dialog window

            string npcName = NameResolver?.Invoke(obj) ?? "stranger";
            return new DialogSession(vm, context, npcName);
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or NotSupportedException)
        {
            Console.Error.WriteLine($"script {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Per-invocation script context, mirroring scriptExecProc's setup
    /// (scripts.cc:1261-1342): source/target/dude, fixedParam,
    /// actionBeingUsed, and a per-run overrides flag.
    /// </summary>
    internal sealed class ScriptContext : IVmExternals
    {
        private readonly ScriptHost _host;
        private readonly MapFile _map;
        private readonly int _sid;
        private readonly MapScriptRecord _record;
        private readonly MapObject _self;
        private readonly MapObject? _source;
        private readonly MapObject? _dude;

        public List<string> Messages { get; } = [];
        public bool Overridden { get; private set; }
        public int FixedParamValue { get; init; }
        public int ActionBeingUsedValue { get; init; } = -1;

        /// <summary>obj_being_used_with: the OTHER party of use_obj_on
        /// (target's proc sees the item; item's proc sees the target).</summary>
        public MapObject? UsedWith { get; init; }

        public int ObjectBeingUsedWithId() => _host.HandleOf(UsedWith);

        public ScriptContext(ScriptHost host, MapFile map, int sid, MapScriptRecord record,
            MapObject self, MapObject? source, MapObject? dude)
        {
            _host = host;
            _map = map;
            _sid = sid;
            _record = record;
            _self = self;
            _source = source;
            _dude = dude;
        }

        public void DisplayMessage(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
                Messages.Add(text.Trim());
        }

        public string GetMessage(int messageListId, int id) => _host.LookupMessage(messageListId, id);

        public void SetScriptOverrides() => Overridden = true;

        public int SelfObjectId() => _host.HandleOf(_self);

        public int SourceObjectId() => _host.HandleOf(_source);

        public int TargetObjectId() => _host.HandleOf(_self); // defaults to self (scripts.cc:1316)

        public int DudeObjectId() => _host.HandleOf(_dude);

        public int FixedParam() => FixedParamValue;

        public int ActionBeingUsed() => ActionBeingUsedValue;

        public string ObjectName(int objectHandle) =>
            _host.ObjectOf(objectHandle) is { } obj
                ? _host.NameResolver?.Invoke(obj) ?? "object"
                : "object";

        public int GetGlobalVar(int index) =>
            _host._globalVars.TryGetValue(index, out int value) ? value : 0;

        public void SetGlobalVar(int index, int value) => _host._globalVars[index] = value;

        public int GetLocalVar(int index)
        {
            int[] slice = _host.GetLocalVarSlice(_map, _sid, _record);
            return index >= 0 && index < slice.Length ? slice[index] : 0;
        }

        public void SetLocalVar(int index, int value)
        {
            int[] slice = _host.GetLocalVarSlice(_map, _sid, _record);
            if (index >= 0 && index < slice.Length)
                slice[index] = value;
        }

        public int GetMapVar(int index) =>
            index >= 0 && index < _map.GlobalVariables.Length ? _map.GlobalVariables[index] : 0;

        public void SetMapVar(int index, int value)
        {
            if (index >= 0 && index < _map.GlobalVariables.Length)
                _map.GlobalVariables[index] = value;
        }

        // metarule: 14 FIRST_RUN (host tracks revisits); 22 IS_LOADGAME = 0;
        // 49 WEAPON_DAMAGE_TYPE (the misc-10 explosion marker → EXPLOSION, for the
        // temple-door damage_p_proc); everything else 0.
        public int Metarule(int rule, int argument) => rule switch
        {
            14 => _host.IsFirstRun(_map) ? 1 : 0,
            49 => _host.ObjectOf(argument) is { } o
                  && o.Fid == Fid.Build(ObjectType.Misc, 10, 0, 0) ? 6 /* DAMAGE_TYPE_EXPLOSION */ : 0,
            _ => 0,
        };

        public int GameTime() => (int)(_host.ClockTicks?.Invoke() ?? 302400);

        // ---- timers + geometry + caps (phase-5 M0)

        public void AddTimerEvent(int objectHandle, int delayTicks, int param)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                _host.AddTimer(_map, obj, delayTicks, param);
        }

        public void RemoveTimerEvents(int objectHandle)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                _host.RemoveTimers(obj);
        }

        public void RemoveTimerEventsWithParam(int objectHandle, int param)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                _host.RemoveTimers(obj, param);
        }

        public int ObjTile(int objectHandle) => _host.ObjectOf(objectHandle)?.HexTile ?? -1;

        public int CurrentMapIndex() => _host.CurrentMapIndexProvider?.Invoke() ?? 0;

        public int CapsTotal(int objectHandle) =>
            _host.ObjectOf(objectHandle) is { } obj ? _host.CapsTotal(obj) : 0;

        public int CapsAdjust(int objectHandle, int amount) =>
            _host.ObjectOf(objectHandle) is { } obj ? _host.CapsAdjust(obj, amount) : -1;

        /// <summary>PoC sight: within 20 hexes, no wall LOS (engine uses PE*2 + obstacles).</summary>
        public bool ObjCanSee(int objectHandle, int targetHandle)
        {
            MapObject? source = _host.ObjectOf(objectHandle);
            MapObject? target = _host.ObjectOf(targetHandle);
            if (source is null || target is null)
                return false;
            return Hex.HexGrid.Distance(source.HexTile, target.HexTile) <= 20;
        }

        public void AnimateMoveToTile(int objectHandle, int tile, int speed)
        {
            if (_host.ObjectOf(objectHandle) is { } obj && Hex.HexGrid.IsValid(tile))
                _host.MoveRequested?.Invoke(obj, tile);
        }

        public int GetCritterStat(int objectHandle, int stat) =>
            _host.ObjectOf(objectHandle) is { } obj ? _host.CritterStatValue(obj, stat) : -1;

        // ported from fallout2-ce interpreter_extra.cc opSetCritterStat():
        // ADJUSTS the base stat; only the dude is modifiable.
        public int AdjustCritterBaseStat(int objectHandle, int stat, int amount)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj)
                return -1;
            if (obj != _dude || stat is < 0 or > 34)
                return -1;
            if (_host.StatsOf(obj) is not { } stats)
                return -1;
            stats.BaseStats[stat] += amount;
            return 0;
        }

        // ported from fallout2-ce interpreter_extra.cc opHasTrait()
        public int HasTrait(int type, int objectHandle, int param)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj)
                return 0;
            return type switch
            {
                0 => 0, // CRITTER_TRAIT_PERK — no perk system in the PoC
                1 => param switch // CRITTER_TRAIT_OBJECT
                {
                    5 => obj.AiPacket,
                    6 => obj.Team,
                    10 => obj.Rotation,
                    666 => obj.IsHidden ? 0 : 1,
                    669 => 0, // inventory weight — unweighted PoC
                    _ => 0,
                },
                2 => _host.DudeTraits.Contains(param) ? 1 : 0, // CRITTER_TRAIT_TRAIT
                _ => 0,
            };
        }

        // ported from fallout2-ce src/stat.cc statRoll(): d10 vs SPECIAL+mod
        // (opDoCheck restricts to the seven primary stats).
        public int DoCheck(int objectHandle, int stat, int modifier)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj || stat is < 0 or > 6)
                return 1; // ROLL_FAILURE
            int value = _host.CritterStatValue(obj, stat) + modifier;
            return _host.Rng.Next(1, 11) <= value ? 2 : 1; // ROLL_SUCCESS : ROLL_FAILURE
        }

        public int GetPcStat(int stat) => _host.PcStatProvider?.Invoke(stat) ?? 0;

        // ported from fallout2-ce interpreter_extra.cc opCritterAddTrait():
        // kind 1 = object traits; perks (kind 0) are out of PoC scope.
        public void CritterAddTrait(int objectHandle, int kind, int param, int value)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj || Fid.PidType(obj.Pid) != 1)
                return;
            if (kind != 1)
                return;
            switch (param)
            {
                case 5: // CRITTER_TRAIT_OBJECT_AI_PACKET
                    obj.AiPacket = value;
                    break;
                case 6: // CRITTER_TRAIT_OBJECT_TEAM
                    obj.Team = value;
                    break;
            }
        }

        // ported from fallout2-ce interpreter_extra.cc opAttackComplex():
        // inactive/hidden parties and fleeing targets are ignored.
        public void AttackComplex(int targetHandle)
        {
            if (_host.ObjectOf(targetHandle) is not { } target)
                return;
            if (_self.IsDead || _self.IsHidden || target.IsDead || target.IsHidden)
                return;
            _host.AttackRequested?.Invoke(_self, target);
        }

        public bool AnimBusy(int objectHandle) =>
            _host.ObjectOf(objectHandle) is { } obj && (_host.AnimBusyResolver?.Invoke(obj) ?? false);

        public void GiveExpPoints(int amount) => _host.ExpAwarded?.Invoke(amount);

        public void PlayMovie(int movieId) => _host.MoviePlayed?.Invoke(movieId);

        public void PartyAdd(int objectHandle)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj || _host.PartyMembers.Contains(obj))
                return;
            _host.PartyMembers.Add(obj);
            _host.PartyChanged?.Invoke(obj, true);
        }

        public void PartyRemove(int objectHandle)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj || !_host.PartyMembers.Remove(obj))
                return;
            _host.PartyChanged?.Invoke(obj, false);
        }

        public int PartyMemberByPid(int pid) =>
            _host.HandleOf(_host.PartyMembers.FirstOrDefault(m => m.Pid == pid));

        // ported from fallout2-ce interpreter_extra.cc opCritterDamage()
        public void CritterDamage(int objectHandle, int amount, int damageTypeWithFlags)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj || Fid.PidType(obj.Pid) != 1)
                return;
            _host.CritterDamaged?.Invoke(obj, amount, (damageTypeWithFlags & 0x100) != 0);
        }

        // ported from fallout2-ce interpreter_extra.cc opOverrideMapStart()
        public void OverrideMapStart(int x, int y, int elevation, int rotation)
        {
            int tile = 200 * y + x;
            if (Hex.HexGrid.IsValid(tile))
                _host.MapStartOverridden?.Invoke(tile, elevation, rotation);
        }

        // ---- dialog state (one "round" = one reply + its options)

        public string DialogReplyText { get; private set; } = "";
        public List<(string Text, int ProcedureIndex, int Reaction)> DialogOptions { get; } = [];
        public bool SessionEnded { get; private set; }

        public void ResetDialogRound()
        {
            DialogReplyText = "";
            DialogOptions.Clear();
            Messages.Clear();
        }

        public void DialogStart()
        {
            DialogReplyText = "";
            DialogOptions.Clear();
        }

        public void DialogReply(string text) => DialogReplyText = text;

        public void DialogOption(string text, int procedureIndex, int reaction) =>
            DialogOptions.Add((text, procedureIndex, reaction));

        public void DialogEnd()
        {
            // _gdialogGo: reply with no options auto-gets a "[Done]" exit.
            if (DialogReplyText.Length > 0 && DialogOptions.Count == 0)
                DialogOptions.Add(("[Done]", -1, 50));
        }

        public void DialogSessionEnd() => SessionEnded = true;

        public int DialogIntelligence() => 5;

        public void FloatMessage(int objectHandle, string text, int type)
        {
            if (!string.IsNullOrWhiteSpace(text))
                Messages.Add(text.Trim());
        }

        // ported from fallout2-ce game_dialog.cc gameDialogBarter(): the
        // opcode only flags the session; its arg OVERWRITES the modifier.
        public bool BarterRequested { get; private set; }
        public int BarterModifier { get; private set; }

        public void Barter(int modifier)
        {
            BarterModifier = modifier;
            BarterRequested = true;
        }

        public void GdialogSetBarterMod(int modifier) => BarterModifier = modifier;

        public bool TakeBarterRequest(out int modifier)
        {
            modifier = BarterModifier;
            bool requested = BarterRequested;
            BarterRequested = false;
            return requested;
        }

        // ported from fallout2-ce interpreter_extra.cc
        // opMoveObjectInventoryToObject(): everything moves, stacks merge.
        /// <summary>Where the talk script parked its stock: shopkeepers load
        /// goods from a box in the talk_p_proc prologue and return them in the
        /// epilogue — which, in our run-to-completion dialog model, has already
        /// executed by the time the trade window opens. The last container the
        /// npc moved its inventory INTO is the live stock.</summary>
        public MapObject? StockBox { get; private set; }

        public void MoveAllInventory(int sourceHandle, int targetHandle)
        {
            if (_host.ObjectOf(sourceHandle) is not { } source
                || _host.ObjectOf(targetHandle) is not { } target || source == target)
                return;

            if (source == _self && target != _dude)
                StockBox = target;

            foreach (MapObject item in source.Inventory.ToList())
            {
                if (target.Inventory.FirstOrDefault(i => i.Pid == item.Pid) is { } existing)
                    existing.StackCount += Math.Max(item.StackCount, 1);
                else
                    target.Inventory.Add(item);
            }

            source.Inventory.Clear();
        }

        // ---- door/container state (handle 0 no-ops like the engine)

        public bool ObjIsLocked(int objectHandle) =>
            _host.ObjectOf(objectHandle)?.IsLockedState ?? false;

        public void ObjSetLocked(int objectHandle, bool locked)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                obj.IsLockedState = locked;
        }

        public bool ObjIsOpen(int objectHandle) =>
            _host.ObjectOf(objectHandle) is { } obj && (_host.IsOpenResolver?.Invoke(obj) ?? false);

        public void ObjSetOpen(int objectHandle, bool open)
        {
            if (_host.ObjectOf(objectHandle) is { } obj)
                _host.OpenStateChanged?.Invoke(obj, open);
        }

        // ---- world mutation (phase-4 M3)

        public int CreateObject(int pid, int tile, int elevation, int scriptIndex = -1)
        {
            Proto.ProtoInfo proto;
            try
            {
                proto = _host.Protos.Get(pid);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
            {
                Console.Error.WriteLine($"create_object: bad pid 0x{pid:X8}: {ex.Message}");
                return 0;
            }

            var obj = new MapObject
            {
                Id = -3,
                HexTile = tile == -1 ? 0 : tile, // engine quirk: -1 coerced to 0
                X = 0,
                Y = 0,
                Frame = 0,
                Rotation = 0,
                Fid = proto.Fid,
                Flags = 0,
                Pid = pid,
                Sid = -1,
            };

            // Script binding (engine scr_new + scriptSetScriptIndex): allocate
            // a fresh sid so the new object's procs (use_skill_on disarm,
            // examine) actually run.
            if (scriptIndex >= 0)
            {
                int sid = _host.AllocateSid(_map, scriptIndex);
                obj.Sid = sid;
            }

            if (elevation is >= 0 and < MapFile.ElevationCount && _map.Elevations[elevation] is { } elev)
            {
                elev.Objects.Add(obj);
                _host.ObjectPlaced?.Invoke(obj, _map);
            }

            return _host.HandleOf(obj);
        }

        public void DestroyObject(int objectHandle)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj)
                return;

            foreach (MapElevation? elev in _map.Elevations)
                elev?.Objects.Remove(obj);
            foreach (MapElevation? elev in _map.Elevations)
                if (elev is not null)
                    foreach (MapObject holder in elev.Objects)
                        holder.Inventory.Remove(obj);
            _host.ObjectRemoved?.Invoke(obj);
        }

        public void AddToInventory(int targetHandle, int itemHandle, int quantity)
        {
            if (_host.ObjectOf(targetHandle) is not { } target || _host.ObjectOf(itemHandle) is not { } item)
                return;

            foreach (MapElevation? elev in _map.Elevations)
                elev?.Objects.Remove(item);
            _host.ObjectRemoved?.Invoke(item);

            // Merge stacks of the same prototype like itemAdd does.
            if (target.Inventory.FirstOrDefault(i => i.Pid == item.Pid) is { } existing)
                existing.StackCount += Math.Max(quantity, 1);
            else
            {
                item.StackCount = Math.Max(quantity, 1);
                target.Inventory.Add(item);
            }
        }

        public int RemoveFromInventory(int targetHandle, int itemHandle, int quantity)
        {
            if (_host.ObjectOf(targetHandle) is not { } target || _host.ObjectOf(itemHandle) is not { } item)
                return 0;

            MapObject? held = target.Inventory.FirstOrDefault(i => i == item || i.Pid == item.Pid);
            if (held is null)
                return 0;

            int removed = Math.Min(Math.Max(quantity, 1), held.StackCount);
            held.StackCount -= removed;
            if (held.StackCount <= 0)
                target.Inventory.Remove(held);
            return removed;
        }

        public int MoveTo(int objectHandle, int tile, int elevation)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj)
                return -1;

            foreach (MapElevation? elev in _map.Elevations)
                elev?.Objects.Remove(obj);
            obj.HexTile = tile;
            if (elevation is >= 0 and < MapFile.ElevationCount && _map.Elevations[elevation] is { } targetElev)
            {
                targetElev.Objects.Add(obj);
                _host.ObjectPlaced?.Invoke(obj, _map);
            }

            return tile;
        }

        public void SetObjectVisibility(int objectHandle, bool hidden)
        {
            if (_host.ObjectOf(objectHandle) is not { } obj || obj.IsHidden == hidden)
                return;

            obj.Flags = hidden ? obj.Flags | 0x01 : obj.Flags & ~0x01;
            if (hidden)
                _host.ObjectRemoved?.Invoke(obj);
            else
                _host.ObjectPlaced?.Invoke(obj, _map);
        }

        public int ObjPid(int objectHandle) => _host.ObjectOf(objectHandle)?.Pid ?? -1;

        public int TileContainsPidObj(int tile, int elevation, int pid)
        {
            if (elevation is < 0 or >= MapFile.ElevationCount || _map.Elevations[elevation] is not { } elev)
                return 0;
            MapObject? found = elev.Objects.FirstOrDefault(o => o.HexTile == tile && o.Pid == pid);
            return _host.HandleOf(found);
        }
    }
}
