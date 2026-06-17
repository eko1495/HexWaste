using Hexwaste.Formats.Map;
using Hexwaste.Formats.Sound;

namespace Hexwaste.Formats.Tests;

public class SfxNameTests
{
    [Fact]
    public void ComposesDoorNames()
    {
        Assert.Equal("SODOORSA", SfxName.Door(SfxName.SceneryAction.Open, (byte)'A'));
        Assert.Equal("SCDOORSB", SfxName.Door(SfxName.SceneryAction.Close, (byte)'b'));
        Assert.Equal("SODOORSA", SfxName.Door(SfxName.SceneryAction.Open, 0)); // missing -> 'A'
        Assert.Equal(@"sound\sfx\SODOORSA.acm", SfxName.Path("SODOORSA"));
    }

    [Fact]
    public void CharNameComposesBaseWeaponAnimWithDeathOverride()
    {
        // mascrp + FALL_BACK(20) + Die: GetAnimationCode(20,0)=('b','a'); Die overrides the weapon char 'b'->'Z'.
        Assert.Equal("MASCRPZA", SfxName.CharName("mascrp", 20, SfxName.CharacterSoundEffect.Die, 0));
        // hit-from-front(14), no override: ('a','o') -> MASCRPAO.
        Assert.Equal("MASCRPAO", SfxName.CharName("mascrp", 14, SfxName.CharacterSoundEffect.Unused, 0));
        // punch(16) + Contact override -> weapon char 'Z': ('a','q') -> MASCRPZQ.
        Assert.Equal("MASCRPZQ", SfxName.CharName("mascrp", 16, SfxName.CharacterSoundEffect.Contact, 0));
        // null base name (unresolvable critter) -> null (the engine's artCopyFileName==-1 path).
        Assert.Null(SfxName.CharName(null, 20, SfxName.CharacterSoundEffect.Die, 0));
    }

    [Fact]
    public void WeaponNameComposesEffectVariantMaterial()
    {
        Assert.Equal("WA01XXX1", SfxName.WeaponName(SfxName.WeaponSoundEffect.Attack, (byte)'0', primaryOrPunch: true));
        Assert.Equal("WO01XXX1", SfxName.WeaponName(SfxName.WeaponSoundEffect.OutOfAmmo, (byte)'0', primaryOrPunch: true));
        Assert.Equal("WR01XXX1", SfxName.WeaponName(SfxName.WeaponSoundEffect.Ready, (byte)'0', primaryOrPunch: true));
        // a non-primary HIT → variant 2, material F.
        Assert.Equal("WH02FXX1", SfxName.WeaponName(SfxName.WeaponSoundEffect.Hit, (byte)'0', primaryOrPunch: false, material: 'F'));
        // the back-compat shims match the old hardcoded forms.
        Assert.Equal("WA01XXX1", SfxName.WeaponAttack((byte)'0'));
        Assert.Equal("WH01FXX1", SfxName.WeaponHit((byte)'0'));
    }
}

public class AmbientSfxTests
{
    [Fact]
    public void RollIndexIsChanceWeighted()
    {
        var entries = new[] { ("a", 30), ("b", 70) };
        Assert.Equal(0, AmbientSfx.RollIndex(entries, _ => 0));   // 0 < 30 → a
        Assert.Equal(0, AmbientSfx.RollIndex(entries, _ => 29));  // 29 < 30 → a
        Assert.Equal(1, AmbientSfx.RollIndex(entries, _ => 30));  // 30 >= 30, 0 < 70 → b
        Assert.Equal(1, AmbientSfx.RollIndex(entries, _ => 99));  // → b
        Assert.Equal(-1, AmbientSfx.RollIndex([], _ => 0));        // empty → -1
    }

    [Fact]
    public void RemapsBirdsToCricketsAtNightOnly()
    {
        Assert.Equal("cricket", AmbientSfx.RemapBirdForNight("brdchir1", 2000));  // 20:00 night
        Assert.Equal("cricket1", AmbientSfx.RemapBirdForNight("brdchirp", 400));  // 04:00 night
        Assert.Equal("brdchir1", AmbientSfx.RemapBirdForNight("brdchir1", 1200)); // noon → unchanged
        Assert.Equal("dogbark", AmbientSfx.RemapBirdForNight("dogbark", 2000));   // non-bird → unchanged
    }
}

public class SoundRealGameDataTests
{
    [GameDataFact]
    public void DoorSfxFilesExistAndMapsHaveMusic()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        Assert.True(vfs.Exists(SfxName.Path("SODOORSA")), "SODOORSA.acm missing");
        Assert.True(vfs.Exists(SfxName.Path("FOOTSTEP")), "FOOTSTEP.acm missing");

        MapList maps = MapList.Load(vfs);
        Assert.False(string.IsNullOrEmpty(maps.GetMusic("artemple.map")));
        Assert.False(string.IsNullOrEmpty(maps.GetMusic("denbus1.map")));

        // P34-M5: the faithful CharName resolves to a REAL .acm for scorpions (mascrp → MASCRP*),
        // and is engine-faithfully SILENT for the slice's generic humans (hmwarr → HMWARR* ships none).
        string scorpionHit = SfxName.CharName("mascrp", 14, SfxName.CharacterSoundEffect.Unused, 0)!;
        Assert.Equal("MASCRPAO", scorpionHit);
        Assert.True(vfs.Exists(SfxName.Path(scorpionHit)), "MASCRPAO.acm (scorpion hit grunt) missing");
        string humanHit = SfxName.CharName("hmwarr", 14, SfxName.CharacterSoundEffect.Unused, 0)!;
        Assert.False(vfs.Exists(SfxName.Path(humanHit)), "HMWARR* should NOT exist (engine-faithful human silence)");

        // P34-M5: the per-map ambient_sfx list parses (denbus2 carries a dogbark entry).
        var ambient = maps.GetAmbientSfx("denbus2.map");
        Assert.NotEmpty(ambient);
        Assert.Contains(ambient, e => e.Name == "dogbark");
    }
}
