using Hexwaste.Formats;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Carried-weight + encumbrance math (P24), ported from fallout2-ce item.cc itemGetWeight /
/// objectGetInventoryWeight and the stat.cc AP penalty. Pure tests over synthetic protos plus a
/// GameDataFact proving the proto weight field (previously skipped) now parses to sane values.
/// </summary>
public class InventoryWeightTests
{
    private static MapObject Item(int pid, int stack = 1, int ammoQty = -1, int ammoPid = -1) => new()
    {
        Id = 0, HexTile = 0, X = 0, Y = 0, Frame = 0, Rotation = 0, Fid = 0, Flags = 0, Pid = pid,
        StackCount = stack, AmmoQuantity = ammoQty, AmmoTypePid = ammoPid,
    };

    private static ProtoInfo Proto(int pid, int weight, int subType = -1,
        WeaponProtoStats? weapon = null, AmmoProtoStats? ammo = null) =>
        new(pid, 0, 0, 0, 0, subType, Weapon: weapon, Ammo: ammo, Weight: weight);

    // ---- pure math ------------------------------------------------------

    [Fact]
    public void TotalWeightSumsItemWeightTimesStack()
    {
        var protos = new Dictionary<int, ProtoInfo> { [10] = Proto(10, 5), [20] = Proto(20, 3) };
        var items = new List<MapObject> { Item(10, stack: 4), Item(20, stack: 2) };
        Assert.Equal(4 * 5 + 2 * 3, InventoryWeight.TotalWeight(items, p => protos.GetValueOrDefault(p)));
    }

    [Fact]
    public void WeightlessItemsLikeCapsContributeNothing()
    {
        var protos = new Dictionary<int, ProtoInfo> { [41] = Proto(41, 0) };
        Assert.Equal(0, InventoryWeight.TotalWeight([Item(41, stack: 2000)], p => protos.GetValueOrDefault(p)));
    }

    [Fact]
    public void UnknownProtoCountsZeroNotThrow()
    {
        Assert.Equal(0, InventoryWeight.TotalWeight([Item(999)], _ => null));
    }

    [Fact]
    public void PowerArmorWeightIsHalved()
    {
        // proto_types.h PROTO_ID_POWER_ARMOR = 3, an ITEM_TYPE_ARMOR (subType 0). item.cc:779.
        var protos = new Dictionary<int, ProtoInfo>
        {
            [3] = Proto(3, weight: 40, subType: 0),    // power armor → /2
            [7] = Proto(7, weight: 40, subType: 0),    // ordinary armor → unchanged
        };
        Assert.Equal(20, InventoryWeight.ItemWeight(Item(3), p => protos.GetValueOrDefault(p)));
        Assert.Equal(40, InventoryWeight.ItemWeight(Item(7), p => protos.GetValueOrDefault(p)));
    }

    [Fact]
    public void ContainerAddsItsContents()
    {
        // item.cc:783 — a carried container (subType 1) adds the weight of what's inside it.
        var protos = new Dictionary<int, ProtoInfo>
        {
            [50] = Proto(50, weight: 2, subType: 1),   // a bag
            [10] = Proto(10, weight: 5),
        };
        MapObject bag = Item(50);
        bag.Inventory.Add(Item(10, stack: 3));
        Assert.Equal(2 + 3 * 5, InventoryWeight.ItemWeight(bag, p => protos.GetValueOrDefault(p)));
    }

    [Fact]
    public void WeaponAddsLoadedAmmoWeight()
    {
        // item.cc:786-795 — loaded ammo adds boxWeight × ceil(rounds / boxSize).
        var weapon = new WeaponProtoStats(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, AmmoTypePid: 88, AmmoCapacity: 30, 0);
        var protos = new Dictionary<int, ProtoInfo>
        {
            [9] = Proto(9, weight: 6, subType: 3, weapon: weapon),
            [88] = Proto(88, weight: 2, subType: 4, ammo: new AmmoProtoStats(0, Quantity: 24, 0, 0, 0, 0)),
        };
        // 24 rounds loaded → ceil(24/24)=1 box → +2; gun base 6 → 8.
        Assert.Equal(8, InventoryWeight.ItemWeight(Item(9, ammoQty: 24, ammoPid: 88), p => protos.GetValueOrDefault(p)));
        // 25 rounds → ceil(25/24)=2 boxes → +4; → 10.
        Assert.Equal(10, InventoryWeight.ItemWeight(Item(9, ammoQty: 25, ammoPid: 88), p => protos.GetValueOrDefault(p)));
        // ammoQty -1 derives the proto AmmoCapacity (30) → ceil(30/24)=2 → +4 → 10.
        Assert.Equal(10, InventoryWeight.ItemWeight(Item(9, ammoQty: -1, ammoPid: 88), p => protos.GetValueOrDefault(p)));
    }

    [Theory]
    [InlineData(100, 150, false, 0)] // under capacity
    [InlineData(150, 150, false, 0)] // exactly at capacity is NOT encumbered (strict >)
    [InlineData(151, 150, true, 1)]  // 1 over → penalty (1/40)+1 = 1
    [InlineData(200, 150, true, 2)]  // 50 over → (50/40)+1 = 2
    [InlineData(231, 150, true, 3)]  // 81 over → (81/40)+1 = 3
    public void EncumbranceAndApPenaltyMatchTheEngine(int carried, int capacity, bool enc, int penalty)
    {
        Assert.Equal(enc, InventoryWeight.IsEncumbered(carried, capacity));
        Assert.Equal(penalty, InventoryWeight.ActionPointPenalty(carried, capacity));
    }

    // ---- real game data -------------------------------------------------

    [GameDataFact]
    public void RealItemProtosParseSaneWeights()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);

        // Caps (pid 41) are weightless in Fallout 2; a 10mm SMG (pid 9) has a real weight.
        Assert.Equal(0, protos.Get(41).Weight);
        int smg = protos.Get(9).Weight;
        Assert.InRange(smg, 1, 60); // a hand weapon, single-digit-ish pounds — not a garbage/misaligned read
    }
}
