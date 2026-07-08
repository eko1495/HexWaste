namespace Hexwaste.Formats.Int;

/// <summary>
/// The scripts.lst registry: line N (0-based) names script N, used to resolve
/// a map object's script file (scripts\&lt;name&gt;.int) and its dialog messages
/// (text\english\dialog\&lt;name&gt;.msg). ported from fallout2-ce
/// src/scripts.cc scriptsGetFileName() / scriptsGetMessageList() — note that
/// message-list ids passed to message_str are scripts.lst indices PLUS ONE.
/// </summary>
public sealed class ScriptList
{
    private readonly List<string> _names = [];
    private readonly List<int> _localVarsCounts = [];

    public int Count => _names.Count;

    public static ScriptList Load(GameFileSystem vfs)
    {
        var list = new ScriptList();
        using Stream stream = vfs.OpenRead(@"scripts\scripts.lst");
        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            string name = line.Split(';')[0].Trim();
            int dot = name.IndexOf('.');
            if (dot >= 0)
                name = name[..dot];
            list._names.Add(name); // keep blanks to preserve line indexing

            // Pristine maps zero localVarsCount; the engine re-derives it from
            // the "# local_vars=N" comment (scripts.cc _scr_find_str_run_info).
            int localVars = 0;
            int marker = line.IndexOf("local_vars=", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
            {
                string tail = line[(marker + "local_vars=".Length)..];
                int end = 0;
                while (end < tail.Length && char.IsDigit(tail[end]))
                    end++;
                _ = int.TryParse(tail[..end], out localVars);
            }
            list._localVarsCounts.Add(localVars);
        }

        return list;
    }

    /// <summary>Local-variable count for a script (scripts.lst "# local_vars=N").</summary>
    public int GetLocalVarsCount(int index) =>
        index >= 0 && index < _localVarsCounts.Count ? _localVarsCounts[index] : 0;

    public string? GetName(int index) =>
        index >= 0 && index < _names.Count && _names[index].Length > 0 ? _names[index] : null;

    public string? GetScriptPath(int index) =>
        GetName(index) is { } name ? $@"scripts\{name}.int" : null;

    /// <summary>Dialog .msg path for a message_str list id (1-based scripts.lst index).</summary>
    public string? GetDialogMessagePath(int messageListId) =>
        GetName(messageListId - 1) is { } name ? Localization.Localize($@"text\english\dialog\{name}.msg") : null; // P131
}
