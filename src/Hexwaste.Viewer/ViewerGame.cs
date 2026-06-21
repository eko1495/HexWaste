using Hexwaste.Formats;
using Hexwaste.Formats.Art;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Pal;
using Hexwaste.Formats.Proto;
using Hexwaste.Formats.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Hexwaste.Viewer;

public sealed partial class ViewerGame : Game, Formats.Combat.ICombatHost
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
    private string _baseTitle = "Hexwaste viewer";

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

    /// <summary>Stub-hit histogram for the current map (dumped on map exit).</summary>
    private readonly Dictionary<string, int> _stubbedExternals = [];

    /// <summary>The dude's character sheet (premade\player.gcd); null falls
    /// back to the art proto's stats like phase 5.</summary>
    private Formats.Combat.GcdFile? _dudeGcd;
    private int _dudeLevel = 1;
    private int _dudeXp = 0; // accrues in P6-M3 (kill XP at combat end)
    private int _unspentSkillPoints;
    // P31 B-M0: the PC-stat karma + generic reputation (gPcStatValues[4]/[3]). Read-only in the engine
    // (no auto-award), so script-/harness-set; default 0 → get_pc_stat(3/4) returns 0 like the old stub.
    private int _dudeKarma;
    private int _dudeReputation;

    /// <summary>The dude's per-perk ranks (P28-M2; one slot per perk id). All-zero = no perks,
    /// so the perk stat modifiers are inert by default. Persisted (SaveState.DudePerkRanks).</summary>
    private int[] _dudePerkRanks = new int[Formats.Perks.PerkTable.Count];

    /// <summary>Game-days before a script-stocked merchant container restocks
    /// (P8-M5 — the engine's box scripts use a 1-2 day timer; ours approximates).</summary>
    private const int RestockDays = 3;

    /// <summary>The active premade name (for save/restore of the base sheet);
    /// "player" = the blank default.</summary>
    private string _activeCharacter = "player";

    /// <summary>Skill allocator panel (K): open + currently-highlighted skill.</summary>
    private bool _skillAllocOpen;
    private int _skillAllocIndex;

    /// <summary>Deterministic combat rolls for headless transcripts (--rng-seed).</summary>
    public int? RngSeed { get; set; }

    /// <summary>Game difficulty — skews the encounter occurrence frequency and the
    /// weighted pick (phase-10 #12). Set via --difficulty; default Normal.</summary>
    public Formats.Map.GameDifficulty Difficulty { get; set; } = Formats.Map.GameDifficulty.Normal;

    /// <summary>The dude's called-shot location for attacks (V cycles it; --aim sets
    /// it headlessly). UNCALLED = no aiming. See Formats.Combat.CriticalTables.</summary>
    public int AimLocation { get; set; } = Formats.Combat.CriticalTables.LocationUncalled;

    /// <summary>The HUD weapon-slot attack mode (P15 M1): which fire mode F (and a
    /// slot/N cycle) uses — Single or Burst for a burst-capable gun. The bar's mode
    /// label reflects it live.</summary>
    private enum WeaponMode { Single, Burst }
    private WeaponMode _weaponMode = WeaponMode.Single;

    private static readonly string[] AimNames =
        ["head", "left arm", "right arm", "torso", "right leg", "left leg", "eyes", "groin", "uncalled"];
    private static string AimName(int loc) => AimNames[Math.Clamp(loc, 0, AimNames.Length - 1)];

    /// <summary>Premade character sheet to start with (combat/diplomat/stealth);
    /// null/empty = the blank player.gcd. Test plumbing for builds + gender.</summary>
    public string? CharacterName { get; set; }
    private Formats.Combat.ICombatRng _combatRng = new Formats.Combat.SystemCombatRng();

    /// <summary>The turn machine (phase-9 M0). Owns combat state + orchestration;
    /// this ViewerGame is its ICombatHost. Created in LoadContent once the seeded
    /// RNG is known.</summary>
    private Formats.Combat.CombatEngine _combat = null!;

    /// <summary>critter_p_proc round-robin (the engine's _script_chk_critters
    /// ticker runs ONE critter script per frame; we pump at the 10 Hz game
    /// tick instead of our 60 Hz frame rate).</summary>
    private double _critterProcTimerMs;
    private int _critterProcIndex;

    /// <summary>Main-menu front door (v0.6): Title → character pick → play.
    /// Headless/test flags skip it entirely.</summary>
    public bool StartInMenu { get; set; }

    private enum MenuState { None, Title, CharacterPick, CreateStats, CreateTraits, CreateTags }

    private MenuState _menu = MenuState.None;
    private int _menuIndex;
    private List<(string Label, string VirtualPath)> _premadeGcds = [];

    // Character creation (P8-M4): 7 SPECIAL at base 5 + 5 free points, a
    // gender row, then a 3-skill tag pick.
    private readonly int[] _createSpecial = [5, 5, 5, 5, 5, 5, 5];
    private int _createPoints = 5;
    private int _createCursor; // 0-6 = SPECIAL stat, 7 = gender
    private int _createGender;
    private readonly List<int> _createTags = [];
    // P29-M3: up to two optional traits, picked between SPECIAL and tag skills (the engine's
    // optional-trait step). Empty = a trait-less character (premade traits still apply on Load).
    private readonly List<int> _createTraits = [];
    private int _createTraitIndex;
    private const int TraitCount = 16; // trait_defs.h Trait enum: Fast Metabolism (0) … Gifted (15)

    /// <summary>Movie caption card (play_gmovie): title + .sve subtitle lines.</summary>
    private List<string>? _movieCard;

    /// <summary>scripts.lst index per party member — their follow script gets
    /// re-bound on every map (fresh sid via AllocateSid).</summary>
    private readonly Dictionary<MapObject, int> _partyScriptIndex = [];

    /// <summary>Armed "use item on object": the next click applies the item.</summary>
    private MapObject? _pendingUseItem;

    /// <summary>Skilldex (P12 M0): the use-skill picker. Open shows the 8-skill flyout;
    /// picking one arms <see cref="_pendingUseSkill"/> so the next click applies that
    /// skill to the target (the use_skill_on_p_proc path that lockpick already uses).</summary>
    private bool _skilldexOpen;
    private int? _pendingUseSkill;
    /// <summary>The dude's two-layer sneak state (P29 A-M0; critter.cc): the FLAG (Skilldex/S toggle)
    /// + Working (the periodic SKILL_SNEAK roll, A-M2). IsSneaking = flag &amp;&amp; Working.</summary>
    private readonly Formats.Combat.SneakState _sneak = new();
    /// <summary>Heartbeats until the next periodic sneak re-roll (A-M2; one reschedule "tick" = one
    /// 100 ms heartbeat — a documented approximation of the engine's game-time EVENT_TYPE_SNEAK queue).</summary>
    private int _sneakTicksRemaining;
    /// <summary>Dedicated seeded RNG for the sneak roll (A-M2) — isolated from the combat/worldmap/
    /// party/skill streams so enabling sneak never perturbs an existing golden.</summary>
    private Formats.Combat.ICombatRng? _sneakRng;
    /// <summary>Seeded skill-roll RNG (deterministic under --rng-seed for goldens),
    /// separate from the combat/party/worldmap streams.</summary>
    private Formats.Combat.ICombatRng? _skillRng;
    /// <summary>Per-skill uses counted against <see cref="_skillUsesDay"/> — the engine's
    /// skillGetFreeUsageSlot "wait a while" cap (3/day), reset on a new game-day.</summary>
    private readonly Dictionary<int, int> _skillUsesByDay = [];
    private int _skillUsesDay = -1;

    /// <summary>The Skilldex skill ids in panel order (skilldex.cc gSkilldexSkills):
    /// Sneak, Lockpick, Steal, Traps, First Aid, Doctor, Science, Repair.</summary>
    private static readonly int[] SkilldexSkills = [8, 9, 10, 11, 6, 7, 12, 13];

    /// <summary>Pip-Boy (P12 M1): the status + rest panel (PIP.FRM). _pipboyRestMenu
    /// = the rest-duration sub-page (pipboy.cc PipboyRestDuration). Automaps, archives/
    /// holodisks and the alarm are out of scope (content-gated).</summary>
    private bool _pipboyOpen;
    private bool _pipboyRestMenu;
    private Texture2D? _pipboyBg;

    /// <summary>Skilldex authentic art (P13 follow-up): SKLDXBOX background + SKLDXOFF/
    /// SKLDXON button states (skilldex.cc). Null if the art is missing → text fallback.</summary>
    private Texture2D? _skilldexBox;
    private Texture2D? _skilldexBtnOff;
    private Texture2D? _skilldexBtnOn;

    /// <summary>Perk-picker authentic art (P29-M5): PERKWIN.FRM (573x230) background
    /// (character_editor.cc perkDialogShow). Null if the art is missing → text fallback.</summary>
    private Texture2D? _perkWin;
    private bool _perkWinTried;

    /// <summary>Full-window automap (P15 M0): the AUTOMAP.FRM (519x480) view, opened from
    /// the Pip-Boy (A), plotting the current elevation's objects as colored dots
    /// (automap.cc automapRenderInMapWindow).</summary>
    private bool _automapOpen;
    private Texture2D? _automapBg;

    /// <summary>Objects the dude has come within sight of — the automap's OBJECT_SEEN
    /// fog-of-war (P20-M2). Cleared per map, accumulated as the dude moves (RevealAround);
    /// the automap + mini-map plot only seen objects. SIMPLIFICATION: a flat-radius
    /// proximity reveal (not true line-of-sight), not persisted across save.</summary>
    private readonly HashSet<MapObject> _seenObjects = [];
    private const int AutomapSightRadius = 14; // hexes the dude "sees" around itself

    /// <summary>Objects a map's reg_anim_animate_forever registered this map (P21-M1) —
    /// recorded for the --reg-anim-probe; cleared per map.</summary>
    private readonly List<string> _regAnimForever = [];

    /// <summary>reg_anim_func batch moves/animations executed this map (P33-M1) —
    /// recorded for the --reg-anim-move probe; cleared per map.</summary>
    private readonly List<string> _regAnimMoves = [];

    /// <summary>Options / pause menu (P12 M2): Esc or the OPT button opens it —
    /// Save / Load / Quit to main menu / Quit to desktop / Resume (the actions of
    /// options.cc showOptions; Preferences is out of scope — no preferences system).</summary>
    private bool _optionsOpen;
    private Texture2D? _optionsBg;

    /// <summary>Rest options (pipboy.cc PipboyRestDuration order, subset): positive =
    /// rest that many game-minutes; -1 = until healed; -2/-3 = until next 06:00 / 18:00.</summary>
    private static readonly (string Label, int Minutes)[] RestOptions =
    [
        ("Ten minutes", 10), ("Thirty minutes", 30), ("One hour", 60),
        ("Two hours", 120), ("Three hours", 180), ("Six hours", 360),
        ("Until morning", -2), ("Until evening", -3), ("Until healed", -1),
    ];

    /// <summary>Open trade session (gdialog_barter): merchant + price modifier.</summary>
    private MapObject? _barterNpc;
    private MapObject? _barterStock;
    private int _barterModifier;
    private MapObject? _dialogNpc;

    // Companion control hub (phase-10 M4): talking to a recruited (or dismissed)
    // member opens a wait/follow/dismiss/rejoin hub instead of scripted dialog.
    private enum CompanionCmd { Talk, Trade, Wait, Follow, Dismiss, Rejoin, Tactics, Cancel }
    private MapObject? _companionHub;
    /// <summary>The companion whose inventory the trade panel is pointed at (phase-10
    /// M5). Non-null = the loot panel is in TRADE mode: a flat 1:1 item move (no caps,
    /// no barter price), with Shift+1-9 giving to the follower.</summary>
    private MapObject? _tradePartner;
    /// <summary>Shared overflow-paging window for the item panels (phase-15 M2): row N of
    /// the visible list is item <c>_panelPage*9 + N</c>. Reset to 0 whenever a panel opens;
    /// PgUp/PgDn step it within <see cref="MaxPanelPage"/> so the 10th+ item is reachable.</summary>
    private int _panelPage;
    private readonly List<(string Label, CompanionCmd Cmd)> _hubOptions = [];
    /// <summary>Party members told to "wait here" — PumpCritterProcs skips them, so
    /// their follow critter_p_proc stops and they hold position.</summary>
    private readonly HashSet<MapObject> _waitingCompanions = [];
    /// <summary>Dismissed former companions LIVE on the current map, by script index,
    /// so talking to one offers "rejoin". Extracted into <see cref="_dismissedByMap"/>
    /// on map exit / save, re-injected on entry.</summary>
    private readonly Dictionary<MapObject, int> _dismissedCompanions = [];
    /// <summary>Dismissed companions kept by the map they were left on, so they persist
    /// across travel + save/load and can be rejoined on return (P10 #3). Mirrors
    /// SaveState.DismissedCompanions.</summary>
    private readonly Dictionary<string, List<SaveState.DismissedCompanion>> _dismissedByMap = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The team a critter had before recruiting, restored on dismiss
    /// (e.g. Vic's team 25) — captured once, preserved across dismiss/rejoin.</summary>
    private readonly Dictionary<MapObject, int> _originalTeam = [];

    // Companion proto level-ups (#10 M2, lights up the #13 foundation): party.txt
    // tables, the per-member level-up bookkeeping, and the swapped-in stage proto
    // (an OVERRIDE, never a mutation of the shared proto cache). The level roll has
    // its own seeded RNG so it doesn't perturb the worldmap/combat streams.
    private Formats.Party.PartyTable? _partyTable;
    private readonly Dictionary<MapObject, Formats.Party.PartyLevelUpState> _companionLevelState = [];
    private readonly Dictionary<MapObject, Formats.Proto.CritterProtoStats> _companionStatOverride = [];
    // P29-M6: per-companion perk ranks (forward-looking infrastructure). Empty on the shippable slice —
    // no companion levels up perks today — so GetCritterState passes null (inert). Persisted in the save.
    private readonly Dictionary<MapObject, int[]> _companionPerkRanks = [];
    private Formats.Combat.ICombatRng? _partyRng;

    private readonly Random _ambientRandom = new(20260612);
    private double _fidgetTimerMs;
    private double _wanderTimerMs;

    /// <summary>Disables ambient NPC life (deterministic screenshots).</summary>
    public bool DisableAmbientLife { get; set; }
    private MapList _mapList = null!;
    private CityList _cities = null!;
    private AudioManager? _audio;
    private int _stepCounter;

    // Per-map ambient sfx (P34-M5): the weighted maps.txt list, a wall-time countdown, and a DEDICATED
    // seeded RNG kept off the combat/worldmap/skill/sneak streams so a future wall-time golden can't shift.
    private IReadOnlyList<(string Name, int Chance)> _mapAmbient = [];
    private double _ambientTimerMs;
    private Random? _ambientRng;
    private const double AmbientIntervalMs = 17000; // ~the engine's 10*randomBetween(15,20) game-ticks
    private WorldmapScreen? _worldmapScreen;
    private bool _worldmapOpen;
    private InterfaceBar? _interfaceBar;
    /// <summary>The bar's screen footprint this frame (0-height when hidden) so the
    /// message log + HUD text lift above it instead of colliding (P11 M0).</summary>
    private int _hudBarHeight;

    /// <summary>The HP/AC values currently shown on the bar — they roll toward the real
    /// stat one unit at a time (the iconic Fallout counter animation; P11 M5 polish).
    /// -1 = uninitialised → snap to the real value on the next step. Purely cosmetic:
    /// never printed, so golden transcripts are unaffected.</summary>
    private int _hudDisplayedHp = -1;
    private int _hudDisplayedAc = -1;
    private double _hudRollAccumulatorMs;

    private WorldArea? _hoveredArea;

    /// <summary>The dude's last worldmap position + area, persisted across saves
    /// (phase-10 M2). -1 = no worldmap state yet (a fresh game before any travel);
    /// set when the player travels to a city, restored on load.</summary>
    private int _worldPosX = -1, _worldPosY = -1;
    private int _currentAreaId = -1;

    /// <summary>The parsed worldmap.txt (random-encounter tables); lazy (phase-10 M1).</summary>
    private Formats.Map.WorldmapFile? _worldmap;
    private Formats.Map.WorldmapFile Worldmap => _worldmap ??=
        Formats.Map.WorldmapFile.Parse(System.Text.Encoding.Latin1.GetString(_vfs.ReadAllBytes(@"data\worldmap.txt")));

    /// <summary>Subtile fog-of-war (P22): the explored-subtile state the party reveals as it
    /// walks the worldmap. Lazy + tied to <see cref="Worldmap"/>; nulled alongside _worldmap on
    /// new-game/load so it re-creates against the freshly parsed file (then imports the save).</summary>
    private Formats.Map.WorldmapFog? _worldFog;
    private Formats.Map.WorldmapFog WorldFog => _worldFog ??= new Formats.Map.WorldmapFog(Worldmap);

    /// <summary>perk.msg / trait.msg display names (P28-M4); lazy. perk i → msg 101+i, trait i →
    /// msg 100+i (perk.cc:218 / trait.cc:74). Fall back to a generic label if the file is absent.</summary>
    private Formats.Text.MessageFile? _perkMsg; private bool _perkMsgTried;
    private Formats.Text.MessageFile? _traitMsg; private bool _traitMsgTried;
    private Formats.Text.MessageFile? LazyMsg(string path, ref bool tried, ref Formats.Text.MessageFile? cache)
    {
        if (!tried)
        {
            tried = true;
            if (_vfs.Exists(path))
            {
                using Stream s = _vfs.OpenRead(path);
                cache = Formats.Text.MessageFile.Load(s);
            }
        }
        return cache;
    }
    private string PerkName(int i) =>
        LazyMsg(@"text\english\game\perk.msg", ref _perkMsgTried, ref _perkMsg)?.GetText(101 + i) is { Length: > 0 } n ? n : $"Perk {i}";
    /// <summary>The perk's description text (perk.msg 1101+i; perk.cc:223). Empty if absent.</summary>
    private string PerkDescription(int i) =>
        LazyMsg(@"text\english\game\perk.msg", ref _perkMsgTried, ref _perkMsg)?.GetText(1101 + i) ?? "";

    // P31 B-M1: data\genrep.txt generic-reputation thresholds, lazily parsed (empty if absent).
    private IReadOnlyList<Formats.Map.ReputationEntry>? _genrep; private bool _genrepTried;
    private IReadOnlyList<Formats.Map.ReputationEntry> GenrepTable()
    {
        if (!_genrepTried)
        {
            _genrepTried = true;
            if (_vfs.Exists(@"data\genrep.txt"))
                _genrep = Formats.Map.GenericReputation.Parse(
                    System.Text.Encoding.Latin1.GetString(_vfs.ReadAllBytes(@"data\genrep.txt")));
        }
        return _genrep ?? [];
    }

    // P31 B-M2: data\karmavar.txt karma-title GVAR rows, lazily parsed (empty if absent).
    private IReadOnlyList<Formats.Map.KarmaEntry>? _karmavar; private bool _karmavarTried;
    private IReadOnlyList<Formats.Map.KarmaEntry> KarmavarTable()
    {
        if (!_karmavarTried)
        {
            _karmavarTried = true;
            if (_vfs.Exists(@"data\karmavar.txt"))
                _karmavar = Formats.Map.KarmaTitles.Parse(
                    System.Text.Encoding.Latin1.GetString(_vfs.ReadAllBytes(@"data\karmavar.txt")));
        }
        return _karmavar ?? [];
    }

    // P32-M1: data\vault13.gam new-game global seed (positional values), lazily parsed once.
    private IReadOnlyList<int>? _gamSeed; private bool _gamSeedTried;
    /// <summary>Seed the non-zero vault13.gam globals into GlobalVars at new-game (game.cc
    /// gameLoadGlobalVars). Sparse — only the ~12 non-zero values are written (an unset key reads 0, so
    /// the 684 zero-seeds are implicit). SILENT (no stdout) so it can't perturb a golden transcript; the
    /// effect is purely the seeded values a script may branch on. No-op if vault13.gam is absent.</summary>
    private void SeedGlobalVars()
    {
        if (_scriptHost is null)
            return;
        if (!_gamSeedTried)
        {
            _gamSeedTried = true;
            if (_vfs.Exists(@"data\vault13.gam"))
                _gamSeed = Formats.Int.GameGlobalVars.Parse(
                    System.Text.Encoding.Latin1.GetString(_vfs.ReadAllBytes(@"data\vault13.gam")));
        }
        if (_gamSeed is null)
            return;
        for (int i = 0; i < _gamSeed.Count; i++)
            if (_gamSeed[i] != 0)
                _scriptHost.GlobalVars[i] = _gamSeed[i];
    }

    /// <summary>The karma/reputation display lines (P31 B-M3) shared by the character sheet + Pip-Boy:
    /// the karma number (PC_STAT_KARMA), the generic-reputation value + title (GVAR_PLAYER_REPUTATION =
    /// GlobalVars[0] via genrep.txt), any earned karma titles, and non-Neutral slice-town standings.</summary>
    private List<string> KarmaDisplayLines()
    {
        int Gv(int g) => _scriptHost?.GlobalVars.GetValueOrDefault(g, 0) ?? 0;
        var lines = new List<string> { $"Karma: {_dudeKarma}" };
        int rep = Gv(0); // GVAR_PLAYER_REPUTATION
        string repTitle = EditorMsg(Formats.Map.GenericReputation.TitleFor(rep, GenrepTable()));
        lines.Add($"Reputation: {rep}{(repTitle.Length > 0 ? $" ({repTitle})" : "")}");
        List<string> earned = [.. Formats.Map.KarmaTitles.Active(KarmavarTable(), Gv)
            .Select(t => EditorMsg(t.NameMessageId)).Where(s => s.Length > 0)];
        if (earned.Count > 0)
            lines.Add($"Titles: {string.Join(", ", earned)}");
        foreach ((int gvar, string townName) in Formats.Map.TownReputation.SliceTowns)
            if (Formats.Map.TownReputation.LevelFor(Gv(gvar)) is var lvl && lvl != Formats.Map.TownRepLevel.Neutral)
                lines.Add($"{townName}: {lvl}");
        // P38: active drug addictions, the engine's "::: Addictions :::" rows (character_editor.cc:4611
        // reads gAddictionReputationVars non-zero, names from editor.msg 1004+index).
        List<string> addictions = [.. Formats.Item.DrugAddiction.ReputationVars
            .Where(r => Gv(r.Gvar) != 0).Select(r => EditorMsg(r.EditorMsgId)).Where(s => s.Length > 0)];
        if (addictions.Count > 0)
            lines.Add($"Addictions: {string.Join(", ", addictions)}");
        return lines;
    }
    // P31 B-M3: editor.msg — the karma/reputation/town title strings (character_editor.cc uses
    // gCharacterEditorMessageList = editor.msg). Lazy; empty if absent.
    private Formats.Text.MessageFile? _editorMsg; private bool _editorMsgTried;
    private string EditorMsg(int id) =>
        id < 0 ? "" : LazyMsg(@"text\english\game\editor.msg", ref _editorMsgTried, ref _editorMsg)?.GetText(id) ?? "";

    private string TraitName(int i) =>
        i < 0 ? "" : LazyMsg(@"text\english\game\trait.msg", ref _traitMsgTried, ref _traitMsg)?.GetText(100 + i) is { Length: > 0 } n ? n : $"Trait {i}";

    /// <summary>worldmap.msg — the encounter display names (phase-16 M0); lazy, null if
    /// absent. Indexed by <see cref="Formats.Map.EncounterResult.MessageId"/>.</summary>
    private Formats.Text.MessageFile? _worldmapMsg;
    private bool _worldmapMsgTried;
    private string? EncounterName(Formats.Map.EncounterResult enc)
    {
        if (!_worldmapMsgTried)
        {
            _worldmapMsgTried = true;
            const string path = @"text\english\game\worldmap.msg";
            if (_vfs.Exists(path))
                using (Stream s = _vfs.OpenRead(path))
                    _worldmapMsg = Formats.Text.MessageFile.Load(s);
        }
        string? name = _worldmapMsg?.GetText(enc.MessageId);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
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
    /// <summary>The dude's bag. Aliased to the dude MapObject's Inventory at
    /// spawn so script externals (item_caps_*, inventory checks) see the same
    /// pocket the panels do.</summary>
    private List<MapObject> _dudeInventory = [];
    private readonly GameClock _clock = new();
    private bool _dudeUnderRoof;
    private int _lastAmbientHour = -1;

    /// <summary>Path for F5/F9 saves and the --save-to/--load-from flags.</summary>
    public string SavePath { get; set; } = "hexwaste-save.json";

    /// <summary>Save right before exiting a screenshot run (testing).</summary>
    public bool SaveOnExit { get; set; }

    /// <summary>Load this save after startup (testing / resume).</summary>
    public bool LoadOnStart { get; set; }
    private Texture2D? _panelPixel;
    private FloorRenderer? _floorRenderer;
    private readonly List<string> _messageLog = [];

    /// <summary>P52-M5: lines scrolled back from the newest in the green monitor (display_monitor.cc
    /// _disp_curr). 0 = newest at the bottom; clamped to the history each frame in DrawInterfaceBar.</summary>
    private int _monitorScroll;
    private const int MessageLogFallbackLines = 5; // the bar-hidden bottom-left log keeps the old 5-line cap

    /// <summary>P52-M6: seconds elapsed in the post-load fade-in (>= <see cref="MapFadeSeconds"/> = done).
    /// A full-screen black quad ramps from opaque to clear over a map load — the visible analogue of the
    /// engine's modal paletteFadeTo (map.cc mapLoad). DOCUMENTED DIVERGENCE: a GPU quad, not a palette
    /// lerp, and fade-IN only (our map load is synchronous — no prior-frame budget to fade OUT first).</summary>
    private double _mapFadeElapsed = MapFadeSeconds;
    private const double MapFadeSeconds = 0.35;

    /// <summary>Open dialog with the object at this screen point on start (testing).</summary>
    public Point? TalkAt { get; set; }

    /// <summary>Open dialog with the critter at this hex tile on start (testing).</summary>
    public int? TalkAtHex { get; set; }

    /// <summary>Scripted startup sequence (testing): use/lockpick objects,
    /// loot open containers, transition maps, save/load — in CLI order.</summary>
    public abstract record StartupAction
    {
        public sealed record UseHex(int Hex, bool Lockpick) : StartupAction;
        public sealed record ExamineCritter(int Hex) : StartupAction;
        public sealed record Attack(int Hex) : StartupAction;
        public sealed record Burst(int Hex) : StartupAction;
        /// <summary>Burst at TargetHex from an explicit dude tile FromHex (phase-20 M4) —
        /// aim the collateral cone at a real bystander.</summary>
        public sealed record BurstAt(int FromHex, int TargetHex) : StartupAction;
        /// <summary>Talk to the critter at Hex and auto-pick Choices (1-based), printing
        /// each round. Composable: several in one run share the session GVAR dict, so a
        /// gated chain (talk Vic → talk Metzger → talk Vic) works (#10 M0/M1).</summary>
        public sealed record TalkChoose(int Hex, int[] Choices) : StartupAction;
        /// <summary>Test plumbing: force a global var (probe GVAR-gated dialog branches).</summary>
        public sealed record SetGlobal(int Id, int Value) : StartupAction;
        /// <summary>Print party size + dude caps as a state-only transcript line (no
        /// dialog text) — the assertion target for the legitimate-recruit fixture.</summary>
        public sealed record PartyCount : StartupAction;
        /// <summary>Fire a HUD bar button by name (INV/OPT/MAP/CHA/PIP/SKILLDEX) and
        /// report the resulting panel state — regression-proofs the M4 click wiring.</summary>
        public sealed record HudClick(string Name) : StartupAction;
        /// <summary>Click an item-panel row (phase-15 M2): Side 0=left/40, 1=right/420;
        /// Row is 0-based within the current page. Drives the same geometry+dispatch path
        /// a live mouse click does, then reports the result — regression-proofs row clicks.</summary>
        public sealed record PanelClick(int Side, int Row) : StartupAction;
        /// <summary>Click a row of the Options or Pip-Boy menu (phase-15 M3): Menu is
        /// "options" / "pipboy" / "pipboy-rest", Row is 0-based. Drives the same
        /// geometry + dispatch a live click does and reports which row was hit.</summary>
        public sealed record MenuClick(string Menu, int Row) : StartupAction;
        public sealed record UseSkill(int Skill, int TargetHex) : StartupAction;
        public sealed record RestFor(int Minutes) : StartupAction;
        public sealed record OpenAutomap : StartupAction;
        /// <summary>Phase-22: travel a worldmap leg from (X,Y) toward AreaIndex (avoiding the
        /// prompt) and report the fog-of-war reveal — proves subtiles get marked VISITED/KNOWN
        /// as the party walks, and that the destination subtile becomes clear.</summary>
        public sealed record FogProbe(int X, int Y, int AreaIndex) : StartupAction;
        /// <summary>Center the camera on a hex (screenshot testing, e.g. P23 translucency).</summary>
        public sealed record CenterHex(int Hex) : StartupAction;
        /// <summary>Report the dude's carried weight / capacity / encumbered / AP-penalty (P24).</summary>
        public sealed record WeightProbe : StartupAction;
        /// <summary>Set the sneaking flag (P29 A-M0) and report the two-layer state + Sneak skill.</summary>
        public sealed record SneakProbe(int Flag) : StartupAction;
        /// <summary>Report the Silent Death facing test for two rotations (P30 A-M1).</summary>
        public sealed record BackstabProbe(int AttackerRotation, int DefenderRotation) : StartupAction;
        /// <summary>Seed the sneak RNG, enable the flag, do one periodic roll, report it (P30 A-M2).</summary>
        public sealed record SneakRoll(int Seed) : StartupAction;
        /// <summary>Report the isWithinPerception detection decision for a controlled setup (P30 A-M3).</summary>
        public sealed record DetectProbe(int Perception, int Distance, int CanSee, int Flag, int Working) : StartupAction;
        /// <summary>Report the dude's PC stats read via get_pc_stat — karma/reputation/level/xp (P31 B-M0).</summary>
        public sealed record KarmaProbe : StartupAction;
        /// <summary>Report the generic-reputation title message id for a value (P31 B-M1; id only).</summary>
        public sealed record RepTitle(int Value) : StartupAction;
        /// <summary>Report the town-reputation band for a value (P31 B-M2).</summary>
        public sealed record TownRep(int Value) : StartupAction;
        /// <summary>Report the count + name-ids of the currently-earned karma titles (P31 B-M2).</summary>
        public sealed record KarmaTitlesProbe : StartupAction;
        /// <summary>Set the dude's PC-stat karma + reputation (P31 B-M3 harness; pcSetStat clamps).</summary>
        public sealed record SetKarma(int Karma, int Reputation) : StartupAction;
        /// <summary>Read a global var (P32-M1; verifies vault13.gam seeding after a new game).</summary>
        public sealed record GetGlobal(int Id) : StartupAction;
        /// <summary>P53: look up a dialogue line's audio field (or force one) + report the composed speech
        /// path + the ShouldSpeak verdict — asset PATHS only, never the message text. ForcedAudio "-" =
        /// the real MSG lookup for (ListId, MsgId); any other value forces that audio basename.</summary>
        public sealed record SpeechProbe(int ListId, int MsgId, string ForcedAudio) : StartupAction;
        /// <summary>Relocate the critter at FromHex to ToHex via the placement path (P32; verifies
        /// critter_attempt_placement actually moves a critter to a different tile).</summary>
        public sealed record PlaceProbe(int FromHex, int ToHex) : StartupAction;
        /// <summary>Drive a reg_anim_func batch (begin -> move-to-tile -> end) on the critter at
        /// FromHex toward ToHex via the executor (P33-M1; no slice script fires the move ops).</summary>
        public sealed record RegAnimMove(int FromHex, int ToHex) : StartupAction;
        /// <summary>Report is_in_combat + critter_state(critter@Hex) — the two heartbeat externals
        /// (P34-M1). Hex&lt;0 reports is_in_combat only.</summary>
        public sealed record CritterStateProbe(int Hex) : StartupAction;
        /// <summary>OR Flags (DAM_* mask) into the critter@Hex's CombatResults, then report whether
        /// its AI packet's hurt_too_much mask would now make it flee (P34-M2).</summary>
        public sealed record HurtTooMuchProbe(int Hex, int Flags) : StartupAction;
        /// <summary>Report the dude's movement anim-code under each run guard (P34-M3).</summary>
        public sealed record RunProbe : StartupAction;
        /// <summary>Position the dude adjacent to FightHex, then report the combat-outline type each
        /// living critter would get (P34-M4; zero-RNG, no combat entry).</summary>
        public sealed record OutlineProbe(int FightHex) : StartupAction;
        /// <summary>Report the gore death-anim a burst/explosion/laser kill would give the critter
        /// at Hex — the picked anim + the art-resolved anim (P26), proving gore art availability.</summary>
        public sealed record DeathProbe(int Hex) : StartupAction;
        /// <summary>Report the composed combat-sfx names for the critter at Hex (swing/hit/die) +
        /// a weapon name + the map's first ambient entry (P34-M5).</summary>
        public sealed record SfxProbe(int Hex) : StartupAction;
        /// <summary>Report the reaction-anim codes (hit/dodge/fall/getup) the critter at Hex would get
        /// from an attacker at AttackerRotation (P34-M6).</summary>
        public sealed record ReactionProbe(int Hex, int AttackerRotation) : StartupAction;
        /// <summary>Spawn a sample combat float over the critter at Hex and report the float-text layer's
        /// STATE — count, lifetime, the outcome colours (as hex ints), and the engine anchor offset
        /// (P45). STATE-only: never the message text (a damage NUMBER / the hardcoded "Missed" only).</summary>
        public sealed record FloatTextProbe(int Hex) : StartupAction;
        /// <summary>M0 diagnostic: run the map's map_update_p_proc (map script + object scripts,
        /// SCRIPT_PROC 23) once and report its observable side effects — lighting calls, the ambient
        /// before/after, and any NEW stubbed externals. STATE-only (counts/ids), no game strings.</summary>
        public sealed record MapUpdateProbe : StartupAction;
        /// <summary>Per-map content-coverage smoke scan: census the loaded map (critters / containers /
        /// doors / scripted objects) and report the FULL set of stubbed (unwired) externals its scripts
        /// fired (map_enter on load + a map_update pass) — a NEW city's silent-quest-gap detector.
        /// STATE-only (counts + external NAMES), deterministic + headless, no walking / UI / RNG.</summary>
        public sealed record SmokeScan : StartupAction;
        /// <summary>Drive the real drag-to-equip path (P47): drag the inventory item at FromRow onto a
        /// slot — Slot 0=weapon, 2=armor, -1=drop. Reports pid + equipped flag + AC/DT/DR. STATE-only
        /// (pid + ints), never the item's name/message text.</summary>
        public sealed record DragEquip(int FromRow, int Slot) : StartupAction;
        /// <summary>Drive the called-shot dialog's row selection (P49): pick dialog row 0..8 and report
        /// the resulting AimLocation + its to-hit penalty. STATE-only (ints + the part name).</summary>
        public sealed record AimClick(int Row) : StartupAction;
        /// <summary>Drive the combat-control window (P50): open it for the critter at Hex, cycle window
        /// row Row Count times (the real CycleTacticsRow path), and report the resulting EFFECTIVE
        /// disposition/knobs. STATE-only (enum names).</summary>
        public sealed record CompanionTactics(int Hex, int Row, int Count) : StartupAction;
        /// <summary>Run the critter@Hex's per-turn combat_p_proc (fp=4) and report whether it defines the
        /// proc + whether it script_overrides the turn (P35).</summary>
        public sealed record CombatProcProbe(int Hex) : StartupAction;
        /// <summary>Fire the critter@Hex's ON-HIT combat_p_proc (fp=2, target = the dude) and report the
        /// dude's poison delta — proves the scorpion's sting poisons whom it struck (P35 fp=2).</summary>
        public sealed record CombatProcHit(int AttackerHex) : StartupAction;
        /// <summary>Set the dude's poison to InitialPoison, advance the game clock GameMinutes, process the
        /// poison damage ticks, and report the poison + HP deltas (P35-M3 poison-over-time).</summary>
        public sealed record PoisonTick(int InitialPoison, int GameMinutes) : StartupAction;
        /// <summary>Snapshot the dude's BonusStats, advance the clock GameMinutes, fire the scheduled
        /// drug wear-off, and report every changed stat index before→after (P37 — proves the immediate
        /// effect + the timed reversal). Pid is informational; the drug must already be in effect via
        /// a preceding --use-item.</summary>
        public sealed record DrugProbe(int Pid, int GameMinutes) : StartupAction;
        /// <summary>Seed the addiction RNG, give+use one drug Pid (the faithful UseDrug→roll path),
        /// advance the clock GameMinutes, fire the withdrawal onset/recovery, and report the addiction
        /// GVAR + active withdrawal stat penalty + pending count (P38 — STATE-only ints). Seed is chosen
        /// so the deterministic roll hits.</summary>
        public sealed record AddictProbe(int Pid, int Seed, int GameMinutes) : StartupAction;
        /// <summary>Report the dude's kill tally (P38; killsGetByType). KillType &gt;= 0 reports that one
        /// type's count; KillType &lt; 0 reports every non-zero type. STATE-only ints.</summary>
        public sealed record KillsProbe(int KillType) : StartupAction;
        /// <summary>Give one book Pid and read it (the faithful UseInventoryItem→book path), reporting the
        /// trained skill's value before/after + the gain (P39). STATE-only ints.</summary>
        public sealed record UseBook(int Pid) : StartupAction;
        /// <summary>Switch the equipped weapon to the given ammo Pid (unload current + reload-with-pid),
        /// reporting the loaded type + the combat-relevant ammo mods (P40). STATE-only ints.</summary>
        public sealed record LoadAmmo(int AmmoPid) : StartupAction;
        /// <summary>Give the critter@Hex a stimpak, drop it to 1 HP, and run the AI heal (the real
        /// TryNpcHeal path), reporting the heal (P42). STATE-only ints.</summary>
        public sealed record AiHealProbe(int Hex) : StartupAction;
        /// <summary>Force the critter@Hex's wielded gun dry and run the AI inventory weapon switch
        /// (the real CritterInventoryWeapons → best_weapon fold → EquipWeapon path), reporting its
        /// best_weapon pref, equipped + carried weapon pids, and what it switched to (P43). STATE-only.</summary>
        public sealed record AiWeaponProbe(int Hex) : StartupAction;
        /// <summary>Enter combat with the critter@Hex, drop it to ≤half HP, run its fp=4 combat_p_proc, and
        /// report whether terminate_combat ended the fight + the critter's maneuver (P35-M5).</summary>
        public sealed record TerminateCombatProbe(int Hex) : StartupAction;
        /// <summary>Report whether the proto Pid carries the OBJECT_MULTIHEX flag (P36 — verifies a slice
        /// critter is multihex + that the +15 to-hit / spawn propagation has a real driver).</summary>
        public sealed record MultihexProbe(int Pid) : StartupAction;
        /// <summary>Set the dude's two traits (id&lt;0 = none) and report the live effect on his
        /// stats/skills + has_trait (P28-M1).</summary>
        public sealed record TraitProbe(int Trait1, int Trait2) : StartupAction;
        /// <summary>At dude level Level, test whether perk Index can be taken (the gates) and, if so,
        /// add a rank and report the live stat effect (P28-M2).</summary>
        public sealed record PerkProbe(int Index, int Level) : StartupAction;
        /// <summary>At dude level Level, open the perk picker and select the Row-th eligible perk
        /// (P28-M4) — drives the real AvailablePerkPicks/EligiblePerks/ChoosePerk path.</summary>
        public sealed record PerkPick(int Level, int Row) : StartupAction;
        /// <summary>Open NPC dialogue with the dude's IN forced to ForceIn and report the option
        /// COUNT at the greeting (P25 IQ-gating; never the copyrighted option text). ForceIn &lt; 0
        /// leaves IN unchanged.</summary>
        public sealed record IqProbe(int Hex, int ForceIn) : StartupAction;
        /// <summary>Phase-21: report the ambient light after map_enter — proves the map's
        /// scripted set_light_level took effect.</summary>
        public sealed record LightProbe : StartupAction;
        /// <summary>Phase-21: report the map's reg_anim_animate_forever registrations — proves
        /// the (otherwise arity-stubbed) external reached the animator.</summary>
        public sealed record RegAnimProbe : StartupAction;
        public sealed record Explode(int Hex) : StartupAction;
        public sealed record Throw(int Hex) : StartupAction;
        public sealed record ProjectileCheck(int Hex) : StartupAction;
        public sealed record LoadTransient(string Map) : StartupAction;
        public sealed record EncounterWalk(int X0, int Y0, int X1, int Y1, int Steps) : StartupAction;
        public sealed record EncounterSpawnAt(string Map, string Group, int Count) : StartupAction;
        /// <summary>Spawn an X-FIGHTING-Y encounter (phase-16 M3): two groups on distinct
        /// teams on a transient map; SpawnEncounter starts the brawl. Reports the team
        /// census + that combat opened.</summary>
        public sealed record EncounterFight(string Map, string GroupA, int CountA, string GroupB, int CountB) : StartupAction;
        public sealed record TravelFrom(int X, int Y, int AreaIndex) : StartupAction;
        /// <summary>Pre-answer a detected encounter's avoid prompt (phase-16 M1):
        /// Engage=true engages, false avoids+continues. Must precede the travel action.</summary>
        public sealed record EncounterAnswer(bool Engage) : StartupAction;
        /// <summary>Simulate leaving an encounter map mid-leg (phase-16 M2): set the in-
        /// flight travel state at (X,Y) bound for AreaIndex, walk off the edge, and assert
        /// travel auto-resumes toward the destination with no worldmap re-click.</summary>
        public sealed record TravelResume(int X, int Y, int AreaIndex) : StartupAction;
        /// <summary>Drive the ANIMATED travel path headlessly (phase-17 M2/M4): start an
        /// animated leg from (X,Y) toward AreaIndex and drain StepAnimatedTravel tick-by-
        /// tick, reporting cadence-ticks vs pixel-steps (the terrain pacing) + the outcome.</summary>
        public sealed record TravelStepDemo(int X, int Y, int AreaIndex) : StartupAction;
        /// <summary>Save MID-travel then load (phase-17 M4): start an animated leg, step
        /// Ticks cadence ticks, save+load in-process, and report whether the dot worldPos +
        /// the in-flight destination round-trip (the save resumes travel on load).</summary>
        public sealed record TravelSaveMid(int X, int Y, int AreaIndex, int Ticks) : StartupAction;
        /// <summary>Phase-18 M0/M1: open combat on the critter at FightHex, set the dude's AP
        /// to Ap, walk toward WalkHex, and report how far the AP-gated walk got (HurtLeg
        /// cripples a leg first to show the 4x cost). Proves in-combat movement costs AP.</summary>
        public sealed record CombatWalk(int FightHex, int WalkHex, int Ap, bool CrippleLeg) : StartupAction;
        /// <summary>Override the party's best Outdoorsman skill (phase-16 M1 test plumbing)
        /// so the detect path fires deterministically regardless of the dude's build.</summary>
        public sealed record ForceOutdoorsman(int Value) : StartupAction;
        public sealed record Fight(int Hex) : StartupAction;
        public sealed record Give(int Pid, int Count) : StartupAction;
        public sealed record UseItemByPid(int Pid) : StartupAction;
        public sealed record UseOn(int Pid, int Hex) : StartupAction;
        public sealed record Recruit(int Hex) : StartupAction;
        public sealed record CompanionLifecycle(int Hex) : StartupAction;
        public sealed record TradeWith(int Hex, int Pid) : StartupAction;
        public sealed record CompanionPersist(int Hex) : StartupAction;
        public sealed record DismissPersist(int Hex) : StartupAction;
        public sealed record Buy(int Pid) : StartupAction;
        public sealed record Sell(int Pid) : StartupAction;
        public sealed record EndBarter : StartupAction;
        public sealed record TakeAll : StartupAction;
        public sealed record Transit(string MapFile, int Tile, int Elevation) : StartupAction;
        public sealed record SaveNow : StartupAction;
        public sealed record LoadNow : StartupAction;
        /// <summary>P48: save the game into slot N (0..9), under SaveDir. STATE-only report.</summary>
        public sealed record SaveToSlot(int Slot) : StartupAction;
        /// <summary>P48: load the game from slot N if occupied. STATE-only report.</summary>
        public sealed record LoadFromSlot(int Slot) : StartupAction;
        /// <summary>P48: report every slot's state (empty / L&lt;level&gt; / old) — STATE-only, no names.</summary>
        public sealed record SlotsProbe : StartupAction;
        /// <summary>P48: clear the slot files in SaveDir (a fresh-slate harness primitive for goldens).</summary>
        public sealed record ResetSlots : StartupAction;
        /// <summary>P48: open the save/load slot picker (Mode 0=save, 1=load) — for screenshots.</summary>
        public sealed record ShowSaveLoad(int Mode) : StartupAction;
        public sealed record GrantXp(int Amount) : StartupAction;
        public sealed record SpendSkill(int Skill) : StartupAction;
        public sealed record OpenSkills : StartupAction;
        public sealed record Rest : StartupAction;
        public sealed record Hurt(int Amount) : StartupAction;
        public sealed record CreateCharacter(int[] Special, int[] Tags, int Gender, int[] Traits) : StartupAction;
        public sealed record ShowCreate(string Step = "") : StartupAction;
        public sealed record AdvanceDays(int Days) : StartupAction;
    }

    public List<StartupAction> StartupActions { get; set; } = [];

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

    /// <summary>Per-map world deltas (doors, taken/created objects, container
    /// contents, MVARs), captured on map exit and replayed over pristine
    /// reloads. Keyed by header map name; pristine objects are identified by
    /// load-order ordinal because MAP object Ids collide by the hundreds.</summary>
    private readonly Dictionary<string, SaveState.MapDelta> _visitedMaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<MapObject, int> _objectOrdinals = [];
    private MapObject[] _ordinalObjects = [];
    private (int Tile, int Rotation, int Elevation)[] _pristinePositions = [];

    /// <summary>Ordinals holding inventory right after map_enter (script-stocked
    /// containers) — captured even when later emptied, so looting sticks.</summary>
    private readonly HashSet<int> _stockedOrdinals = [];

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
        if (RngSeed is { } seed)
            _combatRng = new Formats.Combat.SystemCombatRng(seed);
        _combat = new Formats.Combat.CombatEngine(this, _combatRng);

        _protos = new ProtoDatabase(_vfs);
        _cycler = new PaletteCycler(_palette);
        _artIndex = new ArtIndex(_vfs);
        _frmCache = new FrmCache(_vfs, _artIndex, GraphicsDevice, _palette);
        _mapList = MapList.Load(_vfs);
        _cities = CityList.Load(_vfs);
        if (!DisableAudio)
            _audio = new AudioManager(_vfs, _gameDir);
        _protoMessages = new ProtoMessages(_vfs, _protos);

        // --character picks a premade sheet (combat/diplomat/stealth/player);
        // default is the blank player.gcd. Used for testing builds + gender.
        _activeCharacter = string.IsNullOrEmpty(CharacterName) ? "player" : CharacterName;
        string gcdPath = $@"premade\{_activeCharacter}.gcd";
        if (_vfs.Exists(gcdPath))
        {
            using Stream gcdStream = _vfs.OpenRead(gcdPath);
            _dudeGcd = Formats.Combat.GcdFile.Load(gcdStream);
            Console.WriteLine($"dude sheet: {_dudeGcd.Name} (gender {_dudeGcd.Stats.BaseStats[34]}),"
                + $" SPECIAL {string.Join("/", Enumerable.Range(0, 7).Select(s => _dudeGcd.Stats.BaseStats[s] + _dudeGcd.Stats.BonusStats[s]))},"
                + $" tags [{string.Join(",", _dudeGcd.TaggedSkills.Where(t => t >= 0))}]");
        }
        else
        {
            Console.Error.WriteLine($"{gcdPath} not found — dude uses art-proto stats");
        }

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
                CurrentMapIndexProvider = () => _mapList.GetIndexByFileName(_currentMapName),
                OnScriptMessage = message => Log(message),
                MoveRequested = (npc, tile) => StartNpcWalk(npc, tile),
                // Phase-21: script-driven lighting. set_light_level sets the global ambient
                // (pin it so the day/night clock stops driving it); obj_set_light_level
                // re-lights one object + recomputes the grid.
                LightLevelRequested = level =>
                {
                    _lightGrid.Ambient = Formats.Light.LightGrid.AmbientFromLightLevel(level);
                    AmbientFixed = true; // a script light level pins the ambient (clock stops driving it)
                },
                ObjectLightRequested = (obj, intensity, distance) =>
                {
                    // opSetObjectLightLevel: intensity 0-100% -> 0..65536 (the engine's literal 65636).
                    obj.LightIntensity = intensity != 0 ? intensity * 65636 / 100 : 0;
                    obj.LightDistance = distance;
                    if (obj.LightIntensity > 0) obj.Flags |= 0x20; else obj.Flags &= ~0x20; // OBJECT_LIGHTING
                    RebuildLighting();
                },
                AnimateForeverRequested = (obj, anim) =>
                {
                    // reg_anim_animate_forever: loop animation code `anim` on the object. A
                    // critter gets its anim-coded FID + a looping animator state; scenery just
                    // loops its FRM. SLICE NOTE: every slice usage targets SCENERY (firepits,
                    // a waterfall) which our multi-frame art already auto-loops, so the call is
                    // faithful but visually redundant here; the critter path lights up for free.
                    // Idempotent per object (the engine has ONE anim slot per object): a script's
                    // map_enter AND map_update both register the same firepits (P46 map_update wiring),
                    // so skip a re-registration rather than stack a duplicate loop / double-count.
                    string foreverKey = $"{ObjectName(obj)}@{obj.HexTile}:{Fid.Type(obj.Fid)}:anim{anim}";
                    if (_regAnimForever.Contains(foreverKey))
                        return;
                    _regAnimForever.Add(foreverKey);
                    if (Fid.Type(obj.Fid) is ObjectType.Critter)
                        _animator.SetCritterAnimation(obj, Fid.Build(ObjectType.Critter, Fid.Index(obj.Fid), anim,
                            Fid.WeaponCode(obj.Fid), obj.Rotation));
                    else
                        _animator.AddLooping(obj);
                },
                // First hit of each distinct stub goes to stderr; counts are
                // dumped per map on exit (gap analysis for wiring externals).
                OnStubbedExternal = name =>
                {
                    if (!_stubbedExternals.TryAdd(name, 1))
                        _stubbedExternals[name]++;
                    else
                        Console.Error.WriteLine(name);
                },
                StatsResolver = obj => obj == _dude?.Dude ? _dudeGcd?.Stats : null,
                PlaceObjectRequested = (obj, tile, elevation) => PlaceObject(obj, tile, elevation),
                RegAnimRequested = ExecuteRegAnim,
                RegAnimClearRequested = ClearAnimation,
                AttackRequested = (attacker, target) =>
                {
                    // P30 A-M3: a sneaking dude can slip past scripted aggro (isWithinPerception,
                    // combat_ai.cc:3499). GATED on the sneak FLAG so a non-sneaking dude ALWAYS engages
                    // (the gate short-circuits → every existing golden is byte-identical); only an
                    // actively-sneaking dude out of the NPC's (reduced) perception range goes undetected.
                    if (target == _dude?.Dude && _sneak.FlagSet && !DudePerceivedBy(attacker))
                        return;
                    _combat.BeginScriptAggro(attacker, target);
                },
                ExpAwarded = amount => AwardXp(amount),
                MapStartOverridden = (tile, elevation, rotation) => OverrideDudeStart(tile, elevation, rotation),
                MoviePlayed = movieId => ShowMovieCard(movieId),
                CritterDamaged = (victim, amount, bypassArmor) => OnScriptDamage(victim, amount, bypassArmor),
                PartyChanged = (critter, joined) => OnPartyChanged(critter, joined),
                AnimBusyResolver = obj => _animator.TryGetState(obj, out _)
                    || (_npcWalkers.TryGetValue(obj, out DudeController? walker) && walker.Moving),
                DudeTraits = _dudeGcd?.Traits ?? [-1, -1],
                PcStatProvider = stat => stat switch
                {
                    Formats.Int.PcStat.UnspentSkillPoints => _unspentSkillPoints,
                    Formats.Int.PcStat.Level => _dudeLevel,
                    Formats.Int.PcStat.Experience => _dudeXp,
                    Formats.Int.PcStat.Reputation => _dudeReputation, // P31 B-M0 (0 by default → inert)
                    Formats.Int.PcStat.Karma => _dudeKarma,
                    _ => 0,
                },
                PerkRankProvider = perk => Formats.Perks.PerkRules.Rank(_dudePerkRanks, perk),
                KillCountProvider = kt => kt >= 0 && kt < _killsByType.Length ? _killsByType[kt] : 0, // P38: GET_KILL_COUNT
                SneakFlagProvider = () => _sneak.FlagSet, // P29 A-M0: using_skill(dude, SNEAK)
                CombatActiveProvider = () => _combat.Phase != Formats.Combat.CombatPhase.Idle, // P34-M1: is_in_combat(0x8128)
                PoisonRequested = (obj, amount) => ApplyPoison(obj, amount), // P35: poison(0x8122)
                CombatTerminateRequested = () => _combat.RequestTerminateCombat(), // P35-M5: terminate_combat(0x8153)
                DialogVoiceRequested = PlayDialogVoice, // P53: a voiced dialogue reply plays its speech file
            };
            if (RngSeed is { } scriptSeed)
                _scriptHost.Rng = new Random(scriptSeed);
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
        _interfaceBar = new InterfaceBar(GraphicsDevice, _vfs, _palette); // P11 HUD bar
        if (StartInMenu)
        {
            _menu = MenuState.Title;
            _premadeGcds = [.. new[] { "combat", "diplomat", "stealth", "blank" }
                .Select(name => ($@"premade\{name}.gcd", name))
                .Where(t => _vfs.Exists(t.Item1))
                .Select(t =>
                {
                    using Stream stream = _vfs.OpenRead(t.Item1);
                    var gcd = Formats.Combat.GcdFile.Load(stream);
                    string label = string.IsNullOrWhiteSpace(gcd.Name) || gcd.Name == "None"
                        ? char.ToUpper(t.Item2[0]) + t.Item2[1..]
                        : gcd.Name;
                    return ($"{label}  (S{gcd.Stats.BaseStats[0]} P{gcd.Stats.BaseStats[1]} E{gcd.Stats.BaseStats[2]}"
                        + $" C{gcd.Stats.BaseStats[3]} I{gcd.Stats.BaseStats[4]} A{gcd.Stats.BaseStats[5]} L{gcd.Stats.BaseStats[6]})",
                        t.Item1);
                })];
        }
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

        // Dialog hooks run first so --choose can open barter for the
        // --buy/--sell startup actions below.
        if (TalkAtHex is { } talkHex)
        {
            MapObject? hexNpc = CritterAt(talkHex);
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


        RunStartupActions();
    }

    /// <summary>Set once headless startup actions have run (no screenshot/bench);
    /// the next Update saves-if-asked and exits.</summary>
    private bool _exitAfterStartupActions;

    /// <summary>
    /// Loads (or transitions to) a map. <paramref name="spawnAt"/> places the
    /// dude at an exit-grid/stairs destination; null uses the map's entering
    /// position.
    /// </summary>
    /// <summary>Set while the current map is a transient random-encounter map
    /// (saved=No): it gets NO delta slot and regenerates pristine each visit
    /// (phase-10 M0; the engine erases its .SAV — map.cc:1456).</summary>
    private bool _currentMapTransient;

    /// <summary>The encounter whose group the next transient LoadMap spawns, then
    /// clears (phase-10 M3). Set by the worldmap roll / the --encounter demo right
    /// before LoadMap(..., transient: true).</summary>
    private Formats.Map.EncounterResult? _pendingEncounter;

    private void LoadMap(string mapName, MapDestination? spawnAt, bool captureOutgoing = true,
        bool transient = false)
    {
        // Remember what the player changed on the map being left, so a
        // revisit can replay it over the pristine file (engine: SAVE.DAT
        // serializes whole visited maps; the PoC keeps deltas instead).
        // Guard #2 (transient persistence): a transient map being LEFT writes no
        // delta — it is regenerated, not remembered.
        if (captureOutgoing && _map is not null && !_currentMapTransient)
        {
            ExtractPartyFromMap();
            ExtractDismissedFromMap(); // persist this map's dismissed bodies, pull them off it
            CaptureMapDelta();
        }
        _dismissedCompanions.Clear(); // live dismissed set is rebuilt per map (transient ones vanish)
        _currentMapTransient = transient;

        if (_stubbedExternals.Count > 0)
        {
            Console.Error.WriteLine("stub histogram: " + string.Join(" ",
                _stubbedExternals.OrderByDescending(kv => kv.Value).Take(10)
                    .Select(kv => $"{kv.Key}×{kv.Value}")));
            _stubbedExternals.Clear();
        }

        _currentMapName = mapName;
        // P32 robustness: a missing/corrupt map or proto used to throw out of here, through LoadContent
        // and MonoGame's Update loop, into a hard SIGABRT. Catch the expected I/O/parse failures and
        // soft-fail — the teardown below hasn't run yet, so the prior map/menu state stays intact (the
        // player just stays put). No shippable map trips this; it hardens against bad data.
        try
        {
            using Stream stream = _vfs.OpenRead($@"maps\{mapName}");
            _map = MapFile.Load(stream, _protos);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException
            or NotSupportedException or EndOfStreamException)
        {
            Console.Error.WriteLine($"load-map: failed to load '{mapName}': {ex.GetType().Name}: {ex.Message}");
            Log($"Could not load map {mapName}.");
            // A transition keeps the prior map (teardown below hasn't run). With no prior map (initial
            // load / a bad --map arg), fall back to the title menu — which runs null-map-safe — so the
            // app survives instead of NPE-ing on a null _map.
            if (_map is null)
                _menu = MenuState.Title;
            return;
        }

        _animator = new ObjectAnimator(_frmCache);
        _scriptHost?.ClearTimers();
        _scriptHost?.ResetHandles();
        _combat.Reset();
        _walkMode = false;
        _hoveredObject = null;
        _dude = null;
        _openDoors.Clear();
        _floatText.Clear(); // P45: stale combat-text floats don't survive a map change
        _floatDefender = null;
        _seenObjects.Clear(); // automap fog resets per map (P20-M2)
        _regAnimForever.Clear(); // reg_anim record resets per map (P21-M1)
        AmbientFixed = false; // each map re-pins its own ambient via its scripts' set_light_level (P46)
        _regAnimMoves.Clear(); // reg_anim_func batch record resets per map (P33-M1)
        _npcWalkers.Clear();
        _homeTiles.Clear();
        _projectiles.Clear();
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

        // Load-order ordinals are the stable cross-visit identity for
        // pristine objects (MAP object Ids repeat across records).
        _objectOrdinals.Clear();
        _stockedOrdinals.Clear();
        var ordinalObjects = new List<MapObject>();
        var pristine = new List<(int, int, int)>();
        for (int elevation = 0; elevation < MapFile.ElevationCount; elevation++)
        {
            foreach (MapObject obj in _map.Elevations[elevation]?.Objects ?? [])
            {
                _objectOrdinals[obj] = ordinalObjects.Count;
                ordinalObjects.Add(obj);
                pristine.Add((obj.HexTile, obj.Rotation, elevation));
            }
        }
        _ordinalObjects = [.. ordinalObjects];
        _pristinePositions = [.. pristine];

        // Guard #1: a transient map reads NO stored delta on entry — which leaves
        // `delta` null, so RunMapEnter below falls to the pristine firstRun=1
        // default (guard #3). Real maps keep their name-keyed delta replay.
        SaveState.MapDelta? delta = null;
        if (!transient)
            _visitedMaps.TryGetValue(_map.Header.Name, out delta);
        if (delta is not null)
            ApplyDeltaBeforeScripts(delta);

        StartLoopingAnimations();
        SpawnDude(spawnTile, spawnRotation);

        // Run map-entry scripts: locks get set, containers stocked (M3),
        // before lighting is computed. Revisits run with map_first_run = 0
        // (LVARs survive in the host, keyed by map name).
        if (_scriptHost is not null)
        {
            // ALL scripted objects, hidden ones included — shopkeeper stock
            // boxes are invisible items whose map_enter stocks the store and
            // store_external()s the box (the engine runs scripts regardless
            // of visibility).
            IEnumerable<MapObject> scripted = _map.Elevations
                .Where(e => e is not null)
                .SelectMany(e => e!.Objects)
                .Where(o => o.Sid != -1 && o != _dude?.Dude);
            _scriptHost.SpatialsEnabled = false; // _scr_SpatialsEnabled gate (map.cc:973)
            // Guard #3: a transient map is pristine every visit — force firstRun=1
            // (the engine always treats a saved=No map as first-run), overriding the
            // run-once cache. Real maps keep the delta/cache behaviour.
            _scriptHost.RunMapEnter(_map, scripted, _dude?.Dude,
                firstRunOverride: transient ? true : delta is not null ? false : null);
            _scriptHost.SpatialsEnabled = true;

            // The engine's load sequence runs map_enter THEN map_update once each, both on
            // load (map.cc:1010-1011) — map_update is the per-map periodic hook, fired again
            // every 600 game ticks (mapUpdateEventProcess). On the slice its sole observable
            // payload is a one-shot set_light_level (the M0 diagnostic): arcaves dims to the
            // "cavern" level 50 (62.5%) that map_enter left at max; the other slice maps re-set
            // the same max (inert). `scripted` is re-evaluated, so map_enter-created objects are
            // included (faithful). The periodic 600-tick re-run is DEFERRED — the diagnostic
            // found no time-varying map_update content on the slice, so once-on-load suffices.
            _scriptHost.RunMapUpdate(_map, scripted, _dude?.Dude);
        }

        foreach ((MapObject obj, int ordinal) in _objectOrdinals)
        {
            if (obj.Inventory.Count > 0)
                _stockedOrdinals.Add(ordinal);
        }
        if (delta is not null)
            ApplyDeltaAfterScripts(delta);
        InjectPartyMembers();
        InjectDismissedFromRoster(); // recreate this map's dismissed companions (P10 #3)

        // The engine order: map_enter runs, THEN wmSetupRandomEncounter spawns the
        // group (map.cc:974,978). On a transient encounter map the pending roll lays
        // its critters down here; the critter_p_proc heartbeat then aggros them.
        if (transient && _pendingEncounter is { } pending)
        {
            SpawnEncounter(pending);
            _pendingEncounter = null;
        }

        RebuildLighting();

        _camera.SetWindowSize(Window.ClientBounds.Width, Window.ClientBounds.Height);
        _camera.SetCenter(_dude?.Dude.HexTile ?? _map.Header.EnteringTile);
        _camera.PanX = 0;
        _camera.PanY = 0;

        _baseTitle = $"Hexwaste viewer — {_map.Header.Name} (elevation {_elevation})";
        Window.Title = _baseTitle;

        _audio?.PlayMusic(_mapList.GetMusic(mapName));
        _mapAmbient = _mapList.GetAmbientSfx(mapName); // P34-M5: per-map ambient sfx list
        _ambientTimerMs = AmbientIntervalMs;
        _mapFadeElapsed = 0; // P52-M6: fade the freshly-loaded map in from black
    }

    /// <summary>
    /// Per-map ambient sound effects (P34-M5): on a wall-time countdown, roll a weighted entry
    /// (AmbientSfx.RollIndex), remap birds → crickets at night, and play it. Suppressed in combat
    /// (the engine's ambientSoundEffectEventProcess isInCombat gate). Update/wall-time driven + behind
    /// _audio, so the headless harness (which doesn't pump enough wall-time) never fires it.
    /// </summary>
    private void TickAmbientSfx(double elapsedMs)
    {
        if (_audio is null || _mapAmbient.Count == 0 || _combat.Phase != Formats.Combat.CombatPhase.Idle)
            return;
        _ambientTimerMs -= elapsedMs;
        if (_ambientTimerMs > 0)
            return;
        _ambientTimerMs = AmbientIntervalMs;
        _ambientRng ??= new Random(RngSeed ?? Environment.TickCount);
        int idx = Formats.Map.AmbientSfx.RollIndex(_mapAmbient, total => _ambientRng.Next(0, total + 1));
        if (idx >= 0)
            _audio.PlaySfx(Formats.Map.AmbientSfx.RemapBirdForNight(_mapAmbient[idx].Name, _clock.Hour));
    }

    protected override void Update(GameTime gameTime)
    {
        if (_exitAfterStartupActions)
        {
            if (SaveOnExit)
                SaveGame();
            Exit();
            return;
        }

        _frameClock.Restart();
        KeyboardState keyboard = Keyboard.GetState();
        MouseState mouse = Mouse.GetState();

        // Main menu / character creation: the world idles underneath.
        if (_menu != MenuState.None)
        {
            HandleMenuInput(keyboard);
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        // Game over: the world freezes; load, restart or quit.
        if (_combat.IsGameOver)
        {
            if (IsKeyPressed(keyboard, Keys.F9))
                LoadGame();
            if (IsKeyPressed(keyboard, Keys.N))
                StartNewGame();
            if (keyboard.IsKeyDown(Keys.Escape))
                Exit();
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        // Movie caption card: any key dismisses.
        if (_movieCard is not null)
        {
            if (keyboard.GetPressedKeyCount() > 0 && _previousKeyboard.GetPressedKeyCount() == 0)
                _movieCard = null;
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        // Skill allocator (K): arrows pick a skill, Right/Enter/+ spends a
        // point, Esc/K closes.
        // Perk picker (G from the char sheet when a pick is available): a modal over the sheet.
        if (_perkPickOpen)
        {
            List<int> elig = EligiblePerks();
            for (int i = 0; i < 9 && i < elig.Count; i++)
                if (IsKeyPressed(keyboard, Keys.D1 + i) || IsKeyPressed(keyboard, Keys.NumPad1 + i))
                    ChoosePerk(elig[i]);
            // P29-M5: click a row in the PERKWIN list to take that perk (additive to 1-9).
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
                && PerkPickerRowAt(mouse.X, mouse.Y) is var prow && prow >= 0 && prow < elig.Count)
                ChoosePerk(elig[prow]);
            if (IsKeyPressed(keyboard, Keys.Escape) || IsKeyPressed(keyboard, Keys.G))
                _perkPickOpen = false;

            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        if (_skillAllocOpen)
        {
            if (IsKeyPressed(keyboard, Keys.G) && AvailablePerkPicks() > 0)
                _perkPickOpen = true;
            if (IsKeyPressed(keyboard, Keys.Up))
                _skillAllocIndex = (_skillAllocIndex + Formats.Combat.SkillSet.SkillCount - 1) % Formats.Combat.SkillSet.SkillCount;
            if (IsKeyPressed(keyboard, Keys.Down))
                _skillAllocIndex = (_skillAllocIndex + 1) % Formats.Combat.SkillSet.SkillCount;
            if (IsKeyPressed(keyboard, Keys.Right) || IsKeyPressed(keyboard, Keys.Enter)
                || IsKeyPressed(keyboard, Keys.OemPlus) || IsKeyPressed(keyboard, Keys.Add))
                SpendSkillPoint(_skillAllocIndex);
            if (IsKeyPressed(keyboard, Keys.Escape) || IsKeyPressed(keyboard, Keys.K) || IsKeyPressed(keyboard, Keys.C))
                _skillAllocOpen = false;

            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        // Skilldex (S / SKILLDEX button): click a button (or press 1-8) to arm a skill, Esc/S close.
        if (_skilldexOpen)
        {
            for (int i = 0; i < SkilldexSkills.Length; i++)
                if (IsKeyPressed(keyboard, Keys.D1 + i) || IsKeyPressed(keyboard, Keys.NumPad1 + i))
                {
                    ArmSkill(SkilldexSkills[i]);
                    break;
                }
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
                && SkilldexRowAt(mouse.X, mouse.Y) is var row && row >= 0)
                ArmSkill(SkilldexSkills[row]);
            if (IsKeyPressed(keyboard, Keys.Escape) || IsKeyPressed(keyboard, Keys.S))
                _skilldexOpen = false;

            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        // Pip-Boy (P / PIP button): status page; R opens the rest menu, where 1-9 pick
        // a rest duration. Esc backs out of the rest menu, then closes the panel.
        if (_pipboyOpen)
        {
            // A row click fires the same action its keyboard shortcut does (P15 M3).
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
                && PipboyRowAt(mouse.X, mouse.Y) is var prow && prow >= 0)
            {
                PipboyRows()[prow].OnClick();
            }
            else if (_pipboyRestMenu)
            {
                for (int i = 0; i < RestOptions.Length; i++)
                    if (IsKeyPressed(keyboard, Keys.D1 + i) || IsKeyPressed(keyboard, Keys.NumPad1 + i))
                    {
                        DoRest(RestOptions[i].Minutes);
                        break;
                    }
                if (IsKeyPressed(keyboard, Keys.Escape))
                    _pipboyRestMenu = false;
            }
            else
            {
                if (IsKeyPressed(keyboard, Keys.R))
                    _pipboyRestMenu = true;
                if (IsKeyPressed(keyboard, Keys.A))
                    { _pipboyOpen = false; _automapOpen = true; } // Automaps tab
                if (IsKeyPressed(keyboard, Keys.Escape) || IsKeyPressed(keyboard, Keys.P))
                    _pipboyOpen = false;
            }

            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        // Automap (Pip-Boy → A): a full-window object plot; Esc/A closes.
        if (_automapOpen)
        {
            if (IsKeyPressed(keyboard, Keys.Escape) || IsKeyPressed(keyboard, Keys.A))
                _automapOpen = false;
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        // The called-shot dialog (P49): modal while open — pick a hit location, then resume.
        if (_aimDialogOpen)
        {
            HandleAimDialogInput(mouse, keyboard);
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        // The companion combat-control window (P50): modal while open — cycle the tactics, Esc done.
        if (_tacticsMember is not null)
        {
            HandleTacticsInput(mouse, keyboard);
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        // The multi-slot save/load picker (P48): 0-9 / click a slot to save into / load from it.
        if (_saveLoadOpen)
        {
            int slrow = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
                ? SaveLoadSlotAt(mouse.X, mouse.Y) : -1;
            int keySlot = -1;
            for (int i = 0; i < Formats.SaveSlots.Count; i++)
                if (IsKeyPressed(keyboard, Keys.D0 + i) || IsKeyPressed(keyboard, Keys.NumPad0 + i))
                {
                    keySlot = i;
                    break;
                }
            int pick = slrow >= 0 ? slrow : keySlot;
            if (pick >= 0)
            {
                if (_saveLoadMode == SaveLoadMode.Save)
                {
                    SaveGameToSlot(pick);
                    _saveLoadOpen = false;
                }
                else if (_slotInfos[pick].Occupied && !_slotInfos[pick].VersionMismatch)
                {
                    LoadGameFromSlot(pick);
                    _saveLoadOpen = false;
                }
            }
            if (IsKeyPressed(keyboard, Keys.Escape))
                _saveLoadOpen = false;

            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        // Options / pause menu (Esc or the OPT button): S save, L load, M main menu,
        // Q quit to desktop, Esc/D resume (options.cc showOptions key set).
        if (_optionsOpen)
        {
            // A row click fires the same action its keyboard shortcut does (P15 M3):
            // 0 Save, 1 Load, 2 Main Menu, 3 Quit, 4 Resume.
            int orow = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
                ? OptionsRowAt(mouse.X, mouse.Y) : -1;
            if (IsKeyPressed(keyboard, Keys.S) || orow == 0) { _optionsOpen = false; OpenSaveLoad(SaveLoadMode.Save); }
            else if (IsKeyPressed(keyboard, Keys.L) || orow == 1) { _optionsOpen = false; OpenSaveLoad(SaveLoadMode.Load); }
            else if (IsKeyPressed(keyboard, Keys.M) || orow == 2) { _optionsOpen = false; QuitToMainMenu(); }
            else if (IsKeyPressed(keyboard, Keys.Q) || orow == 3) Exit();
            else if (IsKeyPressed(keyboard, Keys.Escape) || IsKeyPressed(keyboard, Keys.D) || orow == 4) _optionsOpen = false;

            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        // Barter mode: 1-9 buy, Shift+1-9 sell (or click a row), Esc close (back to dialog).
        if (_barterNpc is not null)
        {
            bool shift = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            for (int i = 0; i < 9; i++)
            {
                if (IsKeyPressed(keyboard, Keys.D1 + i) || IsKeyPressed(keyboard, Keys.NumPad1 + i))
                {
                    int gi = _panelPage * ItemRowsPerPage + i;
                    if (shift)
                        BarterSell(gi);
                    else
                        BarterBuy(gi);
                    break;
                }
            }

            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
                TryClickItemPanel(mouse.X, mouse.Y, shift);

            HandlePanelPaging(keyboard);

            if (IsKeyPressed(keyboard, Keys.Escape))
                CloseBarter();

            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        // Loot/inventory mode: number keys take/drop (or click a row), A take-all, Esc/I close.
        if (_lootContainer is not null || _inventoryOpen)
        {
            bool shiftHeld = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
            for (int i = 0; i < 9; i++)
            {
                if (IsKeyPressed(keyboard, Keys.D1 + i) || IsKeyPressed(keyboard, Keys.NumPad1 + i))
                {
                    int gi = _panelPage * ItemRowsPerPage + i;
                    if (_tradePartner is not null && shiftHeld)
                    {
                        GiveToFollower(gi); // trade give-side: Shift+1-9 → the follower
                    }
                    else if (_lootContainer is not null)
                    {
                        TakeFromContainer(gi);
                    }
                    else if (shiftHeld)
                    {
                        DropFromInventory(gi);
                    }
                    else if (keyboard.IsKeyDown(Keys.U))
                    {
                        // U+number: arm "use this item on the next clicked object"
                        if (gi < _dudeInventory.Count)
                        {
                            _pendingUseItem = _dudeInventory[gi];
                            _inventoryOpen = false;
                            Log($"Use the {ObjectName(_pendingUseItem)} on what?");
                        }
                    }
                    else
                    {
                        UseInventoryItem(gi);
                    }
                    break;
                }
            }

            // The pure-inventory panel uses drag-and-drop equip (P47); loot/trade keep click-on-
            // press (they transfer, not equip). A row TAP still falls back to click-to-use inside
            // the drag handler, so click-to-equip is preserved.
            if (_inventoryOpen && _lootContainer is null && _tradePartner is null)
                HandleInventoryDrag(mouse, shiftHeld);
            else if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
                TryClickItemPanel(mouse.X, mouse.Y, shiftHeld);

            HandlePanelPaging(keyboard);

            if (_lootContainer is not null && IsKeyPressed(keyboard, Keys.A))
                TakeAllFromContainer();

            if (IsKeyPressed(keyboard, Keys.Escape) || IsKeyPressed(keyboard, Keys.I))
            {
                _lootContainer = null;
                _inventoryOpen = false;
                _tradePartner = null;
            }

            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        if (IsKeyPressed(keyboard, Keys.I))
        {
            _inventoryOpen = true;
            _panelPage = 0;
            PrewarmItemTextures(_dudeInventory);
        }

        // C or K opens the character sheet (spend banked points with K's panel).
        if ((IsKeyPressed(keyboard, Keys.C) || IsKeyPressed(keyboard, Keys.K)) && _dudeGcd is not null)
            _skillAllocOpen = true;

        // S opens the Skilldex use-skill picker (engine KEY_LOWERCASE_S).
        if (IsKeyPressed(keyboard, Keys.S))
            _skilldexOpen = true;

        // P opens the Pip-Boy (engine KEY_LOWERCASE_P).
        if (IsKeyPressed(keyboard, Keys.P))
            _pipboyOpen = true;

        // Z rests to heal (when it's safe).
        if (IsKeyPressed(keyboard, Keys.Z))
            RestToHeal();

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

        // Companion-control hub swallows input (phase-10 M4).
        if (_companionHub is not null)
        {
            for (int i = 0; i < _hubOptions.Count; i++)
                if (IsKeyPressed(keyboard, Keys.D1 + i) || IsKeyPressed(keyboard, Keys.NumPad1 + i))
                {
                    ChooseCompanionOption(i);
                    break;
                }
            if (_companionHub is not null && mouse.LeftButton == ButtonState.Pressed
                && _previousMouse.LeftButton == ButtonState.Released)
            {
                int hit = HitTestDialogOption(mouse.X, mouse.Y);
                if (hit >= 0)
                    ChooseCompanionOption(hit);
            }
            if (_companionHub is not null && IsKeyPressed(keyboard, Keys.Escape))
                _companionHub = null;

            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        // Detected-encounter avoid prompt (phase-16 M1): Y engages, N avoids and travels
        // on. Drawn over the worldmap; it must intercept before the worldmap's click-to-
        // travel below.
        if (_encounterPrompt is { } prompt)
        {
            if (IsKeyPressed(keyboard, Keys.Y))
            {
                _encounterPrompt = null;
                EngageEncounter(prompt.Enc, prompt.MapFile, prompt.Name);
            }
            else if (IsKeyPressed(keyboard, Keys.N) || IsKeyPressed(keyboard, Keys.Escape))
            {
                _encounterPrompt = null;
                Log($"You avoid {prompt.Name ?? "the encounter"} and travel on.");
                TravelTo(prompt.Dest); // resume the leg from the encounter point
            }
            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        // Worldmap mode swallows map input.
        if (_worldmapOpen)
        {
            // Phase-17 M2: while the dot is moving, Esc/click HALTS travel (stay put on the
            // worldmap); a fresh click then re-routes. Esc with no travel closes the map.
            if (_activeTravel is not null)
            {
                bool click = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
                if (click || IsKeyPressed(keyboard, Keys.Escape))
                {
                    _activeTravel = null;
                    Log("You stop to get your bearings.");
                }
            }
            else
            {
                if (IsKeyPressed(keyboard, Keys.Escape) || IsKeyPressed(keyboard, Keys.M))
                    _worldmapOpen = false;

                _hoveredArea = _worldmapScreen?.HitTest(mouse.X, mouse.Y, GraphicsDevice.Viewport.Bounds, WorldFog);
                if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
                    && _hoveredArea is not null)
                    TravelTo(_hoveredArea);
            }

            _previousMouse = mouse;
            _previousKeyboard = keyboard;
            base.Update(gameTime);
            return;
        }

        if (IsKeyPressed(keyboard, Keys.M))
            _worldmapOpen = true;

        // Esc opens the options/pause menu (engine-faithful; the panel offers Quit).
        if (IsKeyPressed(keyboard, Keys.Escape))
            _optionsOpen = true;

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

        if (IsKeyPressed(keyboard, Keys.F4))
            _roofsVisible = !_roofsVisible;

        if (IsKeyPressed(keyboard, Keys.T))
            ToggleWalkMode();

        // L: lockpick the hovered door.
        if (IsKeyPressed(keyboard, Keys.L) && _hoveredObject is { } lockTarget && IsDoor(lockTarget))
            TryLockpick(lockTarget);

        // V: open the called-shot dialog (P49 — replaces the cycle; pick a hit location by
        // click / 1-9). The location feeds the unchanged TryAttack(target, AimLocation) path.
        if (IsKeyPressed(keyboard, Keys.V))
            OpenAimDialog();

        // F: attack the hovered critter at the current aim location, using the selected
        // weapon mode (P15 M1 — burst when the slot is set to BURST on a burst gun).
        if (IsKeyPressed(keyboard, Keys.F) && _hoveredObject is { } attackTarget)
        {
            if (_weaponMode == WeaponMode.Burst)
                _combat.TryBurst(attackTarget);
            else
                _combat.TryAttack(attackTarget, AimLocation);
        }

        // N: cycle the weapon-slot attack mode (single↔burst), same as clicking the slot.
        if (IsKeyPressed(keyboard, Keys.N))
            CycleWeaponMode();

        // B: spray a burst at the hovered critter (only fires if a burst-capable gun
        // is equipped — the SMG/Tommy Gun/Combat Shotgun; #9).
        if (IsKeyPressed(keyboard, Keys.B) && _hoveredObject is { } burstTarget)
            _combat.TryBurst(burstTarget);

        // Space ends the player's combat turn.
        if (IsKeyPressed(keyboard, Keys.Space))
            _combat.EndPlayerTurn();

        // R reloads the equipped gun (2 AP during your combat turn); Shift+R unloads it (eject ammo to
        // the bag, P40 — needed to switch ammo type). roofs moved to F4.
        if (IsKeyPressed(keyboard, Keys.R))
        {
            if (keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift))
                UnloadEquippedWeapon();
            else
                _combat.ReloadEquippedWeapon();
        }

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
        AdvanceProjectiles(gameTime.ElapsedGameTime.TotalMilliseconds);
        _combat.Step();
        _dude?.Update(gameTime.ElapsedGameTime.TotalMilliseconds);
        UpdateAmbientLife(gameTime.ElapsedGameTime.TotalMilliseconds);
        TickAmbientSfx(gameTime.ElapsedGameTime.TotalMilliseconds); // P34-M5 ambient sfx
        _floatText.Tick(gameTime.ElapsedGameTime.TotalMilliseconds); // P45 floating combat text

        // Script timers: pumped only here — dialog/loot/worldmap modes return
        // earlier in Update, matching the engine's _gdialogActive() gate.
        _scriptHost?.PumpTimers(gameTime.ElapsedGameTime.TotalMilliseconds, _dude?.Dude);
        PumpCritterProcs(gameTime.ElapsedGameTime.TotalMilliseconds);

        // Map transitions are queued by exit grids/stairs and applied here,
        // never while DudeController.Update is still on the stack.
        if (_pendingTransition is { } transition)
        {
            _pendingTransition = null;
            ApplyTransition(transition);
        }

        // Phase-16 M2: leaving an encounter map mid-journey auto-resumes the leg toward
        // the original destination (the engine's isWalking) — no worldmap re-click.
        if (_resumeTravelDest is { } resume)
        {
            _resumeTravelDest = null;
            TravelTo(resume);
        }

        // Phase-17 M2: animate the worldmap dot — drain TravelLeg.Step() over wall-time,
        // paced by terrain (mountains slow it). Paused while an avoid prompt is up.
        StepAnimatedTravel(gameTime.ElapsedGameTime.TotalMilliseconds);

        if (_cycler.Update(gameTime.ElapsedGameTime.TotalMilliseconds))
        {
            _frmCache.OnPaletteChanged(_palette);
            _paletteUploads++;
        }

        UpdateClock(gameTime.ElapsedGameTime.TotalMilliseconds);
        UpdateHudRoll(gameTime.ElapsedGameTime.TotalMilliseconds);

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
            // A click on a HUD bar button (INV/OPT/MAP/CHA/PIP/SKILLDEX) is consumed
            // there and does not also walk/interact with the map underneath (#15 M4).
            if (TryClickInterfaceBar(mouse.X, mouse.Y))
            {
                // handled by the bar
            }
            else if (_pendingUseSkill is { } armedSkill)
            {
                // Armed Skilldex skill: apply to the hovered target, else to the dude
                // himself (self-heal / self-skill is a click on empty ground).
                MapObject? skillTarget = _hoveredObject is not null && _hoveredObject != _dude?.Dude
                    ? _hoveredObject : _dude?.Dude;
                _pendingUseSkill = null;
                if (skillTarget is not null)
                    TryUseSkillOn(armedSkill, skillTarget);
            }
            else if (_pendingUseItem is { } useItem && _hoveredObject is not null && _hoveredObject != _dude?.Dude)
                UseItemOn(useItem, _hoveredObject);
            else if (_hoveredObject is not null && _hoveredObject != _dude?.Dude)
                InteractWith(_hoveredObject);
            else if (_dude is not null)
            {
                int target = _camera.ScreenToHex(mouse.X, mouse.Y);
                // Phase-18 M0: in combat a move needs AP for at least the first hex.
                if (_combat.Phase != Formats.Combat.CombatPhase.Idle
                    && GetCritterState(_dude.Dude) is { } walkStats
                    && _combat.DudeAp < Formats.Combat.CritterState.MovePointCost(_dude.Dude.CombatResults))
                    Log("Not enough action points to move.");
                else if (target >= 0 && !_dude.WalkTo(target))
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
        // hmjmps/hfjmps (the engine's vault-suit default per gender — art.cc
        // _art_vault_person_nums[JUMPSUIT][gender]) ship every weapon anim set;
        // hmwarr only had unarmed+spear (phase-7 track A). Gender = gcd
        // baseStats[34] (STAT_GENDER: 0 male, 1 female).
        bool female = _dudeGcd?.Stats.BaseStats[34] == 1;
        string dudeArt = female ? "hfjmps" : "hmjmps";
        int critterIndex = _artIndex.FindCritterIndex(dudeArt);
        if (critterIndex < 0 && female)
            critterIndex = _artIndex.FindCritterIndex("hmjmps"); // fallback
        if (critterIndex < 0)
        {
            Console.Error.WriteLine($"{dudeArt} not found in critters.lst — no dude");
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

        // Combat numbers come from the dude's gcd sheet (the critter proto is
        // the fallback). Read the gcd directly here: GetCritterState keys on
        // _dude, which isn't assigned until below — without this the dude took
        // the generic proto's 30 HP regardless of his SPECIAL.
        Formats.Combat.CritterState? stats = _dudeGcd is not null
            ? new Formats.Combat.CritterState(dude, _dudeGcd.Stats, _dudeGcd.TaggedSkills, _dudeGcd.Traits, _dudePerkRanks)
            : GetCritterState(dude);
        if (stats is not null)
        {
            dude.CurrentHp = stats.MaxHp;
            _combat.SetDudeAp(stats.MaxActionPoints);
        }
        _hudDisplayedHp = _hudDisplayedAc = -1; // snap the HUD counters to the new dude

        // Carry the bag over and alias it to the new dude object so scripts
        // (caps payments, inventory checks) and panels share one pocket.
        foreach (MapObject item in _dudeInventory)
            dude.Inventory.Add(item);
        _dudeInventory = dude.Inventory;

        RebuildBlockedTiles(dude);
        _dude = new DudeController(dude, _frmCache, tile => _blockedTiles.Contains(tile),
            () => DudeMovementAnimCode(dude)); // P34-M3: run by default (the 3 engine guards), NPC walkers keep walking
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

            _scriptHost?.RunSpatialsAt(_map, tile, _elevation, dude);
            CheckExitGridAt(tile);
            RevealAround(tile); // automap fog reveals as the dude explores (P20-M2)

            // Phase-18 M0: in combat, each hex crossed costs MovePointCost AP (1 normally,
            // 4/8 with a crippled leg — P14-M3); halt the walk once the next hex is
            // unaffordable. Out of combat, movement is free (the PoC has no AP model there).
            if (_combat.Phase != Formats.Combat.CombatPhase.Idle && GetCritterState(dude) is { } st)
            {
                _combat.SpendDudeAp(Formats.Combat.CritterState.MovePointCost(dude.CombatResults));
                if (_combat.DudeAp < Formats.Combat.CritterState.MovePointCost(dude.CombatResults))
                    _dude?.Stop();
            }
        };

        InsertSorted(_solidObjects[_elevation], dude);
        _camera.SetCenter(dude.HexTile);
        RevealAround(dude.HexTile); // automap fog: reveal the spawn surroundings (P20-M2)
    }

    private const int PerkSilentRunning = 15; // PERK_SILENT_RUNNING (perk_defs.h)

    /// <summary>
    /// The dude's movement anim-code (walk/run) under the engine's 3 run guards (P34-M3).
    /// ported from fallout2-ce src/animation.cc animationRegisterRunToTile() via RunGuard.MovementAnimCode.
    /// The run-art existence check uses the dude's ACTUAL weapon-code FID (the engine checks weaponCode 0;
    /// matching the loaded FID is faithful-enough and avoids a try-run-then-fail-to-load — documented).
    /// </summary>
    private int DudeMovementAnimCode(MapObject dude)
    {
        int runFid = Fid.Build(ObjectType.Critter, Fid.Index(dude.Fid),
            Formats.Combat.RunGuard.AnimRunning, Fid.WeaponCode(dude.Fid));
        bool runArtExists = _vfs.Exists(_artIndex.GetFrmPath(runFid));
        bool silentRunning = Formats.Perks.PerkRules.Rank(_dudePerkRanks, PerkSilentRunning) > 0;
        return Formats.Combat.RunGuard.MovementAnimCode(dude.CombatResults, _sneak.FlagSet, silentRunning, runArtExists);
    }

    private static void InsertSorted(List<MapObject> objects, MapObject obj)
    {
        int index = objects.FindIndex(o => o.HexTile > obj.HexTile);
        objects.Insert(index < 0 ? objects.Count : index, obj);
    }

    /// <summary>critter_attempt_placement (interpreter_extra.cc:2812 → _obj_attempt_placement): relocate
    /// an object to a tile (or a free tile near it, Placement.FreeTileNear) on an elevation, re-sorting
    /// the draw lists + rebuilding blocking. Used by map_enter scripts that position critters; on the
    /// shippable slice denbus2 calls it for a same-tile placement (a no-op) — it lights up for real when
    /// a script moves a critter to a different tile. The free-tile search uses the CURRENT elevation's
    /// blocking (approximate for off-screen elevations — a documented simplification).</summary>
    private bool PlaceObject(MapObject obj, int tile, int elevation)
    {
        if (!Formats.Hex.HexGrid.IsValid(tile))
            return false;
        // Pull it off every elevation's draw list (an object's elevation is implicit in which list holds
        // it), then rebuild blocking so its old tile frees before we pick the destination.
        for (int e = 0; e < MapFile.ElevationCount; e++)
        {
            _flatObjects[e]?.Remove(obj);
            _solidObjects[e]?.Remove(obj);
        }
        RebuildBlockedTiles(_dude?.Dude);
        obj.HexTile = Formats.Map.Placement.FreeTileNear(tile, t => _blockedTiles.Contains(t));
        int dest = elevation is >= 0 and < MapFile.ElevationCount ? elevation : _elevation;
        InsertSorted(obj.IsFlat ? _flatObjects[dest] : _solidObjects[dest], obj);
        RebuildBlockedTiles(_dude?.Dude);
        return true;
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

        // A script's set_light_level pins the ambient (AmbientFixed) — PRESERVE it across a rebuild,
        // exactly as the day/night clock already does (ViewerGame.cs:8606). Without this, the
        // RebuildLighting after map_enter/map_update clobbered the script ambient back to the
        // InitialAmbient default, so a non-max set_light_level was lost — a latent P21 bug that only
        // hid because every shipped value happened to be max. Fixing it lets arcaves' map_update
        // cavern level (50 -> 40960) actually dim the cave (P46).
        int pinnedAmbient = _lightGrid.Ambient;
        _lightGrid.Reset();
        _lightGrid.Ambient = AmbientFixed
            ? pinnedAmbient
            : (int)Math.Clamp(InitialAmbient * Formats.Light.LightGrid.IntensityMax,
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

        // No wandering or fidgeting while combat owns the choreography.
        if (_combat.Phase != Formats.Combat.CombatPhase.Idle)
            return;

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

    /// <summary>Starts a script- or ambient-driven NPC walk (shared walker plumbing).</summary>
    private bool StartNpcWalk(MapObject npc, int target)
    {
        if (npc == _dude?.Dude || _npcWalkers.ContainsKey(npc)
            || Fid.Type(npc.Fid) is not ObjectType.Critter)
            return false;

        int walkFid = Fid.Build(ObjectType.Critter, Fid.Index(npc.Fid), 1, Fid.WeaponCode(npc.Fid));
        if (!_vfs.Exists(_artIndex.GetFrmPath(walkFid)))
        {
            walkFid = Fid.Build(ObjectType.Critter, Fid.Index(npc.Fid), 1);
            if (!_vfs.Exists(_artIndex.GetFrmPath(walkFid)))
                return false;
        }

        if (target == npc.HexTile || _blockedTiles.Contains(target))
            return false;

        var walker = new DudeController(npc, _frmCache, tile => _blockedTiles.Contains(tile));
        int previousTile = npc.HexTile;
        walker.TileChanged += tile =>
        {
            _blockedTiles.Remove(previousTile);
            _blockedTiles.Add(tile);
            previousTile = tile;
            _scriptHost?.RunSpatialsAt(_map, tile, _elevation, npc);
            List<MapObject> solids = _solidObjects[_elevation];
            solids.Remove(npc);
            InsertSorted(solids, npc);
        };

        if (!walker.WalkTo(target))
            return false;
        _npcWalkers[npc] = walker;
        return true;
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

        if (!DudeCanCarry(ItemAddedWeight(item, item.StackCount))) // P24 (item.cc:313)
        {
            Log("You can't carry that much weight.");
            return;
        }

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

    // --- Encumbrance (P24) -------------------------------------------------

    private Formats.Proto.ProtoInfo? SafeProto(int pid)
    {
        try { return _protos.Get(pid); }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException) { return null; }
    }

    /// <summary>The critter at a hex on the current elevation, or null — the shared lookup the harness
    /// handlers use. <paramref name="aliveOnly"/> skips corpses; <paramref name="includeFlat"/> also
    /// searches the flat list (where dead/NO_BLOCK critters live, for the corpse-aware probes).</summary>
    private MapObject? CritterAt(int hex, bool aliveOnly = false, bool includeFlat = false)
    {
        IEnumerable<MapObject> src = includeFlat
            ? _solidObjects[_elevation].Concat(_flatObjects[_elevation])
            : _solidObjects[_elevation];
        return src.FirstOrDefault(o => o.HexTile == hex
            && Fid.Type(o.Fid) is ObjectType.Critter && (!aliveOnly || !o.IsDead));
    }

    /// <summary>The dude's total carried weight in pounds (P24; InventoryWeight over _dudeInventory).</summary>
    private int DudeCarriedWeight() => Formats.Map.InventoryWeight.TotalWeight(_dudeInventory, SafeProto);

    /// <summary>The dude's carry capacity = STAT_CARRY_WEIGHT (25*ST+25); 0 if unresolved.</summary>
    private int DudeCarryCapacity() =>
        _dude is not null && GetCritterState(_dude.Dude) is { } s ? s.CarryWeight : 0;

    /// <summary>The weight one item (incl. its stack) would add to the bag.</summary>
    private int ItemAddedWeight(MapObject item, int count) =>
        Formats.Map.InventoryWeight.ItemWeight(item, SafeProto) * Math.Max(count, 1);

    /// <summary>Faithful pickup gate (item.cc:313 — currentWeight + add &gt; maxWeight refuses):
    /// can the dude take <paramref name="extra"/> more pounds? Capacity ≤ 0 (unresolved) never
    /// blocks. The refusal message is logged by the caller.</summary>
    private bool DudeCanCarry(int extra)
    {
        int cap = DudeCarryCapacity();
        return cap <= 0 || DudeCarriedWeight() + extra <= cap;
    }

    /// <summary>ICombatHost (P24): the dude's over-encumbrance max-AP penalty for this turn
    /// (stat.cc:198). 0 when within capacity, so an un-overloaded dude is unchanged.</summary>
    public int DudeEncumbranceApPenalty() =>
        Formats.Map.InventoryWeight.ActionPointPenalty(DudeCarriedWeight(), DudeCarryCapacity());

    /// <summary>ICombatHost (P28-M3): the dude's rank in a perk — drives the combat perk effects
    /// (Bonus Rate of Fire, Sniper, Slayer, Sharpshooter). 0 by default → inert.</summary>
    public int DudePerkRank(int perk) => Formats.Perks.PerkRules.Rank(_dudePerkRanks, perk);

    /// <summary>ICombatHost (P29-M1): the dude's selected optional traits — drives the combat-path
    /// trait effects (One Hander, Fast Shot, Finesse, Jinxed). False by default → inert.</summary>
    public bool DudeHasTrait(int trait) =>
        _dudeGcd is { } g && Formats.Combat.TraitModifiers.Has(g.Traits, trait);

    /// <summary>ICombatHost (P30 A-M1): the dude's sneaking FLAG — gates the Silent Death backstab.</summary>
    public bool DudeSneakFlag => _sneak.FlagSet;

    /// <summary>One periodic SKILL_SNEAK roll (P30 A-M2; critter.cc:1195 sneakEventProcess): a d100 vs the
    /// dude's Sneak skill on the isolated _sneakRng sets Working and reschedules the next re-check. Run on
    /// flag-enable and on the heartbeat timer.</summary>
    private void RollSneak()
    {
        _sneakRng ??= new Formats.Combat.SystemCombatRng(RngSeed ?? Environment.TickCount);
        int skill = DudeSkillValue(8); // SKILL_SNEAK
        bool success = _sneakRng.Next(1, 101) <= skill;
        _sneak.Working = success;
        _sneakTicksRemaining = Formats.Combat.SneakState.RescheduleTicks(skill, success);
    }

    /// <summary>True if <paramref name="npc"/> perceives the dude (P30 A-M3; isWithinPerception): the
    /// NPC's Perception + facing + distance vs the dude's (sneak-reduced) detection range. Used to gate
    /// scripted aggro so an actively-sneaking dude can go unnoticed.</summary>
    private bool DudePerceivedBy(MapObject npc)
    {
        if (_dude is null)
            return true;
        int perception = GetCritterState(npc)?.Perception ?? 5;
        int dudeTile = _dude.Dude.HexTile;
        bool canSee = Formats.Combat.PerceptionDetect.CanSee(npc.Rotation, npc.HexTile, dudeTile);
        int distance = Formats.Hex.HexGrid.Distance(npc.HexTile, dudeTile);
        bool inCombat = _combat.Phase != Formats.Combat.CombatPhase.Idle;
        return Formats.Combat.PerceptionDetect.IsWithinPerception(distance, perception, DudeSkillValue(8),
            canSee, targetIsGlass: false, targetIsDude: true, _sneak.IsSneaking, _sneak.FlagSet, inCombat);
    }

    private void TakeFromContainer(int index)
    {
        if (_lootContainer is null || index < 0 || index >= _lootContainer.Inventory.Count)
            return;
        MapObject item = _lootContainer.Inventory[index];
        if (!DudeCanCarry(ItemAddedWeight(item, item.StackCount))) // P24 (item.cc:313)
        {
            Log("You can't carry that much weight.");
            return;
        }
        _lootContainer.Inventory.RemoveAt(index);
        AddToDudeInventory(item);
        Log($"You take: {ObjectName(item)}{(item.StackCount > 1 ? $" x{item.StackCount}" : "")}.");
    }

    /// <summary>Loot every item from the open container (P24): all-or-nothing on weight, like
    /// the engine's "take all" (inventory.cc:4360 — refuses the whole grab if it won't fit), which
    /// also avoids the per-item gate spinning the loop. Closes the loot panel.</summary>
    private void TakeAllFromContainer()
    {
        if (_lootContainer is null)
            return;
        int total = Formats.Map.InventoryWeight.TotalWeight(_lootContainer.Inventory, SafeProto);
        if (!DudeCanCarry(total))
        {
            Log("You can't carry that much weight.");
            return;
        }
        while (_lootContainer.Inventory.Count > 0)
            TakeFromContainer(0);
        _lootContainer = null;
    }

    private void DropFromInventory(int index)
    {
        if (_dude is null || index < 0 || index >= _dudeInventory.Count)
            return;
        MapObject item = _dudeInventory[index];
        _dudeInventory.RemoveAt(index);
        UnequipForTransfer(item);
        item.HexTile = _dude.Dude.HexTile;
        _map.Elevations[_elevation]?.Objects.Add(item);
        OnScriptObjectPlaced(item);
        Log($"You drop: {ObjectName(item)}.");
    }

    /// <summary>Undo any equip an item carried before it leaves the dude's bag (give /
    /// drop): reverse the worn-armor AC/DT/DR bonus and clear the equip flags, so the
    /// dude can't keep the protection of armor they no longer hold (phase-10 M5 review).
    /// Mirrors the UseInventoryItem take-off path + the barter TransferOne flag strip.</summary>
    private void UnequipForTransfer(MapObject item)
    {
        if (item.IsWorn)
        {
            try
            {
                if (_protos.Get(item.Pid).Armor is { } armor)
                    ApplyArmorBonus(armor, -1);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
            {
            }
        }
        item.Flags &= ~(MapObject.FlagInLeftHand | MapObject.FlagInRightHand | MapObject.FlagWorn);
    }

    /// <summary>Inventory "use": weapons toggle the right hand, armor toggles
    /// worn (equip-time bonus-stat mutation — inventory.cc _adjust_ac), drugs
    /// are consumed (_perform_drug_effect's HP path).</summary>
    private void UseInventoryItem(int index)
    {
        if (index < 0 || index >= _dudeInventory.Count)
            return;
        MapObject item = _dudeInventory[index];
        ProtoInfo proto;
        try
        {
            proto = _protos.Get(item.Pid);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            return;
        }

        if (proto.Weapon is not null)
        {
            bool equip = !item.IsInHand;
            foreach (MapObject other in _dudeInventory)
                other.Flags &= ~(MapObject.FlagInLeftHand | MapObject.FlagInRightHand);
            if (equip)
                item.Flags |= MapObject.FlagInRightHand;
            Log(equip ? $"You ready the {ObjectName(item)}." : $"You put away the {ObjectName(item)}.");
            Console.WriteLine($"equip: {ObjectName(item)} {(equip ? "readied" : "stowed")}");
            return;
        }

        if (proto.Armor is not null)
        {
            if (item.IsWorn)
            {
                ApplyArmorBonus(proto.Armor, -1);
                item.Flags &= ~MapObject.FlagWorn;
                Log($"You take off the {ObjectName(item)}.");
            }
            else
            {
                foreach (MapObject other in _dudeInventory.Where(o => o.IsWorn))
                {
                    try
                    {
                        if (_protos.Get(other.Pid).Armor is { } oldArmor)
                            ApplyArmorBonus(oldArmor, -1);
                    }
                    catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
                    {
                    }
                    other.Flags &= ~MapObject.FlagWorn;
                }

                item.Flags |= MapObject.FlagWorn;
                ApplyArmorBonus(proto.Armor, +1);
                Log($"You put on the {ObjectName(item)}.");
            }

            if (GetCritterState(_dude!.Dude) is { } stats)
                Console.WriteLine($"armor: AC={stats.ArmorClass} DT={stats.DamageThreshold} DR={stats.DamageResistance}");
            return;
        }

        if (proto.Drug is not null)
        {
            UseDrug(item, proto.Drug);
            return;
        }

        // P39: skill books (item.cc booksInitVanilla + proto_instance.cc _obj_use_book). A book raises
        // its skill by (100 − effective)/10 points (×1.5 with Comprehension), nothing once effective
        // hits 100, at a game-time cost of 3600*(11−INT)s. The screen fade + scriptsExecMapUpdateProc
        // the engine also runs here are out of scope (no palette fade; map_update_p_proc unwired).
        if (Formats.Item.SkillBooks.TryGet(item.Pid, out int bookSkill, out _) && _dude is not null && _dudeGcd is not null)
        {
            if (_combat.Phase != Formats.Combat.CombatPhase.Idle)
            {
                Log("You can't do that in combat."); // proto.msg 902 — refuse mid-combat
                Console.WriteLine($"book: pid={item.Pid} skill={bookSkill} refused=combat");
                return;
            }
            int effective = GetCritterState(_dude.Dude)?.SkillValue(bookSkill) ?? 0;
            bool comprehension = DudePerkRank(Formats.Perks.PerkId.Comprehension) > 0;
            int increase = Formats.Item.SkillBooks.Increase(effective, comprehension);
            if (increase > 0)
                _dudeGcd.Stats.Skills[bookSkill] += increase; // skillAddForce ×increase (base points)
            int intelligence = GetCritterState(_dude.Dude)?.Stat(Formats.Combat.CritterStat.Intelligence) ?? 5;
            _clock.Ticks += (long)Formats.Item.SkillBooks.ReadSeconds(intelligence) * Formats.GameClock.TicksPerSecond;
            int after = GetCritterState(_dude.Dude)?.SkillValue(bookSkill) ?? 0;
            Log(increase > 0 ? "You learn something new." : "You can't learn anything more from this book.");
            Console.WriteLine($"book: pid={item.Pid} skill={bookSkill} before={effective} increase={increase} after={after}");
            item.StackCount--;
            if (item.StackCount <= 0)
                _dudeInventory.Remove(item);
            return;
        }

        // P40: using an ammo box reloads the equipped weapon with THAT ammo type (the player's type
        // selection — the engine's drag-ammo-onto-weapon). The no-mixed-mags rule blocks a swap into a
        // loaded weapon of a different type → hint to unload (Shift+R) first.
        if (proto.Ammo is not null && _dude is not null)
        {
            (ProtoInfo? wp, MapObject? wi) = EquippedWeapon(_dude.Dude);
            if (wp?.Weapon is not null && wi is not null)
            {
                if (!TryReloadWith(_dude.Dude, wp, wi, item.Pid))
                    Log($"Can't load that type — unload the {ObjectNameByPid(wp.Pid)} first (Shift+R).");
                return;
            }
            Log("You have no compatible weapon equipped.");
            return;
        }

        Log($"You can't use the {ObjectName(item)} that way.");
    }

    /// <summary>Equipping armor mutates bonus stats (inventory.cc:2544
    /// _adjust_ac); index 0 of DT/DR = normal damage.</summary>
    private void ApplyArmorBonus(ArmorProtoStats armor, int sign)
    {
        if (_dudeGcd is null)
            return;
        int[] bonus = _dudeGcd.Stats.BonusStats;
        bonus[Formats.Combat.CritterStat.ArmorClass] += sign * armor.ArmorClass;
        bonus[Formats.Combat.CritterStat.DamageThreshold] += sign * armor.DamageThreshold[0];
        bonus[Formats.Combat.CritterStat.DamageResistance] += sign * armor.DamageResistance[0];
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

        // A live recruited or dismissed companion opens the control hub, not scripted
        // dialog (phase-10 M4) — their scripted dialog is still reachable from the hub's
        // "Talk to them" option. A dead one falls through (its corpse is lootable).
        if (!npc.IsDead && (_scriptHost.PartyMembers.Contains(npc) || _dismissedCompanions.ContainsKey(npc)))
        {
            OpenCompanionHub(npc);
            return;
        }

        OpenScriptedDialog(npc);
    }

    /// <summary>Run a critter's talk_p_proc and show the dialog (or floaters).</summary>
    private void OpenScriptedDialog(MapObject npc)
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
            _dialogNpc = npc;
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

        // gdialog_barter fired inside the option proc: the queued reply is
        // already computed; the trade window opens on top of the dialog and
        // the session's fate (queued node vs end) resolves in CloseBarter.
        if (_dialog.TakeBarterRequest(out int barterModifier) && _dialogNpc is not null)
        {
            OpenBarter(_dialogNpc, barterModifier);
            if (_barterNpc is not null)
                return;
        }

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

    /// <summary>Opens the trade window (engine: inventoryOpenTrade after the
    /// option proc returns). CRITTER_BARTER (proto critter flags bit 0x02)
    /// gates who trades at all (game_dialog.cc:4272).</summary>
    private void OpenBarter(MapObject npc, int modifier)
    {
        try
        {
            if ((_protos.Get(npc.Pid).Critter?.CritterFlags & 0x02) == 0)
            {
                Log($"{ObjectName(npc)} refuses to barter.");
                return;
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            return;
        }

        _barterNpc = npc;
        _panelPage = 0;
        // Stock lives in the shop box once the talk epilogue has run; loose
        // merchants (no box choreography) trade from their own pockets.
        _barterStock = _dialog?.StockBox ?? npc;
        _barterModifier = modifier;
        PrewarmItemTextures(_barterStock.Inventory);
        PrewarmItemTextures(_dudeInventory);
        Console.WriteLine($"barter: open with {ObjectName(npc)} (mod {modifier},"
            + $" npcSkill {NpcBarterSkill(npc)}, dudeSkill {DudeBarterSkill()},"
            + $" npcCaps {_scriptHost?.CapsTotal(_barterStock) ?? 0}, dudeCaps {DudeCaps()})");
    }

    private void CloseBarter()
    {
        _barterNpc = null;
        _barterStock = null;
        Console.WriteLine("barter: closed");
        if (_dialog is not null)
        {
            PrintDialogRound(); // the queued post-barter node (may be a closer)
            if (!_dialog.Active)
            {
                Log("[conversation ends]");
                Console.WriteLine("[conversation ends]");
                _dialog = null;
            }
        }
    }

    private int DudeBarterSkill() =>
        _dude is not null && GetCritterState(_dude.Dude) is { } stats
            ? Formats.Combat.BarterMath.BarterSkill(stats) : 0;

    private int NpcBarterSkill(MapObject npc) =>
        GetCritterState(npc) is { } stats ? Formats.Combat.BarterMath.BarterSkill(stats) : 0;

    private int DudeCaps() => _dude is not null ? _scriptHost?.CapsTotal(_dude.Dude) ?? 0 : 0;

    /// <summary>Merchant goods the panel offers (caps trade only as balance).</summary>
    private List<MapObject> BarterStock() =>
        _barterStock is { } stock
            ? [.. stock.Inventory.Where(i => i.Pid != Formats.Int.ScriptHost.MoneyPid)]
            : [];

    private List<MapObject> BarterGoods() =>
        [.. _dudeInventory.Where(i => i.Pid != Formats.Int.ScriptHost.MoneyPid)];

    private int BarterBuyPrice(MapObject item)
    {
        int cost;
        try
        {
            cost = _protos.Get(item.Pid).Cost;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            return 0;
        }
        return Formats.Combat.BarterMath.BuyPrice(cost, _barterModifier,
            _barterNpc is { } npc ? NpcBarterSkill(npc) : 0, DudeBarterSkill());
    }

    private int BarterSellPrice(MapObject item)
    {
        try
        {
            return Formats.Combat.BarterMath.SellPrice(_protos.Get(item.Pid).Cost);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            return 0;
        }
    }

    /// <summary>Buy one unit of the merchant's i-th item at the marked-up
    /// price; refusal mirrors inventry.msg {28}.</summary>
    private void BarterBuy(int index)
    {
        if (_barterNpc is not { } npc || _dude is null || _scriptHost is null)
            return;
        List<MapObject> stock = BarterStock();
        if (index < 0 || index >= stock.Count)
            return;

        MapObject item = stock[index];
        MapObject till = _barterStock ?? npc;
        if (!DudeCanCarry(ItemAddedWeight(item, 1))) // P24 — one unit per buy (inventory.cc:4706)
        {
            Log("You can't carry that much weight.");
            return;
        }
        int price = BarterBuyPrice(item);
        if (_scriptHost.CapsAdjust(_dude.Dude, -price) != 0)
        {
            Log("No, your offer is not good enough."); // inventry.msg {28}
            return;
        }

        _scriptHost.CapsAdjust(till, price);
        TransferOne(item, till.Inventory, _dudeInventory);
        Log($"OK, that's a good trade. (-{price} caps for {ObjectNameByPid(item.Pid)})"); // {27}
        Console.WriteLine($"barter-buy: {ObjectNameByPid(item.Pid)} for {price} (dudeCaps {DudeCaps()})");
    }

    /// <summary>Sell one unit at face value (the engine credits player goods
    /// at plain cost); the merchant must hold the caps.</summary>
    private void BarterSell(int index)
    {
        if (_barterNpc is not { } npc || _dude is null || _scriptHost is null)
            return;
        List<MapObject> goods = BarterGoods();
        if (index < 0 || index >= goods.Count)
            return;

        MapObject item = goods[index];
        if (item.IsInHand || item.IsWorn)
        {
            Log("You should unequip that first.");
            return;
        }

        MapObject till = _barterStock ?? npc;
        int price = BarterSellPrice(item);
        if (_scriptHost.CapsAdjust(till, -price) != 0)
        {
            Log($"{ObjectName(npc)} can't afford that.");
            return;
        }

        _scriptHost.CapsAdjust(_dude.Dude, price);
        TransferOne(item, _dudeInventory, till.Inventory);
        Log($"OK, that's a good trade. (+{price} caps for {ObjectNameByPid(item.Pid)})");
        Console.WriteLine($"barter-sell: {ObjectNameByPid(item.Pid)} for {price} (dudeCaps {DudeCaps()})");
    }

    private static void TransferOne(MapObject item, List<MapObject> from, List<MapObject> to)
    {
        if (item.StackCount > 1)
        {
            item.StackCount--;
            if (to.FirstOrDefault(i => i.Pid == item.Pid) is { } existing)
            {
                existing.StackCount++;
                return;
            }

            var unit = new MapObject
            {
                Id = -4,
                HexTile = -1,
                X = 0,
                Y = 0,
                Frame = 0,
                Rotation = 0,
                Fid = item.Fid,
                Flags = 0,
                Pid = item.Pid,
                Sid = -1,
            };
            to.Add(unit);
            return;
        }

        from.Remove(item);
        item.Flags &= ~(MapObject.FlagInLeftHand | MapObject.FlagInRightHand | MapObject.FlagWorn);
        if (to.FirstOrDefault(i => i.Pid == item.Pid) is { } stack)
            stack.StackCount += 1;
        else
            to.Add(item);
    }

    /// <summary>The critter's in-hand weapon proto + item; the dude's bag is
    /// the separate _dudeInventory list.</summary>
    public (ProtoInfo? Proto, MapObject? Item) EquippedWeapon(MapObject critter)
    {
        List<MapObject> bag = critter == _dude?.Dude ? _dudeInventory : critter.Inventory;
        foreach (MapObject item in bag.Where(i => i.IsInHand))
        {
            try
            {
                ProtoInfo proto = _protos.Get(item.Pid);
                if (proto.Weapon is not null)
                    return (proto, item);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
            {
            }
        }

        return (null, null);
    }

    /// <summary>The critter's carried weapon items (proto + item) for the AI inventory weapon
    /// switch (_ai_search_inven_weap). Returns ALL weapons in the bag — the CombatEngine fold skips
    /// the one being replaced; a non-weapon or unknown proto is dropped. P43.</summary>
    public IReadOnlyList<(ProtoInfo Proto, MapObject Item)> CritterInventoryWeapons(MapObject critter)
    {
        List<MapObject> bag = critter == _dude?.Dude ? _dudeInventory : critter.Inventory;
        var result = new List<(ProtoInfo, MapObject)>();
        foreach (MapObject item in bag)
        {
            try
            {
                ProtoInfo proto = _protos.Get(item.Pid);
                if (proto.Weapon is not null)
                    result.Add((proto, item));
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
            {
            }
        }
        return result;
    }

    /// <summary>Wield a carried weapon: clear every in-hand flag in the bag, then set the new item's
    /// right hand (_inven_wield HAND_RIGHT) so <see cref="EquippedWeapon"/> returns it. P43.</summary>
    public void EquipWeapon(MapObject critter, MapObject weaponItem)
    {
        List<MapObject> bag = critter == _dude?.Dude ? _dudeInventory : critter.Inventory;
        foreach (MapObject it in bag)
            it.Flags &= ~(MapObject.FlagInLeftHand | MapObject.FlagInRightHand);
        weaponItem.Flags |= MapObject.FlagInRightHand;
    }

    /// <summary>Loaded rounds; -1 sentinel hydrates from the proto capacity
    /// (fresh items, protoItemDataDefaults).</summary>
    public int WeaponAmmo(ProtoInfo weaponProto, MapObject item)
    {
        if (item.AmmoQuantity == -1)
            item.AmmoQuantity = weaponProto.Weapon?.AmmoCapacity ?? 0;
        return item.AmmoQuantity;
    }

    public AmmoProtoStats? LoadedAmmo(ProtoInfo weaponProto, MapObject item)
    {
        int pid = item.AmmoTypePid != -1 ? item.AmmoTypePid : weaponProto.Weapon?.AmmoTypePid ?? -1;
        if (pid <= 0)
            return null;
        try
        {
            return _protos.Get(pid).Ammo;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>_obj_shoot_blocking_at subset: walls/scenery/living critters on
    /// the tile, skipping hidden, NO_BLOCK (open doors) and SHOOT_THRU.</summary>
    public MapObject? ShootBlockerAt(int tile, MapObject shooter, MapObject target)
    {
        const int noBlock = 0x10;
        const uint shootThru = 0x80000000;
        return _solidObjects[_elevation].FirstOrDefault(o =>
            o.HexTile == tile && o != shooter && o != target && !o.IsHidden
            && (o.Flags & noBlock) == 0 && ((uint)o.Flags & shootThru) == 0
            && (Fid.Type(o.Fid) is ObjectType.Wall or ObjectType.Scenery
                || (Fid.Type(o.Fid) is ObjectType.Critter && !o.IsDead)));
    }

    /// <summary>Reload from a matching-caliber ammo item: partial fills, no
    /// mixed mags (item.cc weaponCanBeReloadedWith/weaponReload). The R key / AI
    /// auto-reload path — picks any matching box (preferred pid -1).</summary>
    public bool TryReload(MapObject holder, ProtoInfo weaponProto, MapObject weaponItem) =>
        TryReloadWith(holder, weaponProto, weaponItem, -1);

    /// <summary>Reload, optionally restricting to a SPECIFIC ammo pid (P40 — the player's ammo-type
    /// selection: "reload with THIS box"). preferredAmmoPid &lt; 0 = any matching box (the default
    /// auto-reload, unchanged → byte-identical). The no-mixed-mags rule still applies, so a type swap
    /// needs an empty weapon (unload first).</summary>
    public bool TryReloadWith(MapObject holder, ProtoInfo weaponProto, MapObject weaponItem, int preferredAmmoPid)
    {
        if (weaponProto.Weapon is not { } weapon || weapon.AmmoCapacity <= 0)
            return false;
        int current = WeaponAmmo(weaponProto, weaponItem);
        if (current >= weapon.AmmoCapacity)
            return false;

        List<MapObject> bag = holder == _dude?.Dude ? _dudeInventory : holder.Inventory;
        foreach (MapObject box in bag)
        {
            if (preferredAmmoPid >= 0 && box.Pid != preferredAmmoPid)
                continue; // P40: the player chose a specific ammo type

            ProtoInfo boxProto;
            try
            {
                boxProto = _protos.Get(box.Pid);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
            {
                continue;
            }

            if (boxProto.Ammo is not { } ammo || ammo.Caliber != weapon.Caliber)
                continue;
            if (current > 0 && weaponItem.AmmoTypePid != -1 && weaponItem.AmmoTypePid != box.Pid)
                continue; // no mixed mags

            if (box.AmmoQuantity == -1)
                box.AmmoQuantity = ammo.Quantity;
            int moved = Math.Min(weapon.AmmoCapacity - current, box.AmmoQuantity);
            if (moved <= 0)
                continue;

            weaponItem.AmmoQuantity = current + moved;
            weaponItem.AmmoTypePid = box.Pid;
            box.AmmoQuantity -= moved;
            if (box.AmmoQuantity <= 0)
            {
                box.StackCount--;
                if (box.StackCount <= 0)
                    bag.Remove(box);
                else
                    box.AmmoQuantity = ammo.Quantity; // next box in the stack
            }

            Log(holder == _dude?.Dude
                ? $"You reload the {ObjectNameByPid(weaponProto.Pid)} ({weaponItem.AmmoQuantity}/{weapon.AmmoCapacity})."
                : $"The {ObjectName(holder)} reloads.");
            Console.WriteLine($"reload: {ObjectNameByPid(weaponProto.Pid)} -> {weaponItem.AmmoQuantity}/{weapon.AmmoCapacity}");
            // Weapon-ready sfx on a successful reload (the engine rings the weapon in, combat.cc) — P34-M5.
            if (weapon.SoundCode > 0)
                _audio?.PlaySfx(Formats.Sound.SfxName.WeaponName(Formats.Sound.SfxName.WeaponSoundEffect.Ready, weapon.SoundCode, primaryOrPunch: true));
            return true;
        }

        return false;
    }

    /// <summary>Eject the dude's equipped weapon's loaded ammo into a bag box and empty the weapon
    /// (P40; ported from item.cc weaponUnload :1880 — one box of min(loaded, boxCapacity) rounds, the
    /// remainder stays in the mag). Needed to SWITCH ammo type (the no-mixed-mags rule blocks loading a
    /// different type into a non-empty weapon). The ejected box is added discretely (a partial count
    /// must not merge into a full stack). Returns true if anything was ejected.</summary>
    private bool UnloadEquippedWeapon()
    {
        if (_dude is null)
            return false;
        (ProtoInfo? wp, MapObject? wi) = EquippedWeapon(_dude.Dude);
        if (wp?.Weapon is not { } weapon || wi is null)
        {
            Log("You have no weapon to unload.");
            return false;
        }
        int loaded = WeaponAmmo(wp, wi);
        int typePid = wi.AmmoTypePid != -1 ? wi.AmmoTypePid : weapon.AmmoTypePid;
        if (loaded <= 0 || typePid <= 0)
        {
            Log($"The {ObjectNameByPid(wp.Pid)} is already empty.");
            return false;
        }
        int boxCap = SafeProto(typePid)?.Ammo?.Quantity ?? loaded;
        int ejected = Math.Min(loaded, boxCap);
        if (RebuildObject(typePid, 1) is { } box)
        {
            box.AmmoQuantity = ejected;
            _dudeInventory.Add(box); // discrete — a partial box must not merge into a full stack
        }
        wi.AmmoQuantity = loaded - ejected;
        if (wi.AmmoQuantity == 0)
            wi.AmmoTypePid = -1;
        Log($"You unload the {ObjectNameByPid(wp.Pid)}.");
        Console.WriteLine($"unload: weapon={wp.Pid} ejected={ejected} type={typePid} left={wi.AmmoQuantity}");
        return true;
    }

    /// <summary>Attack art: the weapon's animation code goes into FID bits
    /// 12-15 and the attack anim comes from extendedFlags &amp; 0xF via
    /// item.cc _attack_anim[] (THRUST=41, SWING=42; fists punch at 16).</summary>
    private void StartAttackAnimation(MapObject attacker, ProtoInfo? weaponProto)
    {
        // item.cc:116 _attack_anim, indexed by extendedFlags & 0xF
        ReadOnlySpan<int> attackAnims = [0, 16, 17, 42, 41, 18, 45, 46, 47];
        const int animThrowPunch = 16;

        int anim = animThrowPunch;
        int weaponCode = 0;
        if (weaponProto?.Weapon is { } weapon)
        {
            int index = weaponProto.ExtendedFlags & 0xF;
            anim = index < attackAnims.Length ? attackAnims[index] : animThrowPunch;
            weaponCode = weapon.AnimationCode;
        }

        int fid = Fid.Build(ObjectType.Critter, Fid.Index(attacker.Fid), anim, weaponCode);
        if (!_vfs.Exists(_artIndex.GetFrmPath(fid)))
            fid = Fid.Build(ObjectType.Critter, Fid.Index(attacker.Fid), animThrowPunch, 0);
        if (_vfs.Exists(_artIndex.GetFrmPath(fid)))
            _animator.PlayActionOnce(attacker, fid);
    }

    public string ObjectNameByPid(int pid) =>
        _protoMessages.GetName(pid) ?? $"0x{pid:X8}";

    /// <summary>Resolve the death anim against the critter's art (ported from fallout2-ce
    /// src/actions.cc _check_death): the gore anim DeathAnims.Pick chose (P26) if its art ships,
    /// else FALL_BACK, else FALL_FRONT. The engine's hit-from-front flip is out of PoC scope.</summary>
    public int PickDeathAnim(MapObject critter, int desiredAnim = Formats.Combat.DeathAnims.FallBack)
    {
        const int animFallBack = 20, animFallFront = 21;
        bool Exists(int anim) =>
            _vfs.Exists(_artIndex.GetFrmPath(Fid.Build(ObjectType.Critter, Fid.Index(critter.Fid), anim, 0)));
        if (desiredAnim != animFallBack && desiredAnim != animFallFront && Exists(desiredAnim))
            return desiredAnim;
        return Exists(animFallBack) ? animFallBack : animFallFront;
    }

    /// <summary>ported from fallout2-ce src/critter.cc critterKill(): the
    /// corpse is the single-frame art at death anim + 28, NO_BLOCK, and drawn
    /// flat — which also makes the existing loot panel reachable.</summary>
    public void ConvertToCorpse(MapObject critter, int deathAnim)
    {
        _animator.Remove(critter);

        int corpseFid = Fid.Build(ObjectType.Critter, Fid.Index(critter.Fid), deathAnim + 28, 0);
        if (_vfs.Exists(_artIndex.GetFrmPath(corpseFid)))
            critter.Fid = corpseFid;

        critter.Flags |= 0x10; // OBJECT_NO_BLOCK
        critter.Flags |= 0x08; // flat: corpses draw under standing critters

        for (int elevation = 0; elevation < MapFile.ElevationCount; elevation++)
        {
            if (_solidObjects[elevation].Remove(critter) && !_flatObjects[elevation].Contains(critter))
                InsertSorted(_flatObjects[elevation], critter);
        }
        RebuildBlockedTiles(_dude?.Dude);
    }

    /// <summary>Does this object get a critter_p_proc this tick: a live, scripted,
    /// non-dude critter that isn't a "wait here" companion (phase-10 M4). The single
    /// source of truth for both the heartbeat pump and the --companion diagnostic.</summary>
    private bool IsHeartbeatEligible(MapObject o) =>
        Fid.Type(o.Fid) is ObjectType.Critter && o != _dude?.Dude
        && !o.IsDead && o.Sid != -1 && !_waitingCompanions.Contains(o);

    /// <summary>One critter_p_proc per game tick, round-robin — the flattened
    /// _script_chk_critters ticker (scripts.cc:705), gated like the engine's
    /// !dialog && !combat && !movie check.</summary>
    private void PumpCritterProcs(double elapsedMs)
    {
        if (_scriptHost is null || _combat.Phase != Formats.Combat.CombatPhase.Idle || _combat.IsGameOver
            || _dialog is not null || _companionHub is not null || _lootContainer is not null || _worldmapOpen)
            return;

        _critterProcTimerMs += elapsedMs;
        if (_critterProcTimerMs < 100)
            return;
        _critterProcTimerMs = 0;

        // P30 A-M2: the periodic sneak re-check (sneakEventProcess) on the 100 ms heartbeat — one
        // reschedule "tick" = one heartbeat. Fires only while the flag is set; uses the isolated
        // _sneakRng so it can't perturb any other stream.
        if (_sneak.FlagSet && --_sneakTicksRemaining <= 0)
            RollSneak();

        // A "wait here" companion is skipped, so its follow critter_p_proc never runs
        // and it holds position until told to follow again (phase-10 M4).
        List<MapObject> scripted = [.. _solidObjects[_elevation].Where(IsHeartbeatEligible)];
        if (scripted.Count == 0)
            return;

        _critterProcIndex %= scripted.Count;
        MapObject critter = scripted[_critterProcIndex++];
        var result = _scriptHost.RunObjectProc(critter, _map, _dude?.Dude, "critter_p_proc");
        if (result is not null)
            foreach (string line in result.Messages)
                Log($"{ObjectName(critter)}: {line}");
    }

    /// <summary>pcAddExperience: add XP, level up while thresholds pass —
    /// <summary>The dude's kill tally per KILL_TYPE (gKillsByType, critter.cc:152; 19 types). Incremented
    /// on a dude/team kill, read by metarule3 GET_KILL_COUNT + the char-sheet display (P38).</summary>
    private int[] _killsByType = new int[19];

    /// <summary>ICombatHost (P38): tally a dude/team kill by the victim's KILL_TYPE (killsIncByType,
    /// critter.cc:702). The victim's kill type is its proto field; a bad proto is skipped.</summary>
    public void RecordKill(MapObject victim)
    {
        if (GetCritterState(victim) is { } stats && stats.Proto.KillType is int kt && kt >= 0 && kt < _killsByType.Length)
            _killsByType[kt]++;
    }

    /// <summary>ICombatHost (P42): an NPC quaffs ONE healing item from its bag (the AI _ai_check_drugs
    /// heal, combat_ai.cc:999) — find a healing drug (stimpak/super-stimpak/healing-powder), roll its
    /// HP heal (the -2 random range / stat-35 amount on _combatRng, like the dude's stimpak), apply it
    /// capped at MaxHp, consume one. Returns whether it healed. Inert when the critter carries none.</summary>
    public bool TryNpcHeal(MapObject critter)
    {
        foreach (MapObject item in critter.Inventory)
        {
            if (!Formats.Combat.AiHealing.IsHealingItem(item.Pid) || SafeProto(item.Pid)?.Drug is not { } drug)
                continue;
            int healed = drug.Stats[0] == -2
                ? _combatRng.Next(drug.Amounts[0], drug.Amounts[1] + 1) // stimpak random-range heal
                : Enumerable.Range(0, 3).Where(i => drug.Stats[i] == 35).Sum(i => drug.Amounts[i]);
            if (healed <= 0)
                continue;
            int max = GetCritterState(critter)?.MaxHp ?? critter.CurrentHp;
            int before = critter.CurrentHp;
            critter.CurrentHp = Math.Min(before + healed, max);
            item.StackCount--;
            if (item.StackCount <= 0)
                critter.Inventory.Remove(item);
            Log($"The {ObjectName(critter)} uses a healing item.");
            Console.WriteLine($"ai-heal: {ObjectName(critter)}@{critter.HexTile} +{critter.CurrentHp - before} ({critter.CurrentHp}/{max})");
            return true;
        }
        return false;
    }

    /// <summary>The char-sheet "::: Kills :::" rows (character_editor.cc:2202): each KILL_TYPE with a
    /// non-zero tally, named from proto.msg (1450 + killType, killTypeGetName critter.cc:766). Live UI.</summary>
    private List<string> KillDisplayLines()
    {
        var lines = new List<string>();
        for (int kt = 0; kt < _killsByType.Length; kt++)
            if (_killsByType[kt] > 0)
            {
                string name = ProtoMsg(1450 + kt);
                lines.Add($"{(name.Length > 0 ? name : $"Kill type {kt}")}: {_killsByType[kt]}");
            }
        return lines;
    }
    // proto.msg — kill-type (and other proto) display names (character_editor.cc uses gProtoMessageList);
    // lazy, empty if absent.
    private Formats.Text.MessageFile? _protoMsg; private bool _protoMsgTried;
    private string ProtoMsg(int id) =>
        id < 0 ? "" : LazyMsg(@"text\english\game\proto.msg", ref _protoMsgTried, ref _protoMsg)?.GetText(id) ?? "";

    public void AwardXp(int amount)
    {
        if (amount <= 0)
            return;
        // P28-M3: Swift Learner adds +5% experience per rank (stat.cc:737). 0 ranks → unchanged.
        int swift = DudePerkRank(Formats.Perks.PerkId.SwiftLearner);
        if (swift > 0)
            amount += swift * 5 * amount / 100;
        _dudeXp += amount;
        Log($"You earn {amount} experience points.");
        Console.WriteLine($"xp: +{amount} (total {_dudeXp}, level {_dudeLevel})");

        while (_dudeLevel + 1 < Formats.Combat.Progression.MaxLevel
            && _dudeXp >= Formats.Combat.Progression.XpForLevel(_dudeLevel + 1))
        {
            _dudeLevel++;
            if (_dudeGcd is not null && _dude is not null)
            {
                int endurance = _dudeGcd.Stats.BaseStats[Formats.Combat.CritterStat.Endurance];
                int gain = Formats.Combat.Progression.HpPerLevel(endurance);
                _dudeGcd.Stats.BonusStats[Formats.Combat.CritterStat.MaximumHitPoints] += gain;
                _dude.Dude.CurrentHp += gain; // the engine heals the delta

                // Skill points (P29-M2, character_editor.cc:5686): 5 + 2×IN(with trait mod) +
                // 2×rank(Educated) + 5×Skilled − (Gifted ? 5), banked cap 99. The IN includes the
                // trait modifier (Gifted +1) but not bonuses, matching critterGetBaseStatWithTraitModifier.
                int[] traits = _dudeGcd.Traits;
                int[] baseStats = _dudeGcd.Stats.BaseStats;
                int intel = baseStats[Formats.Combat.CritterStat.Intelligence]
                    + Formats.Combat.TraitModifiers.GetStatModifier(Formats.Combat.CritterStat.Intelligence, traits, baseStats);
                int grant = Formats.Combat.SkillSet.PointsPerLevel(intel,
                    educatedRank: DudePerkRank(Formats.Perks.PerkId.Educated),
                    skilled: Formats.Combat.TraitModifiers.Has(traits, Formats.Combat.TraitModifiers.Skilled),
                    gifted: Formats.Combat.TraitModifiers.Has(traits, Formats.Combat.TraitModifiers.Gifted));
                _unspentSkillPoints = Math.Min(Formats.Combat.SkillSet.PointsBankCap,
                    _unspentSkillPoints + grant);
            }
            Log($"You have reached level {_dudeLevel}! ({_unspentSkillPoints} skill points — press K)");
            Console.WriteLine($"level-up: now level {_dudeLevel}, skillPoints={_unspentSkillPoints}");

            // The engine runs _partyMemberIncLevels once per PC level-up (stat.cc:789):
            // a party.txt companion may swap to its next stage proto (#10 M2 / #13).
            AdvancePartyLevels();
        }
    }

    /// <summary>data\party.txt, lazily parsed (the level-up tables). Null if absent.</summary>
    private Formats.Party.PartyTable? PartyTable()
    {
        if (_partyTable is null && _vfs.Exists(@"data\party.txt"))
            _partyTable = Formats.Party.PartyTable.Parse(
                System.Text.Encoding.Latin1.GetString(_vfs.ReadAllBytes(@"data\party.txt")));
        return _partyTable;
    }

    /// <summary>One PC level-up's worth of companion proto advancement: each party
    /// member that is a party.txt entry rolls via PartyLevelUp.IncLevel; on advance,
    /// its stage proto becomes the per-member stat override and HP resets to the new
    /// max (party_member.cc:1605). Lights up the #13 foundation on a real recruit.</summary>
    private void AdvancePartyLevels()
    {
        if (_scriptHost is null || PartyTable() is not { } table)
            return;
        _partyRng ??= new Formats.Combat.SystemCombatRng(RngSeed ?? Environment.TickCount);

        foreach (MapObject member in _scriptHost.PartyMembers.ToArray())
        {
            if (table.ForPid(member.Pid) is not { } desc)
                continue;
            Formats.Party.PartyLevelUpState state = _companionLevelState.TryGetValue(member, out var s)
                ? s : _companionLevelState[member] = new Formats.Party.PartyLevelUpState();

            if (Formats.Party.PartyLevelUp.IncLevel(desc, state, _dudeLevel, _partyRng) is not { } stagePid || stagePid == -1)
                continue;
            try
            {
                if (_protos.Get(stagePid).Critter is not { } stageStats)
                    continue; // a non-critter stage proto can't supply stats (shouldn't happen)
                _companionStatOverride[member] = stageStats;
                if (GetCritterState(member) is { } cs)
                    member.CurrentHp = cs.MaxHp; // engine resets HP to the new max on advance
                Log($"{ObjectName(member)} has gained in some abilities.");
                Console.WriteLine($"companion-levelup: {ObjectName(member)} -> stage 0x{stagePid:X} level {state.Level} hp {member.CurrentHp}");
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
            {
            }
        }
    }

    /// <summary>Spend one banked point on a skill: cost is read off the
    /// EFFECTIVE (tag-doubled) value, the base point goes into the gcd, and
    /// the value recomputes through SkillSet (skill.cc skillAdd).</summary>
    private void SpendSkillPoint(int skill)
    {
        if (_dudeGcd is null)
            return;
        int[] b = _dudeGcd.Stats.BaseStats, bo = _dudeGcd.Stats.BonusStats, sk = _dudeGcd.Stats.Skills;
        int[] tags = _dudeGcd.TaggedSkills;
        int current = Formats.Combat.SkillSet.Value(b, bo, sk, tags, skill);
        if (current >= Formats.Combat.SkillSet.MaxSkill)
        {
            Log($"{Formats.Combat.SkillSet.Names[skill]} is maxed.");
            return;
        }
        int cost = Formats.Combat.SkillSet.Cost(current);
        if (_unspentSkillPoints < cost)
        {
            Log("Not enough skill points.");
            return;
        }

        sk[skill] += 1;
        _unspentSkillPoints -= cost;
        int after = Formats.Combat.SkillSet.Value(b, bo, sk, tags, skill);
        Log($"{Formats.Combat.SkillSet.Names[skill]} {current}% → {after}% ({_unspentSkillPoints} pts left)");
        Console.WriteLine($"skill-spend: {Formats.Combat.SkillSet.Names[skill]} {current}->{after} cost={cost} left={_unspentSkillPoints}");
    }

    /// <summary>Why resting is blocked right now, or null if allowed. The engine gates
    /// on the per-map can_rest_here flag + the worldmap rest loop; we have neither, so
    /// we gate on combat + local safety (no living non-party critter within sight) — a
    /// documented divergence.</summary>
    private string? RestBlockReason()
    {
        if (_dude is null)
            return "no dude";
        if (_combat.Phase != Formats.Combat.CombatPhase.Idle)
            return "in combat";
        bool danger = _solidObjects[_elevation].Any(o =>
            Fid.Type(o.Fid) is ObjectType.Critter && o != _dude.Dude && !o.IsDead
            && (_scriptHost is null || !_scriptHost.PartyMembers.Contains(o))
            && !_dismissedCompanions.ContainsKey(o) // a companion you just dismissed isn't a threat
            && Formats.Hex.HexGrid.Distance(o.HexTile, _dude.Dude.HexTile)
                <= Formats.Combat.CombatRules.SightRangeHexes);
        return danger ? "enemies near" : null;
    }

    private void LogRestRefusal(string why)
    {
        Log(why == "in combat" ? "You can't rest during a fight." : "It isn't safe to rest here.");
        Console.WriteLine($"rest: refused ({why})");
    }

    private List<MapObject> Sleepers() =>
        [_dude!.Dude, .. (_scriptHost?.PartyMembers ?? []).Where(m => !m.IsDead)];

    /// <summary>
    /// Rest to heal (Z / Pip-Boy "Until healed"), modeled on fallout2-ce
    /// pipboy.cc:2111-2113: advance the clock by hpToHeal / HEALING_RATE × 3 hours
    /// and restore the dude + companions to full.
    /// </summary>
    private void RestToHeal()
    {
        if (_dude is null)
            return;
        if (RestBlockReason() is { } why)
        {
            LogRestRefusal(why);
            return;
        }

        List<MapObject> sleepers = Sleepers();
        int hours = 0;
        foreach (MapObject c in sleepers)
        {
            if (GetCritterState(c) is not { } st)
                continue;
            int need = st.MaxHp - c.CurrentHp;
            if (need <= 0)
                continue;
            int rate = Formats.Combat.Progression.HealingRate(st.Stat(Formats.Combat.CritterStat.Endurance));
            hours = Math.Max(hours, Formats.Combat.Progression.RestHoursToHeal(need, rate));
        }
        if (hours == 0)
        {
            Log("You are already rested.");
            Console.WriteLine("rest: already at full HP");
            return;
        }

        _clock.AdvanceHours(hours);
        _lastAmbientHour = -1; // refresh ambient lighting to the new hour
        foreach (MapObject c in sleepers)
            if (GetCritterState(c) is { } st)
                c.CurrentHp = st.MaxHp;
        Log($"You rest for {hours} hours. Fully healed.");
        Console.WriteLine($"rest: +{hours}h, healed {sleepers.Count} to full (hour {_clock.Hour / 100:00})");
    }

    /// <summary>Pip-Boy rest-option dispatch (P12 M1): positive = that many game-minutes;
    /// -1 = until healed; -2/-3 = until the next 06:00 / 18:00.</summary>
    private void DoRest(int minutes)
    {
        _pipboyRestMenu = false;
        if (minutes == -1)
        {
            RestToHeal();
            return;
        }
        int restMin = minutes switch
        {
            -2 => MinutesUntil(600),
            -3 => MinutesUntil(1800),
            _ => minutes,
        };
        RestForMinutes(restMin);
    }

    /// <summary>Game-minutes from now until the next occurrence of an hhmm time-of-day.</summary>
    private int MinutesUntil(int hhmm)
    {
        int nowMin = _clock.Hour / 100 * 60 + _clock.Hour % 100;
        int targetMin = hhmm / 100 * 60 + hhmm % 100;
        int delta = targetMin - nowMin;
        return delta <= 0 ? delta + 24 * 60 : delta;
    }

    /// <summary>Timed rest: advance the clock and heal each sleeper proportionally
    /// (Progression.HpHealedResting — the inverse of the until-healed hours math).</summary>
    private void RestForMinutes(int minutes)
    {
        if (_dude is null || minutes <= 0)
            return;
        if (RestBlockReason() is { } why)
        {
            LogRestRefusal(why);
            return;
        }

        _clock.Ticks += (long)minutes * 60 * Formats.GameClock.TicksPerSecond;
        _lastAmbientHour = -1;
        int healedCount = 0;
        foreach (MapObject c in Sleepers())
        {
            if (GetCritterState(c) is not { } st)
                continue;
            int need = st.MaxHp - c.CurrentHp;
            if (need <= 0)
                continue;
            int rate = Formats.Combat.Progression.HealingRate(st.Stat(Formats.Combat.CritterStat.Endurance));
            int heal = Math.Min(need, Formats.Combat.Progression.HpHealedResting(minutes, rate));
            if (heal > 0) { c.CurrentHp += heal; healedCount++; }
        }
        Log($"You rest for {minutes} minutes.");
        Console.WriteLine($"rest: +{minutes}min, healed {healedCount} dudeHp {_dude.Dude.CurrentHp} "
            + $"(hour {_clock.Hour / 100:00}, day {_clock.Day})");
    }

    private void PlayWeaponSfx(ProtoInfo? weaponProto)
    {
        if (weaponProto?.Weapon is { SoundCode: > 0 } weapon)
            _audio?.PlaySfx(Formats.Sound.SfxName.WeaponAttack(weapon.SoundCode));
    }

    /// <summary>The game-tick at which the dude's next poison damage tick fires; -1 = not poisoned.
    /// Models the engine's single EVENT_TYPE_POISON queue entry (critter.cc:351) on the game-time clock
    /// (the combat-scoped EventQueue is the wrong tool — poison must outlast combat). (P35-M3.)</summary>
    private long _dudePoisonNextTick = -1;

    /// <summary>
    /// ported from fallout2-ce src/critter.cc critterAdjustPoison() (P35): DUDE-ONLY, poison-resistance
    /// reduced; sets the poison counter + (re)schedules the next damage tick. DOCUMENTED DIVERGENCE: the
    /// engine also shows a misc.msg monitor line ("You have been poisoned!") — we apply silently to keep a
    /// copyrighted game string out of the goldens.
    /// </summary>
    private void ApplyPoison(MapObject obj, int amount)
    {
        if (_dude is null || obj != _dude.Dude)
            return; // critterAdjustPoison: non-dude returns -1 (no-op)
        if (amount > 0)
        {
            int resist = GetCritterState(obj)?.Stat(32) ?? 0; // STAT_POISON_RESISTANCE
            amount -= amount * resist / 100;
        }
        else if (obj.Poison <= 0)
        {
            return; // can't reduce poison that isn't there
        }
        obj.Poison = Math.Max(0, obj.Poison + amount);
        SchedulePoison();
    }

    /// <summary>(Re)time the single poison damage event, ported from critterAdjustPoison's
    /// _queue_clear_type(EVENT_TYPE_POISON) + queueAddEvent(10*(505-5*poison)) (critter.cc:350-351):
    /// the next tick is 10*(505-5*poison) game-ticks from now, or cleared when poison ≤ 0. (P35-M3.)</summary>
    private void SchedulePoison() => _dudePoisonNextTick =
        _dude is { Dude.Poison: > 0 } d ? _clock.Ticks + 10L * (505 - 5 * d.Dude.Poison) : -1;

    /// <summary>
    /// Fire every poison damage tick now due, ported from poisonEventProcess (critter.cc:378): each tick
    /// is DUDE-ONLY, decrements poison by 2 + deals 1 HP, then re-queues at the reduced interval until
    /// poison ≤ 0. The loop drains all ticks a clock JUMP (rest/travel) made due, each re-timed from its
    /// own fire instant (so a big jump deals the right number of ticks). Driven from UpdateClock. The
    /// engine's "You take damage from poison." misc.msg line is omitted (copyrighted; silent — P35 pattern).
    /// </summary>
    private void ProcessPoison()
    {
        if (_dude is not { } d || _dudePoisonNextTick < 0)
            return;
        while (_dudePoisonNextTick >= 0 && _clock.Ticks >= _dudePoisonNextTick && d.Dude.Poison > 0)
        {
            long firedAt = _dudePoisonNextTick;
            d.Dude.Poison = Math.Max(0, d.Dude.Poison - 2);
            d.Dude.CurrentHp -= 1; // critterAdjustHitPoints(obj, -1)
            if (d.Dude.CurrentHp <= 0 && !_combat.IsGameOver)
                GameOver(); // death by poison
            _dudePoisonNextTick = d.Dude.Poison > 0 ? firedAt + 10L * (505 - 5 * d.Dude.Poison) : -1;
        }
    }

    /// <summary>Out-of-ammo empty-click sfx (combat.cc:5745) — P34-M5.</summary>
    public void OnWeaponOutOfAmmo(ProtoInfo weaponProto)
    {
        if (weaponProto.Weapon is { SoundCode: > 0 } weapon)
            _audio?.PlaySfx(Formats.Sound.SfxName.WeaponName(Formats.Sound.SfxName.WeaponSoundEffect.OutOfAmmo, weapon.SoundCode, primaryOrPunch: true));
    }

    // ===================================================================
    //  ICombatHost — the rest of the seam to CombatEngine (phase-9 M0).
    //  The viewer keeps single ownership of the animator, walkers, draw
    //  lists and blocking; the engine reaches them through these methods.
    // ===================================================================

    public MapObject? Dude => _dude?.Dude;
    public void StopDude() => _dude?.Stop();

    /// <summary>Criticals enable after one full game-day, like the engine
    /// (random.cc: gameTime / TICKS_PER_DAY >= 1).</summary>
    public bool CriticalsEnabled => _clock.Ticks / Formats.GameClock.TicksPerDay >= 1;

    /// <summary>ICombatHost (P41): the engine suppresses the DUDE's critical-FAILURE EFFECT until day 6
    /// (combat.cc:4190); the trigger still fires from day 2. Non-dude fumbles have no such gate.</summary>
    public bool DudeCritFailuresEnabled => _clock.Ticks / Formats.GameClock.TicksPerDay >= 6;

    private Formats.Combat.AiPacketTable? _aiPackets;
    private bool _aiPacketsLoaded;

    /// <summary>Resolve a critter's ai.txt packet: instance aiPacket first, proto
    /// fallback (the engine's order); null if 0 or ai.txt is absent.</summary>
    public Formats.Combat.AiPacket? GetAiPacket(MapObject critter)
    {
        if (!_aiPacketsLoaded)
        {
            _aiPacketsLoaded = true;
            try
            {
                _aiPackets = Formats.Combat.AiPacketTable.Parse(
                    System.Text.Encoding.Latin1.GetString(_vfs.ReadAllBytes(@"data\ai.txt")));
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
            {
                _aiPackets = null; // no ai.txt → no packets → pre-M1 behaviour
            }
        }
        if (_aiPackets is null)
            return null;

        int packet = critter.AiPacket;
        if (packet == 0)
        {
            try { packet = _protos.Get(critter.Pid).Critter?.AiPacket ?? 0; }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException) { }
        }
        return _aiPackets.Get(packet);
    }
    public bool IsBlocked(int tile) => _blockedTiles.Contains(tile);
    public bool IsAnimating(MapObject critter) => _animator.TryGetState(critter, out _);
    public bool IsFallInProgress(MapObject critter) =>
        _animator.TryGetState(critter, out AnimationState state) && !state.Finished;
    public bool IsAnyWalkerMoving() => _npcWalkers.Values.Any(w => w.Moving);
    public bool IsWalkerMoving(MapObject critter) =>
        _npcWalkers.TryGetValue(critter, out DudeController? w) && w.Moving;
    public bool StartWalk(MapObject critter, int targetTile) => StartNpcWalk(critter, targetTile);

    public void OnThrowStarted(MapObject thrower, int targetTile, ProtoInfo weaponProto)
    {
        // P45: the throw's defender for the float-text layer (null = an empty/AoE landing tile).
        _floatDefender = CritterAt(targetTile);
        const int animThrow = 18; // ANIM_THROW_ANIM (item.cc _attack_anim[5])
        int code = weaponProto.Weapon?.AnimationCode ?? 0;
        int fid = Fid.Build(ObjectType.Critter, Fid.Index(thrower.Fid), animThrow, code);
        if (!_vfs.Exists(_artIndex.GetFrmPath(fid)))
            fid = Fid.Build(ObjectType.Critter, Fid.Index(thrower.Fid), animThrow, 0);
        if (_vfs.Exists(_artIndex.GetFrmPath(fid)))
            _animator.PlayActionOnce(thrower, fid);
        PlayWeaponSfx(weaponProto);
        LaunchProjectile(thrower.HexTile, targetTile, weaponProto); // the thrown item flies (phase-10 #11)
    }

    public void RemoveFromHand(MapObject thrower, MapObject item)
    {
        List<MapObject> bag = thrower == _dude?.Dude ? _dudeInventory : thrower.Inventory;
        item.StackCount--;
        if (item.StackCount <= 0)
            bag.Remove(item);
    }

    /// <summary>Land a thrown non-explosive weapon on the ground (a fresh Item-type
    /// object, so the existing pickup recovers it).</summary>
    public void DropThrownWeapon(MapObject item, int tile)
    {
        if (RebuildObject(item.Pid, 1) is not { } dropped)
            return;
        dropped.HexTile = tile;
        dropped.Flags |= 0x08; // flat: rests on the ground
        InsertSorted(_flatObjects[_elevation], dropped);
    }

    /// <summary>Spawn the misc-10 explosion marker and broadcast damage_p_proc to
    /// scripted objects in radius 3 with it as the source — so the temple door's
    /// script sees metarule(49) == EXPLOSION and opens (_scr_explode_scenery).</summary>
    public void SpawnExplosionMarker(int tile)
    {
        var marker = new MapObject
        {
            Id = -7, HexTile = tile, X = 0, Y = 0, Frame = 0, Rotation = 0,
            Fid = Fid.Build(ObjectType.Misc, 10, 0, 0), Flags = 0x08 | 0x10, Pid = 0x05000010, Sid = -1,
        };
        foreach (MapObject obj in _solidObjects[_elevation]
            .Where(o => o.Sid != -1 && Formats.Hex.HexGrid.Distance(o.HexTile, tile) <= 3).ToList())
        {
            var scripted = _scriptHost?.RunObjectProc(obj, _map, marker, fixedParam: 20, actionBeingUsed: -1,
                "damage_p_proc");
            if (scripted is not null)
                foreach (string line in scripted.Messages)
                    Log(line);
        }
    }

    /// <summary>Knockback relocation: move a critter to a tile with no walk
    /// animation, re-sorting the draw list + blocking (and tripping any spatial
    /// at the landing tile, like a step would).</summary>
    public void PlaceCritter(MapObject critter, int tile)
    {
        critter.HexTile = tile;
        List<MapObject> solids = _solidObjects[_elevation];
        if (solids.Remove(critter))
            InsertSorted(solids, critter);
        RebuildBlockedTiles(_dude?.Dude);
        _scriptHost?.RunSpatialsAt(_map, tile, _elevation, critter);
    }
    public void Transcript(string line) => Console.WriteLine(line);

    public IReadOnlyCollection<MapObject> PartyMembers =>
        (IReadOnlyCollection<MapObject>?)_scriptHost?.PartyMembers ?? [];

    public IEnumerable<MapObject> CombatCritters =>
        _dude is null ? [] : _solidObjects[_elevation].Where(o =>
            Fid.Type(o.Fid) is ObjectType.Critter && o != _dude.Dude);

    /// <summary>
    /// reg_anim_func END: play a flushed batch of queued reg_anim actions (P33-M1).
    /// The engine gates every reg_anim op on !isInCombat() (interpreter_extra.cc:3460) and
    /// plays the batch SEQUENTIALLY over time; we execute in parallel and ignore the delay
    /// (DOCUMENTED SIMPLIFICATIONS). run==walk (no separate run animation/speed). Animate
    /// loops the FRM rather than playing once (no one-shot primitive). SLICE NOTE: no
    /// shippable map fires the move/animate ops at map_enter (only animate_forever for
    /// scenery, P21), so this is forward-looking — it lights up when content uses it.
    /// </summary>
    private void ExecuteRegAnim(IReadOnlyList<Formats.Int.RegAnimAction> actions)
    {
        if (_combat.Phase != Formats.Combat.CombatPhase.Idle)
            return;

        foreach (Formats.Int.RegAnimAction a in actions)
        {
            switch (a.Kind)
            {
                case Formats.Int.RegAnimKind.MoveToTile:
                case Formats.Int.RegAnimKind.RunToTile:
                {
                    bool started = StartNpcWalk(a.Object, a.Tile);
                    _regAnimMoves.Add(
                        $"{ObjectName(a.Object)}@{a.Object.HexTile}->{a.Tile}:"
                        + $"{(a.Kind == Formats.Int.RegAnimKind.RunToTile ? "run" : "walk")}:{(started ? "ok" : "no")}");
                    break;
                }
                case Formats.Int.RegAnimKind.MoveToObject:
                case Formats.Int.RegAnimKind.RunToObject:
                {
                    // The engine walks to the destination object's tile; if that tile is
                    // blocked, settle on a free neighbour (the Placement port, P33-M0).
                    int dest = a.Dest is null
                        ? -1
                        : Formats.Map.Placement.FreeTileNear(a.Dest.HexTile, t => _blockedTiles.Contains(t));
                    bool started = dest >= 0 && StartNpcWalk(a.Object, dest);
                    _regAnimMoves.Add(
                        $"{ObjectName(a.Object)}@{a.Object.HexTile}->obj@{dest}:"
                        + $"{(a.Kind == Formats.Int.RegAnimKind.RunToObject ? "run" : "walk")}:{(started ? "ok" : "no")}");
                    break;
                }
                case Formats.Int.RegAnimKind.Animate:
                case Formats.Int.RegAnimKind.AnimateReverse:
                {
                    if (Fid.Type(a.Object.Fid) is ObjectType.Critter)
                        _animator.SetCritterAnimation(a.Object, Fid.Build(ObjectType.Critter,
                            Fid.Index(a.Object.Fid), a.Anim, Fid.WeaponCode(a.Object.Fid), a.Object.Rotation));
                    else
                        _animator.AddLooping(a.Object);
                    _regAnimMoves.Add(
                        $"{ObjectName(a.Object)}@{a.Object.HexTile}:anim{a.Anim}"
                        + (a.Kind == Formats.Int.RegAnimKind.AnimateReverse ? "rev" : string.Empty));
                    break;
                }
            }
        }
    }

    /// <summary>reg_anim_clear: drop a pending animation + stop/forget a walker.</summary>
    public void ClearAnimation(MapObject critter)
    {
        _animator.Remove(critter);
        if (_npcWalkers.TryGetValue(critter, out DudeController? walker))
        {
            walker.Stop();
            _npcWalkers.Remove(critter);
        }
    }

    public void OnAttackStarted(MapObject attacker, MapObject target, ProtoInfo? weaponProto)
    {
        // P45: remember THIS attack's real defender for the floating combat-text layer. The
        // outcome Log line ("...hits you for N damage.") names the defender as "you" even for
        // an NPC-vs-NPC blow (ResolveAttack keys the wording on byDude, not the real defender),
        // so the wording can't be trusted — the tracked object can. This also covers the dude
        // AS defender, which OnTargetHit/OnTargetDodge deliberately skip (the camera-anchor dude
        // doesn't visibly react — P34-M6) and which the "different shade for the dude" needs.
        _floatDefender = target;
        if (weaponProto?.Weapon is not null)
            PlayWeaponSfx(weaponProto);
        // Unarmed/melee swing grunt (actions.cc:625 sfxBuildCharName(attacker, ANIM_THROW_PUNCH, CONTACT)) —
        // a wielded weapon plays its own sfx above instead (P34-M5).
        else if (Formats.Sound.SfxName.CharName(_artIndex.CritterBaseName(attacker.Fid), 16 /*ANIM_THROW_PUNCH*/,
                     Formats.Sound.SfxName.CharacterSoundEffect.Contact, Fid.WeaponCode(attacker.Fid)) is { } swing)
            _audio?.PlaySfx(swing);
        StartAttackAnimation(attacker, weaponProto);
        LaunchProjectile(attacker, target, weaponProto);
    }

    // A projectile sprite flying attacker→target over its travel time — a purely
    // visual overlay (phase-10 #11): it doesn't gate combat or emit transcript, so
    // headless runs + the golden harnesses are unaffected.
    private sealed class Projectile
    {
        public required int Fid;
        public required int Rotation;
        public required int FromTile;
        public required int ToTile;
        public required double DurationMs;
        public double ElapsedMs;
    }
    private readonly List<Projectile> _projectiles = [];

    private void LaunchProjectile(MapObject attacker, MapObject target, ProtoInfo? weaponProto) =>
        LaunchProjectile(attacker.HexTile, target.HexTile, weaponProto);

    /// <summary>Send a projectile sprite from one tile to another for a ranged or thrown
    /// shot (melee — adjacent — gets none). Art: the weapon's ProjectilePid, else the
    /// weapon item itself (thrown). Resolves nothing → no projectile (phase-10 #11).</summary>
    private void LaunchProjectile(int fromTile, int toTile, ProtoInfo? weaponProto)
    {
        if (weaponProto?.Weapon is not { } weapon)
            return; // unarmed/melee-proto: no projectile
        int distance = Formats.Hex.HexGrid.Distance(fromTile, toTile);
        if (distance <= 1)
            return; // adjacent = melee swing, no flight

        int projectileFid;
        try
        {
            projectileFid = weapon.ProjectilePid > 0 ? _protos.Get(weapon.ProjectilePid).Fid : weaponProto.Fid;
            _ = _frmCache.GetFrm(projectileFid); // ensure the art loads, else skip
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            return;
        }

        _projectiles.Add(new Projectile
        {
            Fid = projectileFid,
            Rotation = Formats.Hex.HexGrid.RotationTo(fromTile, toTile),
            FromTile = fromTile,
            ToTile = toTile,
            DurationMs = Math.Max(120, distance * 24), // ~24 ms per hex of flight
        });
    }

    private void AdvanceProjectiles(double elapsedMs)
    {
        if (_projectiles.Count == 0)
            return;
        for (int i = _projectiles.Count - 1; i >= 0; i--)
        {
            _projectiles[i].ElapsedMs += elapsedMs;
            if (_projectiles[i].ElapsedMs >= _projectiles[i].DurationMs)
                _projectiles.RemoveAt(i);
        }
    }

    /// <summary>Draw each in-flight projectile at its lerped screen position between the
    /// from/to tile centers (phase-10 #11).</summary>
    private void DrawProjectiles()
    {
        foreach (Projectile p in _projectiles)
        {
            Formats.Frm.FrmFrame frame;
            Texture2D texture;
            try
            {
                frame = _frmCache.GetFrm(p.Fid).GetFrame(0, p.Rotation);
                texture = _frmCache.GetTexture(p.Fid, 0, p.Rotation);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
            {
                continue;
            }
            (int fx, int fy) = _camera.HexToScreen(p.FromTile);
            (int tx, int ty) = _camera.HexToScreen(p.ToTile);
            float t = (float)Math.Clamp(p.ElapsedMs / p.DurationMs, 0, 1);
            float x = fx + (tx - fx) * t - frame.Width / 2f;
            float y = fy + (ty - fy) * t - frame.Height / 2f;
            _spriteBatch.Draw(texture, new Vector2(x, y), LightTint(p.ToTile));
        }
    }

    /// <summary>Hit-react FRM (anim 14) on a surviving, non-dude target.</summary>
    public void OnTargetHit(MapObject target, MapObject attacker, bool knockedDown)
    {
        const int animHitFromFront = 14, animHitFromBack = 15;
        // Got-hit grunt (actions.cc:431 sfxBuildCharName(defender, ANIM_HIT_FROM_FRONT, UNUSED)) —
        // audio-only, plays for any target incl. the dude; null/silent when the base is unresolvable (P34-M5).
        if (Formats.Sound.SfxName.CharName(_artIndex.CritterBaseName(target.Fid), animHitFromFront,
                Formats.Sound.SfxName.CharacterSoundEffect.Unused, Fid.WeaponCode(target.Fid)) is { } grunt)
            _audio?.PlaySfx(grunt);

        // The dude's reaction sprite is deferred (the engine reacts him too, but Hexwaste's
        // camera-anchor dude historically doesn't — documented divergence, P34-M6 spillover).
        if (target == _dude?.Dude)
            return;
        // Already mid-fall (Once-mode = a held FALL)? Don't override it with a hit-react
        // (actions.cc:438 early-returns for a prone defender). P34-M6.
        if (_animator.TryGetState(target, out AnimationState falling) && falling.Mode == AnimationMode.Once)
            return;

        bool front = Formats.Combat.SneakAttack.IsHitFromFront(attacker.Rotation, target.Rotation);
        int weaponCode = Fid.WeaponCode(target.Fid);

        if (knockedDown) // a crit that knocks the target down plays a FALL, not a hit-react (P34-M6).
        {
            int fallFid = Fid.Build(ObjectType.Critter, Fid.Index(target.Fid),
                Formats.Combat.ReactionAnims.KnockdownFall(front), 0);
            if (_vfs.Exists(_artIndex.GetFrmPath(fallFid)))
                _animator.PlayFall(target, fallFid);
            return;
        }

        // Hit-from-front vs back (back only if the critter ships ANIM_HIT_FROM_BACK art — actions.cc:425).
        bool backArt = _vfs.Exists(_artIndex.GetFrmPath(
            Fid.Build(ObjectType.Critter, Fid.Index(target.Fid), animHitFromBack, weaponCode)));
        int anim = Formats.Combat.ReactionAnims.HitReaction(front, backArt);
        int hitFid = Fid.Build(ObjectType.Critter, Fid.Index(target.Fid), anim, weaponCode);
        if (_vfs.Exists(_artIndex.GetFrmPath(hitFid)))
            _animator.PlayActionOnce(target, hitFid);
    }

    /// <summary>Dodge reaction on a miss (P34-M6) — non-dude only (the dude reaction is deferred).</summary>
    public void OnTargetDodge(MapObject target)
    {
        if (target == _dude?.Dude)
            return;
        int fid = Fid.Build(ObjectType.Critter, Fid.Index(target.Fid),
            Formats.Combat.ReactionAnims.Dodge, Fid.WeaponCode(target.Fid));
        if (_vfs.Exists(_artIndex.GetFrmPath(fid)))
            _animator.PlayActionOnce(target, fid);
    }

    /// <summary>Stand-up sprite when a prone critter gets up (P34-M6) — the prone flag is already cleared.</summary>
    public void OnGetUp(MapObject critter)
    {
        if (critter == _dude?.Dude)
            return;
        int anim = Formats.Combat.ReactionAnims.StandUp(Fid.AnimType(critter.Fid));
        int fid = Fid.Build(ObjectType.Critter, Fid.Index(critter.Fid), anim, Fid.WeaponCode(critter.Fid));
        if (_vfs.Exists(_artIndex.GetFrmPath(fid)))
            _animator.PlayActionOnce(critter, fid);
    }

    /// <summary>Death scream + start the fall; true if a fall is playing (caller
    /// waits), false if no fall art (corpse converted immediately).</summary>
    public bool StartDeathFall(MapObject critter, int deathAnim)
    {
        // Death scream (actions.cc:321 sfxBuildCharName(defender, anim, CHARACTER_SOUND_EFFECT_DIE)).
        // NPCs use the faithful CharName (scorpions → MASCRP* which ship; humans → HMWARR* which don't,
        // i.e. engine-faithful silence). The DUDE keeps the HumanDeath HM/HFXXXX fallback (the P8 scream,
        // a documented divergence) so the player death audio isn't regressed (P34-M5).
        if (critter == _dude?.Dude)
        {
            bool female = _dudeGcd?.Stats.BaseStats[34] == 1;
            _audio?.PlaySfx(Formats.Sound.SfxName.HumanDeath(female, deathAnim));
        }
        else if (Formats.Sound.SfxName.CharName(_artIndex.CritterBaseName(critter.Fid), deathAnim,
                     Formats.Sound.SfxName.CharacterSoundEffect.Die, Fid.WeaponCode(critter.Fid)) is { } scream)
        {
            _audio?.PlaySfx(scream);
        }

        int fallFid = Fid.Build(ObjectType.Critter, Fid.Index(critter.Fid), deathAnim, 0);
        if (_vfs.Exists(_artIndex.GetFrmPath(fallFid)))
        {
            _animator.PlayFall(critter, fallFid);
            return true;
        }

        ConvertToCorpse(critter, deathAnim);
        return false;
    }

    /// <summary>Forget bookkeeping for a dead critter (walker + home tile).</summary>
    public void OnCritterRemoved(MapObject critter)
    {
        _npcWalkers.Remove(critter);
        _homeTiles.Remove(critter);
    }

    public IReadOnlyList<string> RunDamageProc(MapObject target, MapObject? source, int damage) =>
        _scriptHost?.RunObjectProc(target, _map, source, fixedParam: damage, actionBeingUsed: -1,
            "damage_p_proc")?.Messages?.ToList() ?? [];

    public (IReadOnlyList<string> Lines, bool Overridden) RunDestroyProc(MapObject critter, MapObject? killer)
    {
        var scripted = _scriptHost?.RunObjectProc(critter, _map, killer, "destroy_p_proc");
        return scripted is null ? ([], false) : (scripted.Messages.ToList(), scripted.Overridden);
    }

    /// <summary>
    /// A combat_p_proc hook (P35). The engine sets source = NULL always (scriptSetObjects(sid, NULL, ...));
    /// the per-turn hook (fp=4) has target null, the on-hit hook (fp=2) sets target = the struck defender.
    /// Routed through ScriptHost.RunCombatProc, which decouples source/target/dude so dude_obj is the real
    /// dude (the P35 RunObjectProc coupling is gone).
    /// </summary>
    public (IReadOnlyList<string> Lines, bool Overridden) RunCombatProc(MapObject critter, int fixedParam, MapObject? target = null)
    {
        var scripted = _scriptHost?.RunCombatProc(critter, target, _dude?.Dude, _map, fixedParam);
        return scripted is null ? ([], false) : (scripted.Messages.ToList(), scripted.Overridden);
    }

    public void RemovePartyMember(MapObject critter)
    {
        if (_scriptHost?.PartyMembers.Remove(critter) == true)
        {
            _partyScriptIndex.Remove(critter);
            Log($"{ObjectName(critter)} has fallen.");
        }
    }

    /// <summary>Death-screen monitor line; the engine sets state + prints the
    /// "GAME OVER" transcript line and shows the screen via _combat.IsGameOver.</summary>
    public void GameOver() => Log("You have died. F9 loads the last save.");

    private void InteractWith(MapObject obj)
    {
        if (Fid.Type(obj.Fid) is ObjectType.Critter)
        {
            // Dead critters are containers (gate on DAM_DEAD).
            if (obj.IsDead)
            {
                if (!IsAdjacentToDude(obj))
                {
                    Log("Too far away.");
                    return;
                }
                _lootContainer = obj;
                _panelPage = 0;
                PrewarmItemTextures(obj.Inventory);
                return;
            }

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
            _panelPage = 0;
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

    /// <summary>Lay out + create a random encounter's critters on the freshly loaded
    /// transient map (phase-10 M3). The pure <see cref="EncounterSpawner"/> picks the
    /// tiles (formations, the dude's Perception, the map's random_start_points, A*
    /// reachability); this turns each instruction into a live MapObject — proto art,
    /// a bound script sid (so the heartbeat aggros it), a hostile team, its gear, and
    /// a corpse for Dead members. Deterministic under --rng-seed.</summary>
    private void SpawnEncounter(Formats.Map.EncounterResult encounter)
    {
        if (_dude is null)
            return;

        int perception = GetCritterState(_dude.Dude)?.Stat(Formats.Combat.CritterStat.Perception) ?? 5;
        // The same live-visible tally metarule(16) uses (the report ties the spawn +2
        // bonus to _getPartyMemberCount — one definition, both tracks).
        int partyCount = _scriptHost is not null ? Formats.Int.ScriptHost.PartyMemberCount(_scriptHost.PartyMembers) : 1;
        IReadOnlyList<int> startTiles = [.. _mapList.GetRandomStartPoints(_currentMapName).Select(p => p.Tile)];

        bool IsBlocked(int t) => _blockedTiles.Contains(t);
        bool Reachable(int from, int to) => Formats.Hex.Pathfinder.FindPath(from, to, IsBlocked) is not null;

        // Reuse the persisted worldmap RNG (seeded off --rng-seed, else wall-clock) so
        // spawns VARY per encounter and per playthrough — a fresh fixed seed here made
        // every wasteland fight identical.
        _wmRng ??= new Formats.Combat.SystemCombatRng(RngSeed ?? Environment.TickCount);
        int getGlobal(int g) => _scriptHost?.GlobalVars.GetValueOrDefault(g, 0) ?? 0;
        IReadOnlyList<Formats.Map.SpawnInstruction> plan = Formats.Map.EncounterSpawner.Plan(
            encounter, Worldmap, _wmRng, _dude.Dude.HexTile, perception, partyCount, startTiles,
            IsBlocked, Reachable, getGlobal, _dudeLevel, _clock.Hour, _clock.Day);

        int placed = 0;
        var spawnedCritters = new List<MapObject>();
        foreach (Formats.Map.SpawnInstruction si in plan)
        {
            if (BuildSpawn(si) is not { } obj)
                continue;
            _map.Elevations[_elevation]!.Objects.Add(obj);
            OnScriptObjectPlaced(obj);
            if (!si.Dead && Fid.Type(obj.Fid) is ObjectType.Critter)
                spawnedCritters.Add(obj);
            if (si.Dead)
            {
                // Complete the death state so the body is lootable + examines as a
                // corpse: the combat kill path sets DAM_DEAD + clears the sid before
                // converting (CombatEngine.cs), so mirror it here.
                obj.CombatResults |= 0x80; // DAM_DEAD
                obj.CurrentHp = 0;
                obj.Sid = -1;
                ConvertToCorpse(obj, PickDeathAnim(obj));
            }
            placed++;
        }
        Console.WriteLine($"encounter-spawn: {encounter.Entry.Spawns.FirstOrDefault()?.Group ?? "?"}"
            + $" planned={plan.Count} placed={placed} on {_currentMapName}");

        // Phase-16 M3: an X-FIGHTING-Y encounter spawned its groups on distinct teams —
        // start the brawl so the player arrives to two factions already at each other's
        // throats (and can watch or join). Only when ≥2 teams actually landed.
        if (encounter.Entry.Situation == "FIGHTING"
            && spawnedCritters.Select(c => c.Team).Distinct().Count() >= 2)
            _combat.StartBrawl(spawnedCritters);
    }

    /// <summary>Build one spawned encounter critter (or scenery): proto art, an
    /// allocated script sid, full HP, a hostile team, and its carried gear with the
    /// in-hand / worn equip flags the CombatEngine reads (phase-10 M3).</summary>
    private MapObject? BuildSpawn(Formats.Map.SpawnInstruction si)
    {
        Formats.Proto.ProtoInfo proto;
        try
        {
            proto = _protos.Get(si.Pid);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            Console.Error.WriteLine($"encounter-spawn: bad pid 0x{si.Pid:X8}: {ex.Message}");
            return null;
        }

        var obj = new MapObject
        {
            Id = -3, // script-created marker, like ScriptContext.CreateObject
            HexTile = si.Tile,
            X = 0,
            Y = 0,
            Frame = 0,
            Rotation = Math.Clamp(si.Rotation, 0, 5),
            Fid = proto.Fid,
            // Propagate the proto's OBJECT_MULTIHEX (0x800) so a spawned Large Radscorpion is a multihex
            // target (the +15 to-hit + the knockback immunity actually apply). P36.
            Flags = proto.Flags & 0x800,
            Pid = si.Pid,
            Sid = si.ScriptIndex >= 0 && _scriptHost is not null
                ? _scriptHost.AllocateSid(_map, si.ScriptIndex)
                : -1,
        };

        if (Fid.Type(obj.Fid) is ObjectType.Critter)
        {
            // Team 0 is the dude's; a nonzero team makes the spawn hostile-eligible
            // (the bound EC script may re-set it via critter_add_trait, but this
            // guarantees an AMBUSH is hostile even before its first heartbeat). A
            // FIGHTING encounter spawns its sub-groups on distinct teams (1, 2, …) so
            // they brawl each other as well as the dude (phase-16 M3).
            obj.Team = si.Team;
            obj.CurrentHp = GetCritterState(obj)?.MaxHp ?? obj.CurrentHp;
        }

        foreach (Formats.Map.SpawnItem it in si.Items)
        {
            if (RebuildObject(it.Pid, it.Count) is not { } item)
                continue;
            if (it.Wielded)
                item.Flags |= MapObject.FlagInRightHand;
            if (it.Worn)
                item.Flags |= MapObject.FlagWorn;
            obj.Inventory.Add(item);
        }
        return obj;
    }

    /// <summary>Script-driven obj_open/obj_close: idempotent door state change.</summary>
    private void SetDoorState(MapObject door, bool open)
    {
        if (_openDoors.Contains(door) != open)
            ToggleDoor(door);
    }

    /// <summary>Lockpick the hovered door — the L-key shortcut, now just the Skilldex
    /// Lockpick (skill 9) path.</summary>
    private void TryLockpick(MapObject door) => TryUseSkillOn(9, door);

    /// <summary>Arm a Skilldex skill: the next click applies it to the target
    /// (closes the picker first).</summary>
    private void ArmSkill(int skill)
    {
        _pendingUseSkill = skill;
        _skilldexOpen = false;
        Log($"Use {SkillName(skill)} on what?");
    }

    private static string SkillName(int skill) =>
        skill >= 0 && skill < Formats.Combat.SkillSet.Names.Length ? Formats.Combat.SkillSet.Names[skill] : $"skill {skill}";

    /// <summary>The dude's effective skill % (skillGetValue) for the heal/contest roll;
    /// a nominal 40 when there's no gcd sheet (the bare default dude).</summary>
    private int DudeSkillValue(int skill) => _dudeGcd is { } g
        ? Formats.Combat.SkillSet.Value(g.Stats.BaseStats, g.Stats.BonusStats, g.Stats.Skills, g.TaggedSkills, skill)
        : 40;

    /// <summary>
    /// Apply a Skilldex skill to a target (ported from skill.cc skillUse + the
    /// use_skill_on_p_proc path lockpick already used). Targeted skills (Lockpick/
    /// Steal/Traps/Science/Repair) run the target's script and fall back to the
    /// lockpick unlock; First Aid heals HP (no Healer perk → 1-5); Doctor also mends
    /// crippled limbs / blindness (P14-M5); Sneak toggles.
    /// </summary>
    private void TryUseSkillOn(int skill, MapObject target)
    {
        bool self = target == _dude?.Dude;
        switch (skill)
        {
            case 6: // First Aid
            case 7: // Doctor
                TryHeal(skill, target, self);
                return;
            case 8: // Sneak — toggle the sneaking FLAG (dudeToggleState, critter.cc:1176). Enabling
                    // does an immediate SKILL_SNEAK roll (dudeEnableState → sneakEventProcess, A-M2);
                    // disabling clears Working. The roll draws from the isolated _sneakRng only.
                _sneak.FlagSet = !_sneak.FlagSet;
                if (_sneak.FlagSet)
                    RollSneak();
                else
                    _sneak.Working = false;
                Log($"Sneak mode {(_sneak.FlagSet ? "on" : "off")}.");
                return;
            default: // 9 Lockpick / 10 Steal / 11 Traps / 12 Science / 13 Repair
                if (self || !IsAdjacentToDude(target))
                {
                    Log("Too far away.");
                    return;
                }
                var scripted = _scriptHost?.RunObjectProc(target, _map, _dude?.Dude,
                    fixedParam: 0, actionBeingUsed: skill, "use_skill_on_p_proc");
                if (scripted is not null)
                    foreach (string line in scripted.Messages)
                        Log(line);
                if (scripted is { Overridden: true })
                    return;
                if (skill == 9) // lockpick default: unlock (PoC rolls succeed)
                {
                    if (!target.IsLockedState)
                        Log("It isn't locked.");
                    else
                    {
                        target.IsLockedState = false;
                        Log($"You pick the lock on the {ObjectName(target)}.");
                    }
                    return;
                }
                Log($"You use {SkillName(skill)} on the {ObjectName(target)}. Nothing happens.");
                return;
        }
    }

    /// <summary>First Aid / Doctor heal, ported from skill.cc:546 (skillUse). Rolls the
    /// dude's skill % vs a d100; success heals 1-5 HP (no Healer perk). First Aid costs
    /// 30 game-min, Doctor 60; both honour the engine's "wait a while" 3-uses-per-day
    /// cap (skillGetFreeUsageSlot).</summary>
    private void TryHeal(int skill, MapObject target, bool self)
    {
        if (GetCritterState(target) is not { } cs)
        {
            Log("You can't use that there.");
            return;
        }
        if (target.CurrentHp <= 0)
        {
            Log("You can't heal the dead.");
            return;
        }
        // Only Doctor (skill 7) mends crippled limbs / blindness (skill.cc:675); First Aid
        // is HP-only, so it has nothing to do on a full-HP target.
        bool needsHp = target.CurrentHp < cs.MaxHp;
        bool doctorCripple = skill == 7 && Formats.Combat.SkillHealing.IsCrippled(target.CombatResults);
        if (!needsHp && !doctorCripple)
        {
            Log(self ? "You look healthy already." : $"{ObjectName(target)} looks healthy already.");
            return;
        }

        if (_skillUsesDay != _clock.Day) { _skillUsesDay = _clock.Day; _skillUsesByDay.Clear(); }
        if (_skillUsesByDay.GetValueOrDefault(skill) >= 3)
        {
            Log("You've taxed your ability with that skill. Wait a while.");
            return;
        }

        _skillRng ??= new Formats.Combat.SystemCombatRng(RngSeed ?? Environment.TickCount);

        // Doctor: try to mend each crippled limb / blindness (clears the CombatResults
        // bits the combat engine reads live).
        if (doctorCripple)
        {
            target.CombatResults = Formats.Combat.SkillHealing.HealLimbs(
                target.CombatResults, DudeSkillValue(skill), _skillRng, out List<string> healed);
            foreach (string limb in healed)
            {
                Log(self ? $"You heal your {limb}." : $"You heal the {ObjectName(target)}'s {limb}.");
                Console.WriteLine($"limb-heal: {ObjectName(target)}@{target.HexTile} {limb}");
            }
            if (healed.Count == 0)
                Log("You fail to mend the injury.");
        }

        // HP heal (both skills) when wounded.
        if (needsHp)
        {
            if (_skillRng.Next(1, 101) <= DudeSkillValue(skill))
            {
                int heal = Math.Min(_skillRng.Next(1, 6), cs.MaxHp - target.CurrentHp);
                target.CurrentHp += heal;
                Log(self ? $"You heal {heal} hit points." : $"You heal the {ObjectName(target)} for {heal} hit points.");
            }
            else
            {
                Log("You fail to do any healing.");
            }
        }

        _skillUsesByDay[skill] = _skillUsesByDay.GetValueOrDefault(skill) + 1;
        _clock.Ticks += (skill == 6 ? 1800 : 3600) * Formats.GameClock.TicksPerSecond;
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
            // Phase-16 M2: if we're leaving a transient encounter map with a leg still in
            // progress, auto-resume travel toward the original destination instead of
            // forcing a worldmap re-click (the engine's isWalking).
            if (_currentMapTransient && _travelDestination is { } resumeDest)
            {
                _resumeTravelDest = resumeDest;
                Console.WriteLine($"travel-resume: left encounter map -> continuing to {resumeDest.Name}");
            }
            else
            {
                Log("You head out to the wasteland.");
            }
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
        _baseTitle = $"Hexwaste viewer — {_map.Header.Name} (elevation {_elevation})";
        Window.Title = _baseTitle;
    }

    /// <summary>Travels to a worldmap area: first usable entrance, resolved via maps.txt lookup names.</summary>
    private void TravelTo(WorldArea area)
    {
        // Phase-10 M3: roll for encounters along the way. The pure walk + roll + map
        // pick lives in Formats.Map.WorldmapTravel.ResolveLeg (#14); the viewer only
        // does the I/O (advance the real clock, load the map). If the wasteland bites,
        // the encounter map loads instead of the town — re-clicking the destination
        // resumes travel from the encounter spot (the engine's isWalking auto-resume is
        // a documented v1 simplification). The very first travel of a game (no worldPos
        // yet) skips the roll and just arrives.
        bool rolled = _worldPosX >= 0 && _worldPosY >= 0;
        if (rolled)
        {
            _wmRng ??= new Formats.Combat.SystemCombatRng(RngSeed ?? Environment.TickCount);
            int getGlobal(int g) => _scriptHost?.GlobalVars.GetValueOrDefault(g, 0) ?? 0;
            Formats.Combat.CritterState? dudeStats = _dude is not null ? GetCritterState(_dude.Dude) : null;
            int luck = dudeStats?.Stat(Formats.Combat.CritterStat.Luck) ?? 5;
            int outdoorsman = _dude is not null ? PartyBestOutdoorsman() : 0;

            // Phase-17 M2: live play ANIMATES the leg — Update drains TravelLeg.Step() over
            // wall-time so a party dot crosses the worldmap (terrain-paced). Headless runs
            // (the goldens) drain the WHOLE leg synchronously, byte-identical (same RNG).
            if (_animateTravel)
            {
                _activeTravel = (new Formats.Map.TravelLeg(Worldmap, _cities.Areas, _mapList,
                    _worldPosX, _worldPosY, area.WorldX, area.WorldY, _clock.Ticks, _wmRng,
                    getGlobal, _dudeLevel, luck, outdoorsman, Difficulty, WorldFog), area);
                _travelCadence = new Formats.Map.TerrainCadence();
                _travelStepAccumMs = 0;
                _worldmapOpen = true;
                return;
            }

            Formats.Map.WorldmapTravel.LegOutcome leg = Formats.Map.WorldmapTravel.ResolveLeg(
                Worldmap, _cities.Areas, _mapList, _worldPosX, _worldPosY, area.WorldX, area.WorldY,
                _clock.Ticks, _wmRng, getGlobal, _dudeLevel, luck, outdoorsman, Difficulty, WorldFog);

            _clock.Ticks += leg.ClockTicksAdded; // the per-step travel time across the leg
            _worldPosX = leg.FinalWorldX;
            _worldPosY = leg.FinalWorldY;
            if (leg.Encounter is { } r)
            {
                HandleLegEncounter(r, leg.EncounterMap!, area);
                return;
            }
        }

        ArriveAt(area, rolled);
    }

    /// <summary>Advance the animated worldmap dot (phase-17 M2): accumulate wall-time and,
    /// each cadence tick, let <see cref="Formats.Map.TerrainCadence"/> decide whether the
    /// dot steps one pixel (slow terrain holds it). On an encounter or arrival the leg ends
    /// and the shared handlers run. Paused while an avoid prompt is up; no-op otherwise.</summary>
    private void StepAnimatedTravel(double elapsedMs)
    {
        if (_activeTravel is null || _encounterPrompt is not null)
            return;

        _travelStepAccumMs += elapsedMs;
        while (_travelStepAccumMs >= TravelTickMs && _activeTravel is { } active)
        {
            _travelStepAccumMs -= TravelTickMs;
            int difficulty = Worldmap.TerrainTravelDifficultyAt(active.Leg.X, active.Leg.Y);
            if (!_travelCadence.Tick(difficulty))
                continue; // slow terrain: the dot lingers this tick

            Formats.Map.TravelStep s = active.Leg.Step();
            _clock.Ticks += Formats.Map.WorldmapTravel.TicksPerStep; // mirror the per-pixel travel time
            _worldPosX = s.X;
            _worldPosY = s.Y;
            if (s.Encounter is { } r)
            {
                _activeTravel = null;
                HandleLegEncounter(r, s.EncounterMap!, active.Dest);
                return;
            }
            if (s.Arrived)
            {
                _activeTravel = null;
                ArriveAt(active.Dest, rolled: true);
                return;
            }
        }
    }

    /// <summary>Handle an encounter that fired mid-leg (shared by the synchronous resolve
    /// and the animated step): a detected encounter grants (100-detect) XP then offers the
    /// yes/no avoid (live = the overlay; headless = _autoEncounterAnswer); engaging loads
    /// the transient map, avoiding resumes travel toward <paramref name="area"/>.</summary>
    private void HandleLegEncounter(Formats.Map.EncounterResult r, string encounterMap, WorldArea area)
    {
        string? name = EncounterName(r);
        if (r.Detected) // worldmap.cc:3475
        {
            if (r.AvoidXp > 0)
                AwardXp(r.AvoidXp);
            Console.WriteLine($"encounter detected: {r.Entry.Spawns.FirstOrDefault()?.Group ?? "?"}"
                + $" name=\"{name ?? "?"}\" avoidXp={r.AvoidXp} -> {encounterMap}");
            if (_autoEncounterAnswer is not { } answer)
            {
                _encounterPrompt = (r, encounterMap, name, area);
                _worldmapOpen = true; // keep the worldmap up under the prompt overlay
                Log($"You spot {name ?? "trouble"} ahead. Encounter it? (Y/N)");
                return;
            }
            if (!answer)
            {
                Log($"You avoid {name ?? "the encounter"} and travel on.");
                Console.WriteLine($"encounter avoided: continuing to area{area.Index}");
                TravelTo(area); // resume the leg from the encounter point
                return;
            }
            // engage → fall through to load the encounter map
        }

        _travelDestination = area; // remember the leg target so it auto-resumes (P16-M2)
        EngageEncounter(r, encounterMap, name);
    }

    /// <summary>Arrive at a worldmap area: resolve its entrance, advance the clock (the
    /// flat estimate only on the very first roll-less travel), record the worldmap
    /// whereabouts, and load the town map.</summary>
    private void ArriveAt(WorldArea area, bool rolled)
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
        // ResolveLeg already advanced the clock per pixel-step across the whole leg;
        // only the first travel of a game (no prior worldPos → no roll) needs the flat
        // estimate, else the clock double-counts the trip.
        if (!rolled)
            _clock.AdvanceHours(8);
        // Record the dude's worldmap whereabouts so a save round-trips it
        // (phase-10 M2); a reload drops you back on the worldmap here.
        _currentAreaId = area.Index;
        _worldPosX = area.WorldX;
        _worldPosY = area.WorldY;
        WorldFog.MarkRadiusVisited(area.WorldX, area.WorldY); // reveal the destination (P22; covers the roll-less first travel that has no leg)
        _travelDestination = null; // clean arrival — the leg is over, nothing to auto-resume
        Console.WriteLine($"travelling to {area.Name} -> {mapFile}");
        LoadMap(mapFile, new MapDestination(mapIndex, entrance.Tile, entrance.Elevation, entrance.Rotation));
        Log($"You arrive at {area.Name}.");
    }

    /// <summary>Pre-answer for a detected encounter in headless runs (phase-16 M1):
    /// true = engage, false = avoid. Null in live play → the interactive Y/N prompt.</summary>
    private bool? _autoEncounterAnswer;

    /// <summary>The destination of an in-progress travel leg (phase-16 M2, the engine's
    /// isWalking target): set when an engaged encounter interrupts the leg, cleared on a
    /// clean arrival. Leaving the encounter map back to the worldmap auto-resumes toward it.</summary>
    private WorldArea? _travelDestination;
    /// <summary>Deferred auto-resume: set when leaving a transient map mid-leg, consumed
    /// at the top of the next Update to continue travel without a re-click (phase-16 M2).</summary>
    private WorldArea? _resumeTravelDest;

    /// <summary>Animate worldmap travel as a moving dot (phase-17 M2). True in live play;
    /// the headless harness travel actions set it false so the goldens drain the whole leg
    /// synchronously (byte-identical RNG).</summary>
    private bool _animateTravel = true;
    /// <summary>The in-flight animated leg + its destination; null = not travelling. Update
    /// drains <see cref="Formats.Map.TravelLeg.Step"/> over wall-time (phase-17 M2).</summary>
    private (Formats.Map.TravelLeg Leg, WorldArea Dest)? _activeTravel;
    private Formats.Map.TerrainCadence _travelCadence = new();
    private double _travelStepAccumMs;
    private const double TravelTickMs = 30; // wall-time per cadence tick (the dot's base pace)

    /// <summary>A detected encounter awaiting the player's avoid choice in live play
    /// (phase-16 M1): the result, its transient map, display name, and the leg's
    /// destination (to resume travel on avoid). Null = no prompt up.</summary>
    private (Formats.Map.EncounterResult Enc, string MapFile, string? Name, WorldArea Dest)? _encounterPrompt;

    /// <summary>Engage a worldmap encounter: spawn the group on its transient map
    /// (phase-10 M3 path; the banner names it via worldmap.msg, phase-16 M0).</summary>
    private void EngageEncounter(Formats.Map.EncounterResult r, string mapFile, string? name)
    {
        _pendingEncounter = r;
        _worldmapOpen = false;
        Console.WriteLine($"encounter while travelling: {r.Entry.Spawns.FirstOrDefault()?.Group ?? "?"}"
            + $" name=\"{name ?? "?"}\" table={r.Table.Index} entry={r.Entry.EntryIndex} -> {mapFile}");
        Log(name is not null
            ? $"{(r.Entry.Situation == "AMBUSH" ? "Ambush! " : "")}{name}"
            : "Ambush! The wasteland bites.");
        LoadMap(mapFile, null, transient: true);
    }

    /// <summary>The worldmap RNG — persisted across travel legs so successive rolls
    /// differ; seeded off --rng-seed for golden transcripts, else wall-clock for a
    /// fresh wasteland each playthrough (phase-10 M3).</summary>
    private Formats.Combat.ICombatRng? _wmRng;

    /// <summary>The best Outdoorsman skill across the dude + companions (party_get_best_
    /// skill_value), feeding the encounter detect-and-avoid roll (phase-10 #12). 17 =
    /// SKILL_OUTDOORSMAN.</summary>
    private int? _forceOutdoorsman; // phase-16 M1 test override (force the detect path)
    private int PartyBestOutdoorsman()
    {
        if (_forceOutdoorsman is { } forced)
            return forced;
        int best = (_dude is not null ? GetCritterState(_dude.Dude)?.SkillValue(17) : 0) ?? 0;
        foreach (MapObject m in _scriptHost?.PartyMembers ?? [])
            best = Math.Max(best, GetCritterState(m)?.SkillValue(17) ?? 0);
        return best;
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
                _baseTitle = $"Hexwaste viewer — {_map.Header.Name} (elevation {_elevation})";
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
        // objects -> roofs. Floors render as lit quads (BasicEffect) before
        // the sprite batch opens.
        if (!_worldmapOpen && _map is not null)
        {
            _dudeUnderRoof = DudeIsUnderRoof();
            DrawFloors();
        }

        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        if (_worldmapOpen)
        {
            _worldmapScreen?.Draw(_spriteBatch, GraphicsDevice.Viewport.Bounds, _hoveredArea, WorldFog);
            // The party dot: "you are here" whenever a worldmap position is known, and the
            // moving marker mid-travel (phase-17 M2/M3 — one dot, the unified position).
            if (_worldPosX >= 0 && _worldPosY >= 0)
                _worldmapScreen?.DrawPartyDot(_spriteBatch, GraphicsDevice.Viewport.Bounds, _worldPosX, _worldPosY);
            DrawEncounterPrompt();
        }
        else
        {
            // The world draws only with a loaded map; without one (a failed/soft-failed load → the
            // title menu) the UI overlays below still render. Inert for every real flow (_map is always
            // loaded there); this just keeps a missing-map soft-fail from NPE-ing on null draw lists.
            if (_map is not null)
            {
                DrawObjects(_flatObjects[_elevation]);
                DrawObjects(_solidObjects[_elevation]);
                DrawProjectiles();
                if (_roofsVisible)
                    DrawRoofs();
                DrawCombatText(); // P45: over the world, under the HUD bar
                DrawInterfaceBar();
            }
            DrawTextOverlay();
            DrawDialogPanel();
            DrawItemPanels();
            DrawSkillAllocator();
            DrawPerkPicker();
            DrawSkilldex();
            DrawPipboy();
            DrawAutomap();
            DrawOptions();
            DrawSaveLoad();
            DrawAimDialog();
            DrawTactics();
        }
        DrawMapFade(gameTime); // P52-M6: the post-load fade-in, on top of everything
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

    /// <summary>
    /// Picks the topmost object whose sprite has an opaque pixel under the
    /// cursor — objects are tested in reverse draw order, and palette index 0
    /// pixels are transparent, so clicks fall through to whatever is beneath
    /// (the behavior DarkFO called out as essential for dense scenes).
    /// </summary>
    private MapObject? PickObject(int screenX, int screenY)
    {
        // Reverse draw order without allocating (hot path: every frame for hover).
        MapObject? hit = PickFromList(_solidObjects[_elevation], screenX, screenY);
        return hit ?? PickFromList(_flatObjects[_elevation], screenX, screenY);
    }

    private MapObject? PickFromList(List<MapObject> objects, int screenX, int screenY)
    {
        for (int i = objects.Count - 1; i >= 0; i--)
        {
            MapObject obj = objects[i];
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

    public string ObjectName(MapObject obj) =>
        _protoMessages.GetName(obj.Pid) ?? $"object 0x{obj.Pid:X8}";

    private string ObjectDescription(MapObject obj) =>
        _protoMessages.GetDescription(obj.Pid)
        ?? "You see nothing out of the ordinary."; // the game's default examine line

    public void Log(string message)
    {
        _messageLog.Add(message);
        // P52-M5: keep a faithful 100-line history (display_monitor.cc ring) instead of the old 5.
        if (_messageLog.Count > Formats.MonitorScrollback.Capacity)
            _messageLog.RemoveAt(0);
        _monitorScroll = 0; // a new message jumps the view to newest (displayMonitorAddMessage: _disp_curr = _disp_start)
        QueueCombatFloat(message);
    }

    // ====================================================================
    //  Floating combat text (P45 — "Numbers in the Air")
    // ====================================================================

    /// <summary>The floating combat-text layer: damage numbers / "Missed" / crit
    /// feedback rising over struck critters. Draw-only + wall-time-ticked, so the
    /// golden suites stay byte-identical (it never writes the transcript).</summary>
    private readonly CombatTextLayer _floatText = new();

    /// <summary>P45: the in-flight attack's real defender, captured at OnAttackStarted/
    /// OnThrowStarted (the Log wording can't be trusted for it). Drives where the float
    /// lands and the dude-vs-NPC colour shade.</summary>
    private MapObject? _floatDefender;

    /// <summary>Present a combat OUTCOME as a floating number / "Missed" over the
    /// defender. The damage int is parsed from the SAME value the host logs (the
    /// Hexwaste-authored Log line — NOT a combat.msg game string), and the defender
    /// object comes from <see cref="_floatDefender"/>. DOCUMENTED DIVERGENCE: Fallout 2
    /// sends combat outcomes to the monitor log, not floats (combat.cc _combat_display);
    /// this is a presentation layer on the engine's real text_object.cc float mechanism.</summary>
    private void QueueCombatFloat(string line)
    {
        if (_floatDefender is not { } defender)
            return;
        // Burst collateral ("The burst also catches the X ...") names a bystander, not the
        // tracked defender — its float is a documented cut (the main target's still floats).
        if (line.Contains("also catches", StringComparison.Ordinal))
            return;

        Microsoft.Xna.Framework.Color color;
        string text;
        System.Text.RegularExpressions.Match dmg =
            System.Text.RegularExpressions.Regex.Match(line, @"for (\d+) damage\.");
        if (dmg.Success) // a landed hit: "...for N damage." (single / burst / thrown, you- or NPC-phrased)
        {
            bool crit = line.EndsWith("Critical hit!", StringComparison.Ordinal);
            bool dudeHit = defender == _dude?.Dude;
            color = crit ? CombatFloatColors.Critical
                : dudeHit ? CombatFloatColors.DamageDude
                : CombatFloatColors.DamageNpc;
            text = dmg.Groups[1].Value;
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(line, @"(missed the |misses you\b| misses\.$|burst misses )"))
        {
            color = CombatFloatColors.Miss;
            text = "Missed"; // a hardcoded English word — NOT read from the user's combat.msg
        }
        else
        {
            return; // not a combat-outcome line (script/status text, "X dies.", etc.)
        }

        _floatText.Add(defender.HexTile, _elevation, text, color);
        // One outcome per attack: clear so a later non-combat "...for N damage." line can't reuse a
        // stale defender. Every attack re-sets it via OnAttackStarted/OnThrowStarted before its Log.
        _floatDefender = null;
    }

    /// <summary>Render the floating combat text — over the world, under the HUD bar.</summary>
    private void DrawCombatText()
    {
        if (_fontRenderer is not null)
            _floatText.Draw(_spriteBatch, _fontRenderer, _camera.HexToScreen, _elevation);
    }

    /// <summary>P52-M6: the post-load fade-in — a full-screen black quad whose alpha ramps from
    /// opaque to clear over <see cref="MapFadeSeconds"/> of WALL time, pumped here (so the headless
    /// harness, which never calls Draw, never advances it). The engine fades via paletteFadeTo
    /// (map.cc mapLoad); we have no palette texture, so this GPU quad is the visible analogue.
    /// Skipped while screenshotting so a verification capture shows the composed map, not the fade.</summary>
    private void DrawMapFade(GameTime gameTime)
    {
        if (_mapFadeElapsed >= MapFadeSeconds || _screenshotPath is not null)
            return;
        _panelPixel ??= CreatePixel();
        float alpha = (float)Math.Clamp(1.0 - _mapFadeElapsed / MapFadeSeconds, 0.0, 1.0);
        _spriteBatch.Draw(_panelPixel, GraphicsDevice.Viewport.Bounds, Color.Black * alpha);
        _mapFadeElapsed += gameTime.ElapsedGameTime.TotalSeconds;
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
        }
        else
        {
            Log($"{ObjectName(obj)}: {ObjectDescription(obj)}");
        }

        if (obj != _dude?.Dude && GetCritterState(obj) is { } state)
            Log($"HP: {state.CurrentHp}/{state.MaxHp}, AC: {state.ArmorClass}");
    }

    /// <summary>Effective combat stats for critters with parsed protos; null
    /// for non-critters and broken pids. The dude uses his gcd sheet.</summary>
    public Formats.Combat.CritterState? GetCritterState(MapObject obj)
    {
        if (obj == _dude?.Dude && _dudeGcd is not null)
            return new Formats.Combat.CritterState(obj, _dudeGcd.Stats, _dudeGcd.TaggedSkills, _dudeGcd.Traits, _dudePerkRanks);
        if (Fid.PidType(obj.Pid) != (int)ObjectType.Critter)
            return null;
        // A leveled-up companion reads its swapped-in stage proto, not the base
        // (#10 M2 / #13). Per-instance, so the shared proto cache stays pristine.
        // P29-M6: a recruited companion's perk ranks (null on the slice → inert) feed the same
        // CritterState 5th arg the dude uses, so any future companion perk applies for free.
        int[]? companionPerks = _companionPerkRanks.GetValueOrDefault(obj);
        if (_companionStatOverride.TryGetValue(obj, out Formats.Proto.CritterProtoStats? overrideStats))
            return new Formats.Combat.CritterState(obj, overrideStats, perkRanks: companionPerks);
        try
        {
            return _protos.Get(obj.Pid).Critter is { } stats
                ? new Formats.Combat.CritterState(obj, stats, perkRanks: companionPerks)
                : null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            return null;
        }
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

    /// <summary>P53: a dialogue REPLY resolved a message-list entry — look up its audio field and play the
    /// speech file (sound\speech\&lt;audio&gt;.acm). Inert on the slice (every line's audio field is empty, and
    /// the GOG data ships no speech assets); lights up only when voiced content is installed. headIsValid is
    /// true — Hexwaste renders no talking head, so the engine's head-FID gate (scripts.cc:2746) cannot apply.</summary>
    private void PlayDialogVoice(int messageListId, int messageId)
    {
        if (_scriptHost?.LookupAudio(messageListId, messageId) is { } audio
            && Formats.Sound.SpeechName.ShouldSpeak(isReply: true, headIsValid: true, audio))
            _audio?.PlaySpeech(audio);
    }

    /// <summary>Text dialog panel: reply on top, numbered options below (keys 1-9 or click).</summary>
    private void DrawDialogPanel()
    {
        if (_companionHub is not null)
        {
            DrawConversationPanel(ObjectName(_companionHub), "What do you need?",
                [.. _hubOptions.Select(o => o.Label)]);
            return;
        }
        if (_dialog is not null)
            DrawConversationPanel(_dialog.NpcName, _dialog.Reply, _dialog.Options, _dialog.OptionReactions);
    }

    /// <summary>The shared conversation panel — reply text + numbered options at the
    /// bottom of the screen. Drives both scripted dialog and the companion-control hub
    /// (phase-10 M4); <see cref="_dialogOptionRects"/> feeds mouse hit-testing.</summary>
    private void DrawConversationPanel(string name, string reply, IReadOnlyList<string> options,
        IReadOnlyList<int>? reactions = null)
    {
        if (_fontRenderer is null)
            return;

        // P52-M1: with the Empathy perk the engine tints each dialogue option by the NPC's
        // reaction to it (game_dialog.cc gameDialogOptionOnMouseEnter:2118 / onMouseExit:2162).
        bool empathy = reactions is not null && DudePerkRank(Formats.Perks.PerkId.Empathy) > 0;

        _panelPixel ??= CreatePixel();

        Rectangle viewport = GraphicsDevice.Viewport.Bounds;
        int panelWidth = Math.Min(720, viewport.Width - 40);
        int textWidth = panelWidth - 32;
        int lineHeight = _fontRenderer.LineHeight;

        List<string> replyLines = _fontRenderer.WrapText(reply, textWidth);
        var optionLines = new List<(int Option, string Line, bool First)>();
        for (int i = 0; i < options.Count; i++)
        {
            List<string> wrapped = _fontRenderer.WrapText($"{i + 1}. {options[i]}", textWidth - 12);
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

        _fontRenderer.Draw(_spriteBatch, name, new Vector2(panelX + 16, y), Color.LightGray);
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
            Color color = hovered ? Color.Yellow : green;
            if (empathy && option < reactions!.Count)
                color = EmpathyOptionColor(Formats.Int.DialogReaction.Classify(reactions[option]), hovered);
            _fontRenderer.Draw(_spriteBatch, line,
                new Vector2(panelX + 16 + (first ? 0 : 12), y), color);
            y += lineHeight;
        }
        if (currentOption >= 0)
            _dialogOptionRects.Add(currentRect);
    }

    /// <summary>Empathy-perk option colour by reaction (game_dialog.cc:2120/2164). The engine picks
    /// _colorTable[idx] entries; we render the RGB555 those indices encode directly (DOCUMENTED
    /// DIVERGENCE: no palette-nearest remap — Hexwaste has no 8-bit dialogue palette). Neutral keeps
    /// the base colour, matching the engine. hovered = the brighter onMouseEnter set.</summary>
    private static Color EmpathyOptionColor(Formats.Int.DialogReactionLevel level, bool hovered) => level switch
    {
        // GOOD: onMouseEnter _colorTable[31775] (R31 G0 B31) / onMouseExit _colorTable[31] (B31).
        Formats.Int.DialogReactionLevel.Good => hovered ? new Color(255, 0, 255) : new Color(0, 0, 255),
        // BAD: onMouseEnter _colorTable[32074] (R31 G10 B10) / onMouseExit _colorTable[31744] (R31).
        Formats.Int.DialogReactionLevel.Bad => hovered ? new Color(255, 82, 82) : new Color(255, 0, 0),
        // NEUTRAL: the base colours — onMouseEnter _colorTable[32747] / onMouseExit _colorTable[992].
        _ => hovered ? Color.Yellow : new Color(0, 252, 0),
    };

    /// <summary>The character sheet (C / K): SPECIAL + derived stats + level
    /// on the left, the 18 skills on the right. Read-only, but a skill can be
    /// raised in place while banked points remain (Right/Enter).</summary>
    private void DrawSkillAllocator()
    {
        if (!_skillAllocOpen || _fontRenderer is null || _dudeGcd is null)
            return;

        _panelPixel ??= CreatePixel();
        int lh = Math.Max(_fontRenderer.LineHeight, 22);
        int x = 48, y = 28, w = 660;
        int h = (Formats.Combat.SkillSet.SkillCount + 3) * lh + 16;
        _spriteBatch.Draw(_panelPixel, new Rectangle(x, y, w, h), new Color(8, 8, 8, 238));

        var gold = new Color(252, 252, 84);
        var green = new Color(0, 252, 0);
        var gray = new Color(150, 150, 150);
        int[] b = _dudeGcd.Stats.BaseStats, bo = _dudeGcd.Stats.BonusStats, sk = _dudeGcd.Stats.Skills;
        int[] tags = _dudeGcd.TaggedSkills;
        int Stat(int i) => b[i] + bo[i];

        // ---- left column: header + SPECIAL + derived ----
        int lx = x + 14, ly = y + 10;
        void Line(string text, Color c) { _fontRenderer.Draw(_spriteBatch, text, new Vector2(lx, ly), c); ly += lh; }
        string name = _dudeGcd is { Name.Length: > 0 } g && g.Name != "None" ? g.Name : "Wanderer";
        Line($"{name}  —  Level {_dudeLevel}", gold);
        int nextXp = Formats.Combat.Progression.XpForLevel(_dudeLevel + 1);
        Line($"XP {_dudeXp}" + (nextXp > 0 ? $" / {nextXp}" : " (max)"), gray);
        Formats.Combat.CritterState? cs = _dude is not null ? GetCritterState(_dude.Dude) : null;
        if (cs is not null)
        {
            Line($"HP {_dude!.Dude.CurrentHp}/{cs.MaxHp}   AP {cs.MaxActionPoints}", green);
            ly += 4;
            string[] sp = ["ST", "PE", "EN", "CH", "IN", "AG", "LK"];
            for (int i = 0; i < 7; i++)
                _fontRenderer.Draw(_spriteBatch, $"{sp[i]} {cs.Stat(i)}", // effective (trait/perk-modified), like the derived stats
                    new Vector2(lx + (i % 2) * 130, ly + i / 2 * lh), gold);
            ly += 4 * lh + 6;
            Line($"Armor Class {cs.ArmorClass}", gray);
            Line($"Melee Damage {cs.MeleeDamage}", gray);
            Line($"Sequence {cs.Sequence}", gray);
            Line($"Critical % {Stat(Formats.Combat.CritterStat.CriticalChance)}", gray);
            Line($"Healing Rate {Math.Max(Stat(Formats.Combat.CritterStat.Endurance) / 3, 1)}", gray);
        }
        ly += 6;
        // Traits + perks (P28-M4): the character-progression payoff.
        string traitStr = string.Join(", ", _dudeGcd.Traits.Where(t => t >= 0).Select(TraitName));
        Line($"Traits: {(traitStr.Length > 0 ? traitStr : "none")}", gray);
        var takenPerks = Enumerable.Range(0, _dudePerkRanks.Length).Where(i => _dudePerkRanks[i] > 0)
            .Select(i => _dudePerkRanks[i] > 1 ? $"{PerkName(i)} ({_dudePerkRanks[i]})" : PerkName(i)).ToList();
        Line($"Perks: {(takenPerks.Count > 0 ? string.Join(", ", takenPerks) : "none")}", gray);
        if (AvailablePerkPicks() > 0)
            Line($"{AvailablePerkPicks()} perk(s) available — press G", green);
        ly += 6;
        // Karma / reputation (P31 B-M3): the karma number + generic-reputation title + any earned
        // karma titles + non-Neutral slice-town standings. Display-only (never transcript-diffed).
        foreach (string kl in KarmaDisplayLines())
            Line(kl, gray);
        // Kills by type (P38; character_editor.cc:2202 "::: Kills :::") — char sheet only.
        List<string> killLines = KillDisplayLines();
        if (killLines.Count > 0)
        {
            ly += 6;
            Line("Kills:", gray);
            foreach (string kl in killLines)
                Line($"  {kl}", gray);
        }
        ly += 6;
        if (_unspentSkillPoints > 0)
            Line($"{_unspentSkillPoints} skill points — raise →", green);
        _fontRenderer.Draw(_spriteBatch, "C / K / G perk / Esc close", new Vector2(lx, y + h - lh - 8), gray);

        // ---- right column: the 18 skills ----
        int rx = x + 330;
        int rowY = y + 10;
        for (int i = 0; i < Formats.Combat.SkillSet.SkillCount; i++)
        {
            int value = Formats.Combat.SkillSet.Value(b, bo, sk, tags, i);
            bool tagged = Array.IndexOf(tags, i) >= 0;
            bool selected = i == _skillAllocIndex && _unspentSkillPoints > 0;
            string tag = tagged ? " (T)" : "";
            string cost = selected ? $"  +1={Formats.Combat.SkillSet.Cost(value)}" : "";
            _fontRenderer.Draw(_spriteBatch, $"{(selected ? ">" : " ")} {Formats.Combat.SkillSet.Names[i]}{tag}",
                new Vector2(rx, rowY), selected ? green : (tagged ? gold : gray));
            _fontRenderer.Draw(_spriteBatch, $"{value}%{cost}", new Vector2(rx + 220, rowY),
                selected ? green : gray);
            rowY += lh;
        }
    }

    // --- Perk selection (P28-M4) -----------------------------------------

    private bool _perkPickOpen;
    private const int PerkPickRows = 12; // perks shown in the picker (the slice never offers more)

    private bool DudeHasSkilled() =>
        _dudeGcd is not null && Formats.Combat.TraitModifiers.Has(_dudeGcd.Traits, Formats.Combat.TraitModifiers.Skilled);

    /// <summary>Perk picks earned by the dude's level minus the ones already taken (one per 3
    /// levels, 4 with Skilled; PerkRules cadence).</summary>
    private int AvailablePerkPicks() => _dudeGcd is null
        ? 0
        : Math.Max(0, Formats.Perks.PerkRules.PicksEarned(_dudeLevel, DudeHasSkilled()) - _dudePerkRanks.Sum());

    /// <summary>The perk indices the dude currently qualifies for (PerkRules.CanAdd over the live
    /// stats/skills/globals), in enum order.</summary>
    private List<int> EligiblePerks()
    {
        var list = new List<int>();
        if (_dude is null)
            return list;
        int GetStat(int s) => GetCritterState(_dude.Dude)?.Stat(s) ?? 0;
        int GetSkill(int s) => GetCritterState(_dude.Dude)?.SkillValue(s) ?? 0;
        int GetGlobal(int g) => _scriptHost?.GlobalVars.GetValueOrDefault(g, 0) ?? 0;
        for (int i = 0; i < Formats.Perks.PerkTable.Count; i++)
            if (Formats.Perks.PerkRules.CanAdd(Formats.Perks.PerkTable.Get(i), _dudePerkRanks, _dudeLevel, GetStat, GetSkill, GetGlobal))
                list.Add(i);
        return list;
    }

    /// <summary>Take a rank of <paramref name="perkIndex"/> if it's eligible and a pick is
    /// available (the picker's commit). Closes the picker when no picks remain.</summary>
    private void ChoosePerk(int perkIndex)
    {
        if (AvailablePerkPicks() <= 0 || !EligiblePerks().Contains(perkIndex))
            return;
        _dudePerkRanks[perkIndex]++;
        Log($"You gain a new perk: {PerkName(perkIndex)}.");
        if (AvailablePerkPicks() <= 0)
            _perkPickOpen = false;
    }

    // PERKWIN.FRM layout (character_editor.cc:89-95): window 573x230, perk list at window-local
    // (45,43) 192x129, the perk card name at (280,27) / description at (280,70).
    private const int PerkWinW = 573, PerkWinH = 230;
    private const int PerkWinListX = 45, PerkWinListY = 43, PerkWinListW = 192, PerkWinListH = 129;
    private const int PerkWinCardX = 280;

    /// <summary>Top-left of the centred PERKWIN window + the per-row height (the list area divided so
    /// up to ~11 perks fit). One source the render + hit-test share (the SkilldexRowAt pattern).</summary>
    private Point PerkWindowOrigin(out int rowH, out int rowsShown, int eligCount)
    {
        rowH = Math.Max(_fontRenderer!.LineHeight + 1, 11);
        rowsShown = Math.Min(eligCount, PerkWinListH / rowH);
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        return new Point(Math.Max(0, (vp.Width - PerkWinW) / 2), Math.Max(0, (vp.Height - PerkWinH) / 2));
    }

    /// <summary>The eligible-perk row under (mx,my), or -1 — the list rows at
    /// (origin + 45, 43 + i*rowH), width 192.</summary>
    private int PerkPickerRowAt(int mx, int my)
    {
        if (!_perkPickOpen || _fontRenderer is null || _perkWin is null)
            return -1;
        List<int> elig = EligiblePerks();
        Point o = PerkWindowOrigin(out int rowH, out int rowsShown, elig.Count);
        for (int i = 0; i < rowsShown; i++)
        {
            var r = new Rectangle(o.X + PerkWinListX, o.Y + PerkWinListY + i * rowH, PerkWinListW, rowH);
            if (r.Contains(mx, my))
                return i;
        }
        return -1;
    }

    /// <summary>The level-up perk picker. P29-M5: the authentic PERKWIN.FRM panel (the perk list on
    /// the left, the hovered perk's name + wrapped description card on the right), falling back to the
    /// text flyout when the art is absent (the Skilldex pattern). Click a row (or 1-9) to take it.</summary>
    private void DrawPerkPicker()
    {
        if (!_perkPickOpen || _fontRenderer is null)
            return;
        if (!_perkWinTried) { _perkWinTried = true; _perkWin = InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\PERKWIN.frm"); }
        if (_perkWin is null)
        {
            DrawPerkPickerTextFallback();
            return;
        }

        List<int> elig = EligiblePerks();
        Point o = PerkWindowOrigin(out int rowH, out int rowsShown, elig.Count);
        _spriteBatch.Draw(_perkWin, new Vector2(o.X, o.Y), Color.White);

        var green = new Color(0, 252, 0);
        var hi = new Color(252, 252, 84);
        var cardColor = new Color(0, 0, 0); // the card area is parchment — dark text reads on it
        int hovered = PerkPickerRowAt(Mouse.GetState().X, Mouse.GetState().Y);
        for (int i = 0; i < rowsShown; i++)
        {
            int pi = elig[i];
            string rank = _dudePerkRanks[pi] > 0 ? $" ({_dudePerkRanks[pi]}/{Formats.Perks.PerkTable.Get(pi).MaxRank})" : "";
            _fontRenderer.Draw(_spriteBatch, PerkName(pi) + rank,
                new Vector2(o.X + PerkWinListX + 4, o.Y + PerkWinListY + i * rowH), i == hovered ? hi : green);
        }

        // The perk card for the hovered (or first) eligible perk: name + wrapped description.
        int card = hovered >= 0 ? elig[hovered] : (elig.Count > 0 ? elig[0] : -1);
        if (card >= 0)
        {
            _fontRenderer.Draw(_spriteBatch, PerkName(card), new Vector2(o.X + PerkWinCardX, o.Y + 27), cardColor);
            float dy = o.Y + 70;
            foreach (string line in _fontRenderer.WrapText(PerkDescription(card), PerkWinW - PerkWinCardX - 24))
            {
                _fontRenderer.Draw(_spriteBatch, line, new Vector2(o.X + PerkWinCardX, dy), cardColor);
                dy += _fontRenderer.LineHeight;
            }
        }
        if (elig.Count == 0)
            _fontRenderer.Draw(_spriteBatch, "(none qualify)", new Vector2(o.X + PerkWinListX + 4, o.Y + PerkWinListY), green);
    }

    /// <summary>The pre-art text flyout, kept as the fallback when PERKWIN.FRM is absent.</summary>
    private void DrawPerkPickerTextFallback()
    {
        _panelPixel ??= CreatePixel();
        int lh = Math.Max(_fontRenderer!.LineHeight, 22);
        List<int> elig = EligiblePerks();
        int shown = Math.Min(elig.Count, PerkPickRows);
        int x = 360, y = 40, w = 320, h = (shown + 3) * lh + 16;
        _spriteBatch.Draw(_panelPixel, new Rectangle(x, y, w, h), new Color(8, 8, 8, 240));
        var green = new Color(0, 252, 0);
        var gray = new Color(150, 150, 150);
        _fontRenderer.Draw(_spriteBatch, $"Pick a perk ({AvailablePerkPicks()} available)", new Vector2(x + 12, y + 10), new Color(252, 252, 84));
        int rowY = y + 10 + lh + 6;
        for (int row = 0; row < shown; row++)
        {
            int pi = elig[row];
            string rank = _dudePerkRanks[pi] > 0 ? $" ({_dudePerkRanks[pi]}/{Formats.Perks.PerkTable.Get(pi).MaxRank})" : "";
            _fontRenderer.Draw(_spriteBatch, $"{row + 1}. {PerkName(pi)}{rank}", new Vector2(x + 12, rowY), green);
            rowY += lh;
        }
        if (elig.Count == 0)
            _fontRenderer.Draw(_spriteBatch, "(none qualify)", new Vector2(x + 12, rowY), gray);
        _fontRenderer.Draw(_spriteBatch, "1-9 pick / Esc close", new Vector2(x + 12, y + h - lh - 8), gray);
    }

    /// <summary>Top-left of the Skilldex box: bottom-right, just above the HUD bar
    /// (skilldex.cc:225-226 — right margin 4, bottom margin 6). btnW/btnH = the SKLDXOFF
    /// button size; row i sits at bar-local (15, 45 + i*36).</summary>
    private Point SkilldexOrigin(out int boxW, out int boxH, out int btnW, out int btnH)
    {
        boxW = _skilldexBox?.Width ?? 185;
        boxH = _skilldexBox?.Height ?? 368;
        btnW = _skilldexBtnOff?.Width ?? 88;
        btnH = _skilldexBtnOff?.Height ?? 33;
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        return new Point(Math.Max(0, vp.Width - boxW - 4), Math.Max(0, vp.Height - _hudBarHeight - boxH - 6));
    }

    /// <summary>The Skilldex row index under (mx,my), or -1 — the 8 buttons at
    /// (origin + 15, 45 + i*36), size btnW×btnH.</summary>
    private int SkilldexRowAt(int mx, int my)
    {
        Point o = SkilldexOrigin(out _, out _, out int btnW, out int btnH);
        for (int i = 0; i < SkilldexSkills.Length; i++)
        {
            var r = new Rectangle(o.X + 15, o.Y + 45 + i * 36, btnW, btnH);
            if (r.Contains(mx, my))
                return i;
        }
        return -1;
    }

    /// <summary>The Skilldex use-skill picker (P12 M0 + P13 art upgrade) — the authentic
    /// SKLDXBOX.FRM panel with SKLDXOFF/SKLDXON buttons, bottom-right above the bar
    /// (skilldex.cc). The skill name is centred on each button and the % is shown to its
    /// right; the hovered row lights with SKLDXON. Click a row (or press 1-8) to arm the
    /// skill. Falls back to a text flyout if the art is missing.</summary>
    private void DrawSkilldex()
    {
        if (!_skilldexOpen || _fontRenderer is null)
            return;

        _skilldexBox ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\SKLDXBOX.frm");
        _skilldexBtnOff ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\SKLDXOFF.frm");
        _skilldexBtnOn ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\SKLDXON.frm");
        if (_skilldexBox is null)
        {
            DrawSkilldexTextFallback();
            return;
        }

        Point o = SkilldexOrigin(out _, out _, out int btnW, out int btnH);
        var titleColor = new Color(252, 252, 84);
        var nameColor = new Color(0, 252, 0);
        var dim = new Color(0, 168, 0);

        _spriteBatch.Draw(_skilldexBox, new Vector2(o.X, o.Y), Color.White);
        _fontRenderer.Draw(_spriteBatch, "SKILLDEX", new Vector2(o.X + 55, o.Y + 14), titleColor);

        MouseState m = Mouse.GetState();
        int hovered = SkilldexRowAt(m.X, m.Y);
        for (int i = 0; i < SkilldexSkills.Length; i++)
        {
            int skill = SkilldexSkills[i];
            var btnPos = new Vector2(o.X + 15, o.Y + 45 + i * 36);
            Texture2D? btn = i == hovered ? _skilldexBtnOn : _skilldexBtnOff;
            if (btn is not null)
                _spriteBatch.Draw(btn, btnPos, Color.White);

            string name = SkillName(skill);
            int nameX = Math.Max(0, (btnW - _fontRenderer.MeasureWidth(name)) / 2);
            int nameY = Math.Max(0, (btnH - _fontRenderer.LineHeight) / 2);
            _fontRenderer.Draw(_spriteBatch, name, new Vector2(btnPos.X + nameX, btnPos.Y + nameY), nameColor);

            // The box bakes placeholder "223 %%" digits in each readout (like iface.frm);
            // field-blank them to the recess colour (32,32,32) and draw the real value
            // right-aligned (skilldex.cc blits BIG_NUMBERS here at x=110).
            _panelPixel ??= CreatePixel();
            int fieldX = o.X + 100, fieldW = 72, fieldY = o.Y + 46 + i * 36;
            _spriteBatch.Draw(_panelPixel, new Rectangle(fieldX, fieldY, fieldW, 26), new Color(32, 32, 32));
            string val = $"{DudeSkillValue(skill)}%";
            _fontRenderer.Draw(_spriteBatch, val,
                new Vector2(fieldX + fieldW - _fontRenderer.MeasureWidth(val) - 4, fieldY + (26 - _fontRenderer.LineHeight) / 2), dim);
        }
    }

    /// <summary>The pre-art text flyout, kept as the fallback when SKLDXBOX is absent.</summary>
    private void DrawSkilldexTextFallback()
    {
        _panelPixel ??= CreatePixel();
        int lh = Math.Max(_fontRenderer!.LineHeight, 18);
        int w = 220, h = (SkilldexSkills.Length + 2) * lh + 12;
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int x = vp.Width - w - 12;
        int y = vp.Height - _hudBarHeight - h - 6;
        _spriteBatch.Draw(_panelPixel, new Rectangle(x, y, w, h), new Color(8, 8, 8, 238));

        var gold = new Color(252, 252, 84);
        var green = new Color(0, 252, 0);
        var gray = new Color(150, 150, 150);
        int ty = y + 8;
        _fontRenderer.Draw(_spriteBatch, "SKILLDEX", new Vector2(x + 12, ty), gold); ty += lh;
        for (int i = 0; i < SkilldexSkills.Length; i++)
        {
            int skill = SkilldexSkills[i];
            _fontRenderer.Draw(_spriteBatch, $"{i + 1}. {SkillName(skill)}", new Vector2(x + 12, ty), green);
            _fontRenderer.Draw(_spriteBatch, $"{DudeSkillValue(skill)}%", new Vector2(x + w - 50, ty), gray);
            ty += lh;
        }
        _fontRenderer.Draw(_spriteBatch, "1-8 use, Esc/S close", new Vector2(x + 12, y + h - lh - 4), gray);
    }

    /// <summary>The Pip-Boy panel (P12 M1): the authentic PIP.FRM (640x480) centred,
    /// with the date/time, a character STATUS page, and a REST sub-page (durations).
    /// Automaps / archives / alarm are out of scope (content-gated). Reuses the AAF font
    /// (green) like the HUD monitor; the "date" is our game-day + clock — no full
    /// calendar (a documented simplification, since our GameClock tracks only ticks).</summary>
    // Pip-Boy content origin + line height — shared by DrawPipboy (render) and the
    // PipboyRow* helpers (hit-test) so a row click always lands where it's drawn.
    private void PipboyContentOrigin(out Point po, out int lh)
    {
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int pw = _pipboyBg?.Width ?? 640, ph = _pipboyBg?.Height ?? 480;
        po = new Point(Math.Max(0, (vp.Width - pw) / 2), Math.Max(0, (vp.Height - ph) / 2));
        lh = (_fontRenderer?.LineHeight ?? 16) + 4;
    }

    // The clickable rows the current Pip-Boy page offers, paired with the action each
    // fires — the SINGLE dispatch shared by the row click and (where they overlap) the
    // keyboard. Rest rows call DoRest without closing the menu, matching the number keys.
    private List<(string Label, Action OnClick)> PipboyRows()
    {
        var rows = new List<(string, Action)>();
        if (!_pipboyRestMenu)
        {
            rows.Add(("Rest", () => _pipboyRestMenu = true));
            rows.Add(("Automap", () => { _pipboyOpen = false; _automapOpen = true; }));
            rows.Add(("Close", () => _pipboyOpen = false));
        }
        else
        {
            for (int i = 0; i < RestOptions.Length; i++)
            {
                int min = RestOptions[i].Minutes;
                rows.Add(($"{i + 1}. {RestOptions[i].Label}", () => DoRest(min)));
            }
            rows.Add(("Back", () => _pipboyRestMenu = false));
        }
        return rows;
    }

    // The clickable rows render in a fixed band below the page's info text (reserve 9
    // lines for the status block, 2 for the REST header) so the geometry is computable
    // independent of the variable status content.
    private Rectangle PipboyRowRect(int index)
    {
        PipboyContentOrigin(out Point po, out int lh);
        int baseY = po.Y + 46 + (_pipboyRestMenu ? 2 : 9) * lh + 8;
        return new Rectangle(po.X + 254 - 6, baseY + index * lh - 2, 220, lh);
    }

    private int PipboyRowAt(int mx, int my)
    {
        int n = PipboyRows().Count;
        for (int i = 0; i < n; i++)
            if (PipboyRowRect(i).Contains(mx, my))
                return i;
        return -1;
    }

    private void DrawPipboy()
    {
        if (!_pipboyOpen || _fontRenderer is null)
            return;
        _pipboyBg ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\PIP.frm");

        PipboyContentOrigin(out Point po, out int lh);
        int pw = _pipboyBg?.Width ?? 640, ph = _pipboyBg?.Height ?? 480;
        var green = new Color(0, 252, 0);
        var dim = new Color(0, 160, 0);
        var hot = new Color(252, 252, 84);

        if (_pipboyBg is not null)
            _spriteBatch.Draw(_pipboyBg, new Vector2(po.X, po.Y), Color.White);
        else
        {
            _panelPixel ??= CreatePixel();
            _spriteBatch.Draw(_panelPixel, new Rectangle(po.X, po.Y, pw, ph), new Color(8, 16, 8, 240));
        }

        // Date/time, top-left (pipboy.cc PIPBOY_WINDOW_DAY/TIME positions 20,17 / 155,17).
        // P20-M3: the real FO2 calendar date (scripts.cc gameTimeGetDate) — not a day count.
        _fontRenderer.Draw(_spriteBatch, _clock.DateString, new Vector2(po.X + 20, po.Y + 17), green);
        _fontRenderer.Draw(_spriteBatch, $"{_clock.Hour / 100:00}:{_clock.Hour % 100:00}",
            new Vector2(po.X + 155, po.Y + 17), green);

        // Content view (pipboy.cc CONTENT_VIEW 254,46): the info block, then the clickable rows.
        int cx = po.X + 254, ty = po.Y + 46;
        void Line(string text, Color c) { _fontRenderer!.Draw(_spriteBatch, text, new Vector2(cx, ty), c); ty += lh; }

        // The embedded mini-map fills the empty left column on the status page (P20-M1).
        if (!_pipboyRestMenu)
            DrawPipboyMiniMap(po.X + 18, po.Y + 46, 210, ph - 92);

        if (!_pipboyRestMenu)
        {
            Line("STATUS", green); ty += 4;
            string name = _dudeGcd is { Name.Length: > 0 } g && g.Name != "None" ? g.Name : "Wanderer";
            Line(name, green);
            Line($"Level {_dudeLevel}   XP {_dudeXp}", dim);
            if (_dude is not null && GetCritterState(_dude.Dude) is { } st)
            {
                Line($"Hit Points {_dude.Dude.CurrentHp}/{st.MaxHp}", dim);
                Line($"Armor Class {st.ArmorClass}", dim);
                Line($"Action Points {st.MaxActionPoints}", dim);
                int carried = DudeCarriedWeight();
                Line($"Carry Weight {carried}/{st.CarryWeight}", // red when over (P24)
                    Formats.Map.InventoryWeight.IsEncumbered(carried, st.CarryWeight) ? new Color(255, 64, 64) : dim);
            }
            ty += 4;
            foreach (string kl in KarmaDisplayLines()) // P31 B-M3
                Line(kl, dim);
        }
        else
        {
            Line("REST", green);
        }

        // The clickable action rows (click or the keyboard shortcut). The hovered row lights.
        int hovered = PipboyRowAt(Mouse.GetState().X, Mouse.GetState().Y);
        var rows = PipboyRows();
        for (int i = 0; i < rows.Count; i++)
        {
            Rectangle r = PipboyRowRect(i);
            _fontRenderer.Draw(_spriteBatch, rows[i].Label, new Vector2(r.X + 6, r.Y + 2), i == hovered ? hot : green);
        }
        _fontRenderer.Draw(_spriteBatch, _pipboyRestMenu ? "click a duration, Esc back" : "click a row, P / Esc close",
            new Vector2(cx, po.Y + ph - 30), dim);
    }

    /// <summary>The automap dot colour for an object by FID type, shared by the full-window
    /// automap and the Pip-Boy mini-map (P20-M1/M2). Dead critters / untyped objects → null.
    /// Walls/scenery match the engine's IN-GAME _colorTable (automap.cc:537/541 — wall
    /// _colorTable[992] = pure green, high-detail scenery [480] = dark green). DOCUMENTED
    /// DIVERGENCE: the engine's in-game map hides critters + items (motion-sensor only) and
    /// paints the dude red; we show them (red/yellow) with a WHITE dude so enemies + loot +
    /// you are all distinguishable — a more useful PoC map.</summary>
    /// <summary>Reveal every current-elevation object within sight of <paramref name="tile"/>
    /// for the automap fog (P20-M2) — accumulated into <see cref="_seenObjects"/> as the dude
    /// explores. Proximity, not true LoS (a documented simplification).</summary>
    private void RevealAround(int tile)
    {
        if (tile < 0)
            return;
        foreach (MapObject obj in _flatObjects[_elevation].Concat(_solidObjects[_elevation]))
            if (obj.HexTile >= 0 && Formats.Hex.HexGrid.Distance(tile, obj.HexTile) <= AutomapSightRadius)
                _seenObjects.Add(obj);
    }

    private static Color? AutomapColor(MapObject obj) => Fid.Type(obj.Fid) switch
    {
        ObjectType.Wall => new Color(0, 248, 0),     // _colorTable[992]
        ObjectType.Scenery => new Color(0, 120, 0),  // _colorTable[480]
        ObjectType.Critter => obj.IsDead ? null : new Color(248, 0, 0),
        ObjectType.Item => new Color(252, 252, 84),
        ObjectType.Misc => new Color(84, 200, 252),
        _ => null,
    };

    /// <summary>The embedded Pip-Boy mini-map (P20-M1): the current elevation's objects
    /// plotted into a small box on the status page (col→x mirrored, row→y, like the full
    /// window scaled). DIVERGENCE: the engine's embedded automap reads the explored-tile
    /// RLE from a GENERATED MAPS\AUTOMAP.DB — which our PoC never writes — so we re-plot the
    /// live objects instead (the same source as the full-window automap). A preview; press
    /// A for the full view.</summary>
    private void DrawPipboyMiniMap(int boxX, int boxY, int boxW, int boxH)
    {
        if (_fontRenderer is null)
            return;
        _panelPixel ??= CreatePixel();
        _spriteBatch.Draw(_panelPixel, new Rectangle(boxX, boxY, boxW, boxH), new Color(0, 20, 0, 210));

        void Plot(int tile, Color c, int size)
        {
            if (tile < 0)
                return;
            int mx = boxX + boxW * (199 - tile % 200) / 199; // mirror col like the full window
            int my = boxY + boxH * (tile / 200) / 199;
            if (mx >= boxX && my >= boxY && mx + size <= boxX + boxW && my + size <= boxY + boxH)
                _spriteBatch.Draw(_panelPixel, new Rectangle(mx, my, size, size), c);
        }

        foreach (MapObject obj in _flatObjects[_elevation].Concat(_solidObjects[_elevation]))
            if (_seenObjects.Contains(obj) && AutomapColor(obj) is { } col) // OBJECT_SEEN fog (P20-M2)
                Plot(obj.HexTile, col, 2);
        if (_dude is not null)
            Plot(_dude.Dude.HexTile, new Color(255, 255, 255), 3);

        _fontRenderer.Draw(_spriteBatch, "MAP (A: full)", new Vector2(boxX + 4, boxY + 2), new Color(0, 252, 0));
    }

    /// <summary>The full-window automap (P15 M0): the authentic AUTOMAP.FRM (519x480)
    /// centred, with every object on the current elevation plotted as a colored dot
    /// (automap.cc automapRenderInMapWindow projection: ax = 449 − 2·col, ay = 2·row + 8,
    /// col = tile%200, row = tile/200). Colors by FID type; the dude is a bright marker.
    /// Fog-of-war is faked all-visible (we don't track OBJECT_SEEN) — a documented PoC
    /// simplification; the embedded Pip-Boy mini-map (needs automap.db RLE) stays out.</summary>
    private void DrawAutomap()
    {
        if (!_automapOpen || _fontRenderer is null)
            return;
        _automapBg ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\AUTOMAP.frm");

        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int w = _automapBg?.Width ?? 519, h = _automapBg?.Height ?? 480;
        var o = new Point(Math.Max(0, (vp.Width - w) / 2), Math.Max(0, (vp.Height - h) / 2));
        _panelPixel ??= CreatePixel();
        if (_automapBg is not null)
            _spriteBatch.Draw(_automapBg, new Vector2(o.X, o.Y), Color.White);
        else
            _spriteBatch.Draw(_panelPixel, new Rectangle(o.X, o.Y, w, h), new Color(8, 16, 8, 240));

        void Plot(int tile, Color c, int size)
        {
            if (tile < 0)
                return;
            int ax = 449 - 2 * (tile % 200);   // automap.cc:550, decomposed
            int ay = 2 * (tile / 200) + 8;
            if (ax < 0 || ay < 0 || ax + size > w || ay + size > h)
                return;
            _spriteBatch.Draw(_panelPixel, new Rectangle(o.X + ax, o.Y + ay, size, size), c);
        }

        foreach (MapObject obj in _flatObjects[_elevation].Concat(_solidObjects[_elevation]))
            if (_seenObjects.Contains(obj) && AutomapColor(obj) is { } col) // OBJECT_SEEN fog (P20-M2)
                Plot(obj.HexTile, col, 2);
        if (_dude is not null)
            Plot(_dude.Dude.HexTile, new Color(255, 255, 255), 3); // the dude marker

        var labelGreen = new Color(0, 252, 0);
        _fontRenderer.Draw(_spriteBatch, $"AUTOMAP — {_currentMapName} (elev {_elevation})", new Vector2(o.X + 20, o.Y + 12), labelGreen);
        _fontRenderer.Draw(_spriteBatch, "Esc / A  close", new Vector2(o.X + 20, o.Y + h - 24), new Color(0, 168, 0));
    }

    /// <summary>The options / pause menu (P12 M2): the authentic OPBASE.FRM (164x217)
    /// centred, with the actions the engine's showOptions offers (minus Preferences,
    /// which we have no system for). Drawn over the paused world.</summary>
    // The options/pause menu rows, top to bottom — index is the dispatch key shared by
    // DrawOptions (render), OptionsRowAt (hit-test) and the click handler.
    private static readonly string[] OptionsItems =
        ["Save Game  (S)", "Load Game  (L)", "Main Menu  (M)", "Quit  (Q)", "Resume  (Esc)"];

    // The clickable rect for the index-th options row — origin + spacing mirror DrawOptions
    // exactly (the FRM-dim fallback keeps it valid before the art loads).
    private Rectangle OptionsRowRect(int index)
    {
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int ow = _optionsBg?.Width ?? 164, oh = _optionsBg?.Height ?? 217;
        int ox = Math.Max(0, (vp.Width - ow) / 2), oy = Math.Max(0, (vp.Height - oh) / 2);
        int lh = (_fontRenderer?.LineHeight ?? 16) + 10;
        int ty0 = oy + (oh - OptionsItems.Length * lh) / 2;
        return new Rectangle(ox, ty0 + index * lh - 2, ow, lh);
    }

    private int OptionsRowAt(int mx, int my)
    {
        for (int i = 0; i < OptionsItems.Length; i++)
            if (OptionsRowRect(i).Contains(mx, my))
                return i;
        return -1;
    }

    /// <summary>The detected-encounter avoid prompt (phase-16 M1): a centred Yes/No
    /// box over the worldmap mirroring the engine's showDialogBox (worldmap.cc:3510).</summary>
    private void DrawEncounterPrompt()
    {
        if (_encounterPrompt is not { } p || _fontRenderer is null)
            return;
        _panelPixel ??= CreatePixel();
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int w = 360, h = 96;
        int x = (vp.Width - w) / 2, y = (vp.Height - h) / 2;
        _spriteBatch.Draw(_panelPixel, new Rectangle(x, y, w, h), new Color(8, 16, 8, 240));
        var green = new Color(0, 252, 0);
        string[] lines =
        [
            "You detect something up ahead.",
            p.Name ?? "An encounter.",
            "Do you wish to encounter it?  (Y / N)",
        ];
        int ty = y + 14;
        foreach (string line in lines)
        {
            int tw = _fontRenderer.MeasureWidth(line);
            _fontRenderer.Draw(_spriteBatch, line, new Vector2(x + (w - tw) / 2, ty), green);
            ty += _fontRenderer.LineHeight + 6;
        }
    }

    private void DrawOptions()
    {
        if (!_optionsOpen || _fontRenderer is null)
            return;
        _optionsBg ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\OPBASE.frm");

        int ow = _optionsBg?.Width ?? 164, oh = _optionsBg?.Height ?? 217;
        var green = new Color(0, 252, 0);
        var hot = new Color(252, 252, 84);

        // Top-left of the panel (recompute the same way OptionsRowRect does).
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int px = Math.Max(0, (vp.Width - ow) / 2), py = Math.Max(0, (vp.Height - oh) / 2);

        if (_optionsBg is not null)
            _spriteBatch.Draw(_optionsBg, new Vector2(px, py), Color.White);
        else
        {
            _panelPixel ??= CreatePixel();
            _spriteBatch.Draw(_panelPixel, new Rectangle(px, py, ow, oh), new Color(8, 16, 8, 240));
        }

        int hovered = OptionsRowAt(Mouse.GetState().X, Mouse.GetState().Y);
        for (int i = 0; i < OptionsItems.Length; i++)
        {
            Rectangle r = OptionsRowRect(i);
            int tw = _fontRenderer.MeasureWidth(OptionsItems[i]);
            _fontRenderer.Draw(_spriteBatch, OptionsItems[i], new Vector2(px + (ow - tw) / 2, r.Y + 2), i == hovered ? hot : green);
        }
    }

    // ====================================================================
    //  Multi-slot save/load picker (P48)
    // ====================================================================
    //
    // A 10-slot save/load modal (the engine's LSGAME screen, loadsave.cc), opened from the
    // Options Save/Load rows: each row shows a slot's metadata (character / level / map / date)
    // or "- EMPTY -", click or 0-9 to save into / load from it. One JSON file per slot
    // (hexwaste-slotN.json) under SaveDir. F5/F9 stay a separate quicksave on the default path.
    // DIVERGENCE: a dark text panel, not the authentic LSGAME.frm art (an art residual, the
    // Skilldex text-then-art pattern); no overwrite-confirm dialog (a click saves directly).

    private enum SaveLoadMode { Save, Load }
    private bool _saveLoadOpen;
    private SaveLoadMode _saveLoadMode;
    private readonly Formats.SlotInfo[] _slotInfos = new Formats.SlotInfo[Formats.SaveSlots.Count];

    /// <summary>P52-M3: the authentic LSGAME.frm load/save window art (640x480, with the slot-list
    /// frame + info box baked in), lazily loaded; null falls back to the dark text panel.</summary>
    private Texture2D? _lsgameFrm;
    private bool _saveLoadArt; // LSGAME.frm loaded — switches the picker geometry to the art window

    /// <summary>The directory holding the per-slot save files (the harness --save-dir; default cwd).</summary>
    public string SaveDir { get; set; } = "";

    private string SlotPath(int slot) => string.IsNullOrEmpty(SaveDir)
        ? Formats.SaveSlots.SlotFileName(slot)
        : Path.Combine(SaveDir, Formats.SaveSlots.SlotFileName(slot));

    private void RefreshSlotInfos()
    {
        for (int i = 0; i < Formats.SaveSlots.Count; i++)
            _slotInfos[i] = Formats.SaveSlots.Describe(SaveState.Load(SlotPath(i)));
    }

    private void OpenSaveLoad(SaveLoadMode mode)
    {
        _saveLoadMode = mode;
        RefreshSlotInfos();
        _saveLoadOpen = true;
    }

    private void SaveGameToSlot(int slot)
    {
        if (!string.IsNullOrEmpty(SaveDir))
            Directory.CreateDirectory(SaveDir);
        string prev = SavePath;
        try { SavePath = SlotPath(slot); SaveGame(); }
        finally { SavePath = prev; }
        RefreshSlotInfos();
    }

    private void LoadGameFromSlot(int slot)
    {
        string prev = SavePath;
        try { SavePath = SlotPath(slot); LoadGame(); }
        finally { SavePath = prev; }
    }

    // The centred modal + per-slot row geometry (one helper shared by render + hit-test, the
    // OptionsRowRect pattern). Row layout: a title line, then the 10 slot rows below it.
    private const int SaveLoadPanelWidth = 470;

    // LSGAME.frm slot list: window-local (55, 87) 230x353, 10 slots evenly (loadsave.cc _ShowSlotList:2032).
    private const int SaveLoadListTop = 87, SaveLoadListX = 55, SaveLoadSlotH = 35;

    private Rectangle SaveLoadPanelRect()
    {
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        if (_saveLoadArt)
        {
            const int w = 640, h = 480; // LSGAME.frm
            return new Rectangle(Math.Max(0, (vp.Width - w) / 2), Math.Max(0, (vp.Height - h) / 2), w, h);
        }
        int lh = (_fontRenderer?.LineHeight ?? 16) + 8;
        int th = (Formats.SaveSlots.Count + 2) * lh + 16;
        int x = Math.Max(0, (vp.Width - SaveLoadPanelWidth) / 2);
        int y = Math.Max(0, (vp.Height - th) / 2);
        return new Rectangle(x, y, SaveLoadPanelWidth, th);
    }

    private Rectangle SaveLoadSlotRect(int slot)
    {
        Rectangle p = SaveLoadPanelRect();
        if (_saveLoadArt)
            return new Rectangle(p.X + SaveLoadListX, p.Y + SaveLoadListTop + slot * SaveLoadSlotH, 230, SaveLoadSlotH);
        int lh = (_fontRenderer?.LineHeight ?? 16) + 8;
        return new Rectangle(p.X + 8, p.Y + 12 + lh + slot * lh, p.Width - 16, lh);
    }

    private int SaveLoadSlotAt(int mx, int my)
    {
        for (int i = 0; i < Formats.SaveSlots.Count; i++)
            if (SaveLoadSlotRect(i).Contains(mx, my))
                return i;
        return -1;
    }

    private void DrawSaveLoad()
    {
        if (!_saveLoadOpen || _fontRenderer is null)
            return;
        // P52-M3: render the authentic LSGAME.frm window when present; the slot-list frame + info box
        // are baked into the art (loadsave.cc). Fall back to the dark text panel when the asset is absent.
        _lsgameFrm ??= InterfaceBar.LoadFrm(GraphicsDevice, _vfs, _palette, @"art\intrface\LSGAME.frm");
        _saveLoadArt = _lsgameFrm is not null;
        _panelPixel ??= CreatePixel();
        Rectangle p = SaveLoadPanelRect();
        var green = new Color(0, 252, 0);
        var hot = new Color(252, 252, 84);
        var gray = new Color(140, 140, 140);

        if (_lsgameFrm is not null)
            _spriteBatch.Draw(_lsgameFrm, new Vector2(p.X, p.Y), Color.White);
        else
            _spriteBatch.Draw(_panelPixel, p, new Color(8, 16, 8, 240));

        string title = _saveLoadMode == SaveLoadMode.Save
            ? "SAVE GAME - pick a slot (0-9 / click, Esc cancel)"
            : "LOAD GAME - pick a slot (0-9 / click, Esc cancel)";
        _fontRenderer.Draw(_spriteBatch, title, new Vector2(p.X + 12, p.Y + (_saveLoadArt ? 60 : 10)), Color.LightGray);

        int hovered = SaveLoadSlotAt(Mouse.GetState().X, Mouse.GetState().Y);
        for (int i = 0; i < Formats.SaveSlots.Count; i++)
        {
            Formats.SlotInfo info = _slotInfos[i];
            Rectangle r = SaveLoadSlotRect(i);
            Color c = i == hovered ? hot : (info.Occupied && !info.VersionMismatch ? green : gray);
            if (_saveLoadArt)
            {
                // Engine slot block: a "[ SLOT NN: ]" header line, then the description below.
                string state = !info.Occupied ? "- EMPTY -" : info.VersionMismatch ? "- OLD VERSION -"
                    : $"{info.Character} L{info.Level}";
                _fontRenderer.Draw(_spriteBatch, $"[  SLOT {i + 1:00}:  ]", new Vector2(r.X + 4, r.Y + 1), c);
                _fontRenderer.Draw(_spriteBatch, state, new Vector2(r.X + 14, r.Y + 1 + _fontRenderer.LineHeight), c);
            }
            else
            {
                string label = !info.Occupied ? "- EMPTY -" : info.VersionMismatch ? "- OLD VERSION -"
                    : $"{info.Character} L{info.Level}  {info.Map}  {info.Date}";
                _fontRenderer.Draw(_spriteBatch, $"{i}. {label}", new Vector2(r.X + 6, r.Y + 2), c);
            }
        }

        // The info box baked into LSGAME at window-local (396,254) 164x60 (loadsave.cc _DrawInfoBox):
        // the hovered (else cursor) slot's fuller metadata.
        if (_saveLoadArt)
        {
            Formats.SlotInfo sel = _slotInfos[hovered >= 0 ? hovered : 0];
            int bx = p.X + 396, by = p.Y + 258;
            if (sel.Occupied && !sel.VersionMismatch)
            {
                _fontRenderer.Draw(_spriteBatch, sel.Character, new Vector2(bx, by), green);
                _fontRenderer.Draw(_spriteBatch, $"Level {sel.Level}", new Vector2(bx, by + _fontRenderer.LineHeight), green);
                _fontRenderer.Draw(_spriteBatch, sel.Map, new Vector2(bx, by + 2 * _fontRenderer.LineHeight), green);
                _fontRenderer.Draw(_spriteBatch, sel.Date, new Vector2(bx, by + 3 * _fontRenderer.LineHeight), green);
            }
        }
    }

    // ====================================================================
    //  Called-shot click dialog (P49-M1)
    // ====================================================================
    //
    // Replaces the V-key aim CYCLE with a click dialog (the engine's CALLED.frm body-part
    // picker, combat.cc:5476 calledShotSelectHitLocation): V opens it, 1-9 / click a row picks
    // a hit location, Esc cancels. Each row shows the location's to-hit penalty (the defining
    // per-location stat, combat.cc:172 hit_location_penalty). The location feeds the unchanged
    // TryAttack(target, AimLocation) path (penalty + crit-table lookup). DIVERGENCE: a single-
    // column text list, not the authentic CALLED.frm critter-pic overlay (art residual, the
    // Skilldex text-then-art pattern); the live per-part to-hit % is a residual (penalty shown).

    private bool _aimDialogOpen;

    // The dialog rows -> AimLocation values, in the engine's CALLED.frm button order
    // (head/eyes/right-arm/right-leg, then torso/groin/left-arm/left-leg — combat.cc:1894-1907),
    // then uncalled. AimNames/LocationPenalty are indexed by the AimLocation value.
    private static readonly int[] AimDialogOrder = { 0, 6, 2, 4, 3, 7, 1, 5, 8 };

    private void OpenAimDialog() => _aimDialogOpen = true;

    private Rectangle AimDialogPanelRect()
    {
        Rectangle vp = GraphicsDevice.Viewport.Bounds;
        int lh = (_fontRenderer?.LineHeight ?? 16) + 8;
        int w = 320, h = (AimDialogOrder.Length + 2) * lh + 12;
        return new Rectangle(Math.Max(0, (vp.Width - w) / 2), Math.Max(0, (vp.Height - h) / 2), w, h);
    }

    private Rectangle AimDialogRowRect(int row)
    {
        Rectangle p = AimDialogPanelRect();
        int lh = (_fontRenderer?.LineHeight ?? 16) + 8;
        return new Rectangle(p.X + 8, p.Y + 10 + lh + row * lh, p.Width - 16, lh);
    }

    private int AimDialogRowAt(int mx, int my)
    {
        for (int i = 0; i < AimDialogOrder.Length; i++)
            if (AimDialogRowRect(i).Contains(mx, my))
                return i;
        return -1;
    }

    /// <summary>Pick a hit location from the dialog (a row index 0..8) and close it. Shared by the
    /// live click + the --aim-click harness so they drive the identical selection path.</summary>
    private void SelectAimRow(int row)
    {
        if (row < 0 || row >= AimDialogOrder.Length)
            return;
        AimLocation = AimDialogOrder[row];
        _aimDialogOpen = false;
        Log($"Aiming: {AimName(AimLocation)}.");
    }

    private void HandleAimDialogInput(MouseState mouse, KeyboardState keyboard)
    {
        if (IsKeyPressed(keyboard, Keys.Escape))
        {
            _aimDialogOpen = false;
            return;
        }
        for (int i = 0; i < AimDialogOrder.Length; i++)
            if (IsKeyPressed(keyboard, Keys.D1 + i) || IsKeyPressed(keyboard, Keys.NumPad1 + i))
            {
                SelectAimRow(i);
                return;
            }
        if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
            && AimDialogRowAt(mouse.X, mouse.Y) is int clicked && clicked >= 0)
            SelectAimRow(clicked);
    }

    private void DrawAimDialog()
    {
        if (!_aimDialogOpen || _fontRenderer is null)
            return;
        _panelPixel ??= CreatePixel();
        Rectangle p = AimDialogPanelRect();
        _spriteBatch.Draw(_panelPixel, p, new Color(8, 16, 8, 240));
        var green = new Color(0, 252, 0);
        var hot = new Color(252, 252, 84);
        _fontRenderer.Draw(_spriteBatch, "AIM - pick a hit location (1-9 / click, Esc cancel)",
            new Vector2(p.X + 12, p.Y + 8), Color.LightGray);
        int hovered = AimDialogRowAt(Mouse.GetState().X, Mouse.GetState().Y);
        // P52-M4: the LIVE per-location to-hit % against the aimed-at critter (the hovered target the
        // V key opens the dialog for). Recomputed per row via the same CombatEngine math the attack
        // uses; shown alongside the static penalty when a critter is targeted.
        MapObject? aimTarget = _hoveredObject is { } h && h != _dude?.Dude ? h : null;
        for (int i = 0; i < AimDialogOrder.Length; i++)
        {
            int loc = AimDialogOrder[i];
            int penalty = Formats.Combat.CriticalTables.LocationPenalty[loc];
            int? pct = aimTarget is not null ? _combat.PreviewToHit(aimTarget, loc) : null;
            string hit = pct is { } pc ? $"  {pc}%" : "";
            string label = loc == Formats.Combat.CriticalTables.LocationUncalled
                ? $"{i + 1}. uncalled (no aim){hit}"
                : $"{i + 1}. {AimName(loc)}  ({penalty:+0;-0;+0}){hit}";
            Rectangle r = AimDialogRowRect(i);
            _fontRenderer.Draw(_spriteBatch, label, new Vector2(r.X + 6, r.Y + 2),
                i == hovered ? hot : (loc == AimLocation ? hot : green));
        }
    }

    // Phase-15 M2: the four item panels (inventory / loot / barter / trade) share one
    // layout + one set of clickable rows + one overflow-paging window. A "kind" tags
    // each panel so a row CLICK can route to the same action its number key fires.
    private enum ItemPanelKind { Inventory, Loot, BarterStock, BarterGoods, TradeTake, TradeGive }

    // x position + title + list + dispatch kind + optional price column. One per visible
    // panel; the left panel is x=40, the right (sell/give side) x=420.
    private readonly record struct ItemPanel(
        int X, string Title, List<MapObject> Items, ItemPanelKind Kind, Func<MapObject, int>? Price);

    private const int ItemRowsPerPage = 9; // the 1-9 number-key row maps to one page

    // The panels currently on screen, in draw order. SINGLE source of truth shared by
    // DrawItemPanels (render) and TryClickItemPanel (hit-test) so a click always targets
    // exactly what's drawn. Mirrors the old DrawItemPanels branch order (barter > trade >
    // loot > inventory — OpenTrade sets both _tradePartner and _lootContainer, so trade
    // must be tested first).
    private List<ItemPanel> CurrentItemPanels()
    {
        var panels = new List<ItemPanel>(2);
        if (_barterNpc is { } merchant)
        {
            panels.Add(new(40, $"{ObjectName(merchant)} sells (caps {(_barterStock is { } till ? _scriptHost?.CapsTotal(till) : 0) ?? 0}) - click/1-9 buy",
                BarterStock(), ItemPanelKind.BarterStock, BarterBuyPrice));
            panels.Add(new(420, $"You sell (caps {DudeCaps()}) - click/Shift+1-9 sell, Esc done",
                BarterGoods(), ItemPanelKind.BarterGoods, BarterSellPrice));
        }
        else if (_tradePartner is { } follower)
        {
            panels.Add(new(40, $"Trading with {ObjectName(follower)} - click/1-9 take, A take all",
                follower.Inventory, ItemPanelKind.TradeTake, null));
            panels.Add(new(420, "You carry - click/Shift+1-9 give, Esc done",
                _dudeInventory, ItemPanelKind.TradeGive, null));
        }
        else if (_lootContainer is { } container)
        {
            panels.Add(new(40, $"{ObjectName(container)} - click/1-9 take, A take all, Esc close",
                container.Inventory, ItemPanelKind.Loot, null));
        }
        else if (_inventoryOpen)
        {
            panels.Add(new(40, "Inventory - click/1-9 use/equip, Shift drop, Esc close",
                _dudeInventory, ItemPanelKind.Inventory, null));
        }
        return panels;
    }

    private void DrawItemPanels()
    {
        if (_fontRenderer is null)
            return;
        foreach (ItemPanel panel in CurrentItemPanels())
        {
            int bottom = DrawItemList(panel.Title, panel.Items, panel.X, panel.Price);
            if (ReferenceEquals(panel.Items, _dudeInventory)) // the dude's side carries the weight readout (P24)
                DrawWeightReadout(panel.X, bottom);
        }
        DrawEquipSlots(); // P47: the weapon/armor equip slots + the dragged-item ghost
    }

    // ====================================================================
    //  Inventory drag-and-drop equip (P47)
    // ====================================================================
    //
    // The inventory panel supports drag: press an item (a list row or an occupied equip
    // slot), drag it to a slot to EQUIP / out of a slot to UNEQUIP; a tap on a row (no real
    // drag) falls back to the existing click-to-use/equip. Loot/barter/trade keep click-on-
    // press (they transfer, not equip). Ported from fallout2-ce inventory.cc — the press->
    // drag->release state machine + the slot hit-test cascade (inventory.cc:2386-2537) + the
    // _switch_hand equip/swap. DIVERGENCE: Hexwaste renders the slots as boxes beside the
    // text list, not the authentic INVBOX.frm paperdoll window (a documented art residual, the
    // Skilldex text-then-art pattern); and there is no LEFT-hand slot (single-weapon model).

    private enum DragSource { None, Row, WeaponSlot, ArmorSlot }
    private MapObject? _dragItem;       // the item currently being dragged, or null
    private DragSource _dragSource;     // where the drag started
    private Point _dragStart;           // the press position (to tell a tap from a drag)

    // The two equip-slot rects (screen coords; the inventory list panel is fixed at x=40,
    // width 360, so x=420 is free — the same column the barter/trade right panel uses).
    private static readonly Rectangle WeaponSlotRect = new(420, 96, 90, 60);
    private static readonly Rectangle ArmorSlotRect = new(420, 176, 90, 60);

    private static Formats.Combat.EquipSlot? EquipSlotAt(int mx, int my) =>
        WeaponSlotRect.Contains(mx, my) ? Formats.Combat.EquipSlot.Weapon
        : ArmorSlotRect.Contains(mx, my) ? Formats.Combat.EquipSlot.Armor
        : null;

    /// <summary>The inventory list row (0..8) under a point, or -1. Shares ItemRowRect with
    /// the renderer + TryClickItemPanel so they never disagree (panel x = 40).</summary>
    private int InventoryRowAt(int mx, int my)
    {
        for (int row = 0; row < ItemRowsPerPage; row++)
            if (ItemRowRect(40, row).Contains(mx, my))
                return row;
        return -1;
    }

    /// <summary>The dude's item currently in a slot — the wielded weapon, or the worn armor.</summary>
    private MapObject? EquippedInSlot(Formats.Combat.EquipSlot slot) =>
        slot == Formats.Combat.EquipSlot.Weapon
            ? _dudeInventory.FirstOrDefault(i => i.IsInHand && SafeProto(i.Pid)?.Weapon is not null)
            : _dudeInventory.FirstOrDefault(i => i.IsWorn);

    /// <summary>The live press/drag/release handler for the inventory panel (P47). Only the pure-
    /// inventory case reaches here; loot/barter/trade keep click-on-press in the caller.</summary>
    private void HandleInventoryDrag(MouseState mouse, bool shift)
    {
        bool press = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        bool release = mouse.LeftButton == ButtonState.Released && _previousMouse.LeftButton == ButtonState.Pressed;

        if (press)
        {
            _dragStart = new Point(mouse.X, mouse.Y);
            _dragItem = null;
            _dragSource = DragSource.None;
            if (EquipSlotAt(mouse.X, mouse.Y) is { } slot && EquippedInSlot(slot) is { } equipped)
            {
                _dragItem = equipped;
                _dragSource = slot == Formats.Combat.EquipSlot.Weapon ? DragSource.WeaponSlot : DragSource.ArmorSlot;
            }
            else if (InventoryRowAt(mouse.X, mouse.Y) is int row && row >= 0)
            {
                int gi = _panelPage * ItemRowsPerPage + row;
                if (gi < _dudeInventory.Count)
                {
                    _dragItem = _dudeInventory[gi];
                    _dragSource = DragSource.Row;
                }
            }
            return;
        }

        if (release && _dragItem is { } dragged)
        {
            Formats.Combat.EquipSlot? overSlot = EquipSlotAt(mouse.X, mouse.Y);
            if (_dragSource == DragSource.Row && overSlot is { } dropSlot)
                EquipFromDrag(dragged, dropSlot); // list -> slot: equip
            else if (_dragSource is DragSource.WeaponSlot && overSlot != Formats.Combat.EquipSlot.Weapon)
                UnequipSlot(Formats.Combat.EquipSlot.Weapon); // dragged the weapon off its slot: unequip
            else if (_dragSource is DragSource.ArmorSlot && overSlot != Formats.Combat.EquipSlot.Armor)
                UnequipSlot(Formats.Combat.EquipSlot.Armor);
            else if (_dragSource == DragSource.Row
                && Math.Abs(mouse.X - _dragStart.X) <= 4 && Math.Abs(mouse.Y - _dragStart.Y) <= 4)
                TryClickItemPanel(_dragStart.X, _dragStart.Y, shift); // a tap: the click-to-use fallback
            _dragItem = null;
            _dragSource = DragSource.None;
        }
    }

    /// <summary>Equip an item dropped onto a slot — the _switch_hand equip path (inventory.cc:2490).
    /// A wrong-type drop (armor on the weapon slot, etc.) is rejected by EquipRules. Reuses the same
    /// flag/armor-bonus mutations as the click-to-equip (UseInventoryItem).</summary>
    private void EquipFromDrag(MapObject item, Formats.Combat.EquipSlot slot)
    {
        if (_dude is null)
            return;
        Formats.Proto.ProtoInfo? proto = SafeProto(item.Pid);
        if (!Formats.Combat.EquipRules.CanEquip(proto?.Weapon is not null, proto?.Armor is not null, slot))
            return;

        if (slot == Formats.Combat.EquipSlot.Weapon)
        {
            foreach (MapObject other in _dudeInventory)
                other.Flags &= ~(MapObject.FlagInLeftHand | MapObject.FlagInRightHand);
            item.Flags |= MapObject.FlagInRightHand;
            Log($"You ready the {ObjectName(item)}.");
        }
        else // Armor
        {
            if (item.IsWorn)
                return;
            foreach (MapObject other in _dudeInventory.Where(o => o.IsWorn))
            {
                if (SafeProto(other.Pid)?.Armor is { } oldArmor)
                    ApplyArmorBonus(oldArmor, -1);
                other.Flags &= ~MapObject.FlagWorn;
            }
            item.Flags |= MapObject.FlagWorn;
            if (proto!.Armor is { } armor)
                ApplyArmorBonus(armor, +1);
            Log($"You put on the {ObjectName(item)}.");
        }
    }

    /// <summary>Unequip the item currently in a slot (dragged off it) — clears the flag + reverses
    /// the armor bonus, mirroring UnequipForTransfer without removing the item from the bag.</summary>
    private void UnequipSlot(Formats.Combat.EquipSlot slot)
    {
        if (slot == Formats.Combat.EquipSlot.Weapon)
        {
            foreach (MapObject it in _dudeInventory.Where(i => i.IsInHand).ToList())
            {
                it.Flags &= ~(MapObject.FlagInLeftHand | MapObject.FlagInRightHand);
                Log($"You put away the {ObjectName(it)}.");
            }
        }
        else
        {
            foreach (MapObject it in _dudeInventory.Where(i => i.IsWorn).ToList())
            {
                if (SafeProto(it.Pid)?.Armor is { } armor)
                    ApplyArmorBonus(armor, -1);
                it.Flags &= ~MapObject.FlagWorn;
                Log($"You take off the {ObjectName(it)}.");
            }
        }
    }

    /// <summary>Draw the weapon + armor equip slots and the dragged item's ghost icon. Only in the
    /// pure-inventory view (loot/barter/trade have no equip slots).</summary>
    private void DrawEquipSlots()
    {
        if (_fontRenderer is null || !_inventoryOpen || _lootContainer is not null
            || _tradePartner is not null || _barterNpc is not null)
            return;
        _panelPixel ??= CreatePixel();
        DrawEquipSlot(WeaponSlotRect, "WEAPON", EquippedInSlot(Formats.Combat.EquipSlot.Weapon));
        DrawEquipSlot(ArmorSlotRect, "ARMOR", EquippedInSlot(Formats.Combat.EquipSlot.Armor));
        if (_dragItem is { } dragged) // the ghost icon follows the cursor (from the last Update mouse)
            DrawItemIcon(dragged, new Rectangle(_previousMouse.X - 14, _previousMouse.Y - 11, 28, 22));
    }

    private void DrawEquipSlot(Rectangle rect, string label, MapObject? item)
    {
        _spriteBatch.Draw(_panelPixel, rect, new Color(8, 8, 8, 230));
        var border = new Color(0, 252, 0);
        _spriteBatch.Draw(_panelPixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), border);
        _spriteBatch.Draw(_panelPixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), border);
        _spriteBatch.Draw(_panelPixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), border);
        _spriteBatch.Draw(_panelPixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), border);
        _fontRenderer!.Draw(_spriteBatch, label, new Vector2(rect.X + 4, rect.Y - 22), Color.LightGray);
        if (item is not null)
            DrawItemIcon(item, new Rectangle(rect.X + 8, rect.Y + 6, rect.Width - 16, rect.Height - 12));
        else
            _fontRenderer.Draw(_spriteBatch, "(empty)", new Vector2(rect.X + 8, rect.Y + rect.Height / 2 - 8), Color.Gray);
    }

    /// <summary>The carried-weight readout, drawn just below the dude's inventory panel (P24;
    /// inventory.cc:3164 "Total Wt: N/M") — green within capacity, red when over
    /// (critterIsEncumbered). Below the panel so it never collides with the title/rows.</summary>
    private void DrawWeightReadout(int panelX, int panelBottom)
    {
        if (_fontRenderer is null || _dude is null)
            return;
        int carried = DudeCarriedWeight(), cap = DudeCarryCapacity();
        Color color = Formats.Map.InventoryWeight.IsEncumbered(carried, cap)
            ? new Color(255, 64, 64) : new Color(0, 252, 0);
        _fontRenderer.Draw(_spriteBatch, $"Total Wt: {carried}/{cap}", new Vector2(panelX + 10, panelBottom + 4), color);
    }

    // The clickable rect for the displayRow-th row (0..8) of the panel at x. Both the
    // renderer and the hit-test go through this so they can never disagree on geometry.
    private Rectangle ItemRowRect(int x, int displayRow)
    {
        int lineHeight = Math.Max(_fontRenderer?.LineHeight ?? 26, 26);
        int rowY = 60 + 8 + lineHeight + 6 + displayRow * lineHeight;
        return new Rectangle(x + 6, rowY - 4, 360 - 12, lineHeight);
    }

    /// <summary>Draws the panel and returns the y just below it (P24 — the weight readout sits there).</summary>
    private int DrawItemList(string title, List<MapObject> items, int x,
        Func<MapObject, int>? price = null)
    {
        _panelPixel ??= CreatePixel();
        int lineHeight = Math.Max(_fontRenderer!.LineHeight, 26);
        int panelWidth = 360;
        int start = _panelPage * ItemRowsPerPage;
        int shown = Math.Clamp(items.Count - start, 0, ItemRowsPerPage);
        int panelHeight = (Math.Max(shown, 1) + 2) * lineHeight + 16;
        int y = 60;

        _spriteBatch.Draw(_panelPixel, new Rectangle(x, y, panelWidth, panelHeight), new Color(8, 8, 8, 230));
        _fontRenderer.Draw(_spriteBatch, title, new Vector2(x + 10, y + 8), Color.LightGray);

        int rowY = y + 8 + lineHeight + 6;
        var green = new Color(0, 252, 0);
        if (items.Count == 0)
            _fontRenderer.Draw(_spriteBatch, "(empty)", new Vector2(x + 10, rowY), Color.Gray);

        for (int row = 0; row < ItemRowsPerPage; row++)
        {
            int gi = start + row;
            if (gi >= items.Count)
                break;
            MapObject item = items[gi];
            DrawItemIcon(item, new Rectangle(x + 28, rowY - 2, 28, 22));
            string count = item.StackCount > 1 ? $" x{item.StackCount}" : "";
            string tag = price is null ? "" : $"  ${price(item)}";
            _fontRenderer.Draw(_spriteBatch, $"{row + 1}.", new Vector2(x + 10, rowY), green);
            _fontRenderer.Draw(_spriteBatch, $"{ObjectName(item)}{count}{tag}", new Vector2(x + 62, rowY), green);
            rowY += lineHeight;
        }

        if (items.Count > ItemRowsPerPage)
        {
            int pages = (items.Count + ItemRowsPerPage - 1) / ItemRowsPerPage;
            _fontRenderer.Draw(_spriteBatch, $"(page {Math.Min(_panelPage + 1, pages)}/{pages} - PgUp/PgDn)",
                new Vector2(x + 10, rowY), Color.Gray);
        }
        return y + panelHeight;
    }

    // Highest page index across the visible panels (shared paging window).
    private int MaxPanelPage()
    {
        int max = 0;
        foreach (ItemPanel panel in CurrentItemPanels())
            max = Math.Max(max, (Math.Max(panel.Items.Count, 1) - 1) / ItemRowsPerPage);
        return max;
    }

    // Route a row CLICK to the same action its number key fires. `shift` only matters
    // for the single inventory panel (use vs drop); the other panels are physically
    // split (buy/sell, take/give), so a plain click is unambiguous.
    private void DispatchItemPanel(ItemPanelKind kind, int index, bool shift)
    {
        switch (kind)
        {
            case ItemPanelKind.BarterStock: BarterBuy(index); break;
            case ItemPanelKind.BarterGoods: BarterSell(index); break;
            case ItemPanelKind.TradeTake:
            case ItemPanelKind.Loot:        TakeFromContainer(index); break;
            case ItemPanelKind.TradeGive:   GiveToFollower(index); break;
            case ItemPanelKind.Inventory:
                if (shift) DropFromInventory(index);
                else UseInventoryItem(index);
                break;
        }
    }

    // Hit-test a click against the visible panel rows; dispatch the first match. Returns
    // false if the click missed every row (so the caller can fall through). Geometry-only
    // (no Draw dependency) so the headless --panel-click harness can drive it too.
    private bool TryClickItemPanel(int mx, int my, bool shift)
    {
        foreach (ItemPanel panel in CurrentItemPanels())
        {
            int start = _panelPage * ItemRowsPerPage;
            for (int row = 0; row < ItemRowsPerPage; row++)
            {
                int gi = start + row;
                if (gi >= panel.Items.Count)
                    break;
                if (ItemRowRect(panel.X, row).Contains(mx, my))
                {
                    DispatchItemPanel(panel.Kind, gi, shift);
                    return true;
                }
            }
        }
        return false;
    }

    // PgUp/PgDn step the shared paging window so overflow past the 9th row is reachable.
    private void HandlePanelPaging(KeyboardState keyboard)
    {
        if (IsKeyPressed(keyboard, Keys.PageDown))
            _panelPage = Math.Min(_panelPage + 1, MaxPanelPage());
        else if (IsKeyPressed(keyboard, Keys.PageUp))
            _panelPage = Math.Max(_panelPage - 1, 0);
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

    /// <summary>Step the HP/AC HUD counters one unit per <c>StepMs</c> toward the real
    /// stat — the iconic Fallout digit roll (P11 M5). -1 snaps (fresh dude/load); a big
    /// swing rolls visibly over a beat. Cosmetic only; never printed.</summary>
    private void UpdateHudRoll(double elapsedMs)
    {
        if (_dude is null || GetCritterState(_dude.Dude) is not { } stats)
            return;

        if (_hudDisplayedHp < 0 || _hudDisplayedAc < 0)
        {
            _hudDisplayedHp = stats.CurrentHp;
            _hudDisplayedAc = stats.ArmorClass;
            _hudRollAccumulatorMs = 0;
            return;
        }

        const double StepMs = 25; // ~40 digits/sec — fast enough to feel snappy, slow enough to read
        _hudRollAccumulatorMs += elapsedMs;
        while (_hudRollAccumulatorMs >= StepMs)
        {
            _hudRollAccumulatorMs -= StepMs;
            _hudDisplayedHp += Math.Sign(stats.CurrentHp - _hudDisplayedHp);
            _hudDisplayedAc += Math.Sign(stats.ArmorClass - _hudDisplayedAc);
        }
    }

    /// <summary>The authentic bottom HUD bar (P11): the iface.frm panel pinned
    /// bottom-centre at native scale, with live readouts composed on top. Sets
    /// <see cref="_hudBarHeight"/> so the message log + HUD text lift above it.</summary>
    private void DrawInterfaceBar()
    {
        if (_interfaceBar is not { Loaded: true } bar || _worldmapOpen)
        {
            _hudBarHeight = 0;
            return;
        }

        Rectangle viewport = GraphicsDevice.Viewport.Bounds;
        _hudBarHeight = InterfaceBar.Height;
        bar.Draw(_spriteBatch, viewport);

        if (_dude is null || GetCritterState(_dude.Dude) is not { } stats)
            return;
        Point o = bar.Origin(viewport); // bar-local coords (interface.cc) -> screen = o + coord

        // --- M2: equipped-weapon slot (centre, bar-local 267,26 188x67; interface.cc:505,315) ---
        (Formats.Proto.ProtoInfo? weaponProto, MapObject? weaponItem) = EquippedWeapon(_dude.Dude);
        if (weaponItem is not null)
        {
            try
            {
                int fid = _protos.Get(weaponItem.Pid).InventoryFid;
                if (fid != -1)
                {
                    Texture2D tex = _frmCache.GetTexture(fid);
                    // native size, downscaled only if larger than the slot, centred
                    float s = Math.Min(1f, Math.Min(188f / tex.Width, 67f / tex.Height));
                    int dw = (int)(tex.Width * s), dh = (int)(tex.Height * s);
                    _spriteBatch.Draw(tex, new Rectangle(o.X + 267 + (188 - dw) / 2, o.Y + 26 + (67 - dh) / 2, dw, dh), Color.White);
                }
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
            {
            }
        }
        // Ammo count for guns, over the baked ammo bar (NUMBERS.FRM, white band).
        if (weaponProto?.Weapon is { } w && w.IsGun(weaponProto.ExtendedFlags) && weaponItem is not null && bar.Numbers is { } numAmmo)
            DrawCounter(numAmmo, WeaponAmmo(weaponProto, weaponItem), band: 0, xRight: o.X + 458, yTop: o.Y + 76);
        // The active attack-mode label, bright, at the weapon-button top-left. For a
        // burst-capable gun it reflects the LIVE _weaponMode (P15 M1 — the slot/N cycle);
        // otherwise the proto's attack-anim nibble (SWING/THRUST/SINGLE/…).
        if (weaponProto is not null && _fontRenderer is not null)
        {
            string mode = Formats.Combat.CombatEngine.IsBurstWeapon(weaponProto)
                ? (_weaponMode == WeaponMode.Burst ? "BURST" : "SINGLE")
                : AttackModeName(weaponProto);
            _fontRenderer.Draw(_spriteBatch, mode, new Vector2(o.X + 271, o.Y + 28), new Color(252, 252, 84));
        }

        // --- M1: HP/AC via NUMBERS.FRM. The bar has baked placeholder digits ("036"/
        // "-258") in dark recessed fields; blank each field to its background colour
        // first (the engine restores the field background before re-rendering), then
        // draw the live value right-aligned over it.
        if (bar.Numbers is { } numbers)
        {
            _panelPixel ??= CreatePixel();
            var fieldBg = new Color(32, 32, 32); // the recessed digit-box interior colour
            _spriteBatch.Draw(_panelPixel, new Rectangle(o.X + 474, o.Y + 40, 33, 17), fieldBg);  // HP box
            _spriteBatch.Draw(_panelPixel, new Rectangle(o.X + 474, o.Y + 75, 33, 17), fieldBg);  // AC box
            // The counters roll toward the live stat (M5); fall back to the real value
            // until the first roll step initialises them.
            int shownHp = _hudDisplayedHp >= 0 ? _hudDisplayedHp : stats.CurrentHp;
            int shownAc = _hudDisplayedAc >= 0 ? _hudDisplayedAc : stats.ArmorClass;
            // HP: white band normal, yellow <50%, red <25% (interface.cc:889-894) — from
            // the shown value so the colour tracks the rolling digits.
            int hpBand = shownHp * 4 <= stats.MaxHp ? 2 : shownHp * 2 <= stats.MaxHp ? 1 : 0;
            DrawCounter(numbers, shownHp, hpBand, xRight: o.X + 505, yTop: o.Y + 40);
            DrawCounter(numbers, shownAc, band: 0, xRight: o.X + 505, yTop: o.Y + 75);
        }

        // AP: light the green dot sockets along the top (interface.cc:974,1001 — 10 dots,
        // x=316 step 9, y=14). A bright-green pip per current action point.
        _panelPixel ??= CreatePixel();
        int ap = Math.Clamp(_combat.DudeAp, 0, 10);
        for (int i = 0; i < ap; i++)
            _spriteBatch.Draw(_panelPixel, new Rectangle(o.X + 316 + i * 9, o.Y + 13, 6, 6), new Color(0, 252, 0));

        // --- M3: the green message monitor (the left screen; bar-local 24,26 ~160x55,
        // display_monitor.cc). Reuse font1.aaf (the engine's interface font) tinted
        // green; wrap to the screen width, newest at the bottom, clipped to fit. The
        // bottom-left fallback log only shows when the bar is hidden (DrawTextOverlay).
        if (_fontRenderer is not null && _messageLog.Count > 0)
        {
            const int mx = 24, my = 26, mw = 162, mh = 56;
            int maxLines = Math.Max(1, mh / _fontRenderer.LineHeight);
            var lines = new List<string>();
            foreach (string msg in _messageLog)
                lines.AddRange(_fontRenderer.WrapText(msg, mw));
            // P52-M5: show a scroll-back window (clicking the monitor halves moves _monitorScroll).
            (int start, int end, _monitorScroll) = Formats.MonitorScrollback.Window(lines.Count, maxLines, _monitorScroll);
            int ty = o.Y + my;
            for (int i = start; i < end; i++)
            {
                _fontRenderer.Draw(_spriteBatch, lines[i], new Vector2(o.X + mx, ty), new Color(0, 252, 0), shadow: false);
                ty += _fontRenderer.LineHeight;
            }
        }

        // M5: the combat-mode buttons over the far-right hazard panel — only during a
        // fight (END TURN / END COMBAT, 38x22 @ 590,43 / 590,65; interface.cc:1893).
        bool inCombat = _combat.Phase != Formats.Combat.CombatPhase.Idle;
        if (inCombat)
        {
            if (bar.EndTurn is not null)
                _spriteBatch.Draw(bar.EndTurn, new Vector2(o.X + 590, o.Y + 43), Color.White);
            if (bar.EndCombat is not null)
                _spriteBatch.Draw(bar.EndCombat, new Vector2(o.X + 590, o.Y + 65), Color.White);
        }

        // M5: press/hover feedback. While the left mouse is held on a button, overlay
        // its DOWN-state art (invbutdn/optidn/…, the same native size as the baked UP
        // button — interface.cc buttonCreate w×h) at the button's top-left; merely
        // hovering gets a soft highlight. HEXWASTE_HUD_FORCE_PRESS=<name> forces the
        // pressed look so the art can be checked in a --screenshot (a live press is
        // otherwise only on screen mid-click). Falls back to a darken tint if the DN
        // art is missing.
        _panelPixel ??= CreatePixel();
        MouseState hoverMouse = Mouse.GetState();
        string? forcePress = Environment.GetEnvironmentVariable("HEXWASTE_HUD_FORCE_PRESS");
        foreach (HudButton b in HudButtons())
        {
            if (b.CombatOnly && !inCombat)
                continue;
            var rect = new Rectangle(o.X + b.Local.X, o.Y + b.Local.Y, b.Local.Width, b.Local.Height);
            bool over = rect.Contains(hoverMouse.X, hoverMouse.Y);
            bool pressed = (over && hoverMouse.LeftButton == ButtonState.Pressed)
                || string.Equals(forcePress, b.Name, StringComparison.OrdinalIgnoreCase);
            if (pressed && bar.Pressed.TryGetValue(b.Name, out Texture2D? dn) && dn is not null)
                _spriteBatch.Draw(dn, new Vector2(rect.X, rect.Y), Color.White);
            else if (pressed)
                _spriteBatch.Draw(_panelPixel, rect, new Color(0, 0, 0, 90));
            else if (over)
                _spriteBatch.Draw(_panelPixel, rect, new Color(255, 255, 255, 45));
        }

        // HEXWASTE_HUD_DEBUG=1: translucent overlay of the clickable button rects to
        // verify they align with the baked iface buttons.
        if (Environment.GetEnvironmentVariable("HEXWASTE_HUD_DEBUG") == "1")
            foreach (HudButton b in HudButtons())
                _spriteBatch.Draw(_panelPixel, new Rectangle(o.X + b.Local.X, o.Y + b.Local.Y, b.Local.Width, b.Local.Height), new Color(255, 0, 0, 90));
    }

    /// <summary>Weapon attack-mode label from the proto's primary attack-anim nibble
    /// (extendedFlags &amp; 0xF; item.cc _attack_anim) — SWING/THRUST/SINGLE/BURST/etc.</summary>
    private static readonly string[] AttackAnimNames =
        ["", "PUNCH", "KICK", "SWING", "THRUST", "THROW", "SINGLE", "BURST", "FLAME"];

    private static string AttackModeName(Formats.Proto.ProtoInfo proto)
    {
        int anim = proto.ExtendedFlags & 0xF;
        return anim >= 0 && anim < AttackAnimNames.Length ? AttackAnimNames[anim] : "";
    }

    /// <summary>Toggle the weapon-slot attack mode (single↔burst) for a burst-capable
    /// gun; a non-burst weapon stays single (P15 M1 — the slot click + N).</summary>
    private void CycleWeaponMode()
    {
        (Formats.Proto.ProtoInfo? weaponProto, _) = _dude is null ? (null, null) : EquippedWeapon(_dude.Dude);
        if (!Formats.Combat.CombatEngine.IsBurstWeapon(weaponProto))
        {
            _weaponMode = WeaponMode.Single;
            Log("This weapon has only a single-shot mode.");
            Console.WriteLine("weapon-mode: Single (single-only)");
            return;
        }
        _weaponMode = _weaponMode == WeaponMode.Single ? WeaponMode.Burst : WeaponMode.Single;
        Log($"Attack mode: {(_weaponMode == WeaponMode.Burst ? "burst" : "single")}.");
        Console.WriteLine($"weapon-mode: {_weaponMode}");
    }

    /// <summary>The clickable HUD buttons (bar-local rects, ported from interface.cc;
    /// measured against this iface.frm). Each wires to the same action as its keyboard
    /// shortcut — the buttons are additive, the keys still work (#15 M4).</summary>
    private readonly record struct HudButton(string Name, Rectangle Local, Action OnClick, bool CombatOnly = false);

    // Bar-local button rects, ported verbatim from interface.cc buttonCreate(x,y,w,h)
    // with gInterfaceBarContentOffset=0 (our native 640-wide bar). These match where
    // the baked iface.frm buttons sit, so the DN press-art overlays exactly.
    private HudButton[] HudButtons() =>
    [
        new("INV", new Rectangle(211, 40, 32, 21), () => { _inventoryOpen = true; _panelPage = 0; PrewarmItemTextures(_dudeInventory); }), // interface.cc:360
        new("OPT", new Rectangle(210, 61, 34, 34), () => { _optionsOpen = true; }),                                       // :380
        new("MAP", new Rectangle(526, 39, 41, 19), () => { _worldmapOpen = true; }),                                      // :433
        new("CHA", new Rectangle(526, 58, 41, 19), () => { if (_dudeGcd is not null) _skillAllocOpen = true; }),          // :475
        new("PIP", new Rectangle(526, 77, 41, 19), () => { _pipboyOpen = true; }),                                        // :454
        new("SKILLDEX", new Rectangle(523, 6, 22, 21), () => { _skilldexOpen = true; }),                                  // :406
        // The weapon slot (interface.cc:505 gSingleAttackButton): click cycles the
        // attack mode (single↔burst); F fires with the selected mode (P15 M1).
        new("WEAPON", new Rectangle(267, 26, 188, 67), CycleWeaponMode),                                                 // :505
        // Combat-mode buttons (shown + clickable only during a fight; M5).
        new("ENDTURN", new Rectangle(590, 43, 38, 22), () => _combat.EndPlayerTurn(), CombatOnly: true),                  // :1903
        new("ENDCOMBAT", new Rectangle(590, 65, 38, 22),                                                                  // :1955
            () => { if (_combat.Phase != Formats.Combat.CombatPhase.Idle) _combat.Reset(); }, CombatOnly: true),
    ];

    /// <summary>Route a left-click to a HUD button if it landed on one. Returns true
    /// when handled (the caller then skips the world-interaction click).</summary>
    private bool TryClickInterfaceBar(int mouseX, int mouseY)
    {
        if (_interfaceBar is not { Loaded: true } bar || _worldmapOpen)
            return false;
        Point o = bar.Origin(GraphicsDevice.Viewport.Bounds);
        // P52-M5: the message monitor's two invisible scroll buttons (display_monitor.cc:382/391 —
        // the top half scrolls toward older history, the bottom half toward the newest).
        var monitor = new Rectangle(o.X + 24, o.Y + 26, 162, 56);
        if (monitor.Contains(mouseX, mouseY))
        {
            _monitorScroll = Math.Max(0, _monitorScroll + (mouseY < monitor.Y + monitor.Height / 2 ? 1 : -1));
            return true;
        }
        bool inCombat = _combat.Phase != Formats.Combat.CombatPhase.Idle;
        foreach (HudButton b in HudButtons())
        {
            if (b.CombatOnly && !inCombat)
                continue;
            var screen = new Rectangle(o.X + b.Local.X, o.Y + b.Local.Y, b.Local.Width, b.Local.Height);
            if (screen.Contains(mouseX, mouseY))
            {
                b.OnClick();
                return true;
            }
        }
        return false;
    }

    /// <summary>Blit a right-aligned integer from NUMBERS.FRM (the engine digit font):
    /// 3 colour bands (band*120), digits 9px (src-x band*120+9*d), minus 6px (+108).
    /// Ported from fallout2-ce src/interface.cc interfaceRenderCounter (:2049-2088).</summary>
    private void DrawCounter(Texture2D numbers, int value, int band, int xRight, int yTop)
    {
        bool negative = value < 0;
        string digits = Math.Abs(value).ToString();
        int width = digits.Length * 9 + (negative ? 6 : 0);
        int x = xRight - width;
        int bandX = band * 120;
        if (negative)
        {
            _spriteBatch.Draw(numbers, new Rectangle(x, yTop, 6, 17), new Rectangle(bandX + 108, 0, 6, 17), Color.White);
            x += 6;
        }
        foreach (char c in digits)
        {
            int d = c - '0';
            _spriteBatch.Draw(numbers, new Rectangle(x, yTop, 9, 17), new Rectangle(bandX + 9 * d, 0, 9, 17), Color.White);
            x += 9;
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

        // AP/HP text HUD above the message log.
        if (_dude is not null && GetCritterState(_dude.Dude) is { } dudeStats)
        {
            string hud = $"HP {dudeStats.CurrentHp}/{dudeStats.MaxHp}  AP {_combat.DudeAp}/{dudeStats.MaxActionPoints}"
                + $"  L{_dudeLevel} XP {_dudeXp}";
            if (AimLocation != Formats.Combat.CriticalTables.LocationUncalled)
                hud += $"  |  aim: {AimName(AimLocation)} (V)";
            if (_combat.Phase != Formats.Combat.CombatPhase.Idle)
                hud += $"  |  round {_combat.Round}: "
                    + (_combat.Phase == Formats.Combat.CombatPhase.PlayerTurn ? "your turn (F attack, Space end turn)" : "enemy turn");
            int hudY = GraphicsDevice.Viewport.Height - _hudBarHeight - 8 - (Math.Min(_messageLog.Count, MessageLogFallbackLines) + 1) * _fontRenderer.LineHeight - 4;
            _fontRenderer.Draw(_spriteBatch, hud, new Vector2(8, hudY), new Color(252, 252, 84));
        }

        if (_combat.IsGameOver)
        {
            _panelPixel ??= CreatePixel();
            _spriteBatch.Draw(_panelPixel,
                new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
                new Color(0, 0, 0, 170));
            var center = new Vector2(GraphicsDevice.Viewport.Width / 2f, GraphicsDevice.Viewport.Height / 2f);
            string[] lines =
            [
                "YOU HAVE DIED",
                $"Level {_dudeLevel}  -  {_dudeXp} XP  -  Day {_clock.Day}",
                "",
                "F9  Load last save",
                "N   New game",
                "Esc Quit",
            ];
            float lineY = center.Y - lines.Length * _fontRenderer.LineHeight;
            foreach (string line in lines)
            {
                Color color = line == lines[0] ? new Color(252, 0, 0) : new Color(252, 252, 84);
                _fontRenderer.Draw(_spriteBatch, line,
                    new Vector2(center.X - _fontRenderer.MeasureWidth(line) / 2f, lineY), color);
                lineY += _fontRenderer.LineHeight * 1.6f;
            }
        }

        if (_movieCard is { } card)
        {
            _panelPixel ??= CreatePixel();
            _spriteBatch.Draw(_panelPixel,
                new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
                new Color(0, 0, 0, 235));
            float cardY = GraphicsDevice.Viewport.Height / 2f - card.Count * _fontRenderer.LineHeight;
            foreach (string line in card)
            {
                _fontRenderer.Draw(_spriteBatch, line,
                    new Vector2(GraphicsDevice.Viewport.Width / 2f - _fontRenderer.MeasureWidth(line) / 2f, cardY),
                    line == card[0] ? new Color(252, 252, 84) : new Color(0, 252, 0));
                cardY += _fontRenderer.LineHeight * 1.5f;
            }
        }

        if (_menu != MenuState.None)
        {
            _panelPixel ??= CreatePixel();
            _spriteBatch.Draw(_panelPixel,
                new Rectangle(0, 0, GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
                new Color(0, 0, 0, 200));
            var center = new Vector2(GraphicsDevice.Viewport.Width / 2f, GraphicsDevice.Viewport.Height / 2f);
            var gold = new Color(252, 252, 84);
            var menuGreen = new Color(0, 252, 0);
            var gray = new Color(140, 140, 140);

            const string title = "H E X W A S T E";
            _fontRenderer.Draw(_spriteBatch, title,
                new Vector2(center.X - _fontRenderer.MeasureWidth(title) / 2f, center.Y - 120), gold);
            const string subtitle = "a Fallout 2 engine slice - needs your own game data";
            _fontRenderer.Draw(_spriteBatch, subtitle,
                new Vector2(center.X - _fontRenderer.MeasureWidth(subtitle) / 2f, center.Y - 120 + _fontRenderer.LineHeight * 1.4f), gray);

            if (_menu is MenuState.Title or MenuState.CharacterPick)
            {
                string[] items = _menu == MenuState.Title
                    ? ["New game", "Quit"]
                    : ["Create your own", .. _premadeGcds.Select(g => g.Label)];
                float itemY = center.Y - 20;
                for (int i = 0; i < items.Length; i++)
                {
                    string line = (i == _menuIndex ? "> " : "  ") + items[i];
                    _fontRenderer.Draw(_spriteBatch, line,
                        new Vector2(center.X - _fontRenderer.MeasureWidth(line) / 2f, itemY),
                        i == _menuIndex ? menuGreen : gray);
                    itemY += _fontRenderer.LineHeight * 1.6f;
                }
                string hint = _menu == MenuState.Title
                    ? "arrows + Enter; Esc quits"
                    : "create or pick a character - arrows + Enter; Esc back";
                _fontRenderer.Draw(_spriteBatch, hint,
                    new Vector2(center.X - _fontRenderer.MeasureWidth(hint) / 2f, itemY + _fontRenderer.LineHeight), gray);
            }
            else
            {
                DrawCreationScreen(center, gold, menuGreen, gray);
            }
        }

        // The log lives in the bar's green monitor (P11 M3); only fall back to the
        // bottom-left when the bar is hidden (no iface art / worldmap open).
        if (_hudBarHeight == 0)
        {
            // The bar-hidden fallback keeps the old recent-5 view (the scrollable history lives in the bar monitor).
            List<string> recent = _messageLog.Count > MessageLogFallbackLines
                ? _messageLog.GetRange(_messageLog.Count - MessageLogFallbackLines, MessageLogFallbackLines)
                : _messageLog;
            int y = GraphicsDevice.Viewport.Height - 8 - recent.Count * _fontRenderer.LineHeight;
            foreach (string message in recent)
            {
                _fontRenderer.Draw(_spriteBatch, message, new Vector2(8, y), green);
                y += _fontRenderer.LineHeight;
            }
        }
    }

    /// <summary>The SPECIAL/gender point-buy and the 3-skill tag picker.</summary>
    private void DrawCreationScreen(Vector2 center, Color gold, Color green, Color gray)
    {
        if (_fontRenderer is null)
            return;
        float lh = _fontRenderer.LineHeight * 1.3f;

        void Row(float x, float yy, string s, Color c) =>
            _fontRenderer.Draw(_spriteBatch, s, new Vector2(x, yy), c);

        if (_menu == MenuState.CreateStats)
        {
            Row(center.X - 200, center.Y - 90, $"CREATE CHARACTER — {_createPoints} points left", gold);
            string[] sp = ["Strength", "Perception", "Endurance", "Charisma", "Intelligence", "Agility", "Luck"];
            float y = center.Y - 60;
            for (int i = 0; i < 7; i++)
            {
                bool sel = _createCursor == i;
                Row(center.X - 200, y, $"{(sel ? ">" : " ")} {sp[i]}", sel ? green : gray);
                Row(center.X - 30, y, $"{_createSpecial[i]}", sel ? green : gold);
                y += lh;
            }
            bool gsel = _createCursor == 7;
            Row(center.X - 200, y, $"{(gsel ? ">" : " ")} Gender", gsel ? green : gray);
            Row(center.X - 30, y, _createGender == 1 ? "Female" : "Male", gsel ? green : gold);

            // live derived readout
            int st = _createSpecial[0], pe = _createSpecial[1], en = _createSpecial[2], ag = _createSpecial[5], lk = _createSpecial[6];
            float dx = center.X + 70, dy = center.Y - 60;
            Row(dx, dy, $"Hit Points {st + 2 * en + 15}", gray);
            Row(dx, dy + lh, $"Action Pts {ag / 2 + 5}", gray);
            Row(dx, dy + lh * 2, $"Armor Class {ag}", gray);
            Row(dx, dy + lh * 3, $"Melee Dmg {Math.Max(st - 5, 1)}", gray);
            Row(dx, dy + lh * 4, $"Sequence {2 * pe}", gray);
            Row(dx, dy + lh * 5, $"Critical % {lk}", gray);
            Row(dx, dy + lh * 6, $"Heal Rate {Math.Max(en / 3, 1)}", gray);

            string hint = _createPoints == 0
                ? "Left/Right adjust · Enter: choose traits · Esc back"
                : "Left/Right adjust · spend all points to continue · Esc back";
            Row(center.X - 200, center.Y + 150, hint, gray);
        }
        else if (_menu == MenuState.CreateTraits)
        {
            Row(center.X - 200, center.Y - 130, $"OPTIONAL TRAITS — {_createTraits.Count}/2 chosen (optional)", gold);
            int perColT = 8;
            for (int i = 0; i < TraitCount; i++)
            {
                bool sel = i == _createTraitIndex;
                bool picked = _createTraits.Contains(i);
                float cx = center.X - 200 + (i / perColT) * 230;
                float cy = center.Y - 100 + (i % perColT) * lh;
                Row(cx, cy, $"{(sel ? ">" : " ")} [{(picked ? "x" : " ")}] {TraitName(i)}",
                    sel ? green : (picked ? gold : gray));
            }
            Row(center.X - 200, center.Y + 90, "Space toggles a trait (max 2) · Enter: tag skills · Esc back", gray);
        }
        else // CreateTags
        {
            Row(center.X - 200, center.Y - 130, $"TAG 3 SKILLS — {_createTags.Count}/3 chosen", gold);
            int perCol = 9;
            for (int i = 0; i < Formats.Combat.SkillSet.SkillCount; i++)
            {
                bool sel = i == _skillAllocIndex;
                bool tagged = _createTags.Contains(i);
                float cx = center.X - 200 + (i / perCol) * 230;
                float cy = center.Y - 100 + (i % perCol) * lh;
                Row(cx, cy, $"{(sel ? ">" : " ")} [{(tagged ? "x" : " ")}] {Formats.Combat.SkillSet.Names[i]}",
                    sel ? green : (tagged ? gold : gray));
            }
            Row(center.X - 200, center.Y + 90,
                _createTags.Count == 3 ? "Space toggles · Enter: begin · Esc back" : "Space toggles a tag · Esc back",
                gray);
        }
    }

    /// <summary>Idle clock + hour-driven ambient (skipped when --ambient fixed it).</summary>
    private void UpdateClock(double elapsedMs)
    {
        _clock.AdvanceRealTime(elapsedMs);
        ProcessPoison(); // P35-M3: poison damage ticks on game time (also catches up after rest/travel jumps)
        ProcessDrugs(); // P37: scheduled drug stat reversals fire on game time (same catch-up after jumps)
        ProcessWithdrawals(); // P38: withdrawal onset/recovery fire on game time (drain-loop for clock jumps)

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

    /// <summary>Snapshots the current map's player-visible changes — every
    /// door's open/locked state, pristine objects gone from the world (by
    /// ordinal), created objects still in it, full container contents, MVARs
    /// — so revisits and saves replay them over pristine + map_enter(0).</summary>
    private void CaptureMapDelta()
    {
        // A transient (saved=No) encounter map is never remembered — it regenerates
        // pristine every visit (phase-10 M0/M3). This is the single _visitedMaps writer,
        // so guarding it here closes BOTH the map-exit path and the F5/save path (which
        // calls this directly, bypassing LoadMap's guard #2) — otherwise saving mid-
        // encounter wrote a phantom delta that replayed the spawned critters on load.
        if (_currentMapTransient)
            return;

        var delta = new SaveState.MapDelta { MapVars = [.. _map.GlobalVariables], SnapshotDay = _clock.Day };

        var present = new HashSet<MapObject>();
        foreach (MapElevation? elev in _map.Elevations)
            if (elev is not null)
                present.UnionWith(elev.Objects);

        // Party members AND live dismissed bodies travel via state.Party /
        // state.DismissedCompanions, OUTSIDE map deltas. The map-exit path pulls them
        // first (→ not present → taken), but an F5 save calls this directly. So mark a
        // still-on-map managed critter's pristine ordinal TAKEN too, and skip them in
        // the Created/Moved/Container loops — otherwise a companion recruited in place
        // (then F5'd before leaving) is restored twice on load.
        var managed = new HashSet<MapObject>(_scriptHost?.PartyMembers ?? []);
        managed.UnionWith(_dismissedCompanions.Keys);

        for (int ordinal = 0; ordinal < _ordinalObjects.Length; ordinal++)
        {
            MapObject o = _ordinalObjects[ordinal];
            if (!present.Contains(o) || managed.Contains(o))
                delta.TakenOrdinals.Add(ordinal);
        }

        for (int elevation = 0; elevation < MapFile.ElevationCount; elevation++)
        {
            foreach (MapObject obj in _map.Elevations[elevation]?.Objects ?? [])
            {
                // Party members are injected after the ordinal build (so they're not in
                // _objectOrdinals) and travel OUTSIDE map deltas via state.Party — exclude
                // them like the dude, else an F5 save (no ExtractPartyFromMap first)
                // captures each companion as a Created object and load duplicates them.
                if (!_objectOrdinals.ContainsKey(obj) && obj != _dude?.Dude && !managed.Contains(obj))
                    delta.Created.Add(new SaveState.CreatedObject(
                        obj.Pid, obj.HexTile, elevation, Math.Max(obj.StackCount, 1)));
                if (IsDoor(obj))
                    delta.Doors.Add(new SaveState.SavedDoor(
                        obj.HexTile, obj.Pid, _openDoors.Contains(obj), obj.IsLockedState));
            }
        }

        // Position drift (wandering NPCs, script moves) — V2.
        var elevationOf = new Dictionary<MapObject, int>();
        for (int elevation = 0; elevation < MapFile.ElevationCount; elevation++)
            foreach (MapObject obj in _map.Elevations[elevation]?.Objects ?? [])
                elevationOf[obj] = elevation;
        for (int ordinal = 0; ordinal < _ordinalObjects.Length; ordinal++)
        {
            MapObject obj = _ordinalObjects[ordinal];
            if (managed.Contains(obj) || !elevationOf.TryGetValue(obj, out int currentElevation))
                continue; // party/dismissed are taken above, not drifted
            (int tile, int rotation, int elevation0) = _pristinePositions[ordinal];
            if (obj.HexTile != tile || obj.Rotation != rotation || currentElevation != elevation0)
                delta.MovedOrdinals.Add(new SaveState.MovedObject(
                    ordinal, obj.HexTile, currentElevation, obj.Rotation));
        }

        // Snapshot containers that hold something now OR were script-stocked
        // at map_enter — an empty snapshot is what keeps looted ones looted.
        // Corpses count as containers (their loot must not resurrect).
        foreach ((MapObject obj, int ordinal) in _objectOrdinals)
        {
            if (!present.Contains(obj) || managed.Contains(obj))
                continue; // party/dismissed carry their own inventory outside the delta
            if (obj.IsDead && Fid.PidType(obj.Pid) == (int)ObjectType.Critter)
                delta.DeadOrdinals.Add(ordinal);
            if (obj.Inventory.Count > 0 || _stockedOrdinals.Contains(ordinal)
                || (obj.IsDead && Fid.PidType(obj.Pid) == (int)ObjectType.Critter))
                delta.ContainerInventories[ordinal] =
                    [.. obj.Inventory.Select(i => new SaveState.SavedItem(i.Pid, Math.Max(i.StackCount, 1),
                        i.Flags & (MapObject.FlagInLeftHand | MapObject.FlagInRightHand | MapObject.FlagWorn), i.AmmoQuantity, i.AmmoTypePid))];
        }

        _visitedMaps[_map.Header.Name] = delta;
    }

    /// <summary>Pre-map_enter delta replay: MVARs scripts read, and removal of
    /// taken objects (their scripts must not run, like absent .SAV objects).</summary>
    private void ApplyDeltaBeforeScripts(SaveState.MapDelta delta)
    {
        for (int i = 0; i < delta.MapVars.Length && i < _map.GlobalVariables.Length; i++)
            _map.GlobalVariables[i] = delta.MapVars[i];

        foreach (int ordinal in delta.TakenOrdinals)
        {
            if (ordinal < 0 || ordinal >= _ordinalObjects.Length)
                continue;
            MapObject taken = _ordinalObjects[ordinal];
            foreach (MapElevation? elev in _map.Elevations)
                elev?.Objects.Remove(taken);
            foreach (List<MapObject> list in _flatObjects.Concat(_solidObjects))
                list.Remove(taken);
        }

        // Drifted objects settle into their saved spots BEFORE map_enter,
        // like objects loading from a .SAV.
        foreach (SaveState.MovedObject moved in delta.MovedOrdinals)
        {
            if (moved.Ordinal < 0 || moved.Ordinal >= _ordinalObjects.Length
                || moved.Elevation is < 0 or >= MapFile.ElevationCount
                || _map.Elevations[moved.Elevation] is not { } targetElev)
                continue;
            MapObject obj = _ordinalObjects[moved.Ordinal];

            foreach (List<MapObject> list in _flatObjects.Concat(_solidObjects))
                list.Remove(obj);
            foreach (MapElevation? elev in _map.Elevations)
                elev?.Objects.Remove(obj);

            obj.HexTile = moved.Tile;
            obj.Rotation = moved.Rotation;
            targetElev.Objects.Add(obj);
            if (!obj.IsHidden && Fid.Type(obj.Fid) is not ObjectType.Head && obj.HexTile >= 0)
                InsertSorted(obj.IsFlat ? _flatObjects[moved.Elevation] : _solidObjects[moved.Elevation], obj);
        }

        // Dead critters: scripts removed BEFORE map_enter (the engine nulls
        // the sid on death and .SAV reloads keep it — combat.cc:4876), so
        // their procs never run again.
        foreach (int ordinal in delta.DeadOrdinals)
        {
            if (ordinal < 0 || ordinal >= _ordinalObjects.Length)
                continue;
            MapObject dead = _ordinalObjects[ordinal];
            dead.Sid = -1;
            dead.CombatResults |= 0x80; // DAM_DEAD
            dead.CurrentHp = Math.Min(dead.CurrentHp, 0);
        }
    }

    /// <summary>Post-map_enter delta replay: door states, created objects,
    /// and container snapshots (overwriting whatever map_enter restocked).</summary>
    private void ApplyDeltaAfterScripts(SaveState.MapDelta delta)
    {
        foreach (SaveState.SavedDoor saved in delta.Doors)
        {
            MapObject? door = _solidObjects.SelectMany(list => list)
                .FirstOrDefault(o => o.HexTile == saved.HexTile && o.Pid == saved.Pid);
            if (door is null)
                continue;
            door.IsLockedState = saved.Locked;
            SetDoorState(door, saved.Open);
        }

        foreach (SaveState.CreatedObject created in delta.Created)
        {
            if (created.Elevation is < 0 or >= MapFile.ElevationCount
                || _map.Elevations[created.Elevation] is not { } elev
                || RebuildObject(created.Pid, created.Count) is not { } obj)
                continue;
            obj.HexTile = created.Tile;
            elev.Objects.Add(obj);
            if (!obj.IsHidden && Fid.Type(obj.Fid) is not ObjectType.Head && obj.HexTile >= 0)
                InsertSorted(obj.IsFlat ? _flatObjects[created.Elevation] : _solidObjects[created.Elevation], obj);
        }

        // Corpse conversion replay (no fall animation on revisit — the body
        // is long cold).
        foreach (int ordinal in delta.DeadOrdinals)
        {
            if (ordinal < 0 || ordinal >= _ordinalObjects.Length)
                continue;
            MapObject dead = _ordinalObjects[ordinal];
            if (Fid.AnimType(dead.Fid) == 0) // not yet converted
                ConvertToCorpse(dead, PickDeathAnim(dead));
        }

        // A script-stocked merchant container restocks from pristine data once
        // its snapshot is older than the window: skip the stale snapshot and
        // keep the fresh map_enter stock (the box's own caps + goods). World
        // loot (footlockers) is never script-stocked, so it always honors its
        // snapshot — a looted chest stays looted.
        int daysElapsed = delta.SnapshotDay > 0 ? _clock.Day - delta.SnapshotDay : 0;
        foreach ((int ordinal, List<SaveState.SavedItem> items) in delta.ContainerInventories)
        {
            if (ordinal < 0 || ordinal >= _ordinalObjects.Length)
                continue;
            if (daysElapsed >= RestockDays && _stockedOrdinals.Contains(ordinal))
            {
                Console.WriteLine($"restock: ordinal {ordinal} refreshed ({daysElapsed}d since snapshot)");
                continue;
            }

            MapObject container = _ordinalObjects[ordinal];
            container.Inventory.Clear();
            foreach (SaveState.SavedItem item in items)
            {
                if (RebuildObject(item.Pid, item.Count) is { } obj)
                {
                    obj.Flags |= item.Flags;
                    obj.AmmoQuantity = item.AmmoQuantity;
                    obj.AmmoTypePid = item.AmmoTypePid;
                    container.Inventory.Add(obj);
                }
            }
        }

        RebuildBlockedTiles(_dude?.Dude);
    }

    /// <summary>Reinstantiates a serialized object from its prototype (deltas
    /// keep only pid + count); null for unknown/broken pids.</summary>
    private MapObject? RebuildObject(int pid, int count)
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
                Fid = _protos.Get(pid).Fid,
                Flags = 0,
                Pid = pid,
                Sid = -1,
            };
            obj.StackCount = Math.Max(count, 1);
            return obj;
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            Console.Error.WriteLine($"load: dropping unknown pid 0x{pid:X8}: {ex.Message}");
            return null;
        }
    }

    /// <summary>The front-door state machine: Title → character pick (Create
    /// or a premade) → optional SPECIAL/tags creation → play.</summary>
    private void HandleMenuInput(KeyboardState k)
    {
        switch (_menu)
        {
            case MenuState.Title:
                MoveMenu(k, 2);
                if (MenuActivated(k))
                {
                    if (_menuIndex == 0) { _menu = MenuState.CharacterPick; _menuIndex = 0; }
                    else Exit();
                }
                if (IsKeyPressed(k, Keys.Escape)) Exit();
                break;

            case MenuState.CharacterPick:
            {
                int n = _premadeGcds.Count + 1; // index 0 = "Create your own"
                MoveMenu(k, n);
                if (MenuActivated(k))
                {
                    if (_menuIndex == 0) EnterCreation();
                    else PickPremade(_menuIndex - 1);
                }
                if (IsKeyPressed(k, Keys.Escape)) { _menu = MenuState.Title; _menuIndex = 0; }
                break;
            }

            case MenuState.CreateStats:
            {
                if (IsKeyPressed(k, Keys.Up)) _createCursor = (_createCursor + 7) % 8;
                if (IsKeyPressed(k, Keys.Down)) _createCursor = (_createCursor + 1) % 8;
                int delta = IsKeyPressed(k, Keys.Right) ? 1 : IsKeyPressed(k, Keys.Left) ? -1 : 0;
                if (delta != 0)
                {
                    if (_createCursor < 7) AdjustCreateStat(_createCursor, delta);
                    else _createGender ^= 1;
                }
                if (IsKeyPressed(k, Keys.Enter))
                {
                    if (_createPoints == 0) { _menu = MenuState.CreateTraits; _createTraitIndex = 0; }
                    else Console.WriteLine("create: spend all character points first");
                }
                if (IsKeyPressed(k, Keys.Escape)) { _menu = MenuState.CharacterPick; _menuIndex = 0; }
                break;
            }

            case MenuState.CreateTraits:
                if (IsKeyPressed(k, Keys.Up)) _createTraitIndex = (_createTraitIndex + TraitCount - 1) % TraitCount;
                if (IsKeyPressed(k, Keys.Down)) _createTraitIndex = (_createTraitIndex + 1) % TraitCount;
                if (IsKeyPressed(k, Keys.Space)) ToggleCreateTrait(_createTraitIndex);
                // Traits are optional — Enter advances even with 0 picked.
                if (IsKeyPressed(k, Keys.Enter)) { _menu = MenuState.CreateTags; _skillAllocIndex = 0; }
                if (IsKeyPressed(k, Keys.Escape)) _menu = MenuState.CreateStats;
                break;

            case MenuState.CreateTags:
                if (IsKeyPressed(k, Keys.Up)) _skillAllocIndex = (_skillAllocIndex + 17) % 18;
                if (IsKeyPressed(k, Keys.Down)) _skillAllocIndex = (_skillAllocIndex + 1) % 18;
                if (IsKeyPressed(k, Keys.Space)) ToggleCreateTag(_skillAllocIndex);
                if (IsKeyPressed(k, Keys.Enter)) FinishCreation();
                if (IsKeyPressed(k, Keys.Escape)) _menu = MenuState.CreateTraits;
                break;
        }
    }

    private void MoveMenu(KeyboardState k, int n)
    {
        if (IsKeyPressed(k, Keys.Up)) _menuIndex = (_menuIndex + n - 1) % n;
        if (IsKeyPressed(k, Keys.Down)) _menuIndex = (_menuIndex + 1) % n;
        for (int i = 0; i < n && i < 9; i++)
            if (IsKeyPressed(k, Keys.D1 + i)) _menuIndex = i;
    }

    private bool MenuActivated(KeyboardState k) =>
        IsKeyPressed(k, Keys.Enter) || Enumerable.Range(0, 9).Any(i => IsKeyPressed(k, Keys.D1 + i));

    /// <summary>Quit the current game back to the title menu (options.cc EXIT path).
    /// The world/dude state lingers in memory; New Game / a premade reinitialises it.</summary>
    private void QuitToMainMenu()
    {
        _combat.Reset();
        _menu = MenuState.Title;
        _menuIndex = 0;
        Console.WriteLine("options: quit to main menu");
    }

    private void PickPremade(int idx)
    {
        if (idx < 0 || idx >= _premadeGcds.Count)
            return;
        string path = _premadeGcds[idx].VirtualPath;
        using (Stream stream = _vfs.OpenRead(path))
            _dudeGcd = Formats.Combat.GcdFile.Load(stream);
        _activeCharacter = Path.GetFileNameWithoutExtension(path);
        StartNewGame();
        _menu = MenuState.None;
    }

    private void EnterCreation()
    {
        Array.Fill(_createSpecial, 5);
        _createPoints = 5;
        _createCursor = 0;
        _createGender = 0;
        _createTags.Clear();
        _createTraits.Clear();
        _createTraitIndex = 0;
        _skillAllocIndex = 0;
        _menu = MenuState.CreateStats;
    }

    /// <summary>Adjust a SPECIAL stat 1..10, charging/refunding the point pool.</summary>
    private void AdjustCreateStat(int stat, int delta)
    {
        int v = _createSpecial[stat] + delta;
        if (v is < 1 or > 10)
            return;
        if (delta > 0 && _createPoints <= 0)
            return;
        _createSpecial[stat] = v;
        _createPoints -= delta;
    }

    private void ToggleCreateTag(int skill)
    {
        if (!_createTags.Remove(skill) && _createTags.Count < 3)
            _createTags.Add(skill);
    }

    /// <summary>Toggle an optional trait, capped at two (the engine's selectable-trait limit).</summary>
    private void ToggleCreateTrait(int trait)
    {
        if (!_createTraits.Remove(trait) && _createTraits.Count < 2)
            _createTraits.Add(trait);
    }

    private void FinishCreation()
    {
        if (_createTags.Count != 3)
        {
            Console.WriteLine("create: pick exactly 3 tag skills (Space)");
            return;
        }
        _dudeGcd = Formats.Combat.GcdFile.Create(_createSpecial, [.. _createTags], _createGender, [.. _createTraits]);
        _dudePerkRanks = new int[Formats.Perks.PerkTable.Count]; // a new character has no perks (P28-M2)
        _activeCharacter = "custom";
        Console.WriteLine($"create: SPECIAL {string.Join("/", _createSpecial)} gender {_createGender}"
            + $" tags [{string.Join(",", _createTags)}] traits [{string.Join(",", _createTraits)}] HP {_dudeGcd.Stats.BaseStats[7]}");
        StartNewGame();
        _menu = MenuState.None;
    }

    /// <summary>Fresh start: wipe session state and reload the first map with
    /// the current character sheet.</summary>
    /// <summary>override_map_start: the map script repositions the dude during
    /// map_enter; LoadMap's camera setup afterwards picks the new spot up.</summary>
    private void OverrideDudeStart(int tile, int elevation, int rotation)
    {
        if (_dude is null)
            return;
        if (elevation is >= 0 and < MapFile.ElevationCount && _map.Elevations[elevation] is not null
            && elevation != _elevation)
        {
            _solidObjects[_elevation].Remove(_dude.Dude);
            _elevation = elevation;
            InsertSorted(_solidObjects[_elevation], _dude.Dude);
        }

        _dude.Dude.HexTile = tile;
        _dude.Dude.Rotation = Math.Clamp(rotation, 0, 5);
        _solidObjects[_elevation].Remove(_dude.Dude);
        InsertSorted(_solidObjects[_elevation], _dude.Dude);
        RebuildBlockedTiles(_dude.Dude);
        _camera.SetCenter(tile);
        Console.WriteLine($"override_map_start: tile={tile} elevation={elevation} rotation={rotation}");
    }

    /// <summary>Script-inflicted damage (traps): armor applies unless the
    /// script set the bypass flag; deaths reuse the combat path.</summary>
    private void OnScriptDamage(MapObject victim, int amount, bool bypassArmor)
    {
        int damage = amount;
        if (!bypassArmor && GetCritterState(victim) is { } stats)
        {
            damage = Math.Max(amount - stats.DamageThreshold, 0);
            damage -= damage * Math.Clamp(stats.DamageResistance, 0, 100) / 100;
        }
        if (damage <= 0)
            return;

        victim.CurrentHp -= damage;
        Log(victim == _dude?.Dude
            ? $"You take {damage} damage!"
            : $"The {ObjectName(victim)} takes {damage} damage!");
        Console.WriteLine($"script-damage: {ObjectName(victim)} -{damage} (hp {victim.CurrentHp})");

        if (victim.CurrentHp <= 0)
        {
            if (victim == _dude?.Dude)
                _combat.GameOver();
            else
                _combat.Kill(victim);
        }
    }

    /// <summary>play_gmovie as a caption card: the title plus the shipped
    /// .sve subtitles (no .mve decoding — the honest poor man's cutscene).</summary>
    private void ShowMovieCard(int movieId)
    {
        // game_movie.cc gMovieFileNames (the ids the opening hour can hit)
        string[] names =
        [
            "iplogo", "intro", "elder", "vsuit", "afailed", "adestroy", "car", "cartucci",
            "timeout", "tanker", "enclave", "derrick", "artimer1", "artimer2", "artimer3",
            "artimer4", "credits",
        ];
        string name = movieId >= 0 && movieId < names.Length ? names[movieId] : $"movie{movieId}";
        var lines = new List<string> { $"[ {name}.mve ]" };

        string svePath = $@"text\english\cuts\{name}.sve";
        if (_vfs.Exists(svePath))
        {
            try
            {
                using Stream stream = _vfs.OpenRead(svePath);
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() is { } line && lines.Count < 12)
                {
                    // frame:text per line
                    int colon = line.IndexOf(':');
                    string text = colon >= 0 ? line[(colon + 1)..].Trim() : line.Trim();
                    if (text.Length > 0)
                        lines.Add(text);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
            }
        }

        Console.WriteLine($"gmovie: {name}.mve ({lines.Count - 1} subtitle lines)");
        foreach (string line in lines.Skip(1))
            Console.WriteLine($"  sve: {line}");

        if (StartInMenu) // caption card only in interactive sessions
            _movieCard = lines;
        else
            foreach (string line in lines)
                Log(line);
    }

    /// <summary>Use the armed inventory item on a clicked object (the
    /// use_obj_on path: crowbar pry, key doors, Vic's radio).</summary>
    private void UseItemOn(MapObject item, MapObject target)
    {
        _pendingUseItem = null;
        var result = _scriptHost?.RunUseObjOn(item, target, _map, _dude?.Dude);
        if (result is null)
        {
            Log($"Using the {ObjectName(item)} on the {ObjectName(target)} does nothing.");
            return;
        }
        foreach (string line in result.Messages)
            Log(line);
        Console.WriteLine($"use-on: {ObjectName(item)} -> {ObjectName(target)} overridden={result.Overridden} lines={result.Messages.Count}");
    }

    private void StartNewGame()
    {
        _dudeLevel = 1;
        _dudeXp = 0;
        _unspentSkillPoints = 0;
        _skillAllocOpen = false;
        _skilldexOpen = false;
        _pendingUseSkill = null;
        _sneak.FlagSet = false;
        _sneak.Working = false;
        Array.Clear(_drugBonus);        // P37: no drug in effect on a fresh game (else a stale
        _pendingDrugEvents.Clear();     // pending kick could fire on the reset clock)
        Array.Clear(_withdrawalBonus);  // P38: no addiction/withdrawal on a fresh game
        _pendingWithdrawalEvents.Clear();
        Array.Clear(_killsByType);      // P38: a fresh game has no kills
        _skillUsesByDay.Clear();
        _skillUsesDay = -1;
        _pipboyOpen = false;
        _pipboyRestMenu = false;
        _automapOpen = false;
        _optionsOpen = false;
        _weaponMode = WeaponMode.Single;
        _dudeInventory = [];
        _visitedMaps.Clear();
        _combat.Reset();
        _clock.Ticks = 302400; // engine boot time
        _lastAmbientHour = -1;

        // A fresh wasteland: clear worldmap whereabouts and drop the parsed
        // worldmap so its one-shot encounter counters re-parse pristine (phase-10
        // M2 — otherwise a one-shot consumed in a prior playthrough this process
        // would stay spent in the new game).
        _worldPosX = _worldPosY = -1;
        _currentAreaId = -1;
        _worldmap = null;
        _worldFog = null; // a new game starts with the whole worldmap unexplored (P22)
        if (_scriptHost is not null)
        {
            _scriptHost.GlobalVars.Clear();
            SeedGlobalVars(); // P32-M1: seed the non-zero vault13.gam globals (e.g. Arroyo rep 50, FIND_VIC 1)
            _scriptHost.ClearAllLocalVars();
            _scriptHost.ExternalVars.Clear();
        }
        ResetParty();

        LoadMap(_mapName, spawnAt: null, captureOutgoing: false);
        Log($"Welcome to the wasteland{(_dudeGcd is { Name.Length: > 0 } g && g.Name != "None" ? $", {g.Name}" : "")}.");
    }

    private void SaveGame()
    {
        CaptureMapDelta();
        SyncDismissedToRoster(); // fold the current map's live dismissed bodies into the roster
        var state = new SaveState
        {
            Version = SaveState.CurrentVersion,
            Map = _currentMapName,
            DudeTile = _dude?.Dude.HexTile ?? _map.Header.EnteringTile,
            DudeRotation = _dude?.Dude.Rotation ?? 0,
            DudeLevel = _dudeLevel,
            DudeXp = _dudeXp,
            DudeHp = _dude?.Dude.CurrentHp ?? -1,
            DudePoison = _dude is { Dude.Poison: > 0 } pd ? pd.Dude.Poison : null, // P35-M3 (sparse: null when not poisoned)
            // P37: persist the active drug bonus + pending wear-off kicks, sparse (null when no drug active).
            DrugBonus = _drugBonus.Any(b => b != 0) ? [.. _drugBonus] : null,
            PendingDrugs = _pendingDrugEvents.Count > 0
                ? [.. _pendingDrugEvents.Select(e => new SaveState.PendingDrug(e.FireTick, e.Stats, e.Amounts))]
                : null,
            // P38: the active withdrawal penalty + pending onset/recovery events (addiction GVARs ride GlobalVars).
            WithdrawalBonus = _withdrawalBonus.Any(b => b != 0) ? [.. _withdrawalBonus] : null,
            PendingWithdrawals = _pendingWithdrawalEvents.Count > 0
                ? [.. _pendingWithdrawalEvents.Select(e => new SaveState.PendingWithdrawal(e.FireTick, e.IsStart, e.Pid, e.Perk))]
                : null,
            KillsByType = _killsByType.Any(k => k != 0) ? [.. _killsByType] : null, // P38 (sparse: null when no kills)
            UnspentSkillPoints = _unspentSkillPoints,
            Character = _activeCharacter,
            DudeSkills = _dudeGcd is not null ? [.. _dudeGcd.Stats.Skills] : null,
            // Only persist perk ranks when something was taken (sparse; a fresh game saves null,
            // which loads as no perks — old-save compatible).
            DudePerkRanks = _dudePerkRanks.Any(r => r > 0) ? [.. _dudePerkRanks] : null,
            // P30 A-M2: persist the sneak state, sparse (null when not sneaking → old-save compatible).
            SneakFlag = _sneak.FlagSet ? true : null,
            SneakWorking = _sneak.Working ? true : null,
            // P31 B-M3: karma/reputation PC-stats, sparse (null at 0 → old-save compatible).
            DudeKarma = _dudeKarma != 0 ? _dudeKarma : null,
            DudeReputation = _dudeReputation != 0 ? _dudeReputation : null,
            DudeBaseStats = _dudeGcd is not null ? [.. _dudeGcd.Stats.BaseStats] : null,
            DudeTaggedSkills = _dudeGcd is not null ? [.. _dudeGcd.TaggedSkills] : null,
            Elevation = _elevation,
            ClockTicks = _clock.Ticks,
            GlobalVars = new Dictionary<int, int>(_scriptHost?.GlobalVars ?? []),
            DudeInventory = [.. _dudeInventory.Select(i => new SaveState.SavedItem(i.Pid, i.StackCount, i.Flags & (MapObject.FlagInLeftHand | MapObject.FlagInRightHand | MapObject.FlagWorn), i.AmmoQuantity, i.AmmoTypePid))],
            VisitedMaps = new Dictionary<string, SaveState.MapDelta>(_visitedMaps),
            DismissedCompanions = _dismissedByMap.ToDictionary(kv => kv.Key, kv => new List<SaveState.DismissedCompanion>(kv.Value)),
            // Drop transient (saved=No) maps' LVAR slices: their sids are reallocated
            // fresh each visit, so saved slices would be orphaned dead weight (phase-10 M3).
            LocalVars = (_scriptHost?.ExportAllLocalVars() ?? [])
                .Where(kv => !_mapList.IsTransient(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value),
            Party = [.. (_scriptHost?.PartyMembers ?? []).Select(m => new SaveState.PartyMemberState(
                m.Pid, _partyScriptIndex.GetValueOrDefault(m, -1), m.CurrentHp, m.Team, m.AiPacket,
                m.Inventory.Select(i => new SaveState.SavedItem(i.Pid, Math.Max(i.StackCount, 1),
                    i.Flags & (MapObject.FlagInLeftHand | MapObject.FlagInRightHand | MapObject.FlagWorn),
                    i.AmmoQuantity, i.AmmoTypePid)).ToList(),
                Waiting: _waitingCompanions.Contains(m),
                OriginalTeam: _originalTeam.GetValueOrDefault(m, m.Team),
                LevelUpLevel: _companionLevelState.GetValueOrDefault(m)?.Level ?? 0,
                LevelUpNumLevelUps: _companionLevelState.GetValueOrDefault(m)?.NumLevelUps ?? 0,
                LevelUpIsEarly: _companionLevelState.GetValueOrDefault(m)?.IsEarly ?? 0,
                PerkRanks: _companionPerkRanks.GetValueOrDefault(m), // P29-M6 (null on the slice)
                Disposition: (int)CompanionSettings(m).Disposition, // P50 combat-control settings
                AttackWho: (int)CompanionSettings(m).AttackWho,
                Distance: (int)CompanionSettings(m).Distance,
                RunAway: (int)CompanionSettings(m).RunAway,
                ChemUse: (int)CompanionSettings(m).ChemUse,
                AreaAttack: (int)CompanionSettings(m).AreaAttack,
                WeaponPref: (int)CompanionSettings(m).WeaponPref))],
            WorldPosX = _worldPosX,
            WorldPosY = _worldPosY,
            CurrentAreaId = _currentAreaId,
            TravelDestinationAreaId = _activeTravel?.Dest.Index ?? -1, // in-flight leg target (P17-M4)
            // _worldmap (not Worldmap): only export if worldmap.txt was actually
            // touched this session — never force-parse it just to save.
            EncounterCounters = _worldmap?.ExportCounters() ?? [],
            // _worldFog (not WorldFog): only export explored subtiles if the fog was
            // touched (any travel) — a fresh game saves an empty dict (P22).
            RevealedSubtiles = _worldFog?.Export() ?? [],
        };
        state.Save(SavePath);
        Log($"Game saved ({Path.GetFileName(SavePath)}).");
        Console.WriteLine($"saved: map={state.Map} tile={state.DudeTile} clock={state.ClockTicks} items={state.DudeInventory.Count} maps={state.VisitedMaps.Count} L{state.DudeLevel} xp={state.DudeXp} hp={state.DudeHp} worldPos=({state.WorldPosX},{state.WorldPosY}) area={state.CurrentAreaId} encCounters={state.EncounterCounters.Count}");
    }

    private void LoadGame()
    {
        SaveState? state = SaveState.Load(SavePath);
        if (state is null)
        {
            Log("No saved game found.");
            return;
        }

        // Ordinal-keyed deltas make cross-version saves silently corrupting —
        // refuse anything but an exact match.
        if (state.Version != SaveState.CurrentVersion)
        {
            Log($"Save is from an incompatible version ({state.Version}, need {SaveState.CurrentVersion}).");
            Console.WriteLine($"load refused: save version {state.Version} != {SaveState.CurrentVersion}");
            return;
        }

        _clock.Ticks = state.ClockTicks;
        _lastAmbientHour = -1;
        if (_scriptHost is not null)
        {
            _scriptHost.GlobalVars.Clear();
            foreach ((int key, int value) in state.GlobalVars)
                _scriptHost.GlobalVars[key] = value;

            // LVARs must be in place BEFORE map_enter runs on the restored
            // map — scripts gate their one-time work on them.
            _scriptHost.ClearAllLocalVars();
            foreach ((string mapName, Dictionary<int, int[]> slices) in state.LocalVars)
                _scriptHost.ImportLocalVars(mapName, slices);
        }

        _visitedMaps.Clear();
        foreach ((string mapName, SaveState.MapDelta delta) in state.VisitedMaps)
            _visitedMaps[mapName] = delta;

        // Worldmap whereabouts + consumed one-shot encounter counters (phase-10
        // M2). Drop the parsed worldmap so the restore starts from PRISTINE
        // counters, exactly like StartNewGame — ImportCounters is a sparse delta
        // (only changed tables), so without this reset a one-shot the abandoned
        // session spent would leak past an F9 reload into a save that left it
        // pristine. Nulling preserves the lazy parse: an empty (Count==0) save
        // leaves _worldmap unparsed; a non-empty one re-parses clean, then
        // applies only the saved deltas.
        _worldPosX = state.WorldPosX;
        _worldPosY = state.WorldPosY;
        _currentAreaId = state.CurrentAreaId;
        _worldmap = null;
        _worldFog = null; // re-create against the freshly parsed worldmap, then import the save
        if (state.EncounterCounters.Count > 0)
            Worldmap.ImportCounters(state.EncounterCounters);
        // Restore explored worldmap subtiles (P22 fog). Like the counters: a non-empty save
        // forces the lazy fog to materialise (against pristine Worldmap) and imports the deltas;
        // an empty save leaves it unmaterialised (a fresh all-UNKNOWN fog on first access).
        if (state.RevealedSubtiles.Count > 0)
            WorldFog.Import(state.RevealedSubtiles);

        // Mid-travel state (P17-M4): drop any stale in-flight leg (its Bresenham cursor is
        // meaningless after a reload) + a pending avoid prompt. If the save was taken mid-
        // walk, queue an auto-resume toward the saved destination (the P16-M2 machinery) —
        // a documented divergence from the engine's drop-stopped reload.
        _activeTravel = null;
        _encounterPrompt = null;
        _resumeTravelDest = state.TravelDestinationAreaId >= 0
            ? _cities.Areas.FirstOrDefault(a => a.Index == state.TravelDestinationAreaId)
            : null;

        ResetParty();

        // Dismissed companions (P10 #3): restore the per-map roster AFTER ResetParty
        // (which cleared it) and BEFORE LoadMap, so the loaded map's are injected.
        foreach ((string mapName, List<SaveState.DismissedCompanion> roster) in state.DismissedCompanions)
            _dismissedByMap[mapName] = roster;

        // captureOutgoing: false — the pre-load world must not leak into the
        // freshly imported VisitedMaps. transient: a saved=No map (a save taken
        // mid-encounter) reloads pristine — and per the documented rule we then drop
        // the player back on the worldmap at the saved worldPos, not mid-ambush.
        bool savedOnTransient = _mapList.IsTransient(state.Map);
        LoadMap(state.Map, new MapDestination(0, state.DudeTile, state.Elevation, state.DudeRotation),
            captureOutgoing: false, transient: savedOnTransient);
        if (savedOnTransient)
            _worldmapOpen = true;

        // Progression: rebuild the sheet from the saved base stats + tags +
        // skills (self-contained — works for created characters); fall back to
        // reloading the named premade for older saves. Then replay level HP.
        _activeCharacter = string.IsNullOrEmpty(state.Character) ? "player" : state.Character;
        _dudeLevel = Math.Max(state.DudeLevel, 1);
        _dudeXp = state.DudeXp;
        _unspentSkillPoints = state.UnspentSkillPoints;

        if (state.DudeBaseStats is { Length: 35 } savedBase)
        {
            _dudeGcd = new Formats.Combat.GcdFile
            {
                Stats = new Formats.Proto.CritterProtoStats(0, 0, 0,
                    [.. savedBase], new int[35], state.DudeSkills is { Length: 18 } s ? [.. s] : new int[18],
                    0, 0, 0, 0),
                Name = _activeCharacter == "custom" ? "Wanderer" : _dudeGcd?.Name ?? "Wanderer",
                TaggedSkills = state.DudeTaggedSkills is { Length: 4 } t ? [.. t] : [-1, -1, -1, -1],
                Traits = [-1, -1],
            };
        }
        else
        {
            string sheetPath = $@"premade\{_activeCharacter}.gcd";
            if (_dudeGcd is not null && _vfs.Exists(sheetPath))
            {
                using Stream gcdStream = _vfs.OpenRead(sheetPath);
                _dudeGcd = Formats.Combat.GcdFile.Load(gcdStream);
            }
            if (_dudeGcd is not null && state.DudeSkills is { Length: 18 } savedSkills)
                Array.Copy(savedSkills, _dudeGcd.Stats.Skills, 18);
        }

        if (_dudeGcd is not null)
        {
            int endurance = _dudeGcd.Stats.BaseStats[Formats.Combat.CritterStat.Endurance];
            _dudeGcd.Stats.BonusStats[Formats.Combat.CritterStat.MaximumHitPoints] +=
                (_dudeLevel - 1) * Formats.Combat.Progression.HpPerLevel(endurance);
        }

        // Restore perk ranks (P28-M2); null/short save → no perks (inert).
        _dudePerkRanks = new int[Formats.Perks.PerkTable.Count];
        if (state.DudePerkRanks is { } savedPerks)
            Array.Copy(savedPerks, _dudePerkRanks, Math.Min(savedPerks.Length, _dudePerkRanks.Length));

        // Restore the sneak state (P30 A-M2); null on a pre-P30 save → not sneaking.
        _sneak.FlagSet = state.SneakFlag ?? false;
        _sneak.Working = state.SneakWorking ?? false;

        // Restore karma/reputation (P31 B-M3); null on a pre-P31 save → 0.
        _dudeKarma = state.DudeKarma ?? 0;
        _dudeReputation = state.DudeReputation ?? 0;
        if (_dude is not null)
        {
            _dude.Dude.CurrentHp = state.DudeHp > 0
                ? state.DudeHp
                : GetCritterState(_dude.Dude)?.MaxHp ?? _dude.Dude.CurrentHp;
            _dude.Dude.Poison = state.DudePoison ?? 0; // P35-M3: restore poison + re-derive the tick schedule
            SchedulePoison();
        }

        // Rebuild the dude's bag from prototypes; worn armor re-applies its
        // bonus stats over the freshly reloaded sheet.
        _dudeInventory.Clear();
        foreach (SaveState.SavedItem item in state.DudeInventory)
        {
            if (RebuildObject(item.Pid, item.Count) is { } obj)
            {
                obj.Flags |= item.Flags;
                obj.AmmoQuantity = item.AmmoQuantity;
                obj.AmmoTypePid = item.AmmoTypePid;
                _dudeInventory.Add(obj);
                if (obj.IsWorn)
                {
                    try
                    {
                        if (_protos.Get(obj.Pid).Armor is { } armor)
                            ApplyArmorBonus(armor, +1);
                    }
                    catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
                    {
                    }
                }
            }
        }

        // P37: restore the active drug bonus AFTER the base+armor sheet rebuild above (the drug
        // contribution is NOT in the base block, so re-apply it here or the pending wear-off would
        // drive the stat negative). Then restore the pending wear-off kicks (they fire on the clock).
        Array.Clear(_drugBonus);
        _pendingDrugEvents.Clear();
        if (state.DrugBonus is { } drugBonus && _dudeGcd is not null)
            for (int s = 0; s < 35 && s < drugBonus.Length; s++)
            {
                _drugBonus[s] = drugBonus[s];
                _dudeGcd.Stats.BonusStats[s] += drugBonus[s];
            }
        if (state.PendingDrugs is { } pending)
            foreach (SaveState.PendingDrug e in pending)
                _pendingDrugEvents.Add((e.FireTick, e.Stats, e.Amounts));

        // P38: restore the withdrawal penalty the same way (re-apply AFTER the sheet rebuild) + the
        // pending onset/recovery events. The addiction GVARs themselves ride GlobalVars (restored above).
        Array.Clear(_withdrawalBonus);
        _pendingWithdrawalEvents.Clear();
        if (state.WithdrawalBonus is { } wdBonus && _dudeGcd is not null)
            for (int s = 0; s < 35 && s < wdBonus.Length; s++)
            {
                _withdrawalBonus[s] = wdBonus[s];
                _dudeGcd.Stats.BonusStats[s] += wdBonus[s];
            }
        if (state.PendingWithdrawals is { } pendingWd)
            foreach (SaveState.PendingWithdrawal e in pendingWd)
                _pendingWithdrawalEvents.Add((e.FireTick, e.IsStart, e.Pid, e.Perk));

        // P38: restore the kill tally (sparse-null on a pre-P38 / no-kills save).
        _killsByType = new int[19];
        if (state.KillsByType is { } kills)
            Array.Copy(kills, _killsByType, Math.Min(kills.Length, _killsByType.Length));

        // Rebuild the companions and stand them next to the dude.
        if (_scriptHost is not null)
        {
            foreach (SaveState.PartyMemberState saved in state.Party)
            {
                if (RebuildObject(saved.Pid, 1) is not { } member)
                    continue;
                member.CurrentHp = saved.Hp;
                member.Team = saved.Team;
                member.AiPacket = saved.AiPacket;
                foreach (SaveState.SavedItem item in saved.Inventory)
                {
                    if (RebuildObject(item.Pid, item.Count) is { } obj)
                    {
                        obj.Flags |= item.Flags;
                        obj.AmmoQuantity = item.AmmoQuantity;
                        obj.AmmoTypePid = item.AmmoTypePid;
                        member.Inventory.Add(obj);
                    }
                }

                _scriptHost.PartyMembers.Add(member);
                if (saved.ScriptListIndex >= 0)
                    _partyScriptIndex[member] = saved.ScriptListIndex;
                // P29-M6: restore per-companion perk ranks (null/empty on the slice → nothing to do).
                if (saved.PerkRanks is { Length: > 0 })
                    _companionPerkRanks[member] = saved.PerkRanks;
                // P50: restore the combat-control disposition (old saves default to CompanionAi.Default
                // via the record's ctor defaults → SetCompanionAi clears it → byte-identical).
                SetCompanionAi(member, new Formats.Combat.CompanionAi(
                    (Formats.Combat.Disposition)saved.Disposition, (Formats.Combat.AttackWho)saved.AttackWho,
                    (Formats.Combat.Distance)saved.Distance, (Formats.Combat.RunAway)saved.RunAway,
                    (Formats.Combat.ChemUse)saved.ChemUse,
                    (Formats.Combat.AreaAttack)saved.AreaAttack, (Formats.Combat.WeaponPref)saved.WeaponPref));
                // Restore the companion control state (phase-10 #2): the "wait here"
                // flag and the pre-recruit team so a later dismiss restores it (not 0).
                if (saved.Waiting)
                    _waitingCompanions.Add(member);
                if (saved.OriginalTeam >= 0)
                    _originalTeam[member] = saved.OriginalTeam;

                // Restore the proto level-up bookkeeping (#10 M3) and re-apply the
                // stage proto as the stat override, so a levelled companion comes back
                // with the right stats (HP already restored from saved.Hp above).
                if (saved.LevelUpLevel > 0 || saved.LevelUpNumLevelUps > 0)
                {
                    _companionLevelState[member] = new Formats.Party.PartyLevelUpState
                    {
                        Level = saved.LevelUpLevel,
                        NumLevelUps = saved.LevelUpNumLevelUps,
                        IsEarly = saved.LevelUpIsEarly,
                    };
                    if (saved.LevelUpLevel > 0 && PartyTable()?.ForPid(saved.Pid) is { } desc
                        && saved.LevelUpLevel <= desc.LevelPids.Count)
                    {
                        try
                        {
                            if (_protos.Get(desc.LevelPids[saved.LevelUpLevel - 1]).Critter is { } stageStats)
                                _companionStatOverride[member] = stageStats;
                        }
                        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException) { }
                    }
                }
            }
            InjectPartyMembers();
        }

        Log("Game loaded.");
        Console.WriteLine($"loaded: map={state.Map} tile={state.DudeTile} clock={state.ClockTicks} items={_dudeInventory.Count} maps={_visitedMaps.Count} L{_dudeLevel} xp={_dudeXp} hp={_dude?.Dude.CurrentHp} worldPos=({_worldPosX},{_worldPosY}) area={_currentAreaId} encCounters={state.EncounterCounters.Count}");
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
        _interfaceBar?.Dispose();
        _pipboyBg?.Dispose();
        _skilldexBox?.Dispose();
        _skilldexBtnOff?.Dispose();
        _skilldexBtnOn?.Dispose();
        _optionsBg?.Dispose();
        _automapBg?.Dispose();
        _fontRenderer?.Dispose();
        _frmCache.Dispose();
        _vfs.Dispose();
        base.UnloadContent();
    }
}
