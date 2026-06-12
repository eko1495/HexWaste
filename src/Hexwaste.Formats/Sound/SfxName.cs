namespace Hexwaste.Formats.Sound;

/// <summary>
/// Sound effect file-name composition, ported from fallout2-ce
/// src/game_sound.cc. Sfx live at <c>sound\sfx\&lt;NAME&gt;.acm</c>.
/// </summary>
public static class SfxName
{
    // ported from game_sound.cc _snd_lookup_scenery_action:
    // Open, Close, Lock, Unlock, Use.
    private static readonly char[] SceneryActionCodes = ['O', 'C', 'L', 'N', 'U'];

    public enum SceneryAction
    {
        Open = 0,
        Close = 1,
        Lock = 2,
        Unlock = 3,
        Use = 4,
    }

    /// <summary>
    /// ported from game_sound.cc sfxBuildOpenName() for scenery:
    /// "S{action}DOORS{soundId}". soundId comes from the scenery proto's
    /// sound char; 'A' when missing.
    /// </summary>
    public static string Door(SceneryAction action, byte soundId)
    {
        char sound = soundId is >= 0x20 and < 0x7F ? char.ToUpperInvariant((char)soundId) : 'A';
        return $"S{SceneryActionCodes[(int)action]}DOORS{sound}".ToUpperInvariant();
    }

    /// <summary>ported from game_sound.cc sfxBuildSceneryName(): "S{P|A}{action}{4-char name}1".</summary>
    public static string Scenery(bool passive, SceneryAction action, string name) =>
        $"S{(passive ? 'P' : 'A')}{SceneryActionCodes[(int)action]}{name,-4}1".ToUpperInvariant();

    /// <summary>Virtual VFS path for an sfx name.</summary>
    public static string Path(string name) => $@"sound\sfx\{name}.acm";
}
