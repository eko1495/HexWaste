namespace Hexwaste.Formats.Map;

/// <summary>
/// An item's barter value, ported from fallout2-ce src/item.cc itemGetCost() (:813). The proto's flat
/// <c>cost</c> is the BASE; a loaded WEAPON adds the value of its rounds, an AMMO box is worth its
/// partial-fill fraction, and a CONTAINER adds the value of its contents (recursive). Hexwaste's barter
/// previously used the raw proto cost, mispricing looted loaded guns / partial clips (P76-M2).
/// </summary>
public static class ItemCost
{
    public static int For(MapObject item, Func<int, Proto.ProtoInfo?> proto)
    {
        if (proto(item.Pid) is not { } p)
            return 0;
        int cost = p.Cost;
        if (p.Weapon is { } w)
        {
            // item.cc:831 — a loaded weapon adds its ammo's value: rounds × ammoCost / box capacity.
            int rounds = item.AmmoQuantity >= 0 ? item.AmmoQuantity : w.AmmoCapacity; // -1 = a full mag
            int ammoPid = item.AmmoTypePid != -1 ? item.AmmoTypePid : w.AmmoTypePid;
            if (rounds > 0 && ammoPid > 0 && proto(ammoPid) is { Ammo: { } box } ap && box.Quantity > 0)
                cost += rounds * ap.Cost / box.Quantity;
        }
        else if (p.Ammo is { Quantity: > 0 } ammo)
        {
            // item.cc:847 — a partial ammo box is worth its fraction (cost × rounds / capacity).
            int rounds = item.AmmoQuantity >= 0 ? item.AmmoQuantity : ammo.Quantity;
            cost = cost * rounds / ammo.Quantity;
        }
        // CONTAINER (item.cc:828 → objectGetCost): + the value of the contained items (recursive).
        // Non-containers carry no inventory, so this is a no-op for weapons/ammo/misc.
        foreach (MapObject c in item.Inventory)
            cost += For(c, proto) * Math.Max(c.StackCount, 1);
        return cost;
    }
}
