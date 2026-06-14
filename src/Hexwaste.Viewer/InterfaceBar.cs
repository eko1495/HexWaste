using Hexwaste.Formats;
using Hexwaste.Formats.Frm;
using Hexwaste.Formats.Pal;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Hexwaste.Viewer;

/// <summary>
/// The authentic bottom HUD bar (P11): the 640x99 art\intrface\iface.frm panel
/// pinned bottom-centre at native 1:1 scale, with the green message monitor, the
/// equipped-weapon slot + attack-mode + AP, the HP/AC readout, and (later) the
/// clickable INV/OPT/SKILLDEX/MAP/CHA/PIP buttons. Coordinates are bar-local,
/// ported from fallout2-ce src/interface.cc; screen pos = <see cref="Origin"/> + coord.
///
/// This class owns the loaded iface art + geometry; ViewerGame.DrawInterfaceBar
/// composes the live readouts on top (it has the dude stats, the weapon, the FRM
/// cache and the font).
/// </summary>
public sealed class InterfaceBar : IDisposable
{
    /// <summary>Native bar size (interface.h:12-13; iface.frm is 640x99).</summary>
    public const int Width = 640;
    public const int Height = 99;

    /// <summary>The bar background, or null if the art is missing (HUD then hidden).</summary>
    public Texture2D? Background { get; }

    /// <summary>The 360x17 digit strip (numbers.frm): 3 colour bands (white/yellow/red,
    /// 120px each), each with digits 0-9 (9px) + up/down arrows + minus (6px @ +108).
    /// The engine blits these over the bar's baked placeholder digits.</summary>
    public Texture2D? Numbers { get; }

    public InterfaceBar(GraphicsDevice graphicsDevice, GameFileSystem vfs, Palette palette)
    {
        Background = LoadFrm(graphicsDevice, vfs, palette, @"art\intrface\iface.frm");
        Numbers = LoadFrm(graphicsDevice, vfs, palette, @"art\intrface\numbers.frm");
    }

    public bool Loaded => Background is not null;

    /// <summary>Top-left of the bar: bottom-centre at native scale (interface.cc:319-320).
    /// Clamps x to 0 for windows narrower than the bar (clipping accepted, documented).</summary>
    public Point Origin(Rectangle viewport) =>
        new(Math.Max(0, (viewport.Width - Width) / 2), viewport.Height - Height);

    /// <summary>Load a loose-path intrface FRM (frame 0) as an RGBA texture — the
    /// WorldmapScreen pattern. Index 0 is the FRM transparent colour, so it maps to
    /// transparent black via the palette's alpha. Returns null if the file is absent.</summary>
    public static Texture2D? LoadFrm(GraphicsDevice graphicsDevice, GameFileSystem vfs, Palette palette, string path)
    {
        if (!vfs.Exists(path))
            return null;

        FrmFrame frame = FrmFile.Load(vfs.ReadAllBytes(path)).GetFrame(0);
        byte[] paletteRgba = palette.ToRgba();
        byte[] rgba = new byte[frame.Pixels.Length * 4];
        for (int p = 0; p < frame.Pixels.Length; p++)
            Buffer.BlockCopy(paletteRgba, frame.Pixels[p] * 4, rgba, p * 4, 4);

        var texture = new Texture2D(graphicsDevice, frame.Width, frame.Height, false, SurfaceFormat.Color);
        texture.SetData(rgba);
        return texture;
    }

    /// <summary>Draw just the bar background. Live readouts are composed by the host.</summary>
    public void Draw(SpriteBatch spriteBatch, Rectangle viewport)
    {
        if (Background is null)
            return;
        Point origin = Origin(viewport);
        spriteBatch.Draw(Background, new Vector2(origin.X, origin.Y), Color.White);
    }

    public void Dispose()
    {
        Background?.Dispose();
        Numbers?.Dispose();
    }
}
