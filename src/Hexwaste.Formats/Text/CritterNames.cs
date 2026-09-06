namespace Hexwaste.Formats.Text;

/// <summary>
/// Per-script critter display names (e.g. "Klint" for a tribesman running ACKlint.int),
/// ported from fallout2-ce src/critter.cc critterGetName(): a critter with a bound script
/// takes its name from <c>text\english\game\scrname.msg</c> at message id
/// <c>101 + scriptIndex</c> (scriptIndex = the script's 0-based line in scripts.lst), falling
/// back to the prototype's generic species name (<see cref="ProtoMessages"/>) when that lookup
/// misses — e.g. for critters with no bound script, or ambient/generic NPCs.
/// </summary>
public sealed class CritterNames(GameFileSystem vfs)
{
    private const int MessageIdBase = 101;
    private MessageFile? _messages;
    private bool _loaded;

    public string? GetName(int scriptListIndex)
    {
        MessageFile? messages = GetFile();
        if (messages is null)
            return null;

        try
        {
            return messages.GetText(MessageIdBase + scriptListIndex);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            return null;
        }
    }

    private MessageFile? GetFile()
    {
        if (_loaded)
            return _messages;

        _loaded = true;
        string path = Localization.Localize(@"text\english\game\scrname.msg"); // P131
        if (!vfs.Exists(path))
            return null;

        using Stream stream = vfs.OpenRead(path);
        return _messages = MessageFile.Load(stream);
    }
}
