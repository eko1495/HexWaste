namespace FalloutPoc.Formats.Text;

/// <summary>
/// Fallout .msg files: entries of the form <c>{id}{audio}{text}</c>, ported
/// from fallout2-ce src/message.cc _message_load_field(): fields are read
/// between braces with newlines stripped (multi-line text collapses), and
/// anything outside braces (comments, whitespace) is ignored.
/// </summary>
public sealed class MessageFile
{
    private readonly Dictionary<int, string> _texts = [];

    public int Count => _texts.Count;

    public string? GetText(int id) => _texts.TryGetValue(id, out string? text) ? text : null;

    public static MessageFile Load(Stream stream)
    {
        var result = new MessageFile();
        // Game text is single-byte (cp1252-ish); Latin1 keeps bytes intact.
        using var reader = new StreamReader(stream, System.Text.Encoding.Latin1);
        string content = reader.ReadToEnd();

        int position = 0;
        while (true)
        {
            string? idField = ReadField(content, ref position);
            if (idField is null)
                break;
            string? audioField = ReadField(content, ref position);
            string? textField = ReadField(content, ref position);
            if (audioField is null || textField is null)
                break;

            if (int.TryParse(idField.Trim(), out int id))
                result._texts[id] = textField;
        }

        return result;
    }

    private static string? ReadField(string content, ref int position)
    {
        int open = content.IndexOf('{', position);
        if (open < 0)
            return null;
        int close = content.IndexOf('}', open + 1);
        if (close < 0)
            return null;

        position = close + 1;
        return content[(open + 1)..close].Replace("\n", "").Replace("\r", "");
    }
}
