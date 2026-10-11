namespace Hexwaste.Formats.Int;

/// <summary>The critter script list (gScriptLists[SCRIPT_TYPE_CRITTER]) as the critter heartbeat sees it:
/// ordered sids, appended by scriptAdd, swap-removed by scriptRemove, walked round-robin by
/// _script_chk_critters. The counter (_count_) is never reset, not even by <see cref="Clear"/>.</summary>
public sealed class CritterScriptList
{
    private readonly List<int> _sids = [];
    private int _count;

    public int Count => _sids.Count;

    public IReadOnlyList<int> Sids => _sids;

    // ported from fallout2-ce src/scripts.cc scriptAdd(): new scripts go to the tail.
    public void Add(int sid) => _sids.Add(sid);

    // ported from fallout2-ce src/scripts.cc scriptRemove(): the tail entry moves into the removed slot.
    public void Remove(int sid)
    {
        int index = _sids.IndexOf(sid);
        if (index < 0)
            return;
        int last = _sids.Count - 1;
        _sids[index] = _sids[last];
        _sids.RemoveAt(last);
    }

    // ported from fallout2-ce src/scripts.cc _script_chk_critters(): increment, wrap, then run that slot.
    public int? Next()
    {
        _count++;
        if (_count >= _sids.Count)
            _count = 0;
        return _count < _sids.Count ? _sids[_count] : null;
    }

    public void Clear() => _sids.Clear();
}
