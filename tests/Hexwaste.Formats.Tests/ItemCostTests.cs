using Hexwaste.Formats;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Barter-value math (P76-M2), ported from fallout2-ce item.cc itemGetCost() (:813): a loaded weapon
/// adds its rounds' worth, a partial ammo box is its fill fraction, a container sums its contents.
/// </summary>
public class ItemCostTests
{
    private static MapObject Item(int pid, int stack = 1, int ammoQty = -1, int ammoPid = -1) => new()
    {
        Id = 0, HexTile = 0, X = 0, Y = 0, Frame = 0, Rotation = 0, Fid = 0, Flags = 0, Pid = pid,
        StackCount = stack, AmmoQuantity = ammoQty, AmmoTypePid = ammoPid,
    };

    private static ProtoInfo Proto(int pid, int cost, int subType = -1,
        WeaponProtoStats? weapon = null, AmmoProtoStats? ammo = null) =>
        new(pid, 0, 0, 0, 0, subType, Cost: cost, Weapon: weapon, Ammo: ammo);

    private static WeaponProtoStats Weapon(int ammoPid, int capacity) =>
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, AmmoTypePid: ammoPid, AmmoCapacity: capacity, 0);

    private static AmmoProtoStats Ammo(int capacity) => new(0, capacity, 0, 0, 0, 0);

    [Fact]
    public void PlainItemIsItsProtoCost()
    {
        var protos = new Dictionary<int, ProtoInfo> { [10] = Proto(10, 250) };
        Assert.Equal(250, ItemCost.For(Item(10), p => protos.GetValueOrDefault(p)));
    }

    [Fact]
    public void UnknownProtoCostsZero()
    {
        Assert.Equal(0, ItemCost.For(Item(999), _ => null));
    }

    [Fact]
    public void LoadedWeaponAddsItsRoundsValue()
    {
        // item.cc:831 — cost += rounds × ammoCost / boxCapacity. 1000 + 12×100/50 = 1024.
        var protos = new Dictionary<int, ProtoInfo>
        {
            [9] = Proto(9, 1000, weapon: Weapon(ammoPid: 88, capacity: 12)),
            [88] = Proto(88, 100, ammo: Ammo(capacity: 50)),
        };
        Assert.Equal(1024, ItemCost.For(Item(9, ammoQty: 12, ammoPid: 88), p => protos.GetValueOrDefault(p)));
    }

    [Fact]
    public void FullMagWeaponUsesCapacityForRounds()
    {
        // AmmoQuantity -1 = a full magazine → AmmoCapacity rounds. 1000 + 12×100/50 = 1024.
        var protos = new Dictionary<int, ProtoInfo>
        {
            [9] = Proto(9, 1000, weapon: Weapon(ammoPid: 88, capacity: 12)),
            [88] = Proto(88, 100, ammo: Ammo(capacity: 50)),
        };
        Assert.Equal(1024, ItemCost.For(Item(9), p => protos.GetValueOrDefault(p)));
    }

    [Fact]
    public void UnloadedWeaponIsJustItsCost()
    {
        var protos = new Dictionary<int, ProtoInfo> { [9] = Proto(9, 1000, weapon: Weapon(ammoPid: 88, capacity: 12)) };
        // ammoPid 88 proto absent → no ammo value added.
        Assert.Equal(1000, ItemCost.For(Item(9, ammoQty: 0), p => protos.GetValueOrDefault(p)));
    }

    [Fact]
    public void PartialAmmoBoxIsItsFillFraction()
    {
        // item.cc:847 — cost × rounds / capacity. 200 × 10/50 = 40.
        var protos = new Dictionary<int, ProtoInfo> { [88] = Proto(88, 200, ammo: Ammo(capacity: 50)) };
        Assert.Equal(40, ItemCost.For(Item(88, ammoQty: 10), p => protos.GetValueOrDefault(p)));
    }

    [Fact]
    public void FullAmmoBoxIsItsWholeCost()
    {
        var protos = new Dictionary<int, ProtoInfo> { [88] = Proto(88, 200, ammo: Ammo(capacity: 50)) };
        Assert.Equal(200, ItemCost.For(Item(88), p => protos.GetValueOrDefault(p))); // -1 → full
    }

    [Fact]
    public void ContainerAddsItsContents()
    {
        // item.cc:828 → objectGetCost sums the contained items × stack.
        var protos = new Dictionary<int, ProtoInfo>
        {
            [50] = Proto(50, 5, subType: 1),  // a bag
            [10] = Proto(10, 10),
        };
        MapObject bag = Item(50);
        bag.Inventory.Add(Item(10, stack: 3));
        Assert.Equal(5 + 3 * 10, ItemCost.For(bag, p => protos.GetValueOrDefault(p)));
    }
}
