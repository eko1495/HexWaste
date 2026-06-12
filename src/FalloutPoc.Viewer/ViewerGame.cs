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

    // Ambient NPC life: fidget replays + short wander walks around home tiles.
    private readonly Dictionary<MapObject, DudeController> _npcWalkers = [];
    private readonly Dictionary<MapObject, int> _homeTiles = [];
    private readonly Random _ambientRandom = new(20260612);
    private double _fidgetTimerMs;
    private double _wanderTimerMs;

    /// <summary>Disables ambient NPC life (deterministic screenshots).</summary>
    public bool DisableAmbientLife { get; set; }
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

    /// <summary>True when --ambient or [ ] pinned the ambient level (clock stops driving it).</summary>
    public bool AmbientFixed { get; set; }
    private ProtoMessages _protoMessages = null!;
    private Formats.Int.ScriptHost? _scriptHost;
    private Formats.Int.ScriptHost.DialogSession? _dialog;
    private MapObject? _lootContainer;
    private bool _inventoryOpen;
    private readonly List<MapObject> _dudeInventory = [];
    private readonly GameClock _clock = new();
    private bool _dudeUnderRoof;
    private int _lastAmbientHour = -1;

    /// <summary>Path for F5/F9 saves and the --save-to/--load-from flags.</summary>
    public string SavePath { get; set; } = "fpoc-save.json";

    /// <summary>Save right before exiting a screenshot run (testing).</summary>
    public bool SaveOnExit { get; set; }

    /// <summary>Load this save after startup (testing / resume).</summary>
    public bool LoadOnStart { get; set; }
    private Texture2D? _panelPixel;
    private readonly List<string> _messageLog = [];

    /// <summary>Open dialog with the object at this screen point on start (testing).</summary>
    public Point? TalkAt { get; set; }

    /// <summary>Open dialog with the critter at this hex tile on start (testing).</summary>
    public int? TalkAtHex { get; set; }

    /// <summary>Use/lockpick objects at hex tiles on start, in order (testing).</summary>
    public List<(int Hex, bool Lockpick)> UseAtHexes { get; set; } = [];

    /// <summary>Auto-pick these option indices after --talk, printing a transcript (testing).</summary>
    public int[] AutoChoose { get; set; } = [];
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
        try
        {
            _scriptHost = new Formats.Int.ScriptHost(_vfs, Formats.Int.ScriptList.Load(_vfs), _protos)
            {
                NameResolver = obj => ObjectName(obj),
                IsOpenResolver = obj => _openDoors.Contains(obj),
                OpenStateChanged = (obj, open) => SetDoorState(obj, open),
                ObjectPlaced = (obj, map) => OnScriptObjectPlaced(obj),
                ObjectRemoved = obj => OnScriptObjectRemoved(obj),
                ClockTicks = () => _clock.Ticks,
            };
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            Console.Error.WriteLine($"scripts.lst unavailable — script examine disabled: {ex.Message}");
        }

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

        if (LoadOnStart)
            LoadGame();

        foreach ((int hex, bool lockpick) in UseAtHexes)
        {
            MapObject? target = _solidObjects[_elevation].FirstOrDefault(o => o.HexTile == hex)
                ?? _flatObjects[_elevation].FirstOrDefault(o => o.HexTile == hex);
            if (target is null)
            {
                Console.Error.WriteLine($"nothing at hex {hex}");
                continue;
            }

            _camera.SetCenter(hex);
            if (_dude is not null) // teleport adjacent so range checks pass (test plumbing)
                _dude.Dude.HexTile = Formats.Hex.HexGrid.TileInDirection(hex, 3);
            if (lockpick)
                TryLockpick(target);
            else
                InteractWith(target);
            Console.WriteLine($"{(lockpick ? "lockpick" : "use")}@{hex}: locked={target.IsLockedState} open={_openDoors.Contains(target)}");
            if (_lootContainer is { } looted)
            {
                Console.WriteLine($"LOOT {ObjectName(looted)}:");
                foreach (MapObject item in looted.Inventory)
                    Console.WriteLine($"  ITEM: {ObjectName(item)} x{item.StackCount}");
            }
        }

        if (TalkAtHex is { } talkHex)
        {
            MapObject? hexNpc = _solidObjects[_elevation]
                .FirstOrDefault(o => o.HexTile == talkHex && Fid.Type(o.Fid) is ObjectType.Critter);
            if (hexNpc is not null)
            {
                _camera.SetCenter(talkHex);
                TalkTo(hexNpc);
                foreach (int choice in AutoChoose)
                {
                    if (_dialog is null)
                        break;
                    Console.WriteLine($"CHOOSE: {choice}");
                    ChooseDialogOption(choice - 1);
                }
            }
            else
            {
                Console.Error.WriteLine($"no critter at hex {talkHex}");
            }
        }

        if (TalkAt is { } talkPoint)
        {
            MapObject? npc = PickObject(talkPoint.X, talkPoint.Y);
            if (npc is not null)
            {
                TalkTo(npc);
                foreach (int choice in AutoChoose)
                {
                    if (_dialog is null)
                        break;
                    Console.WriteLine($"CHOOSE: {choice}");
                    ChooseDialogOption(choice - 1); // 1-based on the CLI
                }
            }
            else
            {
                Console.WriteLine($"talk@{talkPoint.X},{talkPoint.Y}: nothing");
            }
        }

        if (ExamineAt is { } examinePoint)
        {
            MapObject? target = PickObject(examinePoint.X, examinePoint.Y);
            string text = target is null ? "nothing"
                : _scriptHost?.GetScriptedDescription(target, _map, _dude?.Dude) is { } scripted
                    ? $"{ObjectName(target)} — [script] {string.Join(" / ", scripted)}"
                    : $"{ObjectName(target)} — {ObjectDescription(target)}";
            Console.WriteLine($"examine@{examinePoint.X},{examinePoint.Y}: {text}");
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
            UpdateAmbientLife(10);
            UpdateClock(10);
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
        _npcWalkers.Clear();
        _homeTiles.Clear();
        _fidgetTimerMs = 0;
        _wanderTimerMs = 0;

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

        // Run map-entry scripts: locks get set, containers stocked (M3),
        // before lighting is computed.
        if (_scriptHost is not null)
        {
            IEnumerable<MapObject> scripted = _flatObjects.Concat(_solidObjects)
                .SelectMany(list => list)
                .Where(o => o.Sid != -1 && o != _dude?.Dude);
            _scriptHost.RunMapEnter(_map, scripted, _dude?.Dude);
        }

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

        // Loot/inventory mode: number keys take/drop, A take-all, Esc/I close.
        if (_lootContainer is not null || _inventoryOpen)
        {
            for (int i = 0; i < 9; i++)
            {
                if (IsKeyPressed(keyboard, Keys.D1 + i) || IsKeyPressed(keyboard, Keys.NumPad1 + i))
                {
                    if (_lootContainer is not null)
                        TakeFromContainer(i);
                    else
                        DropFromInventory(i);
                    break;
                }
            }

            if (_lootContainer is not null && IsKeyPressed(keyboard, Keys.A))
                while (_lootContainer.Inventory.Count > 0)
                    TakeFromContainer(0);

            if (IsKeyPressed(keyboard, Keys.Escape) || IsKeyPressed(keyboard, Keys.I))
            {
                _lootContainer = null;
                _inventoryOpen = false;
            }

            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        if (IsKeyPressed(keyboard, Keys.I))
        {
            _inventoryOpen = true;
            PrewarmItemTextures(_dudeInventory);
        }

        if (IsKeyPressed(keyboard, Keys.F5))
            SaveGame();
        if (IsKeyPressed(keyboard, Keys.F9))
            LoadGame();

        // Dialog mode swallows all input.
        if (_dialog is not null)
        {
            for (int i = 0; i < 9; i++)
            {
                if (IsKeyPressed(keyboard, Keys.D1 + i) || IsKeyPressed(keyboard, Keys.NumPad1 + i))
                {
                    ChooseDialogOption(i);
                    break;
                }
            }

            if (_dialog is not null && mouse.LeftButton == ButtonState.Pressed
                && _previousMouse.LeftButton == ButtonState.Released)
            {
                int hit = HitTestDialogOption(mouse.X, mouse.Y);
                if (hit >= 0)
                    ChooseDialogOption(hit);
            }

            if (_dialog is not null && IsKeyPressed(keyboard, Keys.Escape))
            {
                Log("[conversation ends]");
                _dialog = null;
            }

            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

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

        int panBeforeX = _camera.PanX;
        int panBeforeY = _camera.PanY;
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

        // Scroll clamp (the engine's border check in tileSetCenter): revert
        // pans that push the view center off the hex grid.
        if ((_camera.PanX != panBeforeX || _camera.PanY != panBeforeY)
            && _camera.ScreenToHex(GraphicsDevice.Viewport.Width / 2, GraphicsDevice.Viewport.Height / 2) < 0)
        {
            _camera.PanX = panBeforeX;
            _camera.PanY = panBeforeY;
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

        // L: lockpick the hovered door.
        if (IsKeyPressed(keyboard, Keys.L) && _hoveredObject is { } lockTarget && IsDoor(lockTarget))
            TryLockpick(lockTarget);

        // [ and ] adjust ambient light (day/night preview).
        if (IsKeyPressed(keyboard, Keys.OemOpenBrackets) || IsKeyPressed(keyboard, Keys.OemCloseBrackets))
        {
            double step = IsKeyPressed(keyboard, Keys.OemOpenBrackets) ? -0.1 : 0.1;
            InitialAmbient = Math.Clamp(InitialAmbient + step, 0.25, 1.0);
            _lightGrid.Ambient = (int)(InitialAmbient * Formats.Light.LightGrid.IntensityMax);
            AmbientFixed = true;
            Log($"ambient light {InitialAmbient:P0}");
        }

        _animator.Update(gameTime.ElapsedGameTime.TotalMilliseconds);
        _dude?.Update(gameTime.ElapsedGameTime.TotalMilliseconds);
        UpdateAmbientLife(gameTime.ElapsedGameTime.TotalMilliseconds);

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

        UpdateClock(gameTime.ElapsedGameTime.TotalMilliseconds);

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
                    Log("You cannot get there from here. (Try clicking closer.)");
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

    /// <summary>
    /// Ambient NPC life, no VM. Fidget ported from fallout2-ce
    /// src/animation.cc _dude_fidget(): every 1..10 s (faster with more
    /// candidates) one visible, standing, non-walking critter replays its
    /// stand animation. Wandering is an honest fake (the original drives it
    /// from scripts): a random critter takes a short A* walk within 3 hexes
    /// of its home tile every few seconds.
    /// </summary>
    private void UpdateAmbientLife(double elapsedMs)
    {
        if (DisableAmbientLife || _worldmapOpen)
            return;

        // Advance active NPC walks; drop finished walkers.
        List<MapObject>? finished = null;
        foreach ((MapObject npc, DudeController walker) in _npcWalkers)
        {
            walker.Update(elapsedMs);
            if (!walker.Moving)
                (finished ??= []).Add(npc);
        }
        if (finished is not null)
            foreach (MapObject npc in finished)
                _npcWalkers.Remove(npc);

        Rectangle viewport = GraphicsDevice.Viewport.Bounds;
        bool IsVisible(MapObject obj)
        {
            (int x, int y) = _camera.HexToScreen(obj.HexTile);
            return viewport.Contains(x, y);
        }

        List<MapObject> candidates = [.. _solidObjects[_elevation].Where(o =>
            Fid.Type(o.Fid) is ObjectType.Critter
            && o != _dude?.Dude
            && !_npcWalkers.ContainsKey(o)
            && Fid.AnimType(o.Fid) == 0 // standing
            && IsVisible(o))];

        _fidgetTimerMs -= elapsedMs;
        if (_fidgetTimerMs <= 0)
        {
            // ported from _dude_fidget(): delay 20/candidates seconds (1..7),
            // plus 0..3 s of jitter.
            int delaySeconds = Math.Clamp(candidates.Count == 0 ? 7 : 20 / candidates.Count, 1, 7);
            _fidgetTimerMs = _ambientRandom.Next(0, 3000) + 1000.0 * delaySeconds;

            if (candidates.Count > 0)
            {
                MapObject critter = candidates[_ambientRandom.Next(candidates.Count)];
                try
                {
                    if (_frmCache.GetFrm(critter.Fid).FrameCount > 1)
                        _animator.PlayFidget(critter);
                }
                catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
                {
                }
            }
        }

        _wanderTimerMs -= elapsedMs;
        if (_wanderTimerMs <= 0)
        {
            _wanderTimerMs = _ambientRandom.Next(3000, 9000);
            if (candidates.Count > 0)
                TryStartWander(candidates[_ambientRandom.Next(candidates.Count)]);
        }
    }

    private void TryStartWander(MapObject npc)
    {
        // NPCs without walk art stay put.
        int walkFid = Fid.Build(ObjectType.Critter, Fid.Index(npc.Fid), 1, Fid.WeaponCode(npc.Fid));
        if (!_vfs.Exists(_artIndex.GetFrmPath(walkFid)))
        {
            walkFid = Fid.Build(ObjectType.Critter, Fid.Index(npc.Fid), 1);
            if (!_vfs.Exists(_artIndex.GetFrmPath(walkFid)))
                return;
        }

        int home = _homeTiles.TryGetValue(npc, out int stored) ? stored : (_homeTiles[npc] = npc.HexTile);

        // A few tries at a random free tile within 3 hexes of home.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            int target = home;
            int steps = _ambientRandom.Next(1, 4);
            for (int i = 0; i < steps; i++)
                target = Formats.Hex.HexGrid.TileInDirection(target, _ambientRandom.Next(6));

            if (target == npc.HexTile || _blockedTiles.Contains(target))
                continue;

            var walker = new DudeController(npc, _frmCache, tile => _blockedTiles.Contains(tile));
            int previousTile = npc.HexTile;
            walker.TileChanged += tile =>
            {
                _blockedTiles.Remove(previousTile);
                _blockedTiles.Add(tile);
                previousTile = tile;
                List<MapObject> solids = _solidObjects[_elevation];
                solids.Remove(npc);
                InsertSorted(solids, npc);
            };

            if (walker.WalkTo(target))
            {
                Console.WriteLine($"wander: {ObjectName(npc)} hex {npc.HexTile} -> {target}");
                _npcWalkers[npc] = walker;
                return;
            }
        }
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

    private bool IsContainer(MapObject obj)
    {
        if (Fid.PidType(obj.Pid) != 0) // items only
            return false;
        try
        {
            return _protos.Get(obj.Pid).SubType == 1; // ITEM_TYPE_CONTAINER
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            return false;
        }
    }

    private void PickUpItem(MapObject item)
    {
        var scripted = _scriptHost?.RunObjectProc(item, _map, _dude?.Dude, "pickup_p_proc");
        if (scripted is not null)
            foreach (string line in scripted.Messages)
                Log(line);
        if (scripted is { Overridden: true })
            return;

        OnScriptObjectRemoved(item);
        foreach (MapElevation? elev in _map.Elevations)
            elev?.Objects.Remove(item);
        AddToDudeInventory(item);
        Log($"You pick up: {ObjectName(item)}.");
    }

    private void AddToDudeInventory(MapObject item)
    {
        if (_dudeInventory.FirstOrDefault(i => i.Pid == item.Pid) is { } existing)
            existing.StackCount += Math.Max(item.StackCount, 1);
        else
            _dudeInventory.Add(item);
    }

    private void TakeFromContainer(int index)
    {
        if (_lootContainer is null || index < 0 || index >= _lootContainer.Inventory.Count)
            return;
        MapObject item = _lootContainer.Inventory[index];
        _lootContainer.Inventory.RemoveAt(index);
        AddToDudeInventory(item);
        Log($"You take: {ObjectName(item)}{(item.StackCount > 1 ? $" x{item.StackCount}" : "")}.");
    }

    private void DropFromInventory(int index)
    {
        if (_dude is null || index < 0 || index >= _dudeInventory.Count)
            return;
        MapObject item = _dudeInventory[index];
        _dudeInventory.RemoveAt(index);
        item.HexTile = _dude.Dude.HexTile;
        _map.Elevations[_elevation]?.Objects.Add(item);
        OnScriptObjectPlaced(item);
        Log($"You drop: {ObjectName(item)}.");
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
    /// <summary>Opens scripted conversation; floater-only NPCs just speak into the log.</summary>
    private void TalkTo(MapObject npc)
    {
        if (_scriptHost is null)
            return;

        Formats.Int.ScriptHost.DialogSession? session =
            _scriptHost.StartDialog(npc, _map, _dude?.Dude, out IReadOnlyList<string> floaters);

        foreach (string line in floaters)
            Log($"{ObjectName(npc)}: {line}");

        if (session is not null)
        {
            _dialog = session;
            PrintDialogRound();
        }
        else if (floaters.Count == 0)
        {
            Log($"{ObjectName(npc)} has nothing to say.");
        }
    }

    private void PrintDialogRound()
    {
        if (_dialog is null)
            return;
        Console.WriteLine($"REPLY: {_dialog.Reply}");
        for (int i = 0; i < _dialog.Options.Count; i++)
            Console.WriteLine($"  OPTION {i + 1}: {_dialog.Options[i]}");
    }

    private void ChooseDialogOption(int index)
    {
        if (_dialog is null)
            return;

        _dialog.Choose(index);
        foreach (string line in _dialog.SideMessages)
            Log(line);

        if (!_dialog.Active)
        {
            Log("[conversation ends]");
            _dialog = null;
        }
        else
        {
            PrintDialogRound();
        }
    }

    private void InteractWith(MapObject obj)
    {
        if (Fid.Type(obj.Fid) is ObjectType.Critter)
        {
            // Conversation range ~5 hexes (one hex step is 16..32 screen px).
            if (_dude is not null
                && Formats.Hex.HexGrid.ScreenDistance(_dude.Dude.HexTile, obj.HexTile) > 5 * 32)
            {
                Log("Too far away to talk.");
                return;
            }
            TalkTo(obj);
            return;
        }

        if (IsContainer(obj) || (Fid.Type(obj.Fid) is ObjectType.Item && obj.Inventory.Count > 0))
        {
            if (!IsAdjacentToDude(obj))
            {
                Log("Too far away.");
                return;
            }

            var containerScripted = _scriptHost?.RunObjectProc(obj, _map, _dude?.Dude, "use_p_proc");
            if (containerScripted is not null)
                foreach (string line in containerScripted.Messages)
                    Log(line);
            if (containerScripted is { Overridden: true })
                return;

            if (obj.IsLockedState)
            {
                Log($"The {ObjectName(obj)} is locked.");
                return;
            }

            _lootContainer = obj;
            PrewarmItemTextures(obj.Inventory);
            return;
        }

        if (Fid.Type(obj.Fid) is ObjectType.Item)
        {
            if (!IsAdjacentToDude(obj))
            {
                Log("Too far away.");
                return;
            }
            PickUpItem(obj);
            return;
        }

        if (IsDoor(obj))
        {
            if (!IsAdjacentToDude(obj))
            {
                Console.WriteLine("too far from the door");
                return;
            }

            // Engine order (_obj_use_door): the script's use_p_proc runs first
            // and may override the default open/close entirely.
            var scripted = _scriptHost?.RunObjectProc(obj, _map, _dude?.Dude, "use_p_proc");
            if (scripted is not null)
                foreach (string line in scripted.Messages)
                    Log(line);
            if (scripted is { Overridden: true })
                return;

            if (obj.IsLockedState && !_openDoors.Contains(obj))
            {
                Log($"The {ObjectName(obj)} is locked.");
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

    /// <summary>Script-created or unhidden object enters the draw lists + blocking.</summary>
    private void OnScriptObjectPlaced(MapObject obj)
    {
        if (Fid.Type(obj.Fid) is ObjectType.Head || obj.HexTile < 0)
            return;

        List<MapObject> list = obj.IsFlat ? _flatObjects[_elevation] : _solidObjects[_elevation];
        list.Remove(obj);
        if (!obj.IsHidden)
            InsertSorted(list, obj);
        if (_dude is not null)
            RebuildBlockedTiles(_dude.Dude);
    }

    private void OnScriptObjectRemoved(MapObject obj)
    {
        foreach (List<MapObject> list in _flatObjects.Concat(_solidObjects))
            list.Remove(obj);
        if (_dude is not null)
            RebuildBlockedTiles(_dude.Dude);
    }

    /// <summary>Script-driven obj_open/obj_close: idempotent door state change.</summary>
    private void SetDoorState(MapObject door, bool open)
    {
        if (_openDoors.Contains(door) != open)
            ToggleDoor(door);
    }

    /// <summary>
    /// Lockpick the hovered door: the script's use_skill_on_p_proc runs with
    /// action_being_used = SKILL_LOCKPICK (9); without an override the default
    /// attempt unlocks (PoC rolls always succeed).
    /// </summary>
    private void TryLockpick(MapObject door)
    {
        if (!IsAdjacentToDude(door))
        {
            Log("Too far away.");
            return;
        }

        var scripted = _scriptHost?.RunObjectProc(door, _map, _dude?.Dude,
            fixedParam: 0, actionBeingUsed: 9, "use_skill_on_p_proc");
        if (scripted is not null)
            foreach (string line in scripted.Messages)
                Log(line);
        if (scripted is { Overridden: true })
            return;

        if (!door.IsLockedState)
        {
            Log("It isn't locked.");
            return;
        }

        door.IsLockedState = false;
        Log($"You pick the lock on the {ObjectName(door)}.");
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
        _clock.AdvanceHours(8); // travel takes time
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

    private RenderTarget2D? _screenshotTarget;

    protected override void Draw(GameTime gameTime)
    {
        // Screenshots render via an offscreen target: reading the backbuffer
        // races the GPU on this driver and loses late sprites in the upper
        // screen region (observed: panels above y~250 vanish from readback
        // while displaying fine).
        if (_screenshotPath is not null)
        {
            _screenshotTarget ??= new RenderTarget2D(GraphicsDevice,
                GraphicsDevice.PresentationParameters.BackBufferWidth,
                GraphicsDevice.PresentationParameters.BackBufferHeight);
            GraphicsDevice.SetRenderTarget(_screenshotTarget);
        }

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
            _dudeUnderRoof = DudeIsUnderRoof();
            DrawFloors();
            DrawObjects(_flatObjects[_elevation]);
            DrawObjects(_solidObjects[_elevation]);
            if (_roofsVisible)
                DrawRoofs();
            DrawTextOverlay();
            DrawDialogPanel();
            DrawItemPanels();
        }
        _spriteBatch.End();

        if (_screenshotPath is not null && _screenshotTarget is not null)
        {
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Color.Black);
            _spriteBatch.Begin();
            _spriteBatch.Draw(_screenshotTarget, Vector2.Zero, Color.White);
            _spriteBatch.End();
        }

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
            if (SaveOnExit)
                SaveGame();
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

        DudeController? walker = _dude is not null && obj == _dude.Dude ? _dude
            : _npcWalkers.TryGetValue(obj, out DudeController? npcWalker) ? npcWalker
            : null;
        bool isDude = walker is not null;
        AnimationState? animation = null;
        if (!isDude && _animator.TryGetState(obj, out AnimationState state))
            animation = state;

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

            // Egg-style transparency (approximation of the engine's masked
            // blend): solids drawn in front of the dude that cover him fade,
            // keeping him visible behind walls.
            if (FadesOverDude(obj, sprite))
                tint *= 0.45f;

            _spriteBatch.Draw(texture, new Vector2(sprite.Left, sprite.Top), tint);

            if (obj == _hoveredObject)
            {
                Texture2D outline = _frmCache.GetOutlineTexture(sprite.Fid, sprite.FrameIndex, sprite.Rotation);
                _spriteBatch.Draw(outline, new Vector2(sprite.Left, sprite.Top), new Color(0, 252, 0));
            }
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

    private void Examine(MapObject obj)
    {
        // Script-provided description first (micro INT VM), proto text as the
        // default — mirroring how look_at/description procs override defaults.
        if (_scriptHost?.GetScriptedDescription(obj, _map, _dude?.Dude) is { } scripted)
        {
            Log($"{ObjectName(obj)}:");
            foreach (string line in scripted)
                Log(line);
            return;
        }

        Log($"{ObjectName(obj)}: {ObjectDescription(obj)}");
    }

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
            // lightGetAmbientIntensity, not tile light); they fade instead of
            // vanishing when the dude is indoors.
            byte ambientLevel = (byte)Math.Clamp(
                _lightGrid.Ambient * 255 / Formats.Light.LightGrid.IntensityMax, 0, 255);
            var roofTint = new Color(ambientLevel, ambientLevel, ambientLevel);
            if (_dudeUnderRoof)
                roofTint *= 0.35f;
            Texture2D texture = _frmCache.GetTexture(Fid.Build(ObjectType.Tile, tileId));
            _spriteBatch.Draw(texture, new Vector2(x, y), roofTint);
        }
    }

    private readonly List<Rectangle> _dialogOptionRects = [];

    private int HitTestDialogOption(int x, int y)
    {
        for (int i = 0; i < _dialogOptionRects.Count; i++)
            if (_dialogOptionRects[i].Contains(x, y))
                return i;
        return -1;
    }

    /// <summary>Text dialog panel: reply on top, numbered options below (keys 1-9 or click).</summary>
    private void DrawDialogPanel()
    {
        if (_dialog is null || _fontRenderer is null)
            return;

        _panelPixel ??= CreatePixel();

        Rectangle viewport = GraphicsDevice.Viewport.Bounds;
        int panelWidth = Math.Min(720, viewport.Width - 40);
        int textWidth = panelWidth - 32;
        int lineHeight = _fontRenderer.LineHeight;

        List<string> replyLines = _fontRenderer.WrapText(_dialog.Reply, textWidth);
        var optionLines = new List<(int Option, string Line, bool First)>();
        for (int i = 0; i < _dialog.Options.Count; i++)
        {
            List<string> wrapped = _fontRenderer.WrapText($"{i + 1}. {_dialog.Options[i]}", textWidth - 12);
            for (int l = 0; l < wrapped.Count; l++)
                optionLines.Add((i, wrapped[l], l == 0));
        }

        int panelHeight = (replyLines.Count + optionLines.Count + 3) * lineHeight + 24;
        int panelX = (viewport.Width - panelWidth) / 2;
        int panelY = viewport.Height - panelHeight - 16;

        _spriteBatch.Draw(_panelPixel, new Rectangle(panelX, panelY, panelWidth, panelHeight),
            new Color(8, 8, 8, 230));

        var lightGreen = new Color(140, 252, 140);
        var green = new Color(0, 252, 0);
        int y = panelY + 12;

        _fontRenderer.Draw(_spriteBatch, _dialog.NpcName, new Vector2(panelX + 16, y), Color.LightGray);
        y += lineHeight + lineHeight / 2;

        foreach (string line in replyLines)
        {
            _fontRenderer.Draw(_spriteBatch, line, new Vector2(panelX + 16, y), lightGreen);
            y += lineHeight;
        }

        y += lineHeight / 2;
        MouseState mouse = Mouse.GetState();
        _dialogOptionRects.Clear();
        Rectangle currentRect = Rectangle.Empty;
        int currentOption = -1;
        foreach ((int option, string line, bool first) in optionLines)
        {
            var lineRect = new Rectangle(panelX + 16, y, textWidth, lineHeight);
            if (first && currentOption >= 0)
                _dialogOptionRects.Add(currentRect);
            currentRect = first ? lineRect : Rectangle.Union(currentRect, lineRect);
            currentOption = option;

            bool hovered = lineRect.Contains(mouse.X, mouse.Y);
            _fontRenderer.Draw(_spriteBatch, line,
                new Vector2(panelX + 16 + (first ? 0 : 12), y), hovered ? Color.Yellow : green);
            y += lineHeight;
        }
        if (currentOption >= 0)
            _dialogOptionRects.Add(currentRect);
    }

    /// <summary>Loot or inventory panel: item icons + names + counts, numbered rows.</summary>
    private void DrawItemPanels()
    {
        if (_fontRenderer is null)
            return;

        if (_lootContainer is { } container)
        {
            DrawItemList($"{ObjectName(container)} - 1-9 take, A take all, Esc close",
                container.Inventory, 40);
        }
        else if (_inventoryOpen)
        {
            DrawItemList("Inventory - 1-9 drop, Esc close", _dudeInventory, 40);
        }
    }

    private void DrawItemList(string title, List<MapObject> items, int x)
    {
        _panelPixel ??= CreatePixel();
        int lineHeight = Math.Max(_fontRenderer!.LineHeight, 26);
        int panelWidth = 360;
        int panelHeight = (Math.Max(items.Count, 1) + 2) * lineHeight + 16;
        int y = 60;

        _spriteBatch.Draw(_panelPixel, new Rectangle(x, y, panelWidth, panelHeight), new Color(8, 8, 8, 230));
        _fontRenderer.Draw(_spriteBatch, title, new Vector2(x + 10, y + 8), Color.LightGray);

        int rowY = y + 8 + lineHeight + 6;
        var green = new Color(0, 252, 0);
        if (items.Count == 0)
            _fontRenderer.Draw(_spriteBatch, "(empty)", new Vector2(x + 10, rowY), Color.Gray);

        for (int i = 0; i < items.Count && i < 9; i++)
        {
            MapObject item = items[i];
            DrawItemIcon(item, new Rectangle(x + 28, rowY - 2, 28, 22));
            string count = item.StackCount > 1 ? $" x{item.StackCount}" : "";
            _fontRenderer.Draw(_spriteBatch, $"{i + 1}.", new Vector2(x + 10, rowY), green);
            _fontRenderer.Draw(_spriteBatch, $"{ObjectName(item)}{count}", new Vector2(x + 62, rowY), green);
            rowY += lineHeight;
        }

        if (items.Count > 9)
            _fontRenderer.Draw(_spriteBatch, $"(+{items.Count - 9} more)", new Vector2(x + 10, rowY), Color.Gray);
    }

    /// <summary>
    /// Creating textures (SetData) inside an active SpriteBatch corrupts the
    /// in-flight batch — warm the icon cache before the panel ever draws.
    /// </summary>
    private void PrewarmItemTextures(IEnumerable<MapObject> items)
    {
        foreach (MapObject item in items)
        {
            try
            {
                int inventoryFid = _protos.Get(item.Pid).InventoryFid;
                if (inventoryFid != -1)
                    _frmCache.GetTexture(inventoryFid);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
            {
            }
        }
    }

    private void DrawItemIcon(MapObject item, Rectangle destination)
    {
        try
        {
            int inventoryFid = _protos.Get(item.Pid).InventoryFid;
            if (inventoryFid == -1)
                return;
            Texture2D texture = _frmCache.GetTexture(inventoryFid);
            float scale = Math.Min((float)destination.Width / texture.Width,
                (float)destination.Height / texture.Height);
            var size = new Point((int)(texture.Width * scale), (int)(texture.Height * scale));
            _spriteBatch.Draw(texture,
                new Rectangle(destination.X, destination.Y + (destination.Height - size.Y) / 2, size.X, size.Y),
                Color.White);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
        }
    }

    private Texture2D CreatePixel()
    {
        var pixel = new Texture2D(GraphicsDevice, 1, 1);
        pixel.SetData(new[] { Color.White });
        return pixel;
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

    /// <summary>Idle clock + hour-driven ambient (skipped when --ambient fixed it).</summary>
    private void UpdateClock(double elapsedMs)
    {
        _clock.AdvanceRealTime(elapsedMs);

        int hour = _clock.Hour / 100;
        if (hour == _lastAmbientHour)
            return;
        _lastAmbientHour = hour;

        if (AmbientFixed)
            return;
        _lightGrid.Ambient = (int)Math.Clamp(
            _clock.AmbientFraction * Formats.Light.LightGrid.IntensityMax,
            Formats.Light.LightGrid.IntensityMin, Formats.Light.LightGrid.IntensityMax);
    }

    private void SaveGame()
    {
        var state = new SaveState
        {
            Map = _currentMapName,
            DudeTile = _dude?.Dude.HexTile ?? _map.Header.EnteringTile,
            DudeRotation = _dude?.Dude.Rotation ?? 0,
            Elevation = _elevation,
            ClockTicks = _clock.Ticks,
            GlobalVars = new Dictionary<int, int>(_scriptHost?.GlobalVars ?? []),
            DudeInventory = [.. _dudeInventory.Select(i => new SaveState.SavedItem(i.Pid, i.StackCount))],
            Doors = [.. _solidObjects[_elevation]
                .Where(o => IsDoor(o) && (_openDoors.Contains(o) || o.IsLockedState))
                .Select(o => new SaveState.SavedDoor(o.HexTile, o.Pid, _openDoors.Contains(o), o.IsLockedState))],
        };
        state.Save(SavePath);
        Log($"Game saved ({Path.GetFileName(SavePath)}).");
        Console.WriteLine($"saved: map={state.Map} tile={state.DudeTile} clock={state.ClockTicks} items={state.DudeInventory.Count} doors={state.Doors.Count}");
    }

    private void LoadGame()
    {
        SaveState? state = SaveState.Load(SavePath);
        if (state is null)
        {
            Log("No saved game found.");
            return;
        }

        _clock.Ticks = state.ClockTicks;
        _lastAmbientHour = -1;
        if (_scriptHost is not null)
        {
            _scriptHost.GlobalVars.Clear();
            foreach ((int key, int value) in state.GlobalVars)
                _scriptHost.GlobalVars[key] = value;
        }

        LoadMap(state.Map, new MapDestination(0, state.DudeTile, state.Elevation, state.DudeRotation));

        // Re-apply door deltas over the freshly scripted map.
        foreach (SaveState.SavedDoor saved in state.Doors)
        {
            MapObject? door = _solidObjects[_elevation]
                .FirstOrDefault(o => o.HexTile == saved.HexTile && o.Pid == saved.Pid);
            if (door is null)
                continue;
            door.IsLockedState = saved.Locked;
            SetDoorState(door, saved.Open);
        }

        // Rebuild the dude's bag from prototypes.
        _dudeInventory.Clear();
        foreach (SaveState.SavedItem item in state.DudeInventory)
        {
            try
            {
                var obj = new MapObject
                {
                    Id = -4,
                    HexTile = -1,
                    X = 0,
                    Y = 0,
                    Frame = 0,
                    Rotation = 0,
                    Fid = _protos.Get(item.Pid).Fid,
                    Flags = 0,
                    Pid = item.Pid,
                    Sid = -1,
                };
                obj.StackCount = item.Count;
                _dudeInventory.Add(obj);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
            {
                Console.Error.WriteLine($"load: dropping unknown item pid 0x{item.Pid:X8}: {ex.Message}");
            }
        }

        Log("Game loaded.");
        Console.WriteLine($"loaded: map={state.Map} tile={state.DudeTile} clock={state.ClockTicks} items={_dudeInventory.Count}");
    }

    private void SaveScreenshot(string path)
    {
        int width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        int height = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var pixels = new Color[width * height];
        if (_screenshotTarget is not null)
            _screenshotTarget.GetData(pixels); // offscreen target: race-free
        else
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
