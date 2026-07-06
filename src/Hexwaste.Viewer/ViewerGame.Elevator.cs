using Hexwaste.Formats;
using Hexwaste.Formats.Map;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Hexwaste.Viewer;

// P113 (item 5): elevators. A script calls metarule(15, type); the host records a PendingElevator,
// the viewer scans for an elevator-panel scenery near the caller (which overrides the type + start
// level), opens a level picker, and teleports the dude to the picked floor's (map, elevation, tile).
// ported from fallout2-ce src/scripts.cc scriptsRequestElevator + src/elevator.cc + the same-map/
// cross-map servicing at scripts.cc:926-999.
public sealed partial class ViewerGame
{
    private const int ElevatorStubPid = 0x0200050D; // PROTO_ID_0x200050D (the elevator-panel scenery)

    private (int Type, int Current, int Levels)? _elevatorPicker;
    /// <summary>Harness: pre-pick this button index (0-based) instead of waiting for input.</summary>
    public int? ElevatorPickOverride { get; set; }

    // P119: the authentic panel art (elevator.cc:58/65 + elevatorWindowInit :480). Loaded once,
    // keyed by background/panel list index; a missing FRM falls back to the text picker.
    private readonly Dictionary<int, Texture2D?> _intrfaceFrms = [];
    // The in-flight ride: the gauge sweeps from Gauge to TargetGauge in slice units at
    // GaugeMsPerSlice, then holds 200 ms (inputPauseForTocks, elevator.cc:463) before the teleport.
    private (int Type, int Button, int StartButton, double Gauge, double TargetGauge, double PauseMs)? _elevatorRide;

