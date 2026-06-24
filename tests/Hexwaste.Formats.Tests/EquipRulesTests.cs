using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

/// <summary>P47: the pure equip-validity rules for inventory drag-and-drop
/// (EquipRules), ported from inventory.cc _switch_hand type guards.</summary>
public class EquipRulesTests
{
    [Theory]
    [InlineData(true, false, EquipSlot.Weapon, true)]   // weapon -> weapon slot: ok
    [InlineData(true, false, EquipSlot.Armor, false)]   // weapon -> armor slot: no
    [InlineData(false, true, EquipSlot.Armor, true)]    // armor  -> armor slot: ok
    [InlineData(false, true, EquipSlot.Weapon, false)]  // armor  -> weapon slot: no
    [InlineData(false, false, EquipSlot.Weapon, false)] // misc/ammo -> weapon slot: no
    [InlineData(false, false, EquipSlot.Armor, false)]  // misc      -> armor slot: no
    [InlineData(true, false, EquipSlot.WeaponLeft, true)]  // P81: weapon -> the LEFT hand slot: ok
    [InlineData(false, true, EquipSlot.WeaponLeft, false)] // P81: armor  -> the left hand: no
    [InlineData(false, false, EquipSlot.WeaponLeft, false)]// P81: misc   -> the left hand: no
    public void CanEquipHonoursTheItemTypePerSlot(bool isWeapon, bool isArmor, EquipSlot slot, bool expected) =>
        Assert.Equal(expected, EquipRules.CanEquip(isWeapon, isArmor, slot));

    [Fact]
    public void NaturalSlotIsWeaponForAWeapon() =>
        Assert.Equal(EquipSlot.Weapon, EquipRules.NaturalSlot(isWeapon: true, isArmor: false));

    [Fact]
    public void NaturalSlotIsArmorForArmor() =>
        Assert.Equal(EquipSlot.Armor, EquipRules.NaturalSlot(isWeapon: false, isArmor: true));

    [Fact]
    public void NaturalSlotIsNullForANonEquippable() =>
        Assert.Null(EquipRules.NaturalSlot(isWeapon: false, isArmor: false));
}
