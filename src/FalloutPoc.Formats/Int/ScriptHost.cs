using FalloutPoc.Formats.Map;
using FalloutPoc.Formats.Text;

namespace FalloutPoc.Formats.Int;

/// <summary>
/// Runs object scripts in the micro INT VM with the engine's script-context
/// protocol (phase-4 M0): object handle table, source/target/dude context,
/// LVAR slices (lazily allocated zeroed per script like map.cc
/// _map_malloc_local_var — pristine maps store offset -1), MVARs into the
/// map's global block, and session-level GVARs. Any VM failure falls back to
/// non-scripted behavior — scripts are an enhancement, never a crash.
/// </summary>
public sealed class ScriptHost(GameFileSystem vfs, ScriptList scripts)
{
    private readonly Dictionary<string, IntProgram?> _programs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, MessageFile?> _dialogMessages = [];
    private readonly Dictionary<int, int> _globalVars = [];

    /// <summary>Lazily allocated LVAR slices per (map, sid) — the engine appends
    /// zeroed slices to the map array on first access (scripts.cc:2805/2836).</summary>
    private readonly Dictionary<(MapFile Map, int Sid), int[]> _localVarSlices = [];

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

    /// <summary>Diagnostic sink for arity-stubbed externals.</summary>
    public Action<string>? OnStubbedExternal { get; set; }

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
        params string[] procedureNames)
    {
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

            var externals = new ScriptContext(this, map, obj.Sid, record, self: obj, source: dude, dude: dude);
            var vm = new IntVm(program, externals, OnStubbedExternal);
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
        if (_localVarSlices.TryGetValue((map, sid), out int[]? slice))
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

        _localVarSlices[(map, sid)] = slice;
        return slice;
    }

    /// <summary>
    /// Per-invocation script context, mirroring scriptExecProc's setup
    /// (scripts.cc:1261-1342): source/target/dude, fixedParam,
    /// actionBeingUsed, and a per-run overrides flag.
    /// </summary>
    private sealed class ScriptContext : IVmExternals
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

        // metarule: 14 FIRST_RUN = pristine map (MAP_SAVED bit clear);
        // 22 IS_LOADGAME = 0; everything else 0.
        public int Metarule(int rule, int argument) =>
            rule == 14 ? ((_map.Header.Flags & 0x01) == 0 ? 1 : 0) : 0;
    }
}
