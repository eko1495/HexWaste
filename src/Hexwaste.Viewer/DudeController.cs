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
    Func<int>? movementAnimCode = null)
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
    private double _accumulatorMs;

    public int CurrentFid => Moving
        ? Fid.Build(ObjectType.Critter, Fid.Index(Dude.Fid), _movementAnimCode(), Fid.WeaponCode(Dude.Fid))
        : Dude.Fid;

    public bool WalkTo(int targetTile)
    {
        byte[]? rotations = Pathfinder.FindPath(Dude.HexTile, targetTile, isBlocked);
        if (rotations is null)
            return false;

        _rotations = rotations;
        _step = 0;
        Frame = 0;
        OffsetX = 0;
        OffsetY = 0;
        _accumulatorMs = 0;
        Dude.Rotation = rotations[0];
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

        FrmFile frm = frmCache.GetFrm(CurrentFid);
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
                // Something moved in the way mid-walk; the engine re-paths, the
                // PoC simply stops.
                Stop();
                return;
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
