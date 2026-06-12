using FalloutPoc.Formats;
using FalloutPoc.Formats.Int;
using FalloutPoc.Formats.Map;
using FalloutPoc.Formats.Text;

namespace FalloutPoc.Viewer;

/// <summary>
/// Runs object scripts' examine procedures in the micro INT VM.
/// look_at_p_proc / description_p_proc need exactly three load-bearing
/// externals (script_overrides, message_str, display_msg — see
/// phase3-research-report.md §5); everything else is arity-stubbed by the VM.
/// Any VM failure falls back to proto text — scripts are an enhancement,
/// never a crash.
/// </summary>
public sealed class ScriptHost(GameFileSystem vfs, ScriptList scripts)
{
    private readonly Dictionary<string, IntProgram?> _programs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, MessageFile?> _dialogMessages = [];

    /// <summary>
    /// Runs the object's description_p_proc (falling back to look_at_p_proc).
    /// Returns the display_msg lines when the script overrides the default
    /// description; null otherwise.
    /// </summary>
    public IReadOnlyList<string>? GetScriptedDescription(MapObject obj, MapFile map)
    {
        if (obj.Sid == -1 || !map.ScriptListIndexBySid.TryGetValue(obj.Sid, out int listIndex))
            return null;

        string? path = scripts.GetScriptPath(listIndex);
        if (path is null)
            return null;

        try
        {
            IntProgram? program = GetProgram(path);
            if (program is null)
                return null;

            var externals = new ExamineExternals(this, obj);
            var vm = new IntVm(program, externals);
            if (!vm.TryRunProcedure("description_p_proc") && !vm.TryRunProcedure("look_at_p_proc"))
                return null;

            return externals.Overridden && externals.Messages.Count > 0 ? externals.Messages : null;
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

    private sealed class ExamineExternals(ScriptHost host, MapObject self) : IVmExternals
    {
        public List<string> Messages { get; } = [];
        public bool Overridden { get; private set; }

        public void DisplayMessage(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
                Messages.Add(text.Trim());
        }

        public string GetMessage(int messageListId, int id) => host.LookupMessage(messageListId, id);

        public void SetScriptOverrides() => Overridden = true;

        public int SelfObjectId() => self.Id;

        public string ObjectName(int objectHandle) => "object";

        public int GetGlobalVar(int index) => 0;

        public int GetLocalVar(int index) => 0;

        public int GetMapVar(int index) => 0;
    }
}
