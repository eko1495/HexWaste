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
public enum AnimationMode
{
    /// <summary>Wraps to frame 0 forever (fires, signs).</summary>
    Loop,

    /// <summary>Plays to the last frame and holds it (door opening).</summary>
    Once,

    /// <summary>Plays backwards to frame 0 and finishes (door closing).</summary>
    OnceReverse,

    /// <summary>Plays once, then the state is removed (critter fidget).</summary>
    OnceThenReset,
}

public sealed class AnimationState
{
    /// <summary>FID drawn instead of the object's own (e.g. walk-cycle art); 0 = use object FID.</summary>
    public int DisplayFid { get; init; }

    public AnimationMode Mode { get; init; } = AnimationMode.Loop;

    public int Frame { get; set; }
    public int OffsetX { get; private set; }
    public int OffsetY { get; private set; }
    public bool Finished { get; private set; }

    private double _accumulatorMs;

    public void Advance(double elapsedMs, FrmFile frm, int rotation)
    {
        if (Finished && Mode != AnimationMode.Loop)
            return;

        double msPerFrame = 1000.0 / frm.FramesPerSecond;
        _accumulatorMs += elapsedMs;

        while (_accumulatorMs >= msPerFrame)
        {
            _accumulatorMs -= msPerFrame;

            switch (Mode)
            {
                case AnimationMode.Loop:
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
                    break;

                case AnimationMode.Once:
                case AnimationMode.OnceThenReset:
                    if (Frame + 1 >= frm.FrameCount)
                    {
                        Finished = true;
                        return;
                    }
                    Frame++;
                    break;

                case AnimationMode.OnceReverse:
                    if (Frame == 0)
                    {
                        Finished = true;
                        return;
                    }
                    Frame--;
                    break;
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

    /// <summary>Plays the object's frames forward once and holds the last frame (door opening).</summary>
    public void PlayOnce(MapObject obj) =>
        _states[obj] = new AnimationState { Mode = AnimationMode.Once };

    /// <summary>Plays backwards from the last frame to frame 0 (door closing).</summary>
    public void PlayOnceReverse(MapObject obj, int lastFrame) =>
        _states[obj] = new AnimationState { Mode = AnimationMode.OnceReverse, Frame = lastFrame };

    /// <summary>Plays the object's frames once and snaps back (critter fidget).</summary>
    public void PlayFidget(MapObject obj) =>
        _states[obj] = new AnimationState { Mode = AnimationMode.OnceThenReset };

    /// <summary>Plays substitute art once and reverts (punch, hit reaction).</summary>
    public void PlayActionOnce(MapObject obj, int displayFid) =>
        _states[obj] = new AnimationState { DisplayFid = displayFid, Mode = AnimationMode.OnceThenReset };

    /// <summary>Plays substitute art once and holds the last frame (death fall).</summary>
    public void PlayFall(MapObject obj, int displayFid) =>
        _states[obj] = new AnimationState { DisplayFid = displayFid, Mode = AnimationMode.Once };

    public void Remove(MapObject obj) => _states.Remove(obj);

    public void Update(double elapsedMs)
    {
        List<MapObject>? finishedFidgets = null;
        foreach ((MapObject obj, AnimationState state) in _states)
        {
            int fid = state.DisplayFid != 0 ? state.DisplayFid : obj.Fid;
            FrmFile frm = frmCache.GetFrm(fid);
            int rotation = Math.Clamp(obj.Rotation, 0, FrmFile.RotationCount - 1);
            state.Advance(elapsedMs, frm, rotation);

            if (state is { Mode: AnimationMode.OnceThenReset, Finished: true })
                (finishedFidgets ??= []).Add(obj);
        }

        if (finishedFidgets is not null)
            foreach (MapObject obj in finishedFidgets)
                _states.Remove(obj);
    }
}
