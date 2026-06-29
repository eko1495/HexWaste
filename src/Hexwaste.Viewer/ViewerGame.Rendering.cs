using System;
using System.Collections.Generic;
using Hexwaste.Formats;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Hexwaste.Viewer;

// World sprite rendering: floor quads, object/critter sprite resolution + draw, the combat outline
// + translucency tints, the dude roof-fade test. Pure move from ViewerGame.cs (the Draw() dispatch
// + DrawRoofs/DrawCombatText stay in core); fields stay central.
public sealed partial class ViewerGame
{
    /// <summary>P85: the WORLD-layer zoom transform — scale by <see cref="_zoom"/> about the screen
    /// centre, so the centred hex stays put while the HUD (its own native batch) is untouched. Identity
    /// at 1× so every default frame is unchanged. The camera projection stays in logical (un-zoomed)
    /// pixels; this matrix scales both the position and the size of everything drawn in the world batch.</summary>
    private Matrix WorldZoomMatrix()
    {
        if (_zoom == 1)
            return Matrix.Identity;
        float cx = GraphicsDevice.Viewport.Width / 2f;
        float cy = GraphicsDevice.Viewport.Height / 2f;
        return Matrix.CreateTranslation(-cx, -cy, 0) * Matrix.CreateScale(_zoom) * Matrix.CreateTranslation(cx, cy, 0);
    }

    /// <summary>Inverse of <see cref="WorldZoomMatrix"/> for a single point: turn a physical screen
    /// point (the mouse) into the logical un-zoomed point the camera projection / sprite-bounds picking
    /// expect. Identity at 1×.</summary>
    private (int X, int Y) ToWorldPoint(int screenX, int screenY)
    {
        if (_zoom == 1)
            return (screenX, screenY);
        float cx = GraphicsDevice.Viewport.Width / 2f;
        float cy = GraphicsDevice.Viewport.Height / 2f;
        return ((int)MathF.Round((screenX - cx) / _zoom + cx), (int)MathF.Round((screenY - cy) / _zoom + cy));
    }

    /// <summary>Forward zoom of a single logical world point to its physical screen position — for the
    /// few world-anchored sprites drawn in the native HUD batch (the hex-ring cursor). Identity at 1×.</summary>
    private Vector2 ToScreenPoint(int worldX, int worldY)
    {
        if (_zoom == 1)
            return new Vector2(worldX, worldY);
        float cx = GraphicsDevice.Viewport.Width / 2f;
        float cy = GraphicsDevice.Viewport.Height / 2f;
        return new Vector2((worldX - cx) * _zoom + cx, (worldY - cy) * _zoom + cy);
    }

    /// <summary>The hex under a physical screen point, accounting for zoom (P85). The camera works in
    /// logical pixels, so convert the point first.</summary>
    private int PickHex(int screenX, int screenY)
    {
        (int wx, int wy) = ToWorldPoint(screenX, screenY);
        return _camera.ScreenToHex(wx, wy);
    }

    private void DrawFloors()
    {
        MapElevation? elevation = _map.Elevations[_elevation];
        if (elevation is null)
            return;

        _floorRenderer ??= new FloorRenderer(GraphicsDevice);
        _floorRenderer.Begin(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height, WorldZoomMatrix());

        Rectangle viewport = GraphicsDevice.Viewport.Bounds;

        // ported from fallout2-ce src/tile.cc tileRenderFloorsInRect(): skip
        // squares whose floor word has flag bit 12 set; floor art id is 12 bits.
        // Id 1 (grid000.frm, the blank grid marker) is skipped as an optimization.
        for (int square = 0; square < MapElevation.SquareGridSize; square++)
        {
            int floorValue = elevation.Squares[square] & 0xFFFF;
            if ((((floorValue & 0xF000) >> 12) & 0x01) != 0)
                continue;

            int tileId = floorValue & 0xFFF;
            if (tileId == 1)
                continue;

            (int x, int y) = _camera.SquareToScreen(square);
            if (x < viewport.Left - 80 || x > viewport.Right || y < viewport.Top - 36 || y > viewport.Bottom)
                continue;

            Texture2D texture = _frmCache.GetTexture(Fid.Build(ObjectType.Tile, tileId));

            // Corner light from the neighboring hexes (rotations: 5=NW 0=NE
            // 3=SW 2=SE on screen); the GPU interpolates across the quad —
            // the engine's 10-vertex span fan, minus the CPU.
            int hex = SquareToHex(square);
            _floorRenderer.Add(texture, x, y,
                LightTint(Formats.Hex.HexGrid.TileInDirection(hex, 5)),
                LightTint(Formats.Hex.HexGrid.TileInDirection(hex, 0)),
                LightTint(Formats.Hex.HexGrid.TileInDirection(hex, 3)),
                LightTint(Formats.Hex.HexGrid.TileInDirection(hex, 2)));
        }

        _floorRenderer.End();
    }

