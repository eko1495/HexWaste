using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Recursive inventory-by-pid queries (#10 M-radio): the engine-ported scan behind
/// the obj_is_carrying_obj / obj_carrying_pid_obj externals that gate Vic's radio
/// give. Pure logic — no game data.
/// </summary>
public class InventoryScanTests
{
    private static MapObject Item(int pid, int stack = 1) => new()
    {
        Id = pid, HexTile = 0, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = 0x06000000, Flags = 0, Pid = pid, Sid = -1, StackCount = stack,
    };

    [Fact]
    public void CountByPidSumsStacksAndRecursesIntoContainers()
    {
        MapObject dude = Item(0x1000000);
        dude.Inventory.Add(Item(266));            // Vic's Radio, top-level x1
        dude.Inventory.Add(Item(41, stack: 100)); // caps x100
        MapObject bag = Item(0x55);               // a container...
        bag.Inventory.Add(Item(266, stack: 2));   // ...holding 2 more radios
        dude.Inventory.Add(bag);

        Assert.Equal(3, InventoryScan.CountByPid(dude, 266)); // 1 top-level + 2 nested
        Assert.Equal(100, InventoryScan.CountByPid(dude, 41));
        Assert.Equal(0, InventoryScan.CountByPid(dude, 999)); // not carried
    }

    [Fact]
    public void FindByPidReturnsTheFirstDepthFirstMatchOrNull()
    {
        MapObject dude = Item(0x1000000);
        MapObject topRadio = Item(266);
        dude.Inventory.Add(topRadio);
        MapObject bag = Item(0x55);
        bag.Inventory.Add(Item(266)); // a deeper radio that must NOT win
        dude.Inventory.Add(bag);

        Assert.Same(topRadio, InventoryScan.FindByPid(dude, 266)); // top-level first
        Assert.Null(InventoryScan.FindByPid(dude, 999));
    }

    [Fact]
    public void FindByPidDescendsWhenOnlyNested()
    {
        MapObject dude = Item(0x1000000);
        MapObject bag = Item(0x55);
        MapObject nested = Item(266);
        bag.Inventory.Add(nested);
        dude.Inventory.Add(bag);

        Assert.Same(nested, InventoryScan.FindByPid(dude, 266));
    }

    [Fact]
    public void EmptyInventoryFindsNothing()
    {
        MapObject dude = Item(0x1000000);
        Assert.Equal(0, InventoryScan.CountByPid(dude, 266));
        Assert.Null(InventoryScan.FindByPid(dude, 266));
    }
}
