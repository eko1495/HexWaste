namespace Hexwaste.Formats.Rendering;

/// <summary>
/// Pure math for scaling Hexwaste's UI to fill an arbitrary window. fo2ce's own SDL-based
/// presentation (SDL_RenderSetLogicalSize + SDL_RenderCopy, reference/fallout2-ce/src/svga.cc)
/// is actually a fixed low-resolution buffer (1024x768 by default via f2_res.ini, or 640x480
/// vanilla) UNIFORMLY scaled with letterbox/pillarbox bars on any non-matching aspect ratio —
/// confirmed against the vendored SDL2 source fo2ce builds against, which is architecturally
/// incapable of a non-uniform per-axis stretch. (A one-off session observation of fo2ce filling
/// a 16:9 screen edge-to-edge with no bars was traced to an OS/compositor-level scanout stretch
/// from SDL_WINDOW_FULLSCREEN mode-switching, happening entirely outside fo2ce's own rendering
/// code — not something fo2ce's application logic does, and not portable/reproducible behavior.)
/// Hexwaste deliberately does NOT replicate fo2ce's letterboxing: it picks one UNIFORM scale (the
/// smaller of the two axis ratios, so no pixel distortion) and lets the "virtual" canvas extend
/// past 640x480 on whichever axis has slack, filling the window edge-to-edge instead of showing
/// black bars. Zero MonoGame dependency (Hexwaste.Formats is a pure .NET library) so this is
/// directly unit-testable; Hexwaste.Viewer's ViewerGame.UiScale.cs wraps these with real
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
