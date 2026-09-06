namespace Hexwaste.Formats.Rendering;

/// <summary>
/// Pure math for scaling Hexwaste's UI to fill an arbitrary window, matching fallout2-ce's own
/// stretched-buffer presentation but WITHOUT its non-uniform (aspect-distorting) stretch: fo2ce
/// stretches a fixed 640x480 buffer independently on each axis to fill any window shape;
/// Hexwaste instead picks one UNIFORM scale (the smaller of the two axis ratios) and lets the
/// "virtual" canvas extend past 640x480 on whichever axis has slack, avoiding pixel distortion.
/// Zero MonoGame dependency (Hexwaste.Formats is a pure .NET library) so this is directly
/// unit-testable; Hexwaste.Viewer's ViewerGame.UiScale.cs wraps these with real
/// GraphicsDevice.Viewport / Mouse.GetState() values.
/// </summary>
public static class UiScale
{
    /// <summary>The uniform scale factor for a viewport of the given size, relative to a
    /// baseWidth x baseHeight (640x480 by default) reference canvas. Clamped to a 0.1 minimum
    /// so a degenerate near-zero viewport can't produce zero/negative/NaN downstream.</summary>
    public static float ComputeScale(int viewportWidth, int viewportHeight, int baseWidth = 640, int baseHeight = 480)
    {
        float scale = Math.Min(viewportWidth / (float)baseWidth, viewportHeight / (float)baseHeight);
        return Math.Max(0.1f, scale);
    }

    /// <summary>The "virtual" (pre-scale) canvas size a viewport of the given real size and
    /// scale corresponds to. The axis that was limiting in ComputeScale reproduces the base
    /// dimension exactly; the other axis comes back larger than the base, giving 640x480-sized
    /// content room to center within a taller/wider canvas via (virtualWidth-640)/2 exactly as
    /// today's un-scaled centering math already does.</summary>
    public static (int Width, int Height) ComputeVirtualViewport(int viewportWidth, int viewportHeight, float scale) =>
        ((int)(viewportWidth / scale), (int)(viewportHeight / scale));

    /// <summary>Converts a real (window-pixel) mouse position into the same virtual-canvas
    /// coordinate space as ComputeVirtualViewport, so existing hit-test code (written against
    /// un-scaled logical coordinates) keeps working once rendering is scaled. No offset term is
    /// needed: the virtual viewport is DERIVED from the real one divided by scale, so scaling it
    /// back up exactly tiles the real screen with no letterbox gap to account for.</summary>
    public static (int X, int Y) TransformMouse(int rawX, int rawY, float scale) =>
        ((int)(rawX / scale), (int)(rawY / scale));
}
