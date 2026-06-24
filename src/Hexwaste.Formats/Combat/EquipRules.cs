namespace Hexwaste.Formats.Combat;

/// <summary>The equip slots Hexwaste models — the engine's two READY weapon hands (Weapon = the RIGHT
/// hand / item2; WeaponLeft = the LEFT hand / item1) plus the worn ARMOR. The two hands are independent
/// slots you SWITCH between (the active hand fires), NOT simultaneous dual-wield (inventory.cc — neither
/// _invenWieldFunc nor _switch_hand enforces two-handed off-hand exclusivity); P81. <c>Weapon</c> stays
/// the right-hand alias so every prior caller/golden is unchanged.</summary>
public enum EquipSlot
{
    Weapon,     // the RIGHT hand (item2) — the default/legacy single-weapon slot
    Armor,
    WeaponLeft, // the LEFT hand (item1) — the P81 second ready slot
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
        EquipSlot.Weapon or EquipSlot.WeaponLeft => isWeapon,
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