    /// <summary>
    /// Walls/scenery drawn AFTER the dude (higher hex = in front) whose sprite
    /// covers the dude's upper body fade so he stays visible — the PoC's
    /// approximation of the engine's egg-masked translucency.
    /// </summary>
    private bool FadesOverDude(MapObject obj, SpriteInfo sprite)
    {
        if (_dude is null || obj == _dude.Dude)
            return false;
        if (Fid.Type(obj.Fid) is not (ObjectType.Wall or ObjectType.Scenery))
            return false;
        if (obj.HexTile <= _dude.Dude.HexTile)
            return false; // drawn before the dude -> he's on top anyway

        (int dudeX, int dudeY) = _camera.HexToScreen(_dude.Dude.HexTile);
        // Egg region: an ellipse-ish box around the dude's torso/head.
        var eggRect = new Rectangle(dudeX + 16 - 45, dudeY + 8 - 70, 90, 75);
        var spriteRect = new Rectangle(sprite.Left, sprite.Top, sprite.Frame.Width, sprite.Frame.Height);
        return eggRect.Intersects(spriteRect);
    }

    /// <summary>True when the dude's square has a roof tile (he is indoors).</summary>
    private bool DudeIsUnderRoof()
    {
        if (_dude is null || _map.Elevations[_elevation] is not { } elevation)
            return false;
        int hex = _dude.Dude.HexTile;
        int sx = (hex % Camera.HexGridWidth - 1) / 2;
        int sy = hex / Camera.HexGridWidth / 2;
        if (sx < 0 || sx >= MapElevation.SquareGridWidth || sy < 0 || sy >= MapElevation.SquareGridHeight)
            return false;
        return elevation.RoofTileId(sy * MapElevation.SquareGridWidth + sx) != 1;
    }

    /// <summary>
    /// A square maps to the 2x2 hex block starting at hex (2*sx+1, 2*sy) —
    /// derived from the tile.cc square/hex screen formulas. One sample per
    /// tile approximates the original's per-pixel floor gradient (see
    /// phase3-research-report.md M1 pivot threshold).
    /// </summary>
    private static int SquareToHex(int square)
    {
        int sx = square % MapElevation.SquareGridWidth;
        int sy = square / MapElevation.SquareGridWidth;
        return 2 * sy * Camera.HexGridWidth + 2 * sx + 1;
    }

    /// <summary>Struct: resolved per scanned object every frame — must not allocate.</summary>
    private readonly record struct SpriteInfo(int Fid, int FrameIndex, int Rotation,
        Formats.Frm.FrmFrame Frame, int Left, int Top);

