using FalloutPoc.Formats.Hex;
using FalloutPoc.Formats.Int;
using FalloutPoc.Formats.Map;
using FalloutPoc.Formats.Proto;

namespace FalloutPoc.Formats.Tests;

public class FidRoundTripTests
{
    [Theory]
    [InlineData(ObjectType.Critter, 42, 16, 3, 2)]
    [InlineData(ObjectType.Tile, 4095, 0, 0, 0)]
    [InlineData(ObjectType.Misc, 17, 255, 15, 5)]
    public void BuildAndExtractRoundTrip(ObjectType type, int index, int anim, int weapon, int rotation)
    {
        int fid = Fid.Build(type, index, anim, weapon, rotation);
        Assert.Equal(type, Fid.Type(fid));
        Assert.Equal(index, Fid.Index(fid));
        Assert.Equal(anim, Fid.AnimType(fid));
        Assert.Equal(weapon, Fid.WeaponCode(fid));
        Assert.Equal(rotation, Fid.Rotation(fid));
    }

    [Fact]
    public void ExitGridPidRange()
    {
        Assert.True(Fid.IsExitGridPid(0x5000010));
        Assert.True(Fid.IsExitGridPid(0x5000017));
        Assert.False(Fid.IsExitGridPid(0x5000018));
        Assert.False(Fid.IsExitGridPid(0x500000F));
    }
}

public class PathfinderCapTests
{
    [Fact]
    public void OverCapSearchReturnsNullWithoutHanging()
    {
        // Wall off a huge labyrinth-free open field and ask for a path far
        // beyond the 2000-expansion cap: from one corner area to the other.
        byte[]? path = Pathfinder.FindPath(201 * 1, HexGrid.Size - 402, _ => false);
        Assert.Null(path); // distance >> cap — engine-faithful give-up
    }

    [Fact]
    public void PathJustUnderCapResolves()
    {
        int from = 100 * HexGrid.Width + 100;
        int to = HexGrid.TileInDirection(from, 2, 30);
        byte[]? path = Pathfinder.FindPath(from, to, _ => false);
        Assert.NotNull(path);
        Assert.Equal(30, path.Length);
    }
}

public class HexDistanceTests
{
    [Fact]
    public void DistanceMatchesStraightWalks()
    {
        int start = 100 * HexGrid.Width + 100;
        for (int rotation = 0; rotation < 6; rotation++)
        {
            int target = HexGrid.TileInDirection(start, rotation, 7);
            Assert.Equal(7, HexGrid.Distance(start, target));
        }
    }

    [Fact]
    public void InvalidTilesYield9999()
    {
        Assert.Equal(9999, HexGrid.Distance(-1, 100));
        Assert.Equal(9999, HexGrid.Distance(100, -1));
    }
}

public class CapsAndTimerTests
{
    private static MapObject NewObject(int pid, int tile = 0) => new()
    {
        Id = 1,
        HexTile = tile,
        X = 0,
        Y = 0,
        Frame = 0,
        Rotation = 0,
        Fid = 0,
        Flags = 0,
        Pid = pid,
        Sid = -1,
    };

    [GameDataFact]
    public void CapsAdjustPaysAndRefuses()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        var host = new ScriptHost(vfs, ScriptList.Load(vfs), protos);

        MapObject holder = NewObject(0x01000001);
        Assert.Equal(0, host.CapsTotal(holder));

        // Paying with no caps must REFUSE (the phase-5 bug: the old stub
        // returned success and gave goods away free).
        Assert.Equal(-1, host.CapsAdjust(holder, -50));

        Assert.Equal(0, host.CapsAdjust(holder, 100)); // creates a money stack
        Assert.Equal(100, host.CapsTotal(holder));

        Assert.Equal(0, host.CapsAdjust(holder, -40));
        Assert.Equal(60, host.CapsTotal(holder));

        Assert.Equal(-1, host.CapsAdjust(holder, -61)); // more than total
        Assert.Equal(60, host.CapsTotal(holder)); // unchanged after refusal

        Assert.Equal(0, host.CapsAdjust(holder, -60)); // drain exactly
        Assert.Equal(0, host.CapsTotal(holder));
        Assert.DoesNotContain(holder.Inventory, i => i.Pid == 41); // stack destroyed
    }

    [GameDataFact]
    public void TimerQueueRunsDueProcsInOrderAndClears()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        var host = new ScriptHost(vfs, ScriptList.Load(vfs), protos);

        using Stream stream = vfs.OpenRead(@"maps\denbus1.map");
        MapFile map = MapFile.Load(stream, protos);

        // A scripted object with a timed_event_p_proc: any door script works.
        MapObject? door = map.Elevations[0]!.Objects
            .FirstOrDefault(o => o.Sid != -1 && map.ScriptsBySid.ContainsKey(o.Sid)
                && o.HexTile == 16862);
        Assert.NotNull(door);

        host.AddTimer(map, door, delayTicks: 10, param: 1); // 1 game-second
        Assert.Equal(1, host.PendingTimerCount);

        host.PumpTimers(500, null); // 0.5s — not due yet
        Assert.Equal(1, host.PendingTimerCount);

        host.PumpTimers(600, null); // past due — fires (the proc may re-arm)
        Assert.True(host.PendingTimerCount <= 1, "timer fired at most once and may re-arm");

        host.ClearTimers();
        Assert.Equal(0, host.PendingTimerCount);
    }
}
