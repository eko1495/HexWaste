namespace Hexwaste.Formats.Combat;

/// <summary>The two equip slots Hexwaste models — the right-hand WEAPON and the worn ARMOR.
/// The engine also has a LEFT-hand item slot (dual-wield / item2); Hexwaste equips a single
/// weapon (FlagInRightHand) so the left-hand slot is out (a documented simplification — it
/// needs the two-handed / item2 proto model, and no shippable content dual-wields).</summary>
public enum EquipSlot
{
    Weapon,
    Armor,
}

/// <summary>
/// Pure equip-validity rules for inventory drag-and-drop, ported from fallout2-ce
/// src/inventory.cc _switch_hand() / the drop hit-test cascade (inventory.cc:2386-2537):
/// a weapon may be dropped on a hand slot, armor on the armor slot, and an item of the
/// wrong type is rejected (e.g. ammo is never equipped as a weapon, a non-armor never in
/// the armor slot). The viewer's drag handler consults this before mutating equip flags.
/// </summary>
public static class EquipRules
{
    /// <summary>Whether an item with these type flags may be equipped into <paramref name="slot"/>.</summary>
    public static bool CanEquip(bool isWeapon, bool isArmor, EquipSlot slot) => slot switch
    {
        EquipSlot.Weapon => isWeapon,
        EquipSlot.Armor => isArmor,
        _ => false,
    };

    /// <summary>The slot an item naturally equips into (weapon→Weapon, armor→Armor), or null
    /// if it is neither (a drug/book/ammo/misc item, which has no equip slot).</summary>
    public static EquipSlot? NaturalSlot(bool isWeapon, bool isArmor) =>
        isWeapon ? EquipSlot.Weapon
        : isArmor ? EquipSlot.Armor
        : null;
}