    /// <summary>
    /// Resolves the drawn sprite and its screen rectangle for an object —
    /// shared by rendering and mouse picking so both always agree.
    /// Anchor math ported from fallout2-ce src/object.cc objectGetRect():
    /// hex tile center (+16,+8 from the 32x16 cell origin) + FRM per-rotation
    /// offset + the object's own pixel nudge; art is bottom-centered there.
    /// Animations add their accumulated per-frame offset deltas.
    /// </summary>
    private SpriteInfo? ResolveSprite(MapObject obj)
    {
        if (_failedFids.Contains(obj.Fid))
            return null;

        DudeController? walker = _dude is not null && obj == _dude.Dude ? _dude
            : _npcWalkers.TryGetValue(obj, out DudeController? npcWalker) ? npcWalker
            : null;

        // Animator states (combat punches/hits, fidgets) take over while the
        // walker is standing; mid-walk the walk cycle wins.
        AnimationState? animation = null;
        if (_animator.TryGetState(obj, out AnimationState state) && walker is not { Moving: true })
        {
            animation = state;
            walker = null;
        }

        int fid = walker?.CurrentFid
            ?? (animation is { DisplayFid: not 0 } ? animation.DisplayFid : obj.Fid);

        Formats.Frm.FrmFile frm;
        Formats.Frm.FrmFrame frame;
        int rotation;
        int frameIndex;
        try
        {
            frm = _frmCache.GetFrm(fid);
            rotation = Math.Clamp(obj.Rotation, 0, Formats.Frm.FrmFile.RotationCount - 1);
            frameIndex = walker is not null ? Math.Min(walker.Frame, frm.FrameCount - 1)
                : animation is not null ? animation.Frame
                : Math.Clamp(obj.Frame, 0, frm.FrameCount - 1);
            frame = frm.GetFrame(frameIndex, rotation);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            _failedFids.Add(obj.Fid);
            Console.Error.WriteLine($"skipping FID 0x{obj.Fid:X8}: {ex.Message}");
            return null;
        }

        int extraX = walker?.OffsetX ?? animation?.OffsetX ?? 0;
        int extraY = walker?.OffsetY ?? animation?.OffsetY ?? 0;

        (int hexX, int hexY) = _camera.HexToScreen(obj.HexTile);
        int anchorX = hexX + 16 + frm.RotationOffsetsX[rotation] + obj.X + extraX;
        int anchorY = hexY + 8 + frm.RotationOffsetsY[rotation] + obj.Y + extraY;
        int left = anchorX - frame.Width / 2;
        int top = anchorY - (frame.Height - 1);

        return new SpriteInfo(fid, frameIndex, rotation, frame, left, top);
    }

    private void DrawObjects(List<MapObject> objects)
    {
        Rectangle viewport = GraphicsDevice.Viewport.Bounds;

        foreach (MapObject obj in objects)
        {
            if (ResolveSprite(obj) is not { } sprite)
                continue;

            if (sprite.Left > viewport.Right || sprite.Left + sprite.Frame.Width < viewport.Left
                || sprite.Top > viewport.Bottom || sprite.Top + sprite.Frame.Height < viewport.Top)
                continue;

            Texture2D texture = _frmCache.GetTexture(sprite.Fid, sprite.FrameIndex, sprite.Rotation);
            // ported from fallout2-ce src/object.cc _obj_render_object(): one
            // uniform intensity per object, max(ambient, tile light).
            Color tint = LightTint(obj.HexTile);

            // Translucency (P23): glass/steam/energy/red/wall objects blend over the
            // background instead of drawing opaque (the engine's per-type blend tables).
            if (TranslucencyOf(obj) is { } trans and not Formats.Proto.TransType.None)
                tint = ApplyTranslucency(tint, trans);

            // Egg-style transparency (approximation of the engine's masked
            // blend): solids drawn in front of the dude that cover him fade,
            // keeping him visible behind walls.
            if (FadesOverDude(obj, sprite))
                tint *= 0.45f;

            _spriteBatch.Draw(texture, new Vector2(sprite.Left, sprite.Top), tint);

            // Combat outlines (P34-M4): during combat every visible LIVING critter is outlined by team
            // (red hostile / green friendly / dim perception-only), LoS-gated; out of combat the hovered
            // object gets the green hover outline. One outline per critter (hover suppressed in combat).
            if (_combat.Phase != Formats.Combat.CombatPhase.Idle && _dude is not null
                && Fid.Type(obj.Fid) is ObjectType.Critter && obj != _dude.Dude && !obj.IsDead)
            {
                if (CombatOutlineColor(obj) is { } teamColor)
                {
                    Texture2D outline = _frmCache.GetOutlineTexture(sprite.Fid, sprite.FrameIndex, sprite.Rotation);
                    _spriteBatch.Draw(outline, new Vector2(sprite.Left, sprite.Top), teamColor);
                }
            }
            else if (obj == _hoveredObject && _combat.Phase == Formats.Combat.CombatPhase.Idle
                     && Fid.Type(obj.Fid) is ObjectType.Item)
            {
                // FO2 only highlights GROUND ITEMS on hover (game_mouse.cc:680, the OBJ_TYPE_ITEM case),
                // in the amber item-outline colour (OUTLINE_TYPE_ITEM = _colorTable[30632] ≈ 232,232,64) —
                // NOT every scenery/wall/critter, and NOT green (green/229 is the combat-ally outline).
                Texture2D outline = _frmCache.GetOutlineTexture(sprite.Fid, sprite.FrameIndex, sprite.Rotation);
                _spriteBatch.Draw(outline, new Vector2(sprite.Left, sprite.Top), new Color(232, 232, 64));
            }
        }
    }

