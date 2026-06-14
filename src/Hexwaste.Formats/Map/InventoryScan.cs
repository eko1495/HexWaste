namespace Hexwaste.Formats.Map;

/// <summary>
/// Recursive inventory queries by proto PID, ported from fallout2-ce
/// src/inventory.cc (objectGetCarriedQuantityByPid / objectGetCarriedObjectByPid).
/// Both descend into nested container inventories, exactly like the engine. Used by
/// the obj_is_carrying_obj / obj_carrying_pid_obj script externals (#10 M-radio).
/// </summary>
public static class InventoryScan
{
    /// <summary>Total quantity of <paramref name="pid"/> the owner carries, summing
    /// stack counts and recursing into nested containers (inventory.cc:2857).</summary>
    public static int CountByPid(MapObject owner, int pid)
    {
        int total = 0;
        foreach (MapObject item in owner.Inventory)
        {
            if (item.Pid == pid)
                total += System.Math.Max(item.StackCount, 1);
            total += CountByPid(item, pid);
        }
        return total;
    }

    /// <summary>The first carried item with <paramref name="pid"/> (depth-first), or
    /// null if none — the engine's objectGetCarriedObjectByPid (inventory.cc:2837).</summary>
    public static MapObject? FindByPid(MapObject owner, int pid)
    {
        foreach (MapObject item in owner.Inventory)
        {
            if (item.Pid == pid)
                return item;
            if (FindByPid(item, pid) is { } nested)
                return nested;
        }
        return null;
    }
}
