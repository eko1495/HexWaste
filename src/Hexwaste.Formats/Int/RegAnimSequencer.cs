namespace Hexwaste.Formats.Int;

/// <summary>
/// P114: sequences a reg_anim batch. fo2ce runs a registered batch as an ordered animation sequence
/// (animation.cc animationRunSequence:1352, delay gate :1369-1377), NOT all at once: action N+1 starts
/// when N's async animation completes, or earlier once N+1's own delay has counted down. This pure core
/// holds the dispatch cursor + per-action delay; the viewer supplies "is the prior async action still
/// running?" + the frame delta and actually starts the walker/animation for each dispatched action.
/// (Global strict serialization — fo2ce lets different-object actions overlap; a faithful simplification.)
/// </summary>
public sealed class RegAnimSequencer
{
    private readonly IReadOnlyList<RegAnimAction> _actions;
    private readonly double _frameMs;
    private int _cursor;
    private double _delayMs;

    public RegAnimSequencer(IReadOnlyList<RegAnimAction> actions, double frameMs = 100.0)
    {
        _actions = actions;
        _frameMs = frameMs;
    }

    public bool Done => _cursor >= _actions.Count;

    /// <summary>The first action, dispatched immediately (the sequence head has no pre-delay). Null if empty.</summary>
    public RegAnimAction? Begin()
    {
        if (Done)
            return null;
        RegAnimAction head = _actions[_cursor++];
        ArmNextDelay();
        return head;
    }

    /// <summary>The next action to dispatch this frame, or null to keep waiting. Dispatch once the prior
    /// async action has finished (<paramref name="blockerActive"/> false) AND this action's delay elapsed.</summary>
    public RegAnimAction? Advance(bool blockerActive, double elapsedMs)
    {
        if (Done || blockerActive)
            return null;
        if (_delayMs > 0)
        {
            _delayMs -= elapsedMs;
            if (_delayMs > 0)
                return null;
        }
        RegAnimAction next = _actions[_cursor++];
        ArmNextDelay();
        return next;
    }

    private void ArmNextDelay() => _delayMs = Done ? 0 : _actions[_cursor].Delay * _frameMs;
}
