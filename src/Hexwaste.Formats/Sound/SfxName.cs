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

    /// <summary>ported from fallout2-ce src/game_sound.cc sfxBuildWeaponName():
    /// W{R|A|O|F|H}{soundCode}{variant}{material}XX1. Attack uses material X.</summary>
    public static string WeaponAttack(byte soundCode) =>
        $"WA{(char)soundCode}1XXX1";

    /// <summary>Weapon hit on flesh (material F).</summary>
    public static string WeaponHit(byte soundCode) =>
        $"WH{(char)soundCode}1FXX1";

    /// <summary>Human death scream (game_sound.cc:1117 alias path):
    /// H{M|F}XXXX + the death-anim art code (20 → BA, 21 → BB).</summary>
    public static string HumanDeath(bool female, int deathAnim) =>
        $"H{(female ? 'F' : 'M')}XXXXB{(char)('A' + Math.Clamp(deathAnim - 20, 0, 25))}";
}
