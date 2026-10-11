using Hexwaste.Formats.Int;

namespace Hexwaste.Formats.Tests;

public class CritterScriptListTests
{
    private static CritterScriptList Of(params int[] sids)
    {
        var list = new CritterScriptList();
        foreach (int sid in sids)
            list.Add(sid);
        return list;
    }

    [Fact]
    public void FirstNextRunsSlotOneThenWraps()
    {
        CritterScriptList list = Of(10, 11, 12);
        Assert.Equal(11, list.Next()); // _count_ starts at 0 and is incremented before use
        Assert.Equal(12, list.Next());
        Assert.Equal(10, list.Next()); // wrap at Count
        Assert.Equal(11, list.Next());
    }

    [Fact]
    public void RemoveMovesTheTailIntoTheRemovedSlot()
    {
        CritterScriptList list = Of(10, 11, 12, 13);
        list.Remove(11);
        Assert.Equal([10, 13, 12], list.Sids);
        list.Remove(12); // removing the last entry just shrinks
        Assert.Equal([10, 13], list.Sids);
    }

    [Fact]
    public void RemovingAMissingSidIsANoOp()
    {
        CritterScriptList list = Of(10, 11);
        list.Remove(99);
        list.Remove(-1);
        Assert.Equal([10, 11], list.Sids);
    }

    [Fact]
    public void EmptyListYieldsNull()
    {
        Assert.Null(new CritterScriptList().Next());
    }

    [Fact]
    public void ClearKeepsTheCounter()
    {
        CritterScriptList list = Of(10, 11, 12);
        list.Next(); // counter 1
        list.Next(); // counter 2
        list.Clear();
        list.Add(20);
        list.Add(21);
        list.Add(22);
        list.Add(23);
        Assert.Equal(23, list.Next()); // counter 3, not 1
    }

    [Fact]
    public void DuplicatesAreAllowedAndRemoveTakesTheFirst()
    {
        CritterScriptList list = Of(10, 77, 11, 77);
        list.Remove(77);
        Assert.Equal([10, 77, 11], list.Sids);
    }
}
