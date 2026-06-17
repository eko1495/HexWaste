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

    /// <summary>The character-action variant passed to <see cref="CharName"/> as <c>extra</c>
    /// (game_sound.h:27 CharacterSoundEffect).</summary>
    public enum CharacterSoundEffect { Unused = 0, Knockdown = 1, PassOut = 2, Die = 3, Contact = 4 }

    /// <summary>The weapon-action selector for <see cref="WeaponName"/> (game_sound.h
    /// WeaponSoundEffect); the codes are <c>_snd_lookup_weapon_type</c> = R/A/O/F/H.</summary>
    public enum WeaponSoundEffect { Ready = 0, Attack = 1, OutOfAmmo = 2, AmmoFlying = 3, Hit = 4 }

    private static readonly char[] WeaponEffectCodes = ['R', 'A', 'O', 'F', 'H'];

    // anim codes the CharName override keys on (animation.h).
    private const int AnimThrowPunch = 16, AnimKickLeg = 17, AnimFallBack = 20, AnimFallFront = 21, AnimTakeOut = 38;

    /// <summary>
    /// ported from fallout2-ce src/game_sound.cc sfxBuildCharName(): the critter's FRM base name
    /// + the two-char (weapon,anim) code from _art_get_code, with the death/knockout/contact override
    /// on the WEAPON char (FALL + PassOut→'Y' / Die→'Z'; punch/kick + Contact→'Z'). Returns null when
    /// the base name is unresolvable (the engine's artCopyFileName==-1 path → silent).
    /// </summary>
    public static string? CharName(string? frmBaseName, int anim, CharacterSoundEffect extra, int weaponCode)
    {
        if (frmBaseName is null)
            return null;
        // ANIM_TAKE_OUT passes `extra` as the weapon type; every other anim uses the FID weapon nibble.
        int weaponType = anim == AnimTakeOut ? (int)extra : weaponCode;
        (char weaponChar, char animChar) = Art.ArtIndex.GetAnimationCode(anim, weaponType);

        if (anim is AnimFallFront or AnimFallBack)
        {
            if (extra == CharacterSoundEffect.PassOut)
                weaponChar = 'Y';
            else if (extra == CharacterSoundEffect.Die)
                weaponChar = 'Z';
        }
        else if ((anim is AnimThrowPunch or AnimKickLeg) && extra == CharacterSoundEffect.Contact)
        {
            weaponChar = 'Z';
        }
        return $"{frmBaseName}{weaponChar}{animChar}".ToUpperInvariant();
    }

    /// <summary>
    /// ported from fallout2-ce src/game_sound.cc sfxBuildWeaponName():
    /// "W{R|A|O|F|H}{soundCode}{variant}{material}XX1". Variant 1 for ready/out-of-ammo or a primary/
    /// punch hit-mode, else 2. Material 'X' except a weapon HIT on a known material ('F' = flesh).
    /// </summary>
    public static string WeaponName(WeaponSoundEffect effect, byte soundCode, bool primaryOrPunch, char material = 'X')
    {
        int variant = effect is WeaponSoundEffect.Ready or WeaponSoundEffect.OutOfAmmo ? 1 : primaryOrPunch ? 1 : 2;
        return $"W{WeaponEffectCodes[(int)effect]}{(char)soundCode}{variant}{material}XX1".ToUpperInvariant();
    }

    /// <summary>Weapon attack sfx (material X) — thin shim over <see cref="WeaponName"/>.</summary>
    public static string WeaponAttack(byte soundCode) => WeaponName(WeaponSoundEffect.Attack, soundCode, primaryOrPunch: true);

    /// <summary>Weapon hit on flesh (material F) — thin shim over <see cref="WeaponName"/>.</summary>
    public static string WeaponHit(byte soundCode) => WeaponName(WeaponSoundEffect.Hit, soundCode, primaryOrPunch: true, material: 'F');

    /// <summary>Human death scream (game_sound.cc:1117 alias path):
    /// H{M|F}XXXX + the death-anim art code (20 → BA, 21 → BB). A documented Hexwaste divergence
    /// (the faithful CharName yields HMWARR*/HFWARR* which ship no .acm); kept as the dude fallback.</summary>
    public static string HumanDeath(bool female, int deathAnim) =>
        $"H{(female ? 'F' : 'M')}XXXXB{(char)('A' + Math.Clamp(deathAnim - 20, 0, 25))}";
}