    private Texture2D? InterfaceFrm(int intrfaceId)
    {
        if (intrfaceId < 0)
            return null;
        if (!_intrfaceFrms.TryGetValue(intrfaceId, out Texture2D? tex))
        {
            try
            {
                tex = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette,
                    _artIndex.GetFrmPath(Fid.Build(ObjectType.Interface, intrfaceId)));
            }
            catch (Exception) { tex = null; }
            _intrfaceFrms[intrfaceId] = tex;
        }
        return tex;
    }

    /// <summary>Levels-count → the gauge step per level in slice units (12/(levels−1)).</summary>
    private static double GaugeStep(int levels) => 12.0 / Math.Max(1, levels - 1);

    /// <summary>Consume a script's metarule(15) elevator request: resolve the type + start level
    /// (an elevator-panel scenery near the caller overrides the script args), then open the picker.
    /// Called after the movement pump so the request raised inside a spatial/critter proc is serviced.</summary>
    private void ConsumePendingElevator()
    {
        if (_scriptHost?.PendingElevator is not { } req)
            return;
        _scriptHost.PendingElevator = null;

        int type = req.RequestedType;
        int startLevel = _elevation;

        // fo2ce scans a 10x10 hex window (self.tile - 200*5 - 5, row stride 200) for the elevator-stub
        // scenery, whose data overrides type+level (scripts.cc:1160-1196). We match the same window on
        // our parsed ElevatorType/ElevatorLevel.
        if (FindElevatorPanel(req.SelfTile) is { } panel)
        {
            type = panel.ElevatorType;
            startLevel = panel.ElevatorLevel;
        }

        if (type < 0 || type >= ElevatorTables.Levels.Length)
            return; // type -1 → silent drop (scripts.cc:1200)

        int currentMap = _mapList.GetIndexByFileName(_currentMapName);
        int current = ElevatorTables.CurrentButton(type, currentMap, startLevel);
        int levels = ElevatorTables.Levels[type];
        Console.WriteLine($"elevator: type={type} levels={levels} start={current}");

        if (ElevatorPickOverride is { } forced)
        {
            RideElevator(type, Math.Clamp(forced, 0, levels - 1), current);
            ElevatorPickOverride = null;
            return;
        }
        _elevatorPicker = (type, current, levels);
        // P119 probe line (new prefix — golden-safe): which panel FRMs the live picker resolved.
        (int bgId, int panelId) = ElevatorTables.Backgrounds[type];
        Console.WriteLine($"elevator-art: bg={bgId}:{InterfaceFrm(bgId) is not null}"
            + $" panel={panelId}:{InterfaceFrm(panelId) is not null}"
            + $" buttons={InterfaceFrm(ElevatorTables.ButtonUpFrmId) is not null}"
            + $" gauge={InterfaceFrm(ElevatorTables.GaugeFrmId) is not null}");
    }

    private MapObject? FindElevatorPanel(int selfTile)
    {
        int anchor = selfTile - Camera.HexGridWidth * 5 - 5;
        var window = new HashSet<int>();
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 10; x++)
                window.Add(anchor + y * Camera.HexGridWidth + x);
        return _solidObjects[_elevation].Concat(_flatObjects[_elevation]).FirstOrDefault(o =>
            o.ElevatorType >= 0 && (o.Pid == ElevatorStubPid || window.Contains(o.HexTile)));
    }

    /// <summary>Teleport the dude to the picked floor (scripts.cc:926-999): same map + elevation →
    /// reposition facing SE; otherwise a map transition to (map, elevation, tile). Same-map-different-
    /// elevation reloads the map (a documented simplification — fo2ce does an in-place mapSetElevation).</summary>
    private void RideElevator(int type, int button, int startButton, bool playSfx = true)
    {
        (int map, int elevation, int tile) = ElevatorTables.Descriptions[type][button];
        if (tile == -1)
            return; // unused button slot
        // P117 sfx: the ride sound by level count + levels travelled (elevator.cc:438).
        if (playSfx && Formats.Sound.SfxName.Elevator(ElevatorTables.Levels[type], Math.Abs(button - startButton)) is { } rideSfx)
            _audio?.PlaySfx(rideSfx);
        Log("The elevator doors open.");
        Console.WriteLine($"elevator-ride: button={button} -> map={map} elev={elevation} tile={tile}");

        const int rotationSe = 2; // ROTATION_SE — the dude always arrives facing SE
        int currentMap = _mapList.GetIndexByFileName(_currentMapName);
        if (map == currentMap && elevation == _elevation && _dude is not null)
        {
            _dude.Dude.HexTile = Formats.Map.Placement.FreeTileNear(tile, t => _blockedTiles.Contains(t));
            _dude.Dude.Rotation = rotationSe;
            _camera.SetCenter(_dude.Dude.HexTile);
            RebuildBlockedTiles(_dude.Dude);
            return;
        }
        ApplyTransition(new MapDestination(map, tile, elevation, rotationSe));
    }

    /// <summary>The picker window's top-left screen position (the art centered like
    /// elevatorWindowInit :545, or the text-fallback box).</summary>
    private Point ElevatorWindowPos(Texture2D bg) => new(
        (GraphicsDevice.Viewport.Width - bg.Width) / 2,
        (GraphicsDevice.Viewport.Height - bg.Height) / 2);

    /// <summary>The clickable button rects (window-local x=13, y=40+60·level — elevatorWindowInit
    /// :583-602), in screen space.</summary>
    private Rectangle ElevatorButtonRect(Texture2D bg, Texture2D btn, int level)
    {
        Point p = ElevatorWindowPos(bg);
        return new Rectangle(p.X + 13, p.Y + 40 + level * 60, btn.Width, btn.Height);
    }

    /// <summary>The live level picker: the authentic panel art with mouse buttons + the LABEL-char
    /// hotkeys (gElevatorLevelLabels double as key bindings, elevator.cc:270), plus 1..n; Esc
    /// cancels. While a ride is in flight the modal ignores input and sweeps the gauge
    /// (elevatorSelectLevel :425-464); the teleport fires after the 200 ms hold.</summary>
    private void UpdateElevatorPicker(KeyboardState keyboard, MouseState mouse, GameTime gameTime)
    {
        if (_elevatorRide is { } ride)
        {
            double dt = gameTime.ElapsedGameTime.TotalMilliseconds;
            if (Math.Abs(ride.Gauge - ride.TargetGauge) > 0.001)
            {
                double step = dt / ElevatorTables.GaugeMsPerSlice;
                ride.Gauge = ride.Gauge < ride.TargetGauge
                    ? Math.Min(ride.TargetGauge, ride.Gauge + step)
                    : Math.Max(ride.TargetGauge, ride.Gauge - step);
                _elevatorRide = ride;
                return;
            }
            ride.PauseMs -= dt;
            _elevatorRide = ride;
            if (ride.PauseMs > 0)
                return;
            _elevatorRide = null;
            _elevatorPicker = null;
            RideElevator(ride.Type, ride.Button, ride.StartButton, playSfx: false); // sfx played at ride start
            return;
        }

        if (_elevatorPicker is not { } picker)
            return;
        if (IsKeyPressed(keyboard, Keys.Escape))
        {
            _elevatorPicker = null;
            return;
        }

        int pick = -1;
        char[] labels = ElevatorTables.LevelLabels[picker.Type];
        for (int i = 0; i < picker.Levels && pick < 0; i++)
        {
            char label = labels[i];
            bool hit = (label is >= '1' and <= '9' && IsKeyPressed(keyboard, Keys.D1 + (label - '1')))
                || (label == 'G' && IsKeyPressed(keyboard, Keys.G))
                || IsKeyPressed(keyboard, Keys.D1 + i);
            if (hit)
                pick = i;
        }
        if (pick < 0 && mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
            && InterfaceFrm(ElevatorTables.Backgrounds[picker.Type].BackgroundFrmId) is { } bg
            && InterfaceFrm(ElevatorTables.ButtonUpFrmId) is { } btn)
        {
            for (int i = 0; i < picker.Levels && pick < 0; i++)
                if (ElevatorButtonRect(bg, btn, i).Contains(mouse.X, mouse.Y))
                    pick = i;
        }
        if (pick < 0)
            return;

        if (pick == picker.Current || InterfaceFrm(ElevatorTables.Backgrounds[picker.Type].BackgroundFrmId) is null)
        {
            // Same level (no sweep, elevator.cc:424) or no art to animate → immediate.
            _elevatorPicker = null;
            RideElevator(picker.Type, pick, picker.Current);
            return;
        }
        // Start the sweep: sfx up front like soundPlayFile at :438, gauge in slice units.
        if (Formats.Sound.SfxName.Elevator(picker.Levels, Math.Abs(pick - picker.Current)) is { } rideSfx)
            _audio?.PlaySfx(rideSfx);
        double perLevel = GaugeStep(picker.Levels);
        _elevatorRide = (picker.Type, pick, picker.Current, picker.Current * perLevel, pick * perLevel, 200.0);
    }

    private void DrawElevatorPicker()
    {
        if (_elevatorPicker is not { } picker || _fontRenderer is null)
            return;

        (int bgId, int panelId) = ElevatorTables.Backgrounds[picker.Type];
        Texture2D? bg = InterfaceFrm(bgId);
        Texture2D? btnUp = InterfaceFrm(ElevatorTables.ButtonUpFrmId);
        Texture2D? btnDown = InterfaceFrm(ElevatorTables.ButtonDownFrmId);
        Texture2D? gauge = InterfaceFrm(ElevatorTables.GaugeFrmId);
        if (bg is null || btnUp is null || btnDown is null || gauge is null)
        {
            DrawElevatorPickerFallback(picker);
            return;
        }

        Point p = ElevatorWindowPos(bg);
        _spriteBatch.Draw(bg, new Vector2(p.X, p.Y), Color.White);
        // The optional button-column panel sits flush with the window bottom (elevatorWindowInit :574).
        if (InterfaceFrm(panelId) is { } panel)
            _spriteBatch.Draw(panel, new Vector2(p.X, p.Y + bg.Height - panel.Height), Color.White);

        // The gauge: 13 stacked slices; the shown slice = gauge position in slice units, blitted
        // window-local (121, 41) (elevatorSelectLevel :384-392).
        double gaugePos = _elevatorRide?.Gauge ?? picker.Current * GaugeStep(picker.Levels);
        int sliceH = gauge.Height / ElevatorTables.GaugeSlices;
        int slice = Math.Clamp((int)gaugePos, 0, ElevatorTables.GaugeSlices - 1);
        _spriteBatch.Draw(gauge, new Vector2(p.X + 121, p.Y + 41),
            new Rectangle(0, slice * sliceH, gauge.Width, sliceH), Color.White);

        MouseState mouse = Mouse.GetState();
        for (int i = 0; i < picker.Levels; i++)
        {
            Rectangle r = ElevatorButtonRect(bg, btnUp, i);
            bool pressed = mouse.LeftButton == ButtonState.Pressed && r.Contains(mouse.X, mouse.Y);
            _spriteBatch.Draw(pressed ? btnDown : btnUp, new Vector2(r.X, r.Y), Color.White);
        }
    }

    /// <summary>The pre-P119 text list, kept as the missing-art residual.</summary>
    private void DrawElevatorPickerFallback((int Type, int Current, int Levels) picker)
    {
        _panelPixel ??= CreatePixel();
        int w = 160, h = 24 + picker.Levels * 22;
        int x = (GraphicsDevice.Viewport.Width - w) / 2, y = (GraphicsDevice.Viewport.Height - h) / 2;
        _spriteBatch.Draw(_panelPixel, new Rectangle(x, y, w, h), new Color(12, 12, 12, 235));
        _fontRenderer!.Draw(_spriteBatch, "ELEVATOR", new Vector2(x + 10, y + 6), new Color(252, 252, 84));
        char[] labels = ElevatorTables.LevelLabels[picker.Type];
        for (int i = 0; i < picker.Levels; i++)
        {
            Color c = i == picker.Current ? new Color(252, 252, 84) : new Color(0, 252, 0);
            _fontRenderer.Draw(_spriteBatch, $"[{labels[i]}] Level {labels[i]}", new Vector2(x + 14, y + 24 + i * 22), c);
        }
    }
}
