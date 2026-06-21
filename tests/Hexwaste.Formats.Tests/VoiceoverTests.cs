using System.Text;
using Hexwaste.Formats.Sound;
using Hexwaste.Formats.Text;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Dialogue voiceover (P53): the MSG audio field is retained, the speech path composes flat under
/// sound\speech\, and the engine's play gate (reply-only, head-valid, audio-non-empty, 0x01 flag clear,
/// ported from scripts.cc _scr_get_msg_str_speech) is honoured. The slice is unvoiced (empty audio fields),
/// so this is forward-looking infra — these tests lock the mechanism independently of any voiced content.
/// </summary>
public class VoiceoverTests
{
    [Fact]
    public void SpeechPathIsFlatAndLowercased()
    {
        Assert.Equal(@"sound\speech\dcmetz01.acm", SpeechName.Path("dcmetz01"));
        Assert.Equal(@"sound\speech\dcmetz01.acm", SpeechName.Path("DCMETZ01")); // VFS-normalised
    }

    [Theory]
    // reply + valid head + non-empty audio + flag clear → speak.
    [InlineData(true, true, "dcvic001", 0, true)]
    // an OPTION never speaks (game_dialog.cc:2282 passes a3=0).
    [InlineData(false, true, "dcvic001", 0, false)]
    // no valid dialogue head → the engine forces a3=0 (scripts.cc:2746).
    [InlineData(true, false, "dcvic001", 0, false)]
    // empty / missing audio field → "Missing speech name", no playback.
    [InlineData(true, true, "", 0, false)]
    [InlineData(true, true, null, 0, false)]
    // the 0x01 message flag → a censor beep instead of speech.
    [InlineData(true, true, "dcvic001", 0x01, false)]
    public void ShouldSpeakHonoursTheEngineGate(bool isReply, bool head, string? audio, int flags, bool expected)
        => Assert.Equal(expected, SpeechName.ShouldSpeak(isReply, head, audio, flags));

    [Fact]
    public void MessageFileRetainsTheAudioFieldButLeavesEmptyNull()
    {
        // {id}{audio}{text}: line 1 is voiced, line 2 is the slice's typical empty-audio line.
        byte[] blob = Encoding.Latin1.GetBytes("{1}{dcmetz01}{Hello.}\n{2}{}{You see a man.}\n");
        using var stream = new MemoryStream(blob);
        MessageFile msg = MessageFile.Load(stream);

        Assert.Equal("dcmetz01", msg.GetAudio(1));
        Assert.Equal("Hello.", msg.GetText(1));     // text retention unchanged
        Assert.Null(msg.GetAudio(2));                // empty audio → null (the whole slice)
        Assert.Equal("You see a man.", msg.GetText(2));
    }
}
