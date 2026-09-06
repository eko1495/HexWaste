using Hexwaste.Formats.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Hexwaste.Viewer;

public sealed partial class ViewerGame
{
    /// <summary>The current UI scale factor for this window size — see
    /// Hexwaste.Formats.Rendering.UiScale.ComputeScale. Not yet used by any draw call; later,
    /// separately-specced stages wire it into SpriteBatch.Begin's transformMatrix.</summary>
    private float UiScale()
    {
        Viewport vp = GraphicsDevice.Viewport;
        return Formats.Rendering.UiScale.ComputeScale(vp.Width, vp.Height);
    }

    /// <summary>The "virtual" (pre-scale) canvas for this window size — see
    /// Hexwaste.Formats.Rendering.UiScale.ComputeVirtualViewport. Not yet used by any layout
    /// code; later stages replace existing GraphicsDevice.Viewport.Bounds references with this
    /// wherever content is drawn through the scaled batch.</summary>
    private Rectangle VirtualViewport()
    {
        Viewport vp = GraphicsDevice.Viewport;
        (int w, int h) = Formats.Rendering.UiScale.ComputeVirtualViewport(vp.Width, vp.Height, UiScale());
        return new Rectangle(0, 0, w, h);
    }

    /// <summary>The real mouse position transformed into the same virtual-canvas coordinate
    /// space as VirtualViewport — see Hexwaste.Formats.Rendering.UiScale.TransformMouse. Not yet
    /// used by any hit-test; later stages replace existing Mouse.GetState().X/Y position reads
    /// with this wherever a click is tested against scaled content. Callers that need button-
    /// press state (e.g. Mouse.GetState().LeftButton) keep calling Mouse.GetState() directly for
    /// that — only the position needs transforming.</summary>
    private Point UiMouse()
    {
        MouseState mouse = Mouse.GetState();
        (int x, int y) = Formats.Rendering.UiScale.TransformMouse(mouse.X, mouse.Y, UiScale());
        return new Point(x, y);
    }
}
