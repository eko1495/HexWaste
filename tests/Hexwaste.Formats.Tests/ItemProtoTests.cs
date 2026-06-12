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
