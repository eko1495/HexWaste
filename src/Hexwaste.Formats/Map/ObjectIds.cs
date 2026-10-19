namespace Hexwaste.Formats.Map;

/// <summary>fo2ce's object id counter (oracle F12): every created object takes its id from here. The
/// in-use check is fo2ce's objectFindFirst/objectFindNext walk: objects on a map tile, all elevations.</summary>
// shortcut: fo2ce creation paths Hexwaste does not model do not advance the counter (projectiles actions.cc:780, explosion ring actions.cc:867/1608, hidden barter/loot boxes inventory.cc:4123/5060, the spatial owner scriptGetSelf creates on self_obj); upgrade when a fixture shows a SPAWN gap
public sealed class ObjectIds
{
    // _cur_id starts at 4 (scripts.cc:189) and is never reset; 12 is its value before the map load on the
    // oracle's launch path (main menu, load a slot), observed in both fixtures with a map script (obj#16).
    // shortcut: the start value is observed, not derived; upgrade if a fixture's first map-script id is not 16
    public int Current { get; private set; } = 12;

    /// <summary>ported from fallout2-ce src/scripts.cc scriptsNewObjectId(): only the first increment is
    /// checked against ids in use.</summary>
    public int Next(Func<int, bool> inUse)
    {
        do
            Current++;
        while (inUse(Current));
        return ++Current;
    }
}
