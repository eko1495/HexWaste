namespace Hexwaste.Formats.Sound;

/// <summary>
/// Dialogue speech (voiceover) file-name composition + the engine's play gate, ported from
/// fallout2-ce. The played audio lives FLAT under <c>sound\speech\&lt;audio&gt;.acm</c>
/// (src/game_sound.cc gameSoundFindSpeechSoundPath :1871, <c>_sound_speech_path = "sound\speech\"</c>),
/// where <c>&lt;audio&gt;</c> is the MSG entry's audio field. The per-head <c>sound\speech\&lt;head&gt;\&lt;audio&gt;.lip</c>
/// path is the lip-sync timing file (src/lips.cc) — OUT OF SCOPE (Hexwaste renders no talking head, and
/// GOG Fallout 2 ships no speech assets at all).
/// </summary>
public static class SpeechName
{
    /// <summary>Virtual path for a speech file: <c>sound\speech\&lt;audio&gt;.acm</c> (lowercased, like <see cref="SfxName"/>).</summary>
    public static string Path(string audio) => $@"sound\speech\{audio}.acm".ToLowerInvariant();

    /// <summary>
    /// Whether a dialogue line plays its voice, ported from fallout2-ce scripts.cc _scr_get_msg_str_speech
    /// (:2757-2766): speech fires only for a REPLY (a3==1, not an option — game_dialog.cc:2239 vs :2282),
    /// when the dialogue head FID is a valid HEAD (else a3 is forced 0, :2746), the MSG audio field is
    /// non-empty, and the message's 0x01 flag is clear (set → a censor beep instead, not speech).
    /// </summary>
    public static bool ShouldSpeak(bool isReply, bool headIsValid, string? audio, int msgFlags = 0) =>
        isReply && headIsValid && !string.IsNullOrEmpty(audio) && (msgFlags & 0x01) == 0;
}
