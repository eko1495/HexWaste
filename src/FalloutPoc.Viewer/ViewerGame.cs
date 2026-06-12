using FalloutPoc.Formats;
using FalloutPoc.Formats.Art;
using FalloutPoc.Formats.Map;
using FalloutPoc.Formats.Pal;
using FalloutPoc.Formats.Proto;
using FalloutPoc.Formats.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace FalloutPoc.Viewer;

public sealed class ViewerGame : Game
{
    private readonly string _gameDir;
    private readonly string _mapName;
    private readonly string? _screenshotPath;

    private readonly GraphicsDeviceManager _graphics;
    private readonly Camera _camera = new();

    private GameFileSystem _vfs = null!;
    private Palette _palette = null!;
    private PaletteCycler _cycler = null!;
    private MapFile _map = null!;
    private FrmCache _frmCache = null!;
    private SpriteBatch _spriteBatch = null!;

    /// <summary>Pre-advances palette cycling before the first frame (screenshot testing).</summary>
    public double AdvanceCyclingMs { get; set; }

    /// <summary>When set, measures this many frames, prints a timing report and exits.</summary>
    public int BenchFrames { get; set; }

    /// <summary>Starts with critters in the walk cycle (screenshot testing of the T toggle).</summary>
    public bool StartInWalkMode { get; set; }

    private readonly System.Diagnostics.Stopwatch _frameClock = new();
    private readonly List<double> _updateMs = [];
    private readonly List<double> _drawMs = [];
    private int _paletteUploads;
    private double _fpsTimer;
    private int _fpsFrames;
    private string _baseTitle = "FalloutPoc viewer";

    private ProtoDatabase _protos = null!;
    private ArtIndex _artIndex = null!;
    private ObjectAnimator _animator = null!;
    private bool _walkMode;
    private MapObject? _hoveredObject;
    private bool _pickPrinted;
    private DudeController? _dude;
    private HashSet<int> _blockedTiles = [];
    private MapList _mapList = null!;
    private CityList _cities = null!;
    private AudioManager? _audio;
    private int _stepCounter;
    private WorldmapScreen? _worldmapScreen;
    private bool _worldmapOpen;
    private WorldArea? _hoveredArea;
    private Formats.Light.LightGrid _lightGrid = new();

    /// <summary>Open the worldmap on start (screenshot testing).</summary>
    public bool StartOnWorldmap { get; set; }

    /// <summary>Travel to this city.txt area index right after load (screenshot testing).</summary>
    public int? TravelToArea { get; set; }
    private AafFontRenderer? _fontRenderer;

    /// <summary>Ambient light as a fraction of full brightness (CLI --ambient).</summary>
    public double InitialAmbient { get; set; } = 1.0;
    private ProtoMessages _protoMessages = null!;
    private readonly List<string> _messageLog = [];
    private string _currentMapName = "";

    /// <summary>Screen point to examine before the first frame (screenshot testing).</summary>
    public Point? ExamineAt { get; set; }

    /// <summary>Disables all audio (headless/CI runs).</summary>
    public bool DisableAudio { get; set; }
    private readonly HashSet<MapObject> _openDoors = [];
    private MapDestination? _pendingTransition;

    /// <summary>Hex tile the dude should walk to right after load (screenshot testing).</summary>
    public int? WalkToTile { get; set; }

    /// <summary>Toggles the door at this hex right after load, ignoring distance (screenshot testing).</summary>
    public int? ToggleDoorAtTile { get; set; }

    /// <summary>Screen point to pick before the first frame (screenshot testing).</summary>
    public Point? PickAt { get; set; }

    private int _elevation;
    private bool _roofsVisible = true;
    private MouseState _previousMouse;
    private KeyboardState _previousKeyboard;
    private readonly HashSet<int> _failedFids = [];

    /// <summary>Objects per elevation, pre-sorted for drawing: flats then
    /// non-flats, each in ascending hex tile order (stable within a tile).</summary>
    private readonly List<MapObject>[] _flatObjects = new List<MapObject>[MapFile.ElevationCount];
    private readonly List<MapObject>[] _solidObjects = new List<MapObject>[MapFile.ElevationCount];

