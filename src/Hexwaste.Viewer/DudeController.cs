using Hexwaste.Formats;
using Hexwaste.Formats.Frm;
using Hexwaste.Formats.Hex;
using Hexwaste.Formats.Map;

namespace Hexwaste.Viewer;

/// <summary>
/// The player stand-in: a critter walking the hex grid along A* paths.
/// Movement is ported from fallout2-ce src/animation.cc _object_move():
/// every animation frame applies that frame's offset delta to the pixel
/// offset; when it crosses the one-hex screen delta for the current rotation
/// (tile.cc _off_tile / dword_51D984), the dude advances one tile and the
/// remainder carries over, so walking speed comes entirely from the FRM data.
/// </summary>
public sealed class DudeController(MapObject dude, FrmCache frmCache, Func<int, bool> isBlocked,
    Func<int>? movementAnimCode = null,
    Func<int, bool>? isUsableClosedDoor = null, Action<int>? openDoorAt = null)
{
    private const int AnimWalk = 1;

    // The movement anim-code (walk/run): the dude passes a run-selector (P34-M3); NPC walkers
    // pass nothing and keep walking. ported from fallout2-ce src/animation.cc animationRegisterRunToTile().
    private readonly Func<int> _movementAnimCode = movementAnimCode ?? (() => AnimWalk);

    public MapObject Dude { get; } = dude;

    /// <summary>Pixel offset from the current hex (the engine's obj->x/y during movement).</summary>
    public int OffsetX { get; private set; }
    public int OffsetY { get; private set; }
    public int Frame { get; private set; }

    public bool Moving => _rotations is not null;

    /// <summary>Raised when the dude enters a new hex (camera follow, z-resort).</summary>
    public event Action<int>? TileChanged;

    private byte[]? _rotations;
    private int _step;
    private int _targetTile = -1; // the walk's destination — re-paths aim here (sad->field_24)
    private double _accumulatorMs;

    public int CurrentFid => Moving
        ? Fid.Build(ObjectType.Critter, Fid.Index(Dude.Fid), _movementAnimCode(), Fid.WeaponCode(Dude.Fid))
        : Dude.Fid;

    public bool WalkTo(int targetTile)
    {
        // Player movement never paths to a BLOCKED destination. The pathfinder deliberately exempts the
        // goal tile from its blocking check (so AI can path adjacent to a target, and Reachable() works),
        // but the engine's click-to-move passes _make_path(..., a5=1) which refuses a blocked goal
        // (game_mouse.cc:807 → animation.cc:1718-1722). Without this guard the dude steps ONTO a wall, and
        // clicking into it repeatedly re-paths from that blocked tile and walks him straight through.
        if (isBlocked(targetTile))
            return false;

        // P109: closed-but-usable doors are passable — the engine's pathfinder routes through
        // them (canUseDoor, animation.cc:1802-1808) and the walker opens them on contact.
        byte[]? rotations = Pathfinder.FindPath(Dude.HexTile, targetTile, isBlocked, isUsableClosedDoor);
        if (rotations is null)
            return false;

        _targetTile = targetTile;
        _rotations = rotations;
        _step = 0;
        Frame = 0;
        OffsetX = 0;
        OffsetY = 0;
        _accumulatorMs = 0;
        Dude.Rotation = rotations[0];
        return true;
    }

    /// <summary>P109: walk ADJACENT to a (possibly blocked) target tile — the interaction approach.
    /// fo2ce's use/talk/pickup actions path to the object's tile (the pathfinder exempts the goal
    /// tile from blocking) and stop one step short (_action_use_an_object → move-to-object).
    /// Returns false when there is no path or the dude is already adjacent.</summary>
    public bool WalkToward(int targetTile)
    {
        byte[]? rotations = Pathfinder.FindPath(Dude.HexTile, targetTile, isBlocked, isUsableClosedDoor);
        if (rotations is null || rotations.Length < 2)
            return false;

        byte[] trimmed = rotations[..^1];
        int dest = Dude.HexTile;
        foreach (byte r in trimmed)
            dest = HexGrid.TileInDirection(dest, r);

        _targetTile = dest; // mid-walk re-paths aim at the adjacent stop, not the occupied tile
        _rotations = trimmed;
        _step = 0;
        Frame = 0;
        OffsetX = 0;
        OffsetY = 0;
        _accumulatorMs = 0;
        Dude.Rotation = trimmed[0];
        return true;
    }

    public void Stop()
    {
        _rotations = null;
        Frame = 0;
        OffsetX = 0;
        OffsetY = 0;
    }

    public void Update(double elapsedMs)
    {
        if (_rotations is null)
            return;

        // Missing/corrupt walk art (off-slice critter absent from a partial extraction): abort the
        // walk instead of crashing the loop — the object stays put and is skipped by the draw path.
        if (!frmCache.TryGetFrm(CurrentFid, out FrmFile? frm))
        {
            Stop();
            return;
        }
        double msPerFrame = 1000.0 / frm.FramesPerSecond;
        _accumulatorMs += elapsedMs;

        while (_accumulatorMs >= msPerFrame && _rotations is not null)
        {
            _accumulatorMs -= msPerFrame;
            int rotation = _rotations[_step];

            Frame = (Frame + 1) % frm.FrameCount;
            FrmFrame frame = frm.GetFrame(Frame, rotation);
            OffsetX += frame.OffsetX;
            OffsetY += frame.OffsetY;

            // ported from _object_move(): a hex is crossed when the offset
            // reaches the per-rotation one-hex screen delta.
            int hexX = HexGrid.StepScreenX[rotation];
            int hexY = HexGrid.StepScreenY[rotation];
            bool crossed = (hexX > 0 && hexX <= OffsetX) || (hexX < 0 && hexX >= OffsetX)
                || (hexY > 0 && hexY <= OffsetY) || (hexY < 0 && hexY >= OffsetY);
            if (!crossed)
                continue;

            int nextTile = HexGrid.TileInDirection(Dude.HexTile, rotation);
            if (isBlocked(nextTile) && _step < _rotations.Length - 1)
            {
                // ported from _object_move (animation.cc:2578-2600): a usable closed door in the
                // way is auto-opened and the walk continues; any other obstacle triggers a re-path
                // to the original destination, and only a failed re-path stops the walk.
                if (isUsableClosedDoor?.Invoke(nextTile) == true && openDoorAt is not null)
                {
                    openDoorAt(nextTile); // the host opens it + unblocks the tile; keep walking
                }
                else
                {
                    byte[]? repath = Pathfinder.FindPath(Dude.HexTile, _targetTile, isBlocked, isUsableClosedDoor);
                    if (repath is null)
                    {
                        Stop();
                        return;
                    }
                    // Snap to the current tile and restart on the new path (animation.cc:2584-2593).
                    _rotations = repath;
                    _step = 0;
                    Frame = 0;
                    OffsetX = 0;
                    OffsetY = 0;
                    Dude.Rotation = repath[0];
                    return;
                }
            }

            OffsetX -= hexX;
            OffsetY -= hexY;
            Dude.HexTile = nextTile;
            TileChanged?.Invoke(nextTile);

            // The handler may have halted the walk (e.g. phase-18 AP-gating Stop()s when
            // the dude runs out of action points) — _rotations is now null, so bail before
            // touching it again.
            if (_rotations is null)
                return;

            _step++;
            if (_step >= _rotations.Length)
            {
                Stop();
                return;
            }

            Dude.Rotation = _rotations[_step];
        }
    }
}
