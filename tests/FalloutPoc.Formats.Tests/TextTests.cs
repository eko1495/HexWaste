using FalloutPoc.Formats.Proto;
using FalloutPoc.Formats.Text;

namespace FalloutPoc.Formats.Tests;

public class MessageFileTests
{
    private static MessageFile Parse(string content)
    {
        using var stream = new MemoryStream(System.Text.Encoding.Latin1.GetBytes(content));
        return MessageFile.Load(stream);
    }

    [Fact]
    public void ParsesEntries()
    {
        var msg = Parse("""
            # comment outside braces is ignored
            {100}{}{Plasma Rifle}
            {101}{snd1}{A Winchester Model P94.}
            """);
        Assert.Equal(2, msg.Count);
        Assert.Equal("Plasma Rifle", msg.GetText(100));
        Assert.Equal("A Winchester Model P94.", msg.GetText(101));
        Assert.Null(msg.GetText(102));
    }

    [Fact]
    public void CollapsesNewlinesInsideFields()
    {
        // ported behavior from message.cc _message_load_field: '\n' is skipped.
        var msg = Parse("{1}{}{first line\nsecond line}");
        Assert.Equal("first linesecond line", msg.GetText(1));
    }
}

public class TextRealGameDataTests
{
    [GameDataFact]
    public void LoadsFont1Aaf()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        AafFont font = AafFont.Load(vfs.ReadAllBytes("font1.aaf"));

        Assert.InRange(font.MaxHeight, 5, 30);
        Assert.True(font.Glyphs[(byte)'A'].Width > 0);
        Assert.True(font.Glyphs[(byte)'A'].Height > 0);
        Assert.Equal(font.Glyphs[(byte)'A'].Width * font.Glyphs[(byte)'A'].Height,
            font.Glyphs[(byte)'A'].Pixels.Length);
        Assert.True(font.MeasureWidth("Fallout") > 10);
    }

    [GameDataFact]
    public void ProtoMessagesResolveRealNames()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        var messages = new ProtoMessages(vfs, protos);

        // Scenery pid 0x02000001 is the Den's burning barrel (verified by
        // picking in the viewer); any vanilla scenery proto must have a name.
        string? name = messages.GetName(0x02000001);
        Assert.False(string.IsNullOrWhiteSpace(name));

        // pro_item.msg is large and must parse to plenty of entries.
        using Stream stream = vfs.OpenRead(@"text\english\game\pro_item.msg");
        MessageFile items = MessageFile.Load(stream);
        Assert.True(items.Count > 500, $"pro_item.msg parsed only {items.Count} entries");
    }
}
