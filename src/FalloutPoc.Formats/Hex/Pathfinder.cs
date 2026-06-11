namespace FalloutPoc.Formats.Hex;

/// <summary>
/// A* over the hex grid, ported from fallout2-ce src/animation.cc
/// pathfinderFindPath(): uniform step cost 50, +10 turn penalty, screen-space
/// _idist heuristic, capped at 2000 expanded nodes. Returns the sequence of
/// rotations to walk, or null when unreachable. The goal tile itself is never
/// blocking-checked (so paths can end next to/at an occupied target), matching
/// the original.
/// </summary>
public static class Pathfinder
{
    private const int MaxNodes = 2000;
    private const int StepCost = 50;
    private const int TurnPenalty = 10;

    public static byte[]? FindPath(int from, int to, Func<int, bool> isBlocked)
    {
        if (!HexGrid.IsValid(from) || !HexGrid.IsValid(to) || from == to)
            return null;

        var processed = new HashSet<int> { from };
        var open = new PriorityQueue<Node, int>();
        var start = new Node(from, null, 0, 0);
        open.Enqueue(start, HexGrid.ScreenDistance(from, to));

        int expanded = 0;
        while (open.TryDequeue(out Node? current, out _))
        {
            if (current.Tile == to)
                return BuildRotations(current);

            if (++expanded >= MaxNodes)
                return null;

            for (int rotation = 0; rotation < HexGrid.RotationCount; rotation++)
            {
                int neighbor = HexGrid.TileInDirection(current.Tile, rotation);
                if (!processed.Add(neighbor))
                    continue;

                if (neighbor != to && isBlocked(neighbor))
                    continue;

                int cost = current.Cost + StepCost;
                if (current.Parent is not null && current.Rotation != rotation)
                    cost += TurnPenalty;

                var node = new Node(neighbor, current, rotation, cost);
                open.Enqueue(node, cost + HexGrid.ScreenDistance(neighbor, to));
            }
        }

        return null;
    }

    private static byte[] BuildRotations(Node goal)
    {
        var rotations = new List<byte>();
        for (Node? node = goal; node?.Parent is not null; node = node.Parent)
            rotations.Add((byte)node.Rotation);
        rotations.Reverse();
        return [.. rotations];
    }

    private sealed record Node(int Tile, Node? Parent, int Rotation, int Cost);
}
