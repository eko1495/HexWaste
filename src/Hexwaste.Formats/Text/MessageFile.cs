namespace Hexwaste.Formats.Text;

/// <summary>
/// Fallout .msg files: entries of the form <c>{id}{audio}{text}</c>, ported
/// from fallout2-ce src/message.cc _message_load_field(): fields are read
/// between braces with newlines stripped (multi-line text collapses), and
/// anything outside braces (comments, whitespace) is ignored.
/// </summary>
public sealed class MessageFile
{
    private readonly Dictionary<int, string> _texts = [];

    // P53: the AUDIO field (the second of the three braces) — a speech-file basename, e.g. "dcmetz01"
    // (message.h MessageListItem.audio). The engine plays sound\speech\<audio>.acm for a dialogue REPLY
    // whose audio is non-empty. Stored only for non-empty fields (the whole shippable slice is empty).
    private readonly Dictionary<int, string> _audio = [];

    public int Count => _texts.Count;

    public string? GetText(int id) => _texts.TryGetValue(id, out string? text) ? text : null;

    /// <summary>The speech-file basename for a message, or null when the line is unvoiced (empty audio field).</summary>
    public string? GetAudio(int id) => _audio.TryGetValue(id, out string? audio) ? audio : null;

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
            {
                result._texts[id] = textField;
                if (audioField.Trim() is { Length: > 0 } audio)
                    result._audio[id] = audio; // P53: retain the speech-file name (empty on the slice)
            }
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
