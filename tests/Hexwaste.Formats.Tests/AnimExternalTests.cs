using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// F9: opAnim's direct-manipulation values, ported from
/// fallout2-ce src/interpreter_extra.cc opAnim() (:3420-3428). 1000 sets rotation
/// (guarded by ROTATION_COUNT), 1010 sets the frame (unguarded). Everything else
/// falls through to the ordinary animation request.
/// </summary>
public class AnimExternalTests
{
    private static MapObject Critter() => new()
    {
        Id = 1, HexTile = 100, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = 0x01000000, Flags = 0, Pid = 0x01000005,
    };

    [Fact]
    public void Anim1000SetsRotation()
    {
        MapObject obj = Critter();
        Assert.True(ScriptHost.ApplyDirectAnim(obj, 1000, 3));
        Assert.Equal(3, obj.Rotation);
    }

    [Fact]
    public void Anim1000IgnoresARotationAtOrAboveRotationCount()
    {
        // objectSetRotation rejects direction >= ROTATION_COUNT (6); opAnim guards the
        // same bound, which is what makes the CE animate_rotation pointer bug harmless.
        MapObject obj = Critter();
        obj.Rotation = 2;
        Assert.True(ScriptHost.ApplyDirectAnim(obj, 1000, 6));
        Assert.Equal(2, obj.Rotation);
    }

    [Fact]
    public void Anim1000IgnoresANegativeRotation()
    {
        // Documented divergence: vanilla stores a negative rotation, which would throw
        // in our Fid.Build/array-indexing path rather than render garbage.
        MapObject obj = Critter();
        obj.Rotation = 2;
        Assert.True(ScriptHost.ApplyDirectAnim(obj, 1000, -1));
        Assert.Equal(2, obj.Rotation);
    }

    [Fact]
    public void Anim1010SetsFrame()
    {
        MapObject obj = Critter();
        Assert.True(ScriptHost.ApplyDirectAnim(obj, 1010, 4));
        Assert.Equal(4, obj.Frame);
    }

    [Fact]
    public void AnOrdinaryAnimIsNotHandledDirectly()
    {
        MapObject obj = Critter();
        Assert.False(ScriptHost.ApplyDirectAnim(obj, 5, 0));
        Assert.Equal(0, obj.Rotation);
        Assert.Equal(0, obj.Frame);
    }
}
