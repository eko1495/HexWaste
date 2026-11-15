namespace Hexwaste.Formats.Hex;

/// <summary>
/// A* over the hex grid, ported from fallout2-ce src/animation.cc
/// pathfinderFindPath(): uniform step cost 50, screen-space
/// _idist heuristic, capped at 2000 expanded nodes. +10 turn penalty only out of combat. Returns the sequence of
/// rotations to walk, or null when unreachable. The reference takes an `a5`
/// flag; the default here reproduces `a5 = 0`, under which the goal tile is
/// never blocking-checked (so paths can end next to/at an occupied target,
/// e.g. a melee approach). Pass <paramref name="requireFreeDestination"/> to
/// reproduce `a5 = 1`, which refuses a path onto a blocked destination.
/// </summary>
public static class Pathfinder
{
    private const int MaxNodes = 2000;
    private const int StepCost = 50;
    private const int TurnPenalty = 10;

    /// <param name="isPassableDoor">P109: a blocked tile the walker may nonetheless path through —
    /// a closed, usable door. The engine's pathfinder exempts tiles whose blocker passes canUseDoor
    /// (animation.cc:1802-1808: unlocked scenery door; walk-thru additionally required for the dude);
    /// the walker then auto-opens the door on contact (_object_move, animation.cc:2599). Null = no
    /// exemption (combat AI and NPC ambient walkers keep their current behavior).</param>
    /// <param name="requireFreeDestination">ported from fallout2-ce src/animation.cc
    /// pathfinderFindPath (:1716-1722): the reference's `a5` argument. When set, a BLOCKED
    /// destination yields no path at all — the function returns 0 before searching. Callers that
    /// mirror `_make_path(..., 1)` pass true; the default false reproduces `a5 = 0`, under which the
    /// goal tile is exempt from the blocked test so a path can end on an occupied tile (a melee
    /// approach). F18: TryFlee needed this, because a retreat onto a blocked tile is refused by the
    /// walker and the flight silently never happens.</param>
    /// <param name="inCombat">isInCombat(): the +10 turn penalty is skipped in combat.</param>
    public static byte[]? FindPath(int from, int to, Func<int, bool> isBlocked,
        Func<int, bool>? isPassableDoor = null, bool requireFreeDestination = false, bool inCombat = false)
    {
        if (!HexGrid.IsValid(from) || !HexGrid.IsValid(to) || from == to)
            return null;

        // ported from fallout2-ce src/animation.cc pathfinderFindPath (:1718-1722): `if (a5) { if
        // (callback(object, to, elevation) != nullptr) return 0; }` — the destination's own blocker is
        // tested up front. The door exemption applies here exactly as it does to intermediate tiles.
        if (requireFreeDestination && isBlocked(to) && !(isPassableDoor?.Invoke(to) ?? false))
            return null;

        // ported from fallout2-ce src/animation.cc pathfinderFindPath (:1739-1880): a fixed 2000-slot open
        // list, a new node takes the FIRST free slot, and the linear scan keeps the LOWEST slot among equal
        // cost+estimate — that is the engine's tie-break, so equal-length paths pick the same middle hexes.
        var processed = new HashSet<int> { from };
        var open = new Node?[MaxNodes];
        open[0] = new Node(from, null, 0, 0, HexGrid.ScreenDistance(from, to));
        int openCount = 1;
        int closed = 0;
        while (true)
        {
            int best = -1;
            for (int i = 0, seen = 0; seen < openCount; i++)
            {
                if (open[i] is not { } n)
                    continue;
                seen++;
                if (best < 0 || n.Cost + n.Estimate < open[best]!.Cost + open[best]!.Estimate)
                    best = i;
            }
            Node current = open[best]!;
            open[best] = null;
            openCount--;

            if (current.Tile == to)
                return BuildRotations(current);

            if (++closed >= MaxNodes)
                return null;

            for (int rotation = 0; rotation < HexGrid.RotationCount; rotation++)
            {
                int neighbor = HexGrid.TileInDirection(current.Tile, rotation);
                if (processed.Contains(neighbor))
                    continue;

                if (neighbor != to && isBlocked(neighbor) && !(isPassableDoor?.Invoke(neighbor) ?? false))
                    continue;

                int slot = Array.IndexOf(open, null);
                if (++openCount >= MaxNodes)
                    return null;
                processed.Add(neighbor);

                // The turn penalty applies only OUT of combat, and the start node counts as rotation 0.
                int cost = current.Cost + StepCost;
                if (!inCombat && current.Rotation != rotation)
                    cost += TurnPenalty;
                // shortcut: no +400 (gecko +100) radioactive-goo cost for critters (animation.cc:1847-1862); port when goo maps get an oracle fixture.

                open[slot] = new Node(neighbor, current, rotation, cost, HexGrid.ScreenDistance(neighbor, to));
            }

            if (openCount == 0)
                return null;
        }
    }

    private static byte[] BuildRotations(Node goal)
    {
        var rotations = new List<byte>();
        for (Node? node = goal; node?.Parent is not null; node = node.Parent)
            rotations.Add((byte)node.Rotation);
        rotations.Reverse();
        return [.. rotations];
    }

    private sealed record Node(int Tile, Node? Parent, int Rotation, int Cost, int Estimate);
}
