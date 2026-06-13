using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Hex;

namespace Hexwaste.Formats.Map;

/// <summary>One critter (or scenery) to drop on the encounter map: its proto, the
/// scripts.lst index to bind (-1 = none), the placed tile + facing, whether it is a
/// corpse, and the inventory it carries.</summary>
public sealed record SpawnInstruction(int Pid, int ScriptIndex, int Tile, int Rotation,
    bool Dead, IReadOnlyList<SpawnItem> Items);

/// <summary>An item a spawned critter carries; <see cref="Wielded"/> equips it
/// in-hand, <see cref="Worn"/> equips it as armor (the rest sit in the bag).</summary>
public sealed record SpawnItem(int Pid, int Count, bool Wielded, bool Worn);

/// <summary>
/// The random-encounter group spawn, ported from fallout2-ce src/worldmap.cc
/// (wmSetupRandomEncounter :3657 / wmSetupCritterObjs :3771 / wmSetupRndNextTileNum*
/// :3903-4079). Pure + seeded off <see cref="ICombatRng"/> so a spawn is a golden
/// transcript like combat. Given the picked <see cref="EncounterResult"/>, the world
/// (for the groups), the dude's tile/Perception, the party size, the map's
/// random_start_points, and movement-blocking/reachability predicates, it returns the
/// list of objects to create — the viewer turns each into a MapObject (phase-10 M3).
///
/// v1 divergences (documented): per-member If() conditions are not parsed, so every
/// group member spawns (the engine's wmEvalConditional skip is omitted); the X
/// FIGHTING Y combat-lock between sub-groups is skipped (treated as neutrals — AMBUSH
/// hostility is script-side via the critter_p_proc heartbeat); the placement gate
/// reuses the movement-blocking predicate for the engine's shoot-blocking LoF check;
/// the difficulty −2/+2 skew and Cautious-Nature perk are skipped (Normal only).
/// </summary>
public static class EncounterSpawner
{
    /// <summary>randomBetween in fallout2-ce is INCLUSIVE of both ends; ICombatRng.Next
    /// is exclusive on the top, so shift by one.</summary>
    private static int Between(ICombatRng rng, int min, int max) =>
        max <= min ? min : rng.Next(min, max + 1);

    public static IReadOnlyList<SpawnInstruction> Plan(EncounterResult encounter, WorldmapFile world,
        ICombatRng rng, int dudeTile, int dudePerception, int partyCount,
        IReadOnlyList<int> startTiles, Func<int, bool> isBlocked, Func<int, int, bool> reachable)
    {
        var result = new List<SpawnInstruction>();

        // wmSetupRandomEncounter: each Enc:(min-max) GROUP sub-entry rolls its own
        // size, then wmSetupCritterObjs lays the group out. +2 if a real party.
        foreach (EncounterSpawn sub in encounter.Entry.Spawns)
        {
            int critterCount = Between(rng, sub.Min, sub.Max);
            if (partyCount > 2)
                critterCount += 2;
            if (critterCount == 0)
                continue;

            if (world.Group(sub.Group) is { } group)
                SpawnGroup(group, critterCount, rng, dudeTile, dudePerception, startTiles,
                    isBlocked, reachable, result);
        }

        return result;
    }

    // ported from fallout2-ce src/worldmap.cc wmSetupCritterObjs()
    private static void SpawnGroup(EncounterGroup group, int critterCount, ICombatRng rng,
        int dudeTile, int dudePerception, IReadOnlyList<int> startTiles,
        Func<int, bool> isBlocked, Func<int, int, bool> reachable, List<SpawnInstruction> output)
    {
        var f = new Formation(group.Formation, rng, dudeTile, startTiles);

        // The engine places critters one at a time, so each later placement sees the
        // earlier ones as blocking (wmEvalTileNumForPlacement → _obj_blocking_at). The
        // planner creates nothing, so it tracks its own placed tiles to the same end —
        // two spawns never share a hex.
        var placed = new HashSet<int>();

        foreach (GroupMember member in group.Members)
        {
            if (member.Pid <= 0) // pid -1 (and our parse's 0 for a pid-less member, e.g. Special1) = nothing
                continue;

            // USE_RATIO: ratio*group/100; SINGLE (omitted ratio): exactly 1. Clamp ≥1.
            int count = member.Single ? 1 : member.Ratio * critterCount / 100;
            if (count < 1)
                count = 1;

            for (int i = 0; i < count; i++)
            {
                if (f.NextTile(group, dudePerception, t => isBlocked(t) || placed.Contains(t), reachable) is not { } tile)
                    continue; // 25-retry exhausted: skip this critter (engine continues)
                placed.Add(tile);

                int rotation = HexGrid.RotationTo(tile, dudeTile); // face the dude
                var items = new List<SpawnItem>();
                foreach (EncItem it in member.Items)
                {
                    int qty = Between(rng, it.Min, it.Max);
                    if (qty == 0)
                        continue;
                    items.Add(new SpawnItem(it.Pid, qty, it.Wielded, it.Worn));
                }
                output.Add(new SpawnInstruction(member.Pid, member.ScriptIndex, tile, rotation,
                    member.Dead, items));
            }
        }
    }

