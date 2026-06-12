using FalloutPoc.Formats.Proto;

namespace FalloutPoc.Formats.Text;

/// <summary>
/// Object names and examine descriptions, ported from fallout2-ce
/// src/proto.cc protoGetMessage(): per object type there is a
/// <c>text\english\game\pro_&lt;type&gt;.msg</c> file; a prototype's name lives at
/// its MessageId and the description at MessageId + 1.
/// </summary>
public sealed class ProtoMessages(GameFileSystem vfs, ProtoDatabase protos)
{
    // ported from fallout2-ce src/proto.cc protoInit(): "pro_%.4s.msg" with
    // artGetObjectTypeName truncated to 4 chars.
    private static readonly string[] FileNames =
        ["pro_item.msg", "pro_crit.msg", "pro_scen.msg", "pro_wall.msg", "pro_tile.msg", "pro_misc.msg"];

    private readonly MessageFile?[] _files = new MessageFile?[FileNames.Length];

    public string? GetName(int pid) => GetMessage(pid, 0);

    public string? GetDescription(int pid) => GetMessage(pid, 1);

    private string? GetMessage(int pid, int offset)
    {
        int type = Fid.PidType(pid);
        if (type < 0 || type >= FileNames.Length)
            return null;

        MessageFile? messages = GetFile(type);
        if (messages is null)
            return null;

        try
        {
            return messages.GetText(protos.Get(pid).MessageId + offset);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            return null;
        }
    }

    private MessageFile? GetFile(int type)
    {
        if (_files[type] is { } cached)
            return cached;

        string path = $@"text\english\game\{FileNames[type]}";
        if (!vfs.Exists(path))
            return null;

        using Stream stream = vfs.OpenRead(path);
        return _files[type] = MessageFile.Load(stream);
    }
}