    /// <summary>
    /// The team/LoS outline a visible critter gets during combat, or null for none (P34-M4).
    /// ported from fallout2-ce src/combat.cc _combat_update_critter_outline_for_los() +
    /// src/object.cc _obj_outline_object() — the 5-band gradient collapses to the base palette index.
    /// </summary>
    private Color? CombatOutlineColor(MapObject critter)
    {
        int idx = Formats.Combat.CombatOutline.PaletteIndex(CombatOutlineType(critter));
        if (idx < 0)
            return null;
        (byte r, byte g, byte b) = _palette.GetRgb(idx);
        return new Color(r, g, b);
    }

    private Formats.Combat.OutlineType CombatOutlineType(MapObject critter)
    {
        MapObject dude = _dude!.Dude;
        bool clearLos = Formats.Combat.LineOfFire.Trace(dude.HexTile, critter.HexTile,
            t => ShootBlockerAt(t, dude, critter)).Blocker is null;
        int dist = Formats.Hex.HexGrid.Distance(dude.HexTile, critter.HexTile);
        int pe = GetCritterState(dude)?.Stat(1) ?? 0; // STAT_PERCEPTION (SPECIAL index 1)
        bool glass = TranslucencyOf(critter) == Formats.Proto.TransType.Glass;
        return Formats.Combat.CombatOutline.TypeFor(clearLos, dude.Team, critter.Team, dist, pe, glass);
    }

    private readonly Dictionary<int, Formats.Proto.TransType> _transByPid = [];

    /// <summary>The object's translucency class (P23), cached per pid — the proto carries the
    /// 0xFC000 trans bits (object.cc:943). Unknown protos → None (opaque).</summary>
    private Formats.Proto.TransType TranslucencyOf(MapObject obj)
    {
        if (_transByPid.TryGetValue(obj.Pid, out Formats.Proto.TransType cached))
            return cached;
        Formats.Proto.TransType t;
        try { t = _protos.Get(obj.Pid).Translucency; }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException) { t = Formats.Proto.TransType.None; }
        return _transByPid[obj.Pid] = t;
    }

    // Per-type translucency blend (P23): a GPU approximation of the engine's 8-bit blend tables
    // (object.cc:3467-3471 _dark_translucent_trans_buf_to_buf). The tint is each type's _colorTable
    // seed (RGB555 -> RGB8) softened halfway to white so it reads as a tint not a full multiply;
    // the per-pixel luminance weighting + exact palette composite collapse to one uniform alpha —
    // a documented divergence (SpriteBatch over RGBA has no 8-bit destination buffer to blend into).
    private static readonly Dictionary<Formats.Proto.TransType, (Color Tint, float Alpha)> TransBlend = new()
    {
        [Formats.Proto.TransType.Wall] = (new Color(226, 234, 255), 0.55f),   // seed _colorTable[25439]
        [Formats.Proto.TransType.Glass] = (new Color(164, 255, 255), 0.42f),  // seed _colorTable[10239]
        [Formats.Proto.TransType.Steam] = (new Color(255, 255, 255), 0.50f),  // seed _colorTable[32767]
        [Formats.Proto.TransType.Energy] = (new Color(247, 255, 131), 0.60f), // seed _colorTable[30689]
        [Formats.Proto.TransType.Red] = (new Color(255, 127, 127), 0.50f),    // seed _colorTable[31744]
    };

    /// <summary>Fold a translucency type's tint + alpha into the object's light tint (P23). The
    /// SpriteBatch premultiplied AlphaBlend then composites the lit, type-tinted sprite over the
    /// background at the type's alpha (the same Color*float path the egg-fade uses).</summary>
    private static Color ApplyTranslucency(Color light, Formats.Proto.TransType trans)
    {
        if (!TransBlend.TryGetValue(trans, out (Color Tint, float Alpha) b))
            return light;
        var tinted = new Color(light.R * b.Tint.R / 255, light.G * b.Tint.G / 255, light.B * b.Tint.B / 255, 255);
        return tinted * b.Alpha;
    }
}