    public ViewerGame(string gameDir, string mapName, string? screenshotPath = null, bool roofsVisible = true)
    {
        _gameDir = gameDir;
        _mapName = mapName;
        _screenshotPath = screenshotPath;
        _roofsVisible = roofsVisible;

        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
        };
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.ClientSizeChanged += (_, _) =>
            _camera.SetWindowSize(Window.ClientBounds.Width, Window.ClientBounds.Height);
    }

    protected override void Initialize()
    {
        // The simulation is wall-time driven (palette cycling, soon animations),
        // so rendering speed never affects game speed. MonoGame's default fixed
        // 60 Hz update is kept for interactive use; benchmarks unlock both the
        // timestep and vsync to measure raw frame cost.
        if (BenchFrames > 0)
        {
            IsFixedTimeStep = false;
            _graphics.SynchronizeWithVerticalRetrace = false;
            _graphics.ApplyChanges();
        }

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);

        _vfs = GameFileSystem.Open(_gameDir);
        _palette = Palette.Load(_vfs.ReadAllBytes("color.pal"));

        _protos = new ProtoDatabase(_vfs);
        _cycler = new PaletteCycler(_palette);
        _artIndex = new ArtIndex(_vfs);
        _frmCache = new FrmCache(_vfs, _artIndex, GraphicsDevice, _palette);
        _mapList = MapList.Load(_vfs);
        _cities = CityList.Load(_vfs);
        if (!DisableAudio)
            _audio = new AudioManager(_vfs, _gameDir);
        _protoMessages = new ProtoMessages(_vfs, _protos);

        // font1.aaf is the standard readable interface font.
        if (_vfs.Exists("font1.aaf"))
            _fontRenderer = new AafFontRenderer(GraphicsDevice, AafFont.Load(_vfs.ReadAllBytes("font1.aaf")));
        else
            Console.Error.WriteLine("font1.aaf not found — text overlay disabled");

        LoadMap(_mapName, spawnAt: null);

        _worldmapScreen = new WorldmapScreen(GraphicsDevice, _vfs, _palette, _cities, _fontRenderer);
        if (StartOnWorldmap)
            _worldmapOpen = true;
        if (TravelToArea is { } areaIndex)
        {
            WorldArea? area = _cities.Areas.FirstOrDefault(a => a.Index == areaIndex);
            if (area is not null)
                TravelTo(area);
            else
                Console.Error.WriteLine($"no area {areaIndex} in city.txt");
        }

        if (ExamineAt is { } examinePoint)
        {
            MapObject? target = PickObject(examinePoint.X, examinePoint.Y);
            Console.WriteLine($"examine@{examinePoint.X},{examinePoint.Y}: "
                + (target is null ? "nothing" : $"{ObjectName(target)} — {ObjectDescription(target)}"));
            if (target is not null)
                Examine(target);
        }

        if (StartInWalkMode)
            ToggleWalkMode();
        if (ToggleDoorAtTile is { } doorTile)
        {
            MapObject? door = _solidObjects[_elevation].FirstOrDefault(o => o.HexTile == doorTile && IsDoor(o));
            if (door is not null)
                ToggleDoor(door);
            else
                Console.Error.WriteLine($"no door at hex {doorTile}");
        }

        if (WalkToTile is { } walkTarget && _dude is not null && !_dude.WalkTo(walkTarget))
            Console.Error.WriteLine($"no path to hex {walkTarget}");

        // Step cycling/animations in small increments so pre-advancing N ms
        // lands on the same state as N ms of real frames (screenshot testing).
        for (double advanced = 0; advanced < AdvanceCyclingMs; advanced += 10)
        {
            _cycler.Update(10);
            _animator.Update(10);
            _dude?.Update(10);
            if (_pendingTransition is { } transition)
            {
                _pendingTransition = null;
                ApplyTransition(transition);
            }
        }
        _frmCache.OnPaletteChanged(_palette);
    }

    /// <summary>
    /// Loads (or transitions to) a map. <paramref name="spawnAt"/> places the
    /// dude at an exit-grid/stairs destination; null uses the map's entering
    /// position.
    /// </summary>
    private void LoadMap(string mapName, MapDestination? spawnAt)
    {
        _currentMapName = mapName;
        using (Stream stream = _vfs.OpenRead($@"maps\{mapName}"))
            _map = MapFile.Load(stream, _protos);

        _animator = new ObjectAnimator(_frmCache);
        _walkMode = false;
        _hoveredObject = null;
        _dude = null;
        _openDoors.Clear();

        // Tile -1 in a destination (e.g. city.txt entrances) means "use the
        // map's own entering position".
        int spawnTile = spawnAt is { Tile: > 0 } ? spawnAt.Tile : _map.Header.EnteringTile;
        int spawnRotation = spawnAt is { Tile: > 0 } ? spawnAt.Rotation : _map.Header.EnteringRotation;
        _elevation = spawnAt is { Elevation: >= 0 } ? spawnAt.Elevation : _map.Header.EnteringElevation;
        if (_elevation is < 0 or >= MapFile.ElevationCount || _map.Elevations[_elevation] is null)
            _elevation = Array.FindIndex(_map.Elevations, e => e is not null);

        // ported from fallout2-ce src/object.cc _obj_render_pre_roof(): flat
        // objects draw first, then non-flat, both in hex tile order (the order
        // table sorts by tile-number offset). Heads are out of scope.
        for (int elevation = 0; elevation < MapFile.ElevationCount; elevation++)
        {
            IEnumerable<MapObject> drawable = (_map.Elevations[elevation]?.Objects ?? [])
                .Where(o => !o.IsHidden && Fid.Type(o.Fid) is not ObjectType.Head && o.HexTile >= 0);
            _flatObjects[elevation] = [.. drawable.Where(o => o.IsFlat).OrderBy(o => o.HexTile)];
            _solidObjects[elevation] = [.. drawable.Where(o => !o.IsFlat).OrderBy(o => o.HexTile)];
        }

        // Some critter FIDs reference weapon-pose art that doesn't ship; fall
        // back to the unarmed pose like the engine's artExists() probing.
        foreach (List<MapObject> objects in _flatObjects.Concat(_solidObjects))
        {
            foreach (MapObject obj in objects.Where(o => Fid.Type(o.Fid) is ObjectType.Critter))
            {
                if (!_vfs.Exists(_artIndex.GetFrmPath(obj.Fid)) && Fid.WeaponCode(obj.Fid) != 0)
                {
                    int unarmed = Fid.Build(ObjectType.Critter, Fid.Index(obj.Fid),
                        Fid.AnimType(obj.Fid), 0, Fid.Rotation(obj.Fid));
                    if (_vfs.Exists(_artIndex.GetFrmPath(unarmed)))
                        obj.Fid = unarmed;
                }
            }
        }

        StartLoopingAnimations();
        SpawnDude(spawnTile, spawnRotation);
        RebuildLighting();

        _camera.SetWindowSize(Window.ClientBounds.Width, Window.ClientBounds.Height);
        _camera.SetCenter(_dude?.Dude.HexTile ?? _map.Header.EnteringTile);
        _camera.PanX = 0;
        _camera.PanY = 0;

        _baseTitle = $"FalloutPoc viewer — {_map.Header.Name} (elevation {_elevation})";
        Window.Title = _baseTitle;

        _audio?.PlayMusic(_mapList.GetMusic(mapName));
    }

    protected override void Update(GameTime gameTime)
    {
        _frameClock.Restart();
        KeyboardState keyboard = Keyboard.GetState();
        MouseState mouse = Mouse.GetState();

        // Worldmap mode swallows map input.
        if (_worldmapOpen)
        {
            if (IsKeyPressed(keyboard, Keys.Escape) || IsKeyPressed(keyboard, Keys.M))
                _worldmapOpen = false;

            _hoveredArea = _worldmapScreen?.HitTest(mouse.X, mouse.Y, GraphicsDevice.Viewport.Bounds);
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
                && _hoveredArea is not null)
                TravelTo(_hoveredArea);

            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        if (IsKeyPressed(keyboard, Keys.M))
            _worldmapOpen = true;

        if (keyboard.IsKeyDown(Keys.Escape))
            Exit();

        int panSpeed = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift) ? 32 : 8;
        if (keyboard.IsKeyDown(Keys.Left))
            _camera.PanX += panSpeed;
        if (keyboard.IsKeyDown(Keys.Right))
            _camera.PanX -= panSpeed;
        if (keyboard.IsKeyDown(Keys.Up))
            _camera.PanY += panSpeed;
        if (keyboard.IsKeyDown(Keys.Down))
            _camera.PanY -= panSpeed;

        if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Pressed)
        {
            _camera.PanX += mouse.X - _previousMouse.X;
            _camera.PanY += mouse.Y - _previousMouse.Y;
        }

        // PgUp/PgDn cycle through present elevations.
        if (IsKeyPressed(keyboard, Keys.PageUp))
            SwitchElevation(+1);
        if (IsKeyPressed(keyboard, Keys.PageDown))
            SwitchElevation(-1);

        if (IsKeyPressed(keyboard, Keys.R))
            _roofsVisible = !_roofsVisible;

        if (IsKeyPressed(keyboard, Keys.T))
            ToggleWalkMode();

        // [ and ] adjust ambient light (day/night preview).
        if (IsKeyPressed(keyboard, Keys.OemOpenBrackets) || IsKeyPressed(keyboard, Keys.OemCloseBrackets))
        {
            double step = IsKeyPressed(keyboard, Keys.OemOpenBrackets) ? -0.1 : 0.1;
            InitialAmbient = Math.Clamp(InitialAmbient + step, 0.25, 1.0);
            _lightGrid.Ambient = (int)(InitialAmbient * Formats.Light.LightGrid.IntensityMax);
            Log($"ambient light {InitialAmbient:P0}");
        }

        _animator.Update(gameTime.ElapsedGameTime.TotalMilliseconds);
        _dude?.Update(gameTime.ElapsedGameTime.TotalMilliseconds);

        // Map transitions are queued by exit grids/stairs and applied here,
        // never while DudeController.Update is still on the stack.
        if (_pendingTransition is { } transition)
        {
            _pendingTransition = null;
            ApplyTransition(transition);
        }

        if (_cycler.Update(gameTime.ElapsedGameTime.TotalMilliseconds))
        {
            _frmCache.OnPaletteChanged(_palette);
            _paletteUploads++;
        }

        // Hover picking; click prints the object's identity.
        MapObject? previousHover = _hoveredObject;
        _hoveredObject = PickAt is { } fixedPoint
            ? PickObject(fixedPoint.X, fixedPoint.Y)
            : PickObject(mouse.X, mouse.Y);

        if (PickAt is { } p && !_pickPrinted)
        {
            Console.WriteLine($"pick@{p.X},{p.Y}: "
                + (_hoveredObject is null ? "nothing" : DescribeObject(_hoveredObject)));
            _pickPrinted = true;
        }

        if (_hoveredObject != previousHover)
            Window.Title = _hoveredObject is null ? _baseTitle : $"{_baseTitle} — {DescribeObject(_hoveredObject)}";

        // Right-click examines the object under the cursor.
        if (mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released
            && _hoveredObject is not null && _hoveredObject != _dude?.Dude)
            Examine(_hoveredObject);

        // Click: doors toggle, stairs/ladders travel, other objects identify,
        // open ground walks.
        if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
        {
            if (_hoveredObject is not null && _hoveredObject != _dude?.Dude)
                InteractWith(_hoveredObject);
            else if (_dude is not null)
            {
                int target = _camera.ScreenToHex(mouse.X, mouse.Y);
                if (target >= 0 && !_dude.WalkTo(target))
                    Console.WriteLine($"no path to hex {target}");
            }
        }

        _previousMouse = mouse;
        _previousKeyboard = keyboard;
        base.Update(gameTime);
        _updateMs.Add(_frameClock.Elapsed.TotalMilliseconds);
    }

    private bool IsKeyPressed(KeyboardState keyboard, Keys key) =>
        keyboard.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);

    /// <summary>
    /// Multi-frame scenery/misc art loops forever (vanilla starts these via
    /// scripts; no VM here). Doors stay on their stored frame.
    /// </summary>
    private void StartLoopingAnimations()
    {
        int animatedCount = 0;
        foreach (List<MapObject> objects in _flatObjects.Concat(_solidObjects))
        {
            foreach (MapObject obj in objects)
            {
                if (Fid.Type(obj.Fid) is not (ObjectType.Scenery or ObjectType.Misc))
                    continue;

                try
                {
                    if (Fid.Type(obj.Fid) is ObjectType.Scenery
                        && Fid.PidType(obj.Pid) == (int)ObjectType.Scenery
                        && _protos.Get(obj.Pid).SubType == 0) // SCENERY_TYPE_DOOR
                        continue;

                    if (_frmCache.GetFrm(obj.Fid).FrameCount > 1)
                    {
                        _animator.AddLooping(obj);
                        animatedCount++;
                    }
                }
                catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
                {
                    // missing art/proto — object simply won't animate
                }
            }
        }

        Console.WriteLine($"animating {animatedCount} multi-frame scenery/misc object(s)");
    }

    /// <summary>
    /// Spawns the player stand-in (tribal male) at the map's entering tile and
    /// builds the blocking set the pathfinder consults.
    /// </summary>
    private void SpawnDude(int tile, int rotation)
    {
        int critterIndex = _artIndex.FindCritterIndex("hmwarr");
        if (critterIndex < 0)
        {
            Console.Error.WriteLine("hmwarr not found in critters.lst — no dude");
            return;
        }

        var dude = new MapObject
        {
            Id = -1,
            HexTile = tile,
            X = 0,
            Y = 0,
            Frame = 0,
            Rotation = Math.Clamp(rotation, 0, 5),
            Fid = Fid.Build(ObjectType.Critter, critterIndex),
            Flags = 0,
            Pid = 0x01000001,
        };

        RebuildBlockedTiles(dude);
        _dude = new DudeController(dude, _frmCache, tile => _blockedTiles.Contains(tile));
        _dude.TileChanged += tile =>
        {
            // Keep hex z-order: re-insert at the new tile's sorted position.
            List<MapObject> solids = _solidObjects[_elevation];
            solids.Remove(dude);
            InsertSorted(solids, dude);
            _camera.SetCenter(tile);
            _camera.PanX = 0;
            _camera.PanY = 0;

            // Approximation: the original ties steps to walk-FRM action
            // frames; we alternate the two shipped footstep sfx per hex.
            if (++_stepCounter % 2 == 0)
                _audio?.PlaySfx(_stepCounter % 4 == 0 ? "FOOTSTE1" : "FOOTSTEP");

            CheckExitGridAt(tile);
        };

        InsertSorted(_solidObjects[_elevation], dude);
        _camera.SetCenter(dude.HexTile);
    }

    private static void InsertSorted(List<MapObject> objects, MapObject obj)
    {
        int index = objects.FindIndex(o => o.HexTile > obj.HexTile);
        objects.Insert(index < 0 ? objects.Count : index, obj);
    }

    /// <summary>
    /// Blocking per fallout2-ce src/object.cc _obj_blocking_at(): visible,
    /// non-NO_BLOCK critters/scenery/walls block their tile; MULTIHEX objects
    /// also block their six neighbors. Computed once per elevation (static
    /// scene — only the dude moves).
    /// </summary>
    private void RebuildBlockedTiles(MapObject? exclude)
    {
        const int objectNoBlock = 0x10;
        const int objectMultiHex = 0x800;

        _blockedTiles = [];
        foreach (MapObject obj in _solidObjects[_elevation])
        {
            if (obj == exclude || (obj.Flags & objectNoBlock) != 0)
                continue;
            if (Fid.Type(obj.Fid) is not (ObjectType.Critter or ObjectType.Scenery or ObjectType.Wall))
                continue;

            _blockedTiles.Add(obj.HexTile);
            if ((obj.Flags & objectMultiHex) != 0)
                for (int rotation = 0; rotation < 6; rotation++)
                    _blockedTiles.Add(Formats.Hex.HexGrid.TileInDirection(obj.HexTile, rotation));
        }

        foreach (MapObject door in _openDoors)
            _blockedTiles.Remove(door.HexTile);
    }

    /// <summary>
    /// Computes static per-hex light for the current elevation: every visible
    /// OBJECT_LIGHTING emitter spreads via the ported _obj_adjust_light()
    /// falloff/occlusion. Recomputed on map load and elevation switch (the
    /// scene is static; only the dude moves and he emits no light).
    /// </summary>
    private void RebuildLighting()
    {
        const int objectLighting = 0x20;

        _lightGrid.Reset();
        _lightGrid.Ambient = (int)Math.Clamp(InitialAmbient * Formats.Light.LightGrid.IntensityMax,
            Formats.Light.LightGrid.IntensityMin, Formats.Light.LightGrid.IntensityMax);

        List<MapObject> all = [.. _flatObjects[_elevation], .. _solidObjects[_elevation]];
        var byTile = all.GroupBy(o => o.HexTile).ToDictionary(g => g.Key, g => g.ToList());

        IEnumerable<Formats.Light.LightBlocker> BlockersAt(int tile)
        {
            if (!byTile.TryGetValue(tile, out List<MapObject>? objects))
                yield break;
            foreach (MapObject obj in objects)
            {
                bool isWall = Fid.Type(obj.Fid) is ObjectType.Wall;
                int wallExtendedFlags = 0;
                if (isWall)
                {
                    try
                    {
                        wallExtendedFlags = _protos.Get(obj.Pid).ExtendedFlags;
                    }
                    catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
                    {
                    }
                }

                yield return new Formats.Light.LightBlocker(
                    (obj.Flags & 0x20000000) != 0, // OBJECT_LIGHT_THRU
                    isWall,
                    obj.IsFlat,
                    wallExtendedFlags);
            }
        }

        foreach (MapObject emitter in all)
        {
            if ((emitter.Flags & objectLighting) != 0 && emitter.LightIntensity > 0)
                _lightGrid.AddObjectLight(emitter.HexTile, emitter.LightDistance, emitter.LightIntensity, BlockersAt);
        }
    }

    /// <summary>Brightness multiplier for a hex (the original's per-object uniform intensity).</summary>
    private Color LightTint(int hexTile)
    {
        float factor = _lightGrid.GetTileIntensity(hexTile) / (float)Formats.Light.LightGrid.IntensityMax;
        byte level = (byte)Math.Clamp((int)(factor * 255), 0, 255);
        return new Color(level, level, level);
    }

    private bool IsAdjacentToDude(MapObject obj)
    {
        if (_dude is null)
            return false;
        int dudeTile = _dude.Dude.HexTile;
        if (obj.HexTile == dudeTile)
            return true;
        return Enumerable.Range(0, 6).Any(r => Formats.Hex.HexGrid.TileInDirection(dudeTile, r) == obj.HexTile);
    }

    private bool IsDoor(MapObject obj)
    {
        if (Fid.Type(obj.Fid) is not ObjectType.Scenery || Fid.PidType(obj.Pid) != (int)ObjectType.Scenery)
            return false;
        try
        {
            return _protos.Get(obj.Pid).SubType == 0; // SCENERY_TYPE_DOOR
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>
    /// Hardcoded interactions, no script VM: doors open/close with their FRM
    /// animation and unblock/block their hex; stairs and ladders travel to
    /// their stored destination.
    /// </summary>
    private void InteractWith(MapObject obj)
    {
        if (IsDoor(obj))
        {
            if (!IsAdjacentToDude(obj))
            {
                Console.WriteLine("too far from the door");
                return;
            }
            ToggleDoor(obj);
            return;
        }

        if (obj.Destination is { } destination && Fid.Type(obj.Fid) is ObjectType.Scenery)
        {
            if (!IsAdjacentToDude(obj))
            {
                Console.WriteLine("too far to use");
                return;
            }
            _pendingTransition = destination;
            return;
        }

        Console.WriteLine($"picked: {DescribeObject(obj)}");
    }

    private void ToggleDoor(MapObject door)
    {
        int frameCount;
        try
        {
            frameCount = _frmCache.GetFrm(door.Fid).FrameCount;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            return;
        }

        byte soundId = 0;
        try
        {
            soundId = _protos.Get(door.Pid).SoundId;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
        }

        if (_openDoors.Remove(door))
        {
            _animator.PlayOnceReverse(door, frameCount - 1);
            _blockedTiles.Add(door.HexTile);
            Console.WriteLine("door closes");
            Log($"The {ObjectName(door)} closes.");
            _audio?.PlaySfx(Formats.Sound.SfxName.Door(Formats.Sound.SfxName.SceneryAction.Close, soundId));
        }
        else
        {
            _openDoors.Add(door);
            _animator.PlayOnce(door);
            _blockedTiles.Remove(door.HexTile);
            Console.WriteLine("door opens");
            Log($"The {ObjectName(door)} opens.");
            _audio?.PlaySfx(Formats.Sound.SfxName.Door(Formats.Sound.SfxName.SceneryAction.Open, soundId));
        }
    }

    /// <summary>
    /// Exit grid / stairs / ladder travel, mirroring fallout2-ce
    /// src/proto_instance.cc useStairs()/useLadder*(): map &gt; 0 loads another
    /// map via maps.txt; otherwise it's a teleport within the current map.
    /// </summary>
    private void ApplyTransition(MapDestination destination)
    {
        if (destination.Map > 0)
        {
            string? mapFile = _mapList.GetMapFileName(destination.Map);
            if (mapFile is null)
            {
                Console.WriteLine($"unknown destination map index {destination.Map}");
                return;
            }

            Console.WriteLine($"travelling to {mapFile} (tile {destination.Tile}, elevation {destination.Elevation})");
            LoadMap(mapFile, destination);
            return;
        }

        if (destination.Map < 0)
        {
            _worldmapOpen = true;
            Log("You head out to the wasteland.");
            return;
        }

        // Same-map teleport (stairs/ladders with map == 0).
        if (_dude is null)
            return;
        _dude.Stop();
        _solidObjects[_elevation].Remove(_dude.Dude);
        _elevation = destination.Elevation is >= 0 and < MapFile.ElevationCount
            && _map.Elevations[destination.Elevation] is not null
            ? destination.Elevation
            : _elevation;
        _dude.Dude.HexTile = destination.Tile;
        InsertSorted(_solidObjects[_elevation], _dude.Dude);
        RebuildBlockedTiles(_dude.Dude);
        _camera.SetCenter(destination.Tile);
        _camera.PanX = 0;
        _camera.PanY = 0;
        _baseTitle = $"FalloutPoc viewer — {_map.Header.Name} (elevation {_elevation})";
        Window.Title = _baseTitle;
    }

    /// <summary>Travels to a worldmap area: first usable entrance, resolved via maps.txt lookup names.</summary>
    private void TravelTo(WorldArea area)
    {
        // ported behavior from fallout2-ce src/worldmap.cc
        // wmAreaFindFirstValidMap(): first enabled entrance, else force the first.
        AreaEntrance entrance = area.Entrances.FirstOrDefault(e => e.StartsOn) ?? area.Entrances.First();

        int mapIndex = _mapList.FindByLookupName(entrance.MapLookupName);
        string? mapFile = mapIndex >= 0 ? _mapList.GetMapFileName(mapIndex) : null;
        if (mapFile is null)
        {
            Console.Error.WriteLine($"area '{area.Name}': cannot resolve map '{entrance.MapLookupName}'");
            return;
        }

        _worldmapOpen = false;
        Console.WriteLine($"travelling to {area.Name} -> {mapFile}");
        LoadMap(mapFile, new MapDestination(mapIndex, entrance.Tile, entrance.Elevation, entrance.Rotation));
        Log($"You arrive at {area.Name}.");
    }

    /// <summary>Queues the transition when the dude steps onto an exit grid.</summary>
    private void CheckExitGridAt(int tile)
    {
        MapObject? exitGrid = _flatObjects[_elevation]
            .FirstOrDefault(o => o.HexTile == tile && Fid.IsExitGridPid(o.Pid) && o.Destination is not null);
        if (exitGrid?.Destination is { } destination)
            _pendingTransition = destination;
    }

    /// <summary>Toggles all critters between their map pose and a walk cycle in place.</summary>
    private void ToggleWalkMode()
    {
        _walkMode = !_walkMode;

        foreach (MapObject obj in _solidObjects[_elevation])
        {
            if (Fid.Type(obj.Fid) is not ObjectType.Critter)
                continue;

            if (!_walkMode)
            {
                _animator.Remove(obj);
                continue;
            }

            // ANIM_WALK = 1; prefer the armed walk art, fall back to unarmed.
            int walkFid = Fid.Build(ObjectType.Critter, Fid.Index(obj.Fid), 1, Fid.WeaponCode(obj.Fid));
            if (!_vfs.Exists(_artIndex.GetFrmPath(walkFid)))
                walkFid = Fid.Build(ObjectType.Critter, Fid.Index(obj.Fid), 1);
            if (_vfs.Exists(_artIndex.GetFrmPath(walkFid)))
                _animator.SetCritterAnimation(obj, walkFid);
        }
    }

    private void SwitchElevation(int direction)
    {
        for (int next = _elevation + direction; next >= 0 && next < MapFile.ElevationCount; next += direction)
        {
            if (_map.Elevations[next] is not null)
            {
                if (_dude is not null)
                {
                    _dude.Stop();
                    _solidObjects[_elevation].Remove(_dude.Dude);
                    InsertSorted(_solidObjects[next], _dude.Dude);
                }

                _elevation = next;
                if (_dude is not null)
                    RebuildBlockedTiles(_dude.Dude);
                _baseTitle = $"FalloutPoc viewer — {_map.Header.Name} (elevation {_elevation})";
                break;
            }
        }
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);

        // Draw order ported from the fallout2-ce render loop (src/map.cc
        // isoWindowRefreshRectGame): floors -> flat objects -> non-flat
        // objects -> roofs.
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        if (_worldmapOpen)
        {
            _worldmapScreen?.Draw(_spriteBatch, GraphicsDevice.Viewport.Bounds, _hoveredArea);
        }
        else
        {
            DrawFloors();
            DrawObjects(_flatObjects[_elevation]);
            DrawObjects(_solidObjects[_elevation]);
            if (_roofsVisible)
                DrawRoofs();
            DrawTextOverlay();
        }
        _spriteBatch.End();

        base.Draw(gameTime);
        _drawMs.Add(_frameClock.Elapsed.TotalMilliseconds);

        _fpsFrames++;
        _fpsTimer += gameTime.ElapsedGameTime.TotalSeconds;
        if (_fpsTimer >= 1.0)
        {
            Window.Title = $"{_baseTitle} — {_fpsFrames / _fpsTimer:F0} fps";
            _fpsTimer = 0;
            _fpsFrames = 0;
        }

        if (BenchFrames > 0 && _drawMs.Count >= BenchFrames)
        {
            PrintBenchReport();
            Exit();
        }

        if (_screenshotPath is not null)
        {
            SaveScreenshot(_screenshotPath);
            Exit();
        }
    }

    private void PrintBenchReport()
    {
        // _frameClock spans Update begin -> Draw end, so _drawMs holds full
        // frame times; skip warm-up frames where textures are first created.
        var frames = _drawMs.Skip(Math.Min(30, _drawMs.Count / 10)).OrderBy(t => t).ToList();
        double avg = frames.Average();
        double p95 = frames[(int)(frames.Count * 0.95)];
        Console.WriteLine($"bench: {frames.Count} frames (after warm-up), full frame avg {avg:F2} ms, "
            + $"p95 {p95:F2} ms, max {frames[^1]:F2} ms (~{1000 / avg:F0} fps uncapped)");
        Console.WriteLine($"bench: palette uploads {_paletteUploads}, cycling FRMs {_frmCache.CyclingEntryCount}");
    }

    private void DrawFloors()
    {
        MapElevation? elevation = _map.Elevations[_elevation];
        if (elevation is null)
            return;

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
            _spriteBatch.Draw(texture, new Vector2(x, y), LightTint(SquareToHex(square)));
        }
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

    private sealed record SpriteInfo(int Fid, int FrameIndex, int Rotation,
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

        bool isDude = _dude is not null && obj == _dude.Dude;
        AnimationState? animation = null;
        if (!isDude && _animator.TryGetState(obj, out AnimationState state))
            animation = state;

        int fid = isDude ? _dude!.CurrentFid
            : animation is { DisplayFid: not 0 } ? animation.DisplayFid
            : obj.Fid;

        Formats.Frm.FrmFile frm;
        Formats.Frm.FrmFrame frame;
        int rotation;
        int frameIndex;
        try
        {
            frm = _frmCache.GetFrm(fid);
            rotation = Math.Clamp(obj.Rotation, 0, Formats.Frm.FrmFile.RotationCount - 1);
            frameIndex = isDude ? Math.Min(_dude!.Frame, frm.FrameCount - 1)
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

        int extraX = isDude ? _dude!.OffsetX : animation?.OffsetX ?? 0;
        int extraY = isDude ? _dude!.OffsetY : animation?.OffsetY ?? 0;

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
            Color tint = obj == _hoveredObject ? Color.Yellow : LightTint(obj.HexTile);
            _spriteBatch.Draw(texture, new Vector2(sprite.Left, sprite.Top), tint);
        }
    }

    /// <summary>
    /// Picks the topmost object whose sprite has an opaque pixel under the
    /// cursor — objects are tested in reverse draw order, and palette index 0
    /// pixels are transparent, so clicks fall through to whatever is beneath
    /// (the behavior DarkFO called out as essential for dense scenes).
    /// </summary>
    private MapObject? PickObject(int screenX, int screenY)
    {
        IEnumerable<MapObject> reverseDrawOrder = Enumerable.Reverse(_solidObjects[_elevation])
            .Concat(Enumerable.Reverse(_flatObjects[_elevation]));

        foreach (MapObject obj in reverseDrawOrder)
        {
            if (ResolveSprite(obj) is not { } sprite)
                continue;

            int localX = screenX - sprite.Left;
            int localY = screenY - sprite.Top;
            if (localX < 0 || localX >= sprite.Frame.Width || localY < 0 || localY >= sprite.Frame.Height)
                continue;

            if (sprite.Frame.Pixels[localY * sprite.Frame.Width + localX] != Palette.TransparentIndex)
                return obj;
        }

        return null;
    }

    private string ObjectName(MapObject obj) =>
        _protoMessages.GetName(obj.Pid) ?? $"object 0x{obj.Pid:X8}";

    private string ObjectDescription(MapObject obj) =>
        _protoMessages.GetDescription(obj.Pid)
        ?? "You see nothing out of the ordinary."; // the game's default examine line

    private void Log(string message)
    {
        _messageLog.Add(message);
        if (_messageLog.Count > 5)
            _messageLog.RemoveAt(0);
    }

    private void Examine(MapObject obj) => Log($"{ObjectName(obj)}: {ObjectDescription(obj)}");

    private string DescribeObject(MapObject obj)
    {
        string proto;
        try
        {
            ProtoInfo info = _protos.Get(obj.Pid);
            proto = $"msg {info.MessageId}";
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            proto = "no proto";
        }

        return $"{Fid.Type(obj.Fid)} pid 0x{obj.Pid:X8} fid 0x{obj.Fid:X8} hex {obj.HexTile} ({proto})";
    }

    private void DrawRoofs()
    {
        MapElevation? elevation = _map.Elevations[_elevation];
        if (elevation is null)
            return;

        Rectangle viewport = GraphicsDevice.Viewport.Bounds;

        // ported from fallout2-ce src/tile.cc tileRenderRoofsInRect(): skip
        // flag bit 12 and the blank tile id 1; roofs draw 96 px up.
        for (int square = 0; square < MapElevation.SquareGridSize; square++)
        {
            int roofValue = (elevation.Squares[square] >> 16) & 0xFFFF;
            if ((((roofValue & 0xF000) >> 12) & 0x01) != 0)
                continue;

            int tileId = roofValue & 0xFFF;
            if (tileId == 1)
                continue;

            (int x, int y) = _camera.SquareToRoofScreen(square);
            if (x < viewport.Left - 80 || x > viewport.Right || y < viewport.Top - 36 || y > viewport.Bottom)
                continue;

            // Roofs are lit by ambient only (tileRenderRoofsInRect passes
            // lightGetAmbientIntensity, not tile light).
            byte ambientLevel = (byte)Math.Clamp(
                _lightGrid.Ambient * 255 / Formats.Light.LightGrid.IntensityMax, 0, 255);
            Texture2D texture = _frmCache.GetTexture(Fid.Build(ObjectType.Tile, tileId));
            _spriteBatch.Draw(texture, new Vector2(x, y), new Color(ambientLevel, ambientLevel, ambientLevel));
        }
    }

    /// <summary>Hover name near the cursor + the message log, bottom-left, in Fallout green.</summary>
    private void DrawTextOverlay()
    {
        if (_fontRenderer is null)
            return;

        var green = new Color(0, 252, 0);

        if (_hoveredObject is not null && _hoveredObject != _dude?.Dude)
        {
            MouseState mouse = Mouse.GetState();
            _fontRenderer.Draw(_spriteBatch, ObjectName(_hoveredObject),
                new Vector2(mouse.X + 14, mouse.Y + 6), green);
        }

        int y = GraphicsDevice.Viewport.Height - 8 - _messageLog.Count * _fontRenderer.LineHeight;
        foreach (string message in _messageLog)
        {
            _fontRenderer.Draw(_spriteBatch, message, new Vector2(8, y), green);
            y += _fontRenderer.LineHeight;
        }
    }

    private void SaveScreenshot(string path)
    {
        int width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        int height = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var pixels = new Color[width * height];
        GraphicsDevice.GetBackBufferData(pixels);

        // The backbuffer's alpha channel is meaningless for an opaque window;
        // force it so the PNG matches what's on screen.
        for (int i = 0; i < pixels.Length; i++)
            pixels[i].A = 255;

        using var texture = new Texture2D(GraphicsDevice, width, height);
        texture.SetData(pixels);
        using FileStream stream = File.Create(path);
        texture.SaveAsPng(stream, width, height);
        Console.WriteLine($"screenshot saved to {path}");
    }

    protected override void UnloadContent()
    {
        _audio?.Dispose();
        _worldmapScreen?.Dispose();
        _fontRenderer?.Dispose();
        _frmCache.Dispose();
        _vfs.Dispose();
        base.UnloadContent();
    }
}
