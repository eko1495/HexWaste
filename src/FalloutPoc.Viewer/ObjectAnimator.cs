using FalloutPoc.Formats;
using FalloutPoc.Formats.Frm;
using FalloutPoc.Formats.Map;

namespace FalloutPoc.Viewer;

/// <summary>
/// Frame-sequencing state for one animated map object, advanced like
/// fallout2-ce src/animation.cc _object_animate(): each tick advances one
/// frame at the FRM's fps and applies THAT frame's per-frame offset delta
/// (offsets accumulate across frames); wrapping to frame 0 resets the
/// accumulated shift.
/// </summary>
public sealed class AnimationState
{
    /// <summary>FID drawn instead of the object's own (e.g. walk-cycle art); 0 = use object FID.</summary>
    public int DisplayFid { get; init; }

    public int Frame { get; private set; }
    public int OffsetX { get; private set; }
    public int OffsetY { get; private set; }

    private double _accumulatorMs;

    public void Advance(double elapsedMs, FrmFile frm, int rotation)
    {
        double msPerFrame = 1000.0 / frm.FramesPerSecond;
        _accumulatorMs += elapsedMs;

        while (_accumulatorMs >= msPerFrame)
        {
            _accumulatorMs -= msPerFrame;

            if (Frame + 1 >= frm.FrameCount)
            {
                Frame = 0;
                OffsetX = 0;
                OffsetY = 0;
            }
            else
            {
                Frame++;
                FrmFrame frame = frm.GetFrame(Frame, rotation);
                OffsetX += frame.OffsetX;
                OffsetY += frame.OffsetY;
            }
        }
    }
}

/// <summary>
/// Drives looping animations: scenery/misc art with multiple frames loops
/// forever (fires, blinking signs — vanilla triggers these via scripts'
/// animate_forever; this PoC has no VM, so multi-frame non-door art simply
/// loops), and critters can be toggled into a walk cycle in place.
/// </summary>
public sealed class ObjectAnimator(FrmCache frmCache)
{
    private readonly Dictionary<MapObject, AnimationState> _states = [];

    public bool TryGetState(MapObject obj, out AnimationState state) =>
        _states.TryGetValue(obj, out state!);

    public void AddLooping(MapObject obj) =>
        _states[obj] = new AnimationState();

    public void SetCritterAnimation(MapObject obj, int displayFid) =>
        _states[obj] = new AnimationState { DisplayFid = displayFid };

    public void Remove(MapObject obj) => _states.Remove(obj);

    public void Update(double elapsedMs)
    {
        foreach ((MapObject obj, AnimationState state) in _states)
        {
            int fid = state.DisplayFid != 0 ? state.DisplayFid : obj.Fid;
            FrmFile frm = frmCache.GetFrm(fid);
            int rotation = Math.Clamp(obj.Rotation, 0, FrmFile.RotationCount - 1);
            state.Advance(elapsedMs, frm, rotation);
        }
    }
}
