using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Map;

/// <summary>
/// Carried-weight + encumbrance math (P24), ported from fallout2-ce src/item.cc itemGetWeight
/// (0x477B88) + objectGetInventoryWeight (0x477E98) and the AP penalty in src/stat.cc
/// critterGetStat (STAT_MAXIMUM_ACTION_POINTS, ~:195). Pure — every input is a
/// <see cref="MapObject"/>/<see cref="ProtoInfo"/>, so it's unit-testable and the viewer just
/// supplies a proto lookup. Equipped items stay in the carried list (Hexwaste flags rather than
/// moving them), so they count once via <see cref="TotalWeight"/> — matching the engine's primary
/// inventory loop (item.cc:928).
/// </summary>
public static class InventoryWeight
{
    // Power-armor variants whose carried weight the engine halves (proto_types.h PROTO_ID_*).
    private static readonly HashSet<int> PowerArmorPids = [3, 232, 348, 349];

    private const int ItemTypeArmor = 0;
    private const int ItemTypeContainer = 1;

    /// <summary>The carried weight of one item in pounds (item.cc itemGetWeight): base proto
    /// weight, with power-armor halved, a container adding its contents, and a weapon adding the
    /// weight of its loaded ammo (box weight × boxes). Unknown proto → 0.</summary>
    public static int ItemWeight(MapObject item, Func<int, ProtoInfo?> proto)
    {
        if (proto(item.Pid) is not { } p)
            return 0;

        int weight = p.Weight;

        if (p.SubType == ItemTypeArmor)
        {
            if (PowerArmorPids.Contains(p.Pid))
                weight /= 2;
        }
        else if (p.SubType == ItemTypeContainer)
        {
            weight += TotalWeight(item.Inventory, proto); // item.cc:783 — a carried container adds its contents
        }
        else if (p.Weapon is { } w)
        {
            // item.cc:786-795 — loaded ammo adds box-weight × ceil(rounds / boxSize).
            int rounds = item.AmmoQuantity >= 0 ? item.AmmoQuantity : w.AmmoCapacity;
            int ammoPid = item.AmmoTypePid != -1 ? item.AmmoTypePid : w.AmmoTypePid;
            if (rounds > 0 && ammoPid > 0 && proto(ammoPid) is { Ammo: { } box } ap && box.Quantity > 0)
                weight += ap.Weight * ((rounds - 1) / box.Quantity + 1);
        }

        return weight;
    }

    /// <summary>Total carried weight of an inventory (item.cc objectGetInventoryWeight): each
    /// item's weight × its stack count, summed. Containers recurse via <see cref="ItemWeight"/>.</summary>
    public static int TotalWeight(IEnumerable<MapObject> items, Func<int, ProtoInfo?> proto)
    {
        int total = 0;
        foreach (MapObject item in items)
            total += ItemWeight(item, proto) * Math.Max(item.StackCount, 1);
        return total;
    }

    /// <summary>Over-encumbered = carried strictly exceeds capacity (critter.cc:1366
    /// critterIsEncumbered: maxWeight &lt; currentWeight).</summary>
    public static bool IsEncumbered(int carried, int capacity) => carried > capacity;

    /// <summary>The max-AP penalty for being over-encumbered (stat.cc:198-200): 1 AP per 40 lbs
    /// over the limit, plus 1. Zero when within capacity.</summary>
    public static int ActionPointPenalty(int carried, int capacity) =>
        carried > capacity ? (carried - capacity) / 40 + 1 : 0;
}
