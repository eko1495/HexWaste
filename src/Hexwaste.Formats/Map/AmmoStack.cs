namespace Hexwaste.Formats.Map;

/// <summary>
/// Ammo-box stacking consolidation, ported from fallout2-ce src/item.cc itemAdd() (:371). A Hexwaste
/// ammo stack is <c>(StackCount-1)</c> FULL boxes plus one PARTIAL top box holding
/// <c>AmmoQuantity</c> rounds (the reload path consumes the top box, then refills the next from the
/// proto capacity). Merging two same-pid stacks must CONSOLIDATE the rounds — just bumping the box
/// count (the pre-P75 bug) treats the incoming partial as a full box and invents phantom rounds
/// (two 12-round 24-cap boxes would read as "1 full + 1 partial = 36" instead of 24). P75-M2.
/// </summary>
public static class AmmoStack
{
    /// <summary>Total rounds in a stack: <c>(StackCount-1)·capacity + AmmoQuantity</c>. A negative
    /// AmmoQuantity (a pristine box that never hydrated) reads as a FULL top box.</summary>
    public static int TotalRounds(int stackCount, int ammoQuantity, int capacity)
    {
        int boxes = Math.Max(stackCount, 1);
        int partial = ammoQuantity < 0 ? capacity : Math.Min(ammoQuantity, capacity);
        return (boxes - 1) * capacity + partial;
    }

    /// <summary>Re-box a total round count into <c>(StackCount, AmmoQuantity)</c>: ceil(total/capacity)
    /// boxes, the top holding the remainder (or full when total is an exact multiple of capacity).</summary>
    public static (int StackCount, int AmmoQuantity) FromTotal(int totalRounds, int capacity)
    {
        if (capacity <= 0)
            return (1, totalRounds);
        int stack = Math.Max(1, (totalRounds + capacity - 1) / capacity);
        int top = totalRounds - (stack - 1) * capacity; // 1..capacity
        return (stack, top);
    }

    /// <summary>Merge an incoming ammo stack into an existing one of the same pid, consolidating the
    /// partials (item.cc:371). Returns the merged <c>(StackCount, AmmoQuantity)</c>.</summary>
    public static (int StackCount, int AmmoQuantity) Merge(
        int existingStack, int existingQty, int incomingStack, int incomingQty, int capacity)
    {
        int total = TotalRounds(existingStack, existingQty, capacity)
                  + TotalRounds(incomingStack, incomingQty, capacity);
        return FromTotal(total, capacity);
    }
}