    /// <summary>The formation tile generator, ported from wmSetupRndNextTileNumInit
    /// (:3903) + wmSetupRndNextTileNum (:3969). Surrounding rings the dude at
    /// Perception±2; the cluster formations (line/wedge/cone/huddle) anchor on a random
    /// start point and step out by Spacing. Stateful across calls within one group.</summary>
    private sealed class Formation
    {
        private readonly string _type;
        private readonly ICombatRng _rng;
        private readonly int _dudeTile;
        private readonly int[] _centerTiles = [-1, -1];
        private readonly int[] _tileDirs = [0, 0];
        private static readonly int[] RotOffsets = [1, 5]; // wmRndRotOffsets: [0]=1, [1]=5
        private readonly int _originalCenter;
        private int _index;     // wmRndIndex (alternates the two cluster anchors)
        private int _callCount; // wmRndCallCount (first call returns the anchor unchanged)

        public Formation(string type, ICombatRng rng, int dudeTile, IReadOnlyList<int> startTiles)
        {
            _type = type;
            _rng = rng;
            _dudeTile = dudeTile;

            if (type == "surrounding")
            {
                _centerTiles[0] = dudeTile;
                _tileDirs[0] = Between(rng, 0, HexGrid.RotationCount - 1);
            }
            else
            {
                int anchor = startTiles.Count > 0 ? startTiles[Between(rng, 0, startTiles.Count - 1)] : dudeTile;
                _centerTiles[0] = _centerTiles[1] = anchor;
                _tileDirs[0] = HexGrid.RotationTo(_centerTiles[0], dudeTile);
                _tileDirs[1] = HexGrid.RotationTo(_centerTiles[1], dudeTile);
            }
            _originalCenter = _centerTiles[0];
        }

        /// <summary>The next placement tile, or null when 25 retries can't find an
        /// unblocked, reachable hex.</summary>
        public int? NextTile(EncounterGroup group, int dudePerception,
            Func<int, bool> isBlocked, Func<int, int, bool> reachable)
        {
            for (int attempt = 0; ; attempt++)
            {
                int tile = Step(group, dudePerception);
                _callCount++;

                if (!isBlocked(tile) && reachable(_dudeTile, tile))
                    return tile;
                if (HexGrid.Distance(_originalCenter, _centerTiles[_index]) > 25 || attempt > 25)
                    return null;
            }
        }

        private int Step(EncounterGroup group, int dudePerception)
        {
            int spacing = group.Spacing;
            switch (_type)
            {
                case "surrounding":
                {
                    int distance = group.Distance > 0
                        ? group.Distance
                        : Math.Max(0, Between(_rng, -2, 2) + dudePerception);
                    int origin = HexGrid.TileInDirection(_dudeTile, _tileDirs[0], distance);
                    _tileDirs[0] = (_tileDirs[0] + 1) % HexGrid.RotationCount;
                    int rDist = Between(_rng, 0, distance / 2);
                    int rRot = Between(_rng, 0, HexGrid.RotationCount - 1);
                    return HexGrid.TileInDirection(origin, (rRot + _tileDirs[0]) % HexGrid.RotationCount, rDist);
                }
                case "straight_line":
                case "double_line":
                {
                    int tile = _centerTiles[_index];
                    if (_callCount != 0)
                    {
                        int rot = (RotOffsets[_index] + _tileDirs[_index]) % HexGrid.RotationCount;
                        int origin = HexGrid.TileInDirection(_centerTiles[_index], rot, spacing);
                        tile = HexGrid.TileInDirection(origin, (rot + RotOffsets[_index]) % HexGrid.RotationCount, spacing);
                        _centerTiles[_index] = tile;
                        _index = 1 - _index;
                    }
                    return tile;
                }
                case "wedge":
                {
                    int tile = _centerTiles[_index];
                    if (_callCount != 0)
                    {
                        tile = HexGrid.TileInDirection(_centerTiles[_index],
                            (RotOffsets[_index] + _tileDirs[_index]) % HexGrid.RotationCount, spacing);
                        _centerTiles[_index] = tile;
                        _index = 1 - _index;
                    }
                    return tile;
                }
                case "cone":
                {
                    int tile = _centerTiles[_index];
                    if (_callCount != 0)
                    {
                        tile = HexGrid.TileInDirection(_centerTiles[_index],
                            (_tileDirs[_index] + 3 + RotOffsets[_index]) % HexGrid.RotationCount, spacing);
                        _centerTiles[_index] = tile;
                        _index = 1 - _index;
                    }
                    return tile;
                }
                case "huddle":
                default:
                {
                    int tile = _centerTiles[0];
                    if (_callCount != 0)
                    {
                        _tileDirs[0] = (_tileDirs[0] + 1) % HexGrid.RotationCount;
                        tile = HexGrid.TileInDirection(_centerTiles[0], _tileDirs[0], spacing);
                        _centerTiles[0] = tile;
                    }
                    return tile;
                }
            }
        }
    }
}
