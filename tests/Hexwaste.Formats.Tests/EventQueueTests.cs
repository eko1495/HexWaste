using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Map;
using ET = Hexwaste.Formats.Combat.EventQueue.EventType;

namespace Hexwaste.Formats.Tests;

public class EventQueueTests
{
    private static MapObject Obj(int id) => new()
    {
        Id = id, HexTile = id, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = 0x01000000, Flags = 0, Pid = 0x01000001, Sid = -1,
    };

    [Fact]
    public void ProcessFiresOnlyDueEventsInDueOrder()
    {
        var q = new EventQueue();
        MapObject a = Obj(1), b = Obj(2), c = Obj(3);
        q.Schedule(now: 0, delay: 300, a, ET.Knockout);
        q.Schedule(now: 0, delay: 100, b, ET.Knockout);
        q.Schedule(now: 0, delay: 200, c, ET.Knockout);

        var fired = new List<MapObject>();
        q.Process(now: 150, (o, _) => fired.Add(o));   // only b (due 100)
        Assert.Equal([b], fired);
        Assert.Equal(2, q.Count);

        fired.Clear();
        q.Process(now: 1000, (o, _) => fired.Add(o));   // c (200) then a (300), in order
        Assert.Equal([c, a], fired);
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void EqualDueEventsFireFifo()
    {
        var q = new EventQueue();
        MapObject a = Obj(1), b = Obj(2);
        q.Schedule(now: 0, delay: 100, a, ET.Knockout);
        q.Schedule(now: 0, delay: 100, b, ET.Knockout); // same due as a, scheduled after

        var fired = new List<MapObject>();
        q.Process(now: 100, (o, _) => fired.Add(o));
        Assert.Equal([a, b], fired); // insertion order preserved on ties
    }

    [Fact]
    public void RescheduleDedupsSameOwnerAndType()
    {
        // combat.cc:4802 removes the existing knockout before adding — no double-wake.
        var q = new EventQueue();
        MapObject a = Obj(1);
        q.Schedule(now: 0, delay: 100, a, ET.Knockout);
        q.Schedule(now: 0, delay: 250, a, ET.Knockout); // replaces, does not stack
        Assert.Equal(1, q.Count);

        var fired = new List<MapObject>();
        q.Process(now: 100, (o, _) => fired.Add(o)); // the old (100) is gone
        Assert.Empty(fired);
        q.Process(now: 250, (o, _) => fired.Add(o)); // the live (250) fires
        Assert.Equal([a], fired);
    }

    [Fact]
    public void RemoveByOwnerDropsAllItsEvents()
    {
        var q = new EventQueue();
        MapObject a = Obj(1), b = Obj(2);
        q.Schedule(now: 0, delay: 100, a, ET.Knockout);
        q.Schedule(now: 0, delay: 100, b, ET.Knockout);
        q.Remove(a); // a dies
        Assert.False(q.Has(a, ET.Knockout));
        Assert.True(q.Has(b, ET.Knockout));
        Assert.Equal(1, q.Count);
    }

    [Fact]
    public void HandlerSchedulingAFutureEventDoesNotReenter()
    {
        var q = new EventQueue();
        MapObject a = Obj(1);
        q.Schedule(now: 0, delay: 100, a, ET.Knockout);

        int fires = 0;
        q.Process(now: 100, (o, t) =>
        {
            fires++;
            if (fires == 1)
                q.Schedule(now: 100, delay: 50, o, t); // future (due 150) — must not fire now
        });
        Assert.Equal(1, fires);
        Assert.True(q.Has(a, ET.Knockout)); // the rescheduled one is still pending
    }

    [Theory]
    [InlineData(0, 350)]   // 10*(35-3*0)
    [InlineData(5, 200)]   // 10*(35-15)
    [InlineData(10, 50)]   // 10*(35-30)
    public void KnockoutWakeDelayMatchesEngineFormula(int endurance, int expectedDelay)
    {
        // combat.cc:4805 queueAddEvent(10*(35-3*EN), ...). The caller computes the delay;
        // assert the arithmetic + that the queue wakes exactly at it.
        int delay = 10 * (35 - 3 * endurance);
        Assert.Equal(expectedDelay, delay);

        var q = new EventQueue();
        MapObject a = Obj(1);
        q.Schedule(now: 1000, delay, a, ET.Knockout);
        var fired = new List<MapObject>();
        q.Process(now: 1000 + delay - 1, (o, _) => fired.Add(o)); // one tick early: asleep
        Assert.Empty(fired);
        q.Process(now: 1000 + delay, (o, _) => fired.Add(o));     // wakes
        Assert.Equal([a], fired);
    }
}
