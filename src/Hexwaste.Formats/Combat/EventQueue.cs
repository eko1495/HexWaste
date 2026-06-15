using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Combat;

/// <summary>
/// A minimal timed-event queue, ported from fallout2-ce src/queue.cc — entries are
/// (due-tick, owner, type), fired when game-time reaches the due tick. P14 uses it for
/// the knockout wake (combat.cc:4805 queueAddEvent(10*(35-3*EN), critter, KNOCKOUT)).
/// Combat-scoped: cleared on combat end / map exit and NOT saved — the engine can't
/// save mid-combat, and a saved knockout is cleared on load (_critter_wake_clear).
/// </summary>
public sealed class EventQueue
{
    public enum EventType { Knockout = 1 }

    private readonly record struct Entry(long DueTicks, MapObject Owner, EventType Type);
    private readonly List<Entry> _entries = [];

    /// <summary>Schedule <paramref name="type"/> for <paramref name="owner"/>,
    /// <paramref name="delay"/> ticks from <paramref name="now"/>. Removes any existing
    /// (owner,type) first (queue.cc SFALL multi-event dedup, combat.cc:4802) and inserts
    /// in ascending due order, FIFO on ties (queue.cc:238-268).</summary>
    public void Schedule(long now, long delay, MapObject owner, EventType type)
    {
        Remove(owner, type);
        long due = now + Math.Max(delay, 0);
        int i = 0;
        while (i < _entries.Count && _entries[i].DueTicks <= due)
            i++;
        _entries.Insert(i, new Entry(due, owner, type));
    }

    /// <summary>Fire every event whose due tick has passed, in due order, calling
    /// <paramref name="handler"/>(owner, type). The due set is snapshotted first so a
    /// handler that schedules a fresh (future) event can't re-enter (queue.cc:344-369).</summary>
    public void Process(long now, Action<MapObject, EventType> handler)
    {
        var due = new List<Entry>();
        while (_entries.Count > 0 && _entries[0].DueTicks <= now)
        {
            due.Add(_entries[0]);
            _entries.RemoveAt(0);
        }
        foreach (Entry e in due)
            handler(e.Owner, e.Type);
    }

    public bool Has(MapObject owner, EventType type) =>
        _entries.Any(e => e.Owner == owner && e.Type == type);

    /// <summary>Drop every event for an owner (queue.cc:271 — on death).</summary>
    public void Remove(MapObject owner) => _entries.RemoveAll(e => e.Owner == owner);

    /// <summary>Drop one (owner,type) (queue.cc:299 — the knockout dedup / wake).</summary>
    public void Remove(MapObject owner, EventType type) =>
        _entries.RemoveAll(e => e.Owner == owner && e.Type == type);

    public void ClearAll() => _entries.Clear();

    public int Count => _entries.Count;
}
