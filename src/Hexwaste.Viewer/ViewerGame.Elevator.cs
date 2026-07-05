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
    private void RideElevator(int type, int button, int startButton)
    {
        (int map, int elevation, int tile) = ElevatorTables.Descriptions[type][button];
        if (tile == -1)
            return; // unused button slot
        // P117 sfx: the ride sound by level count + levels travelled (elevator.cc:438).
        if (Formats.Sound.SfxName.Elevator(ElevatorTables.Levels[type], Math.Abs(button - startButton)) is { } rideSfx)
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

    /// <summary>The live level picker — a minimal modal (the authentic panel art is deferred). The
    /// button LABEL chars (elevator.cc gElevatorLevelLabels) are the keyboard shortcuts, plus 1..n.</summary>
    private void UpdateElevatorPicker(KeyboardState keyboard)
    {
        if (_elevatorPicker is not { } picker)
            return;
        if (IsKeyPressed(keyboard, Keys.Escape))
        {
            _elevatorPicker = null;
            return;
        }
        char[] labels = ElevatorTables.LevelLabels[picker.Type];
        for (int i = 0; i < picker.Levels; i++)
        {
            char label = labels[i];
            bool hit = (label is >= '1' and <= '9' && IsKeyPressed(keyboard, Keys.D1 + (label - '1')))
                || (label == 'G' && IsKeyPressed(keyboard, Keys.G))
                || IsKeyPressed(keyboard, Keys.D1 + i);
            if (hit)
            {
                _elevatorPicker = null;
                RideElevator(picker.Type, i, picker.Current);
                return;
            }
        }
    }

    private void DrawElevatorPicker()
    {
        if (_elevatorPicker is not { } picker || _fontRenderer is null)
            return;
        _panelPixel ??= CreatePixel();
        int w = 160, h = 24 + picker.Levels * 22;
        int x = (GraphicsDevice.Viewport.Width - w) / 2, y = (GraphicsDevice.Viewport.Height - h) / 2;
        _spriteBatch.Draw(_panelPixel, new Rectangle(x, y, w, h), new Color(12, 12, 12, 235));
        _fontRenderer.Draw(_spriteBatch, "ELEVATOR", new Vector2(x + 10, y + 6), new Color(252, 252, 84));
        char[] labels = ElevatorTables.LevelLabels[picker.Type];
        for (int i = 0; i < picker.Levels; i++)
        {
            Color c = i == picker.Current ? new Color(252, 252, 84) : new Color(0, 252, 0);
            _fontRenderer.Draw(_spriteBatch, $"[{labels[i]}] Level {labels[i]}", new Vector2(x + 14, y + 24 + i * 22), c);
        }
    }
}
