using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

public class ItemProtoTests
{
    [GameDataFact]
    public void WeaponArmorAndDrugPayloadsMatchEmpiricalValues()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);

        // Spear (pid 7): anim code 4 ('g'), 3-10 dmg, AP 4, range 2, thrust.
        ProtoInfo spear = protos.Get(7);
        Assert.NotNull(spear.Weapon);
        Assert.Equal(4, spear.Weapon.AnimationCode);
        Assert.Equal(3, spear.Weapon.MinDamage);
        Assert.Equal(10, spear.Weapon.MaxDamage);
        Assert.Equal(4, spear.Weapon.ApCost);
        Assert.Equal(2, spear.Weapon.MaxRange1);
        Assert.Equal(4, spear.ExtendedFlags & 0xF); // _attack_anim[4] = THRUST

        // Knife (pid 4): anim code 1 ('d'), 1-6 dmg, AP 3, swing.
        ProtoInfo knife = protos.Get(4);
        Assert.NotNull(knife.Weapon);
        Assert.Equal(1, knife.Weapon.AnimationCode);
        Assert.Equal(1, knife.Weapon.MinDamage);
        Assert.Equal(6, knife.Weapon.MaxDamage);
        Assert.Equal(3, knife.Weapon.ApCost);

        // Leather Jacket (pid 74): AC 8, DR normal 20, DT normal 0.
        ProtoInfo jacket = protos.Get(74);
        Assert.NotNull(jacket.Armor);
        Assert.Equal(8, jacket.Armor.ArmorClass);
        Assert.Equal(20, jacket.Armor.DamageResistance[0]);
        Assert.Equal(0, jacket.Armor.DamageThreshold[0]);

        // Stimpak (pid 40): stats[0] = -2 (random range), heals current HP
        // (stat 35) by amounts[0]..amounts[1].
        ProtoInfo stimpak = protos.Get(40);
        Assert.NotNull(stimpak.Drug);
        Assert.Equal(-2, stimpak.Drug.Stats[0]);
        Assert.Equal(35, stimpak.Drug.Stats[1]);
        Assert.True(stimpak.Drug.Amounts[1] >= stimpak.Drug.Amounts[0]);
        Assert.InRange(stimpak.Drug.Amounts[0], 1, 50);

        // Cost field (M5 prereq): stimpak base price is 175.
        Assert.Equal(175, stimpak.Cost);
    }
}

public class RangedProtoAndMathTests
{
    [GameDataFact]
    public void TenMmPistolAndAmmoMatchEmpiricalValues()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);

        // 10mm Pistol pid 8 (track-A parse: hitscan, mag 12, sound 'A').
        ProtoInfo pistol = protos.Get(8);
        Assert.NotNull(pistol.Weapon);
        Assert.Equal(5, pistol.Weapon.AnimationCode);
        Assert.Equal(5, pistol.Weapon.MinDamage);
        Assert.Equal(12, pistol.Weapon.MaxDamage);
        Assert.Equal(25, pistol.Weapon.MaxRange1);
        Assert.Equal(-1, pistol.Weapon.ProjectilePid); // hitscan
        Assert.Equal(3, pistol.Weapon.MinStrength);
        Assert.Equal(5, pistol.Weapon.ApCost);
        Assert.Equal(12, pistol.Weapon.AmmoCapacity);
        Assert.Equal(29, pistol.Weapon.AmmoTypePid); // 10mm JHP
        Assert.Equal((byte)'A', pistol.Weapon.SoundCode);
        Assert.True(pistol.Weapon.IsGun(pistol.ExtendedFlags));

        // Spear stays melee.
        ProtoInfo spear = protos.Get(7);
        Assert.False(spear.Weapon!.IsGun(spear.ExtendedFlags));

        // 10mm JHP ammo pid 29: box of rounds with damage mods.
        ProtoInfo jhp = protos.Get(29);
        Assert.NotNull(jhp.Ammo);
        Assert.Equal(pistol.Weapon.Caliber, jhp.Ammo.Caliber);
        Assert.True(jhp.Ammo.Quantity > 0);
    }

    [Fact]
    public void RangedToHitTermsBehaveLikeTheEngine()
    {
        // skill 60, PE 5 dude: free range = 2×(PE−2) = 6 hexes.
        int atSix = Hexwaste.Formats.Combat.RangedMath.ToHitChance(60, 6, 5, true, 0, 0, 0, 5, 0);
        int atTen = Hexwaste.Formats.Combat.RangedMath.ToHitChance(60, 10, 5, true, 0, 0, 0, 5, 0);
        Assert.Equal(60, atSix);
        Assert.Equal(60 - 16, atTen); // −4 per hex past free range

        // Close range bonus is capped at +8×PE via the −2·PE clamp.
        int pointBlank = Hexwaste.Formats.Combat.RangedMath.ToHitChance(60, 0, 10, true, 0, 0, 0, 5, 0);
        Assert.Equal(Math.Min(60 + 4 * 16, 95), pointBlank);

        // Crowd penalty, min-ST, ammo AC mod.
        Assert.Equal(atSix - 20, Hexwaste.Formats.Combat.RangedMath.ToHitChance(60, 6, 5, true, 0, 0, 0, 5, 2));
        Assert.Equal(atSix - 40, Hexwaste.Formats.Combat.RangedMath.ToHitChance(60, 6, 5, true, 0, 0, 7, 5, 0));
        Assert.Equal(atSix - 10, Hexwaste.Formats.Combat.RangedMath.ToHitChance(60, 6, 5, true, 5, 5, 0, 5, 0));
        // Negative AC+ammo clamps to zero, never a bonus.
        Assert.Equal(atSix, Hexwaste.Formats.Combat.RangedMath.ToHitChance(60, 6, 5, true, 0, -30, 0, 5, 0));
    }
}
