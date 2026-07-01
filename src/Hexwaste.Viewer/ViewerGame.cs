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

    /// <summary>P81: the dude's ACTIVE weapon hand — always FlagInRightHand or FlagInLeftHand
    /// (the engine's gInterfaceCurrentHand). The active hand's weapon is what EquippedWeapon resolves +
    /// combat fires. Defaults to the right hand (the legacy single-weapon slot) → inert-by-default.</summary>
    private int _activeHand = MapObject.FlagInRightHand;

    /// <summary>Swap which hand is active (the engine's interfaceBarSwapHands, period key). Resets the
    /// attack mode (the new hand's gun may not be burst-capable) and reports the now-active weapon.</summary>
    private void SwapActiveHand()
    {
        _activeHand = _activeHand == MapObject.FlagInRightHand ? MapObject.FlagInLeftHand : MapObject.FlagInRightHand;
        _weaponMode = WeaponMode.Single;
        (_, MapObject? item) = _dude is null ? (null, null) : EquippedWeapon(_dude.Dude);
        string hand = _activeHand == MapObject.FlagInRightHand ? "right" : "left";
        Log($"Active hand: {hand}{(item is null ? " (empty)" : $" — {ObjectName(item)}")}.");
        Console.WriteLine($"swap-hand: active={hand} weapon={(item is null ? "none" : $"0x{item.Pid:X}")}");
    }

    private static readonly string[] AimNames =
        ["head", "left arm", "right arm", "torso", "right leg", "left leg", "eyes", "groin", "uncalled"];
    private static string AimName(int loc) => AimNames[Math.Clamp(loc, 0, AimNames.Length - 1)];

    /// <summary>Premade character sheet to start with (combat/diplomat/stealth);
    /// null/empty = the blank player.gcd. Test plumbing for builds + gender.</summary>
    public string? CharacterName { get; set; }
    private Formats.Combat.ICombatRng _combatRng = new Formats.Combat.SystemCombatRng();
    /// <summary>Isolated AI called-shot RNG (P75-M4) — off the combat to-hit/damage stream so the
    /// 1/called_freq aim roll (≈never for the golden packets) keeps the combat goldens byte-identical.</summary>
    private Formats.Combat.ICombatRng? _calledShotRng;

    /// <summary>The turn machine (phase-9 M0). Owns combat state + orchestration;
    /// this ViewerGame is its ICombatHost. Created in LoadContent once the seeded
    /// RNG is known.</summary>
    private Formats.Combat.CombatEngine _combat = null!;

    /// <summary>critter_p_proc round-robin (the engine's _script_chk_critters
    /// ticker runs ONE critter script per frame; we pump at the 10 Hz game
    /// tick instead of our 60 Hz frame rate).</summary>
    private double _critterProcTimerMs;
    private int _critterProcIndex;

    /// <summary>P1-M2: the recurring map_update heartbeat clock. The engine re-fires SCRIPT_PROC_MAP_UPDATE
    /// for every script every 600 game ticks (scripts.cc:517 mapUpdateEventProcess → queueAddEvent(600));
    /// at Hexwaste's 1-tick=100ms script clock that is 60000 ms. Reset on map load (map_update already runs
    /// once there).</summary>
    private double _mapUpdateClockMs;
    private const double MapUpdateIntervalMs = 600 * 100.0; // 600 game ticks

    /// <summary>Main-menu front door (v0.6): Title → character pick → play.
    /// Headless/test flags skip it entirely.</summary>
    public bool StartInMenu { get; set; }
    /// <summary>P83: optional shell state to boot straight into ("pick" / "create"), for screenshots.</summary>
    public string? MenuStartState { get; set; }

    private enum MenuState { None, Title, CharacterPick, CreateStats, CreateTraits, CreateTags, Credits, Endgame }

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
    /// <summary>Seeded Steal-check RNG (P78) — ISOLATED from the skill/combat streams so a theft never
    /// perturbs them; plus the mark + per-session count/XP for the open steal panel.</summary>
    private Formats.Combat.ICombatRng? _stealRng;
    private MapObject? _stealTarget;
    private int _stealCount;        // _gStealCount — resets per panel open, +1 per item lifted (skill.cc)
    private int _stealSessionXp;    // capped at 300 − Steal skill (inventory.cc:4471)
    private int _stealXpBonus = 10; // grows +10 per stolen item (inventory.cc:4368)
    /// <summary>Seeded AI-taunt RNG (P72-M3) — ISOLATED from the combat stream so the chance/
    /// message rolls never perturb to-hit/damage; the taunt float is Draw-only, so combat goldens
    /// stay byte-identical regardless of whether a critter taunts.</summary>
    private Formats.Combat.ICombatRng? _tauntRng;
    private Formats.Text.MessageFile? _combatAiMsg; private bool _combatAiMsgTried;
    /// <summary>Per-skill uses counted against <see cref="_skillUsesDay"/> — the engine's
    /// skillGetFreeUsageSlot "wait a while" cap (3/day), reset on a new game-day.</summary>
    private readonly Dictionary<int, int> _skillUsesByDay = [];
    private int _skillUsesDay = -1;

    /// <summary>The Skilldex skill ids in panel order (skilldex.cc gSkilldexSkills):
    /// Sneak, Lockpick, Steal, Traps, First Aid, Doctor, Science, Repair.</summary>
    private static readonly int[] SkilldexSkills = [8, 9, 10, 11, 6, 7, 12, 13];

    /// <summary>Pip-Boy (P12 M1): the status + rest panel (PIP.FRM). _pipboyRestMenu
    /// = the rest-duration sub-page (pipboy.cc PipboyRestDuration); _pipboyArchives = the
    /// quest-log page (P88). Holodisks + the alarm remain out of scope (content-gated).</summary>
    private bool _pipboyOpen;
    private bool _pipboyRestMenu;
    private bool _pipboyArchives; // P88: the Archives (quest-log) page
    private Texture2D? _pipboyBg;
    // P88: data\quests.txt + the quest description / location-name message lists, lazy-loaded.
    private IReadOnlyList<Formats.Quest>? _quests;
    private bool _questsTried;
    private Formats.Text.MessageFile? _questsMsg, _mapMsg;
    private bool _questsMsgTried, _mapMsgTried;

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
    private bool _automapHighDetail = true; // the hi/lo-detail switch (automap.cc AUTOMAP_WITH_HIGH_DETAILS) — P82 fix
    private Texture2D? _automapBg;

    /// <summary>The TILES the dude has explored — the automap's OBJECT_SEEN fog-of-war,
    /// ported from fallout2-ce src/object.cc obj_set_seen()/_obj_process_seen(): the engine
    /// marks the tile under each moving object (the dude dominates), then flags objects on
    /// those tiles + a neighbor spread as OBJECT_SEEN. So "seen" is WALKED-TILE accumulation,
    /// NOT a sight radius or line-of-sight (P71). An object shows on the automap iff its tile
    /// is in this set. Accumulated as the dude moves (RevealAround); persisted per-map across
    /// save/load + revisits (P71-M2). DOCUMENTED APPROXIMATION: we reveal the disc of radius
    /// <see cref="AutomapSeenRadius"/> around each walked tile (the engine's _obj_process_seen
    /// ±row/±tile byte-spread doesn't map cleanly onto the hex grid).</summary>
    private HashSet<int> _seenTiles = [];
    private const int AutomapSeenRadius = 4; // the walked-tile neighbor spread (path corridor)

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
    /// <summary>P78-M2: per-NPC combat-drug stat bonus (int[35]) applied when an enemy chems up mid-fight;
    /// GetCritterState folds it into BonusStats. Cleared when combat ends (no timed wear-off for NPCs).</summary>
    private readonly Dictionary<MapObject, int[]> _npcDrugBonus = [];
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
    /// <summary>P87 debug/screenshot aid (--force-head): render this head index on a dialog that the
    /// script gave no head, so the talking-head rendering can be verified on any NPC. -1 = off.</summary>
    public int ForceHeadId { get; set; } = -1;
    /// <summary>The talking head to show for the open dialog: the script's head, else the --force-head
    /// override, else -1 (no head).</summary>
    private int EffectiveHeadId() => _dialog is null ? -1 : (_dialog.HeadId >= 0 ? _dialog.HeadId : ForceHeadId);
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
    // P85: integer zoom (the CLAUDE.md mission's "optional integer zoom"). The WORLD layer scales by
    // _zoom about the screen centre (mouse wheel, 1×..MaxZoom); the HUD/UI stays native. Default 1× =
    // identity → every existing screen/probe path is unchanged.
    private int _zoom = 1;
    private const int MaxZoom = 4;
    /// <summary>Initial/probe world zoom (1×..MaxZoom). Set by --zoom for screenshots; live zoom is the wheel.</summary>
    public int Zoom { get => _zoom; set => _zoom = Math.Clamp(value, 1, MaxZoom); }
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
        public sealed record AwarenessProbe(int Hex) : StartupAction; // P69: the Awareness examine gate
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
        /// <summary>P86: open a barter session with the barter-flagged critter at a hex, bypassing the
        /// dialog gate — a screenshot/debug aid for the barter.frm window (like the other --probe flags).</summary>
        public sealed record OpenBarterAt(int Hex) : StartupAction;
        /// <summary>Click a row of the Options or Pip-Boy menu (phase-15 M3): Menu is
        /// "options" / "pipboy" / "pipboy-rest", Row is 0-based. Drives the same
        /// geometry + dispatch a live click does and reports which row was hit.</summary>
        public sealed record MenuClick(string Menu, int Row) : StartupAction;
        /// <summary>P83-M1: dump the authentic main-menu button layout (window-local rects + the
        /// misc.msg labels + enabled flags), hit-testing each band centre back to prove the geometry
        /// round-trips. Window-independent (local coords), so it is a deterministic data-backed golden.</summary>
        public sealed record MenuProbe : StartupAction;
        public sealed record UseSkill(int Skill, int TargetHex) : StartupAction;
        public sealed record RestFor(int Minutes) : StartupAction;
        public sealed record OpenAutomap : StartupAction;
        /// <summary>P71: reveal the automap fog around a hex (as if the dude walked there) —
        /// drives RevealAround so a --save-now/--load-now round-trip can prove the fog persists.</summary>
        public sealed record RevealAt(int Hex) : StartupAction;
        /// <summary>P74-M3: report the value has_skill (0x80AA) returns for a critter (hex&lt;0 = the dude) —
        /// the critter's effective skill % via the wired SkillResolver. State-only.</summary>
        public sealed record HasSkillProbe(int Hex, int Skill) : StartupAction;
        /// <summary>P75-M3: report the dude's effective MaximumHitPoints (for the Lifegiver level-up proof).</summary>
        public sealed record MaxHpProbe : StartupAction;
        /// <summary>P72-M3: report a critter's ai.txt taunt config + the deterministic attack/run
        /// message-id picks under <paramref name="Seed"/> (state-only — IDs/ranges, never the text).</summary>
        public sealed record TauntProbe(int Hex, int Seed) : StartupAction;
        /// <summary>Phase-22: travel a worldmap leg from (X,Y) toward AreaIndex (avoiding the
        /// prompt) and report the fog-of-war reveal — proves subtiles get marked VISITED/KNOWN
        /// as the party walks, and that the destination subtile becomes clear.</summary>
        public sealed record FogProbe(int X, int Y, int AreaIndex) : StartupAction;
        /// <summary>Center the camera on a hex (screenshot testing, e.g. P23 translucency).</summary>
        public sealed record CenterHex(int Hex) : StartupAction;
        public sealed record CursorAt(int Hex) : StartupAction; // P82-M5: force the hex ring for a screenshot
        public sealed record ActionMenuProbe(int Hex) : StartupAction; // P82-M6: the action-menu item list at a hex
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

        // P100 (Point 1): print the victory-slide selection for the current/forced GVARs (endgame.txt).
        public sealed record EndgameProbe(int? Gvar, int? Value) : StartupAction;

        // P100 (Point 1): print the death-ending narration selected for a reason + RNG seed (enddeath.txt).
        public sealed record DeathEndingProbe(int Reason, int Seed) : StartupAction;
        /// <summary>Report whether a critter PID is a data\party.txt recruitable companion (and its
        /// level_minimum) — verifies a recruitment is the Vic-pattern (feasible) vs needs custom content.</summary>
        public sealed record PartyProbe(int Pid) : StartupAction;
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
        public sealed record AcDodgeProbe(int EnemyHex) : StartupAction;
        public sealed record Steal(int TargetHex, int Row) : StartupAction;
        public sealed record AiDrugProbe(int Hex, int DrugPid) : StartupAction;
        public sealed record SwapHand : StartupAction;
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
        /// <summary>P1-M2 diagnostic: drive the recurring map_update heartbeat directly. Pump half an
        /// interval (must NOT fire) then Beats full 600-tick intervals (each fires once), and report the
        /// counts — a deterministic, headless proof of the heartbeat cadence. STATE-only.</summary>
        public sealed record MapUpdateHeartbeatProbe(int Beats) : StartupAction;
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
        /// <summary>P73: spawn an X-FIGHTING-Y encounter as a dude-ABSENT brawl and run it to
        /// completion (state-only — the winning team + rounds + survivors).</summary>
        public sealed record BrawlWatch(string Map, string GroupA, int CountA, string GroupB, int CountB) : StartupAction;
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
        public sealed record ShowInventory : StartupAction; // P67: open the inventory for a screenshot
        public sealed record ShowCharacter(int Sel = 0) : StartupAction; // P82: open the character sheet (Sel = selected EDITOR_* item) for a screenshot
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
        _calledShotRng = new Formats.Combat.SystemCombatRng(RngSeed ?? Environment.TickCount); // P75-M4 isolated
        _combat = new Formats.Combat.CombatEngine(this, _combatRng, _calledShotRng);

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
                // P54-M2 (Vault City): elevation(obj) finds which elevation list holds the object
                // (the dude/party aren't in those lists → fall back to the current elevation, which is theirs).
                ElevationProvider = ElevationOfObject,
                // anim(obj, code): play a one-shot animation on a critter (anim codes < ANIM_COUNT≈40);
                // rotation/invalid codes (the engine's animate-rotation path) are ignored.
                AnimRequested = (obj, anim) =>
                {
                    if (anim is >= 0 and < 40 && Fid.Type(obj.Fid) is ObjectType.Critter)
                        _animator.PlayActionOnce(obj, Fid.Build(ObjectType.Critter, Fid.Index(obj.Fid), anim,
                            Fid.WeaponCode(obj.Fid), obj.Rotation));
                },
                // P56-M2 (Modoc): set_map_start repositions the dude + camera to the new start tile (engine
                // mapSetStart + tileSetCenter). No dude headless (--map smoke) → only the camera moves, so the
                // census is unchanged. kill_critter_type destroys a proto type (inert on the slice — no map
                // fires it after M1's branch shift — but faithful when a quest activates the branch).
                SetMapStartRequested = (x, y, elev, rot) =>
                {
                    int tile = 200 * y + x;
                    if (_dude is not null)
                    {
                        _dude.Dude.HexTile = tile;
                        _dude.Dude.Rotation = Math.Clamp(rot, 0, 5);
                    }
                    if (elev is >= 0 and < MapFile.ElevationCount)
                        _elevation = elev;
                    _camera.SetCenter(tile);
                },
                KillCritterTypeRequested = KillCrittersByType,
                KillCritterRequested = KillCritterObject, // P0: kill_critter (0x80ED)
                PerkRemoveRequested = perkIndex => // P0: critter_rm_trait (0x8103, perk-only)
                {
                    if (perkIndex >= 0 && perkIndex < _dudePerkRanks.Length)
                        _dudePerkRanks[perkIndex] = 0; // engine loops perkRemove to 0; effects are rank-derived
                },
                UseObjOnObjRequested = (item, target) => // P0: use_obj_on_obj (0x8145)
                {
                    if (_scriptHost?.RunUseObjOn(item, target, _map, _dude?.Dude) is { } r)
                        foreach (string line in r.Messages)
                            Log(line);
                },
                WorldMapRequested = () => // P0: scripts_request_world_map (0x8108) — leave to the worldmap
                    _pendingTransition = new MapDestination(-1, 0, 0, 0),
                LoadMapRequested = mapIndex => // P0: load_map (0x80E4) — deferred transition to the map default start
                    _pendingTransition = new MapDestination(mapIndex, -1, -1, -1),
                MapIndexByNameProvider = name => _mapList.GetIndexByFileName(name), // load_map(string) name->index
                WmAreaSetPosRequested = (city, x, y) => // P0: wm_area_set_pos (0x80E5) — relocate a town marker
                {
                    if (_cities.Areas.FirstOrDefault(a => a.Index == city) is { } area)
                    {
                        area.WorldX = x;
                        area.WorldY = y;
                    }
                },
                AttackSetupRequested = (attacker, defender) => // P0: attack_setup (0x8143) — script forces combat
                {
                    if (defender == _dude?.Dude)
                        _combat.BeginScriptAggro(attacker, defender); // a master/NPC duels or ambushes the player
                    else if (attacker != _dude?.Dude)
                        _combat.StartBrawl([attacker, defender], dudeSpectator: true); // NPC-vs-NPC the dude only watches
                },
                ExplosionRequested = (tile, elevation, minDamage, maxDamage) => // P0: explosion (0x811A) — script blast
                {
                    if (elevation == _elevation) // a blast on another elevation never reaches the current critters
                        _combat.Explode(tile, killer: null, minDamage, maxDamage, radius: 3);
                },
                CritterModSkillRequested = (skill, points) => // P0: critter_mod_skill (0x813C), dude-only
                {
                    if (_dudeGcd is null || skill < 0 || skill >= 18 || points == 0)
                        return;
                    int n = Math.Abs(points);
                    if (Array.IndexOf(_dudeGcd.TaggedSkills, skill) >= 0)
                        n /= 2; // tagged skills grant/cost half (skill.cc:251)
                    int[] sk = _dudeGcd.Stats.Skills; // same array the skill resolver + skill-book read/write
                    for (int i = 0; i < n; i++)
                    {
                        if (points > 0)
                        {
                            if (DudeSkillValue(skill) >= 300) break; // skillAddForce caps at value 300
                            sk[skill]++;
                        }
                        else
                        {
                            sk[skill]--; // skillSubForce
                        }
                    }
                },
                IsLoadingGameProvider = () => _isLoadingGame,
                // P57 (Broken Hills): set_exit_grids retargets every exit-grid object on the source
                // elevation (the engine discards the rotation arg, so preserve the parsed one).
                // ported from fallout2-ce src/interpreter_extra.cc opSetExitGrids()
                SetExitGridsRequested = (elev, destMap, destElev, destTile) =>
                {
                    if (elev < 0 || elev >= MapFile.ElevationCount)
                        return;
                    foreach (MapObject o in _flatObjects[elev].Concat(_solidObjects[elev]))
                        if (Fid.IsExitGridPid(o.Pid))
                            o.Destination = new MapDestination(destMap, destTile, destElev, o.Destination?.Rotation ?? 0);
                },
                // wield_obj_critter: the critter equips the item — weapon to the right hand (the proven
                // P43 EquipWeapon path), armor worn (dude-only AC bonus, mirroring the engine's _adjust_ac;
                // NPC-armor AC is forward-looking infra — the slice wields weapons only).
                // ported from fallout2-ce src/interpreter_extra.cc opWieldItem()
                WieldObjCritterRequested = (critter, item) =>
                {
                    if (SafeProto(item.Pid)?.Armor is { } armor)
                    {
                        item.Flags |= MapObject.FlagWorn;
                        if (critter == _dude?.Dude)
                            ApplyArmorBonus(armor, +1);
                    }
                    else
                        EquipWeapon(critter, item);
                },
                // P58 (New Reno): mark_area_known reveals a worldmap area. INERT on the slice — every NR
                // area is city.txt start_state=On (already discovered) — so this is forward-looking infra.
                // mode 1 (map-mark, no areaIdx table) + markType -66 (INVISIBLE hide, no fog downgrade) are
                // documented no-ops. ported from fallout2-ce src/interpreter_extra.cc opMarkAreaKnown()
                MarkAreaKnownRequested = (markType, areaId, mode) =>
                {
                    if (mode != 0 || markType == -66)
                        return;
                    if (_cities.Areas.FirstOrDefault(a => a.Index == areaId) is { } area)
                        WorldFog.MarkRadiusVisited(area.WorldX, area.WorldY);
                },
                // game_time_advance bumps the clock by the raw tick count (TicksPerDay==864000, 1:1 with the
                // engine) then runs the poison/drug/withdrawal catch-up — the engine's queueProcessEvents()
                // per chunk. ported from fallout2-ce src/interpreter_extra.cc opGameTimeAdvance()
                GameTimeAdvanceRequested = ticks =>
                {
                    _clock.Ticks += ticks;
                    ProcessPoison();
                    ProcessDrugs();
                    ProcessWithdrawals();
                },
                // P63 (Sierra Army Depot): tile_contains_obj_pid scans every object at (tile, elevation) for
                // the pid (the engine's objectFindFirstAtLocation loop). ported from opTileContainsObjectWithPid.
                TileContainsObjPidProvider = (tile, elevation, pid) =>
                    elevation >= 0 && elevation < MapFile.ElevationCount
                    && _solidObjects[elevation].Concat(_flatObjects[elevation]).Any(o => o.HexTile == tile && o.Pid == pid),
                // animate_stand_reverse_obj: the object plays its ANIM_STAND once, !combat-gated like the engine.
                // DOCUMENTED SIMPLIFICATION: the engine plays the stand anim REVERSED (a lie/sit-down); we play
                // it forward via the proven P54 Anim path (cosmetic, Draw-only, never in a golden).
                // ported from fallout2-ce src/interpreter_extra.cc opAnimateStandReverse()
                AnimateStandReverseRequested = obj =>
                {
                    if (_combat.Phase == Formats.Combat.CombatPhase.Idle && Fid.Type(obj.Fid) is ObjectType.Critter)
                        _animator.PlayActionOnce(obj, Fid.Build(ObjectType.Critter, Fid.Index(obj.Fid),
                            0 /* ANIM_STAND */, Fid.WeaponCode(obj.Fid), obj.Rotation));
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
                // P74-M3: has_skill reads the full effective skill (gcd skills + tags + perk/trait mods).
                SkillResolver = (obj, skill) => GetCritterState(obj)?.SkillValue(skill) ?? 0,
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
                EndgameSlideshowRequested = ShowEndgameSlideshow,
                EndgameMovieRequested = ShowEndgameMovie,
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
            switch (MenuStartState) // P83: boot straight into a shell sub-screen (for screenshots)
            {
                case "pick": _menu = MenuState.CharacterPick; _premadeSel = 0; break;
                case "create": EnterCreation(); break;
                case "credits": _menu = MenuState.Credits; _creditsScroll = 320; break; // mid-scroll for the screenshot
                case "death": _menu = MenuState.None; _debugDeathScreen = true; break;
                case "endgame": _scriptHost.GlobalVars[408] = 1; ShowEndgameSlideshow(); break; // Arroyo victory slide, for a screenshot

            }
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

    /// <summary>True only while LoadGame replays the restored map's scripts (map_enter/
    /// map_update). Mirrors the engine's _isLoadingGame() (interpreter_extra.cc:2384) —
    /// gates kill_critter_type so a save-restore never re-destroys critters.</summary>
    private bool _isLoadingGame;

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
        _seenTiles.Clear(); // automap fog resets per map (P71; M2 restores per-map persisted tiles)
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
            // P1: the engine's start pass (map.cc:1006 scriptsExecStartProc) runs BEFORE map_enter so
            // every script's first execution (its global-init prologue) publishes its exported variables —
            // a combat-only script (dcLara/dcTyler) otherwise never exports gang_2_member_* and importers
            // resolve them to 0. Snapshotted before map_enter creates stocking objects (engine order).
            _scriptHost.RunStartProcs(_map, scripted, _dude?.Dude);
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
            // included (faithful). P1-M2 adds the recurring 600-tick re-run (mapUpdateEventProcess)
            // in Update; the clock resets here so the first heartbeat lands 600 ticks after load.
            _scriptHost.RunMapUpdate(_map, scripted, _dude?.Dude);
            _mapUpdateClockMs = 0;
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

        // Reveal the worldmap subtile of the town we just walked into, so the place
        // we're standing isn't a black hole on the worldmap fog. Without this the
        // worldmap stays 100% fogged until the first travel leg. Only non-transient
        // (MAP_SAVED) maps that belong to a worldmap area reveal it; transient encounter
        // maps reveal nothing. ported from fallout2-ce src/worldmap.cc wmMapMarkVisited()
        if (!transient)
            RevealCurrentWorldArea(mapName);
    }

    /// <summary>Reveal the worldmap fog around the area whose entrance is the just-loaded
    /// map — the engine reveals a town's worldmap subtile on entry. ported from fallout2-ce
    /// src/worldmap.cc wmMapMarkVisited() → wmMatchAreaContainingMapIdx → wmAreaMarkVisited →
    /// wmMarkSubTileRadiusVisited.</summary>
    private void RevealCurrentWorldArea(string mapName)
    {
        int mapIdx = _mapList.GetIndexByFileName(mapName);
        if (mapIdx < 0)
            return;
        foreach (WorldArea area in _cities.Areas)
            foreach (AreaEntrance entrance in area.Entrances)
                if (_mapList.FindByLookupName(entrance.MapLookupName) == mapIdx)
                {
                    WorldFog.MarkRadiusVisited(area.WorldX, area.WorldY);
                    return;
                }
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

        // P87: advance the talking-head fidget on a wall-time tick while a head dialog is open (Draw-only
        // state — never read by the headless transcript goldens). Reset when no head is shown.
        if (EffectiveHeadId() >= 0)
        {
            _headFrameTimerMs += gameTime.ElapsedGameTime.TotalMilliseconds;
            while (_headFrameTimerMs >= HeadFrameMs) { _headFrameTimerMs -= HeadFrameMs; _headFrame++; }
        }
        else { _headFrame = 0; _headFrameTimerMs = 0; }

        // Main menu / character creation: the world idles underneath. The menu/creation flow plays the
        // engine's menu music (mainmenu.cc → 07desert); PlayMusic de-dups so calling each frame is a no-op.
        // When LOAD GAME opens the 10-slot picker FROM the menu, defer to the _saveLoadOpen handler below so
        // its input runs (otherwise the picker freezes and Esc quits the app) — P83-M1 review fix.
        if (_menu != MenuState.None && !_saveLoadOpen)
        {
            _audio?.PlayMusic("07desert");
            if (_menu == MenuState.Credits)
                UpdateCredits(gameTime.ElapsedGameTime.TotalMilliseconds, keyboard, mouse);
            else if (_menu == MenuState.Endgame)
                UpdateEndgame(gameTime.ElapsedGameTime.TotalMilliseconds, keyboard, mouse);
            else
            {
                HandleMenuInput(keyboard);
                HandleMenuMouse(mouse);
                HandleSelectorMouse(mouse);
                HandleCreationMouse(mouse);
            }
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

        // Movie caption card: any key OR a mouse click dismisses it (the engine skips a movie on key/click).
        if (_movieCard is not null)
        {
            bool keyDown = keyboard.GetPressedKeyCount() > 0 && _previousKeyboard.GetPressedKeyCount() == 0;
            bool clickDown = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
            if (keyDown || clickDown)
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

            // P82-M2: click a stat/skill/derived/condition info area -> select it (the description
            // card updates); clicking a skill also arms it for an Enter-raise.
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
            {
                if (CharSheetItemAt(mouse.X, mouse.Y) is var sel && sel >= 0)
                {
                    _charSelId = sel;
                    if (sel is >= 61 and < 79)
                        _skillAllocIndex = sel - 61;
                }
                // P82-M4: the DONE / CANCEL buttons (the baked red buttons, y~454) close the sheet.
                Rectangle cvp = GraphicsDevice.Viewport.Bounds;
                int cbx = mouse.X - (cvp.Width - 640) / 2, cby = mouse.Y - (cvp.Height - 480) / 2;
                if (cby is >= 448 and < 476 && cbx is >= 462 and < 640)
                    _skillAllocOpen = false;
            }

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
            // A click fires the same action its keyboard shortcut does (P15 M3): the right-side content rows
            // (PipboyRowAt) OR the PIP.frm left-column tabs (PipboyTabAt — Automaps/Close, P82 fix).
            bool pipPress = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
            if (pipPress && PipboyTabAt(mouse.X, mouse.Y) is { } tabAction)
            {
                tabAction();
            }
            else if (pipPress && PipboyRowAt(mouse.X, mouse.Y) is var prow && prow >= 0)
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

        // Automap (Pip-Boy → A): a full-window object plot. The baked-in AUTOMAP.frm buttons are now wired
        // (P82): CANCEL (or Esc/A) closes; the hi/lo SWITCH (or H/L) toggles detail; SCANNER (or S) is a no-op
        // (no motion scanner modelled); PgUp/PgDn switch elevation on a multi-level map.
        if (_automapOpen)
        {
            (Rectangle scanner, Rectangle cancel, Rectangle detail) = AutomapButtons();
            bool apress = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
            if (IsKeyPressed(keyboard, Keys.Escape) || IsKeyPressed(keyboard, Keys.A)
                || (apress && cancel.Contains(mouse.X, mouse.Y)))
                _automapOpen = false;
            else if (IsKeyPressed(keyboard, Keys.H) || IsKeyPressed(keyboard, Keys.L)
                || (apress && detail.Contains(mouse.X, mouse.Y)))
            {
                _automapHighDetail = !_automapHighDetail;
                Log($"Automap detail: {(_automapHighDetail ? "high" : "low")}.");
            }
            else if (IsKeyPressed(keyboard, Keys.S) || (apress && scanner.Contains(mouse.X, mouse.Y)))
                Log("No motion scanner.");
            if (IsKeyPressed(keyboard, Keys.PageUp))
                SwitchElevation(+1);
            if (IsKeyPressed(keyboard, Keys.PageDown))
                SwitchElevation(-1);
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
                    _menu = MenuState.None; // close the main menu if we loaded from it (no-op in-game)
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
            int barterRows = PanelPageRows(); // P89-fix: the strip shows 5 rows, so only 1-5 map (bug_001)
            for (int i = 0; i < barterRows; i++)
            {
                if (IsKeyPressed(keyboard, Keys.D1 + i) || IsKeyPressed(keyboard, Keys.NumPad1 + i))
                {
                    int gi = _panelPage * barterRows + i;
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
            int rows = PanelPageRows(); // P89-fix: the trade strip shows 5 rows; loot/inventory 9 (bug_001)
            for (int i = 0; i < rows; i++)
            {
                if (IsKeyPressed(keyboard, Keys.D1 + i) || IsKeyPressed(keyboard, Keys.NumPad1 + i))
                {
                    int gi = _panelPage * rows + i;
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
            bool clickPress = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
            // The INVBOX DONE button closes the pure inventory (the baked-in art button, inventory.cc).
            if (clickPress && _inventoryOpen && _lootContainer is null && _tradePartner is null
                && InvBoxDoneRect() is { } done && done.Contains(mouse.X, mouse.Y))
            {
                _inventoryOpen = false;
                _stealTarget = null;
            }
            else if (_inventoryOpen && _lootContainer is null && _tradePartner is null)
                HandleInventoryDrag(mouse, shiftHeld);
            else if (clickPress)
                TryClickItemPanel(mouse.X, mouse.Y, shiftHeld);

            HandlePanelPaging(keyboard);

            if (_lootContainer is not null && IsKeyPressed(keyboard, Keys.A))
                TakeAllFromContainer();

            if (IsKeyPressed(keyboard, Keys.Escape) || IsKeyPressed(keyboard, Keys.I))
            {
                _lootContainer = null;
                _inventoryOpen = false;
                _tradePartner = null;
                _stealTarget = null;
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
            { _pipboyOpen = true; _pipboyArchives = false; } // P88: (re)open on the STATUS page

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

        // P85: the mouse wheel zooms the world 1×..MaxZoom about the screen centre (the HUD stays
        // native). ScrollWheelValue is cumulative; a positive delta (scroll up) magnifies.
        int wheelDelta = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
        if (wheelDelta != 0)
            _zoom = Math.Clamp(_zoom + Math.Sign(wheelDelta), 1, MaxZoom);

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

        // '.' : swap the active weapon hand (P81 — the engine's swap-hands), firing the other ready slot.
        if (IsKeyPressed(keyboard, Keys.OemPeriod))
            SwapActiveHand();

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
        // P78-M2: NPC combat-drug buffs last only the fight — drop them once combat is over.
        if (_npcDrugBonus.Count > 0 && _combat.Phase == Formats.Combat.CombatPhase.Idle)
            _npcDrugBonus.Clear();
        _dude?.Update(gameTime.ElapsedGameTime.TotalMilliseconds);
        UpdateAmbientLife(gameTime.ElapsedGameTime.TotalMilliseconds);
        TickAmbientSfx(gameTime.ElapsedGameTime.TotalMilliseconds); // P34-M5 ambient sfx
        _floatText.Tick(gameTime.ElapsedGameTime.TotalMilliseconds); // P45 floating combat text

        // Script timers: pumped only here — dialog/loot/worldmap modes return
        // earlier in Update, matching the engine's _gdialogActive() gate.
        _scriptHost?.PumpTimers(gameTime.ElapsedGameTime.TotalMilliseconds, _dude?.Dude);
        PumpCritterProcs(gameTime.ElapsedGameTime.TotalMilliseconds);
        PumpMapUpdate(gameTime.ElapsedGameTime.TotalMilliseconds); // P1-M2: recurring 600-tick map_update

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
        (int pickX, int pickY) = ToWorldPoint(mouse.X, mouse.Y); // P85: zoom-correct the pointer before picking
        _hoveredObject = PickAt is { } fixedPoint
            ? PickObject(fixedPoint.X, fixedPoint.Y)
            : PickObject(pickX, pickY);

        if (PickAt is { } p && !_pickPrinted)
        {
            Console.WriteLine($"pick@{p.X},{p.Y}: "
                + (_hoveredObject is null ? "nothing" : DescribeObject(_hoveredObject)));
            _pickPrinted = true;
        }

        if (_hoveredObject != previousHover)
            Window.Title = _hoveredObject is null ? _baseTitle : $"{_baseTitle} — {DescribeObject(_hoveredObject)}";

        // P82-M6: right-click opens the FO2 action menu on the hovered object (the old right-click-
        // examines is now the menu's "Look" item). A second right-click / Esc closes it. (FO2 uses
        // left-click-hold; right-click is our documented divergence.)
        if (!_debugForceActionMenu && mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released)
        {
            if (_actionMenuObj is not null)
                CloseActionMenu();
            else if (_hoveredObject is not null)
                OpenActionMenu(_hoveredObject, mouse.X, mouse.Y);
        }
        if (!_debugForceActionMenu && _actionMenuObj is not null && IsKeyPressed(keyboard, Keys.Escape))
            CloseActionMenu();

        // Click: doors toggle, stairs/ladders travel, other objects identify,
        // open ground walks.
        if (!_debugForceActionMenu && mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
        {
            // P82-M6: the action menu (opened via right-click) consumes the left click first — pick
            // an item, or close if the click misses the menu.
            if (_actionMenuObj is not null)
            {
                int amRow = ActionMenuRowAt(mouse.X, mouse.Y);
                if (amRow >= 0)
                    DispatchActionMenu(amRow);
                else
                    CloseActionMenu();
            }
            // A click on a HUD bar button (INV/OPT/MAP/CHA/PIP/SKILLDEX) is consumed
            // there and does not also walk/interact with the map underneath (#15 M4).
            else if (TryClickInterfaceBar(mouse.X, mouse.Y))
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
                int target = PickHex(mouse.X, mouse.Y); // P85: zoom-correct click-to-move
                // Phase-18 M0: in combat a move needs AP for at least the first hex (P74-M4: the Bonus
                // Move free-move pool counts toward affording the hex).
                if (_combat.Phase != Formats.Combat.CombatPhase.Idle
                    && GetCritterState(_dude.Dude) is { } walkStats
                    && _combat.DudeAp + _combat.DudeFreeMove < Formats.Combat.CritterState.MovePointCost(_dude.Dude.CombatResults))
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
                // P74-M4: the free-move pool + AP together must cover the next hex.
                if (_combat.DudeAp + _combat.DudeFreeMove < Formats.Combat.CritterState.MovePointCost(dude.CombatResults))
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
    /// Blocking per fallout2-ce src/object.cc _obj_blocking_at(): non-NO_BLOCK
    /// critters/scenery/walls block their tile; MULTIHEX objects also block their
    /// six neighbors. Computed once per elevation (static scene — only the dude moves).
    /// IMPORTANT: scans FLAT objects too. OBJECT_FLAT (0x08) is a RENDER flag (the sprite
    /// draws flat on the ground), NOT a collision flag — _obj_blocking_at never tests it.
    /// Many walls/scenery are flat, INCLUDING FO2's invisible collision markers ("Secret
    /// Blocking Hex" / "Block Hex Auto Inviso", flags 0xA0000008, non-NO_BLOCK) that seal
    /// hut/building interiors. Iterating only _solidObjects dropped them, so the dude could
    /// path straight through walls into sealed interiors.
    /// </summary>
    private void RebuildBlockedTiles(MapObject? exclude)
    {
        const int objectNoBlock = 0x10;
        const int objectHidden = 0x01;
        const int objectMultiHex = 0x800;

        _blockedTiles = [];
        foreach (MapObject obj in _solidObjects[_elevation].Concat(_flatObjects[_elevation]))
        {
            // Skip HIDDEN as well as NO_BLOCK — _obj_blocking_at (object.cc:2401) ignores both. Maps
            // scatter HIDDEN flat scenery (spawn/event markers) in the open terrain; once we started
            // scanning flat objects, NOT skipping HIDDEN boxed the dude in (couldn't reach open ground).
            // The interior-sealing markers (0xA0000008) are NOT hidden, so they still block.
            if (obj == exclude || (obj.Flags & (objectNoBlock | objectHidden)) != 0)
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

    /// <summary>P54-M2: the elevation list (0..2) that holds <paramref name="obj"/>, for the
    /// elevation(obj) external. The dude/party live outside the map lists → fall back to the current
    /// elevation (which is theirs). Linear scan; called rarely (a script query).</summary>
    private int ElevationOfObject(MapObject obj)
    {
        if (_map is not null)
            for (int e = 0; e < MapFile.ElevationCount; e++)
                if (_map.Elevations[e]?.Objects.Contains(obj) == true)
                    return e;
        return _elevation;
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
            MergeStackInto(existing, item);
        else
            _dudeInventory.Add(item);
    }

    /// <summary>Fold an incoming item into an existing same-pid stack. AMMO boxes CONSOLIDATE their
    /// rounds (P75-M2; itemAdd item.cc:371) so two partial boxes don't read as a full + a partial;
    /// everything else just bumps the box count.</summary>
    private void MergeStackInto(MapObject existing, MapObject item)
    {
        if (SafeProto(item.Pid)?.Ammo is { Quantity: > 0 } ammo)
            (existing.StackCount, existing.AmmoQuantity) = Formats.Map.AmmoStack.Merge(
                existing.StackCount, existing.AmmoQuantity, Math.Max(item.StackCount, 1), item.AmmoQuantity, ammo.Quantity);
        else
            existing.StackCount += Math.Max(item.StackCount, 1);
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
        // P78: stealing — each lift is a Steal check; a caught lift takes nothing, closes the panel,
        // and turns the mark hostile (ResolveSteal handles all that and returns false).
        if (_stealTarget is { } mark && ReferenceEquals(mark, _lootContainer) && !ResolveSteal(mark, item))
            return;
        _lootContainer.Inventory.RemoveAt(index);
        AddToDudeInventory(item);
        Log($"You take: {ObjectName(item)}{(item.StackCount > 1 ? $" x{item.StackCount}" : "")}.");
    }

    /// <summary>Open the Steal screen on a live critter (P78): reuse the loot panel, reset the per-session
    /// counter, and arm steal mode so each lift runs the check.</summary>
    private void OpenSteal(MapObject mark)
    {
        if (mark.Inventory.Count == 0)
        {
            Log($"The {ObjectName(mark)} has nothing to steal.");
            return;
        }
        _stealRng ??= new Formats.Combat.SystemCombatRng(RngSeed ?? Environment.TickCount);
        _stealTarget = mark;
        _stealCount = 0;
        _stealSessionXp = 0;
        _stealXpBonus = 10;
        _lootContainer = mark;
        _inventoryOpen = true;
        _panelPage = 0;
        Log($"You try to steal from the {ObjectName(mark)}.");
    }

    /// <summary>One Steal attempt on <paramref name="item"/> (skill.cc skillsPerformStealing). Returns true
    /// if the lift succeeds (the caller transfers it); false if caught — the mark turns hostile and the
    /// panel closes. Uses the isolated <see cref="_stealRng"/>.</summary>
    private bool ResolveSteal(MapObject mark, MapObject item)
    {
        if (_dude is null || GetCritterState(_dude.Dude) is not { } thief)
            return false;
        int thiefSteal = thief.SkillValue(10);                 // SKILL_STEAL
        int? targetSteal = GetCritterState(mark)?.SkillValue(10);
        int size = SafeProto(item.Pid)?.Size ?? 0;
        bool pickpocket = DudePerkRank(Formats.Perks.PerkId.Pickpocket) > 0;
        bool front = Formats.Combat.SneakAttack.IsHitFromFront(_dude.Dude.Rotation, mark.Rotation);
        bool incap = (mark.CombatResults
            & (Formats.Combat.CriticalTables.DamKnockedOut | Formats.Combat.CriticalTables.DamKnockedDown)) != 0;
        Formats.Combat.StealResult r = Formats.Combat.StealCheck.Resolve(thiefSteal, targetSteal, size,
            pickpocket, front, incap, thief.Stat(Formats.Combat.CritterStat.CriticalChance),
            _stealCount, CriticalsEnabled, _stealRng!);
        _stealCount++;
        if (r.Caught)
        {
            Log($"You're caught stealing the {ObjectName(item)}!");
            Transcript($"steal: caught item={item.Pid:X} from {mark.HexTile}");
            _stealTarget = null;
            _lootContainer = null;
            _inventoryOpen = false;
            _combat.BeginScriptAggro(mark, _dude.Dude); // the mark reacts (combat.cc steal_p_proc → aggro)
            return false;
        }
        // Steal XP accrues 10/20/30… per item, the session total capped at 300 − Steal skill
        // (inventory.cc:4368/4471); party members give none — moot, the dude can't steal from allies here.
        int grant = Math.Min(_stealXpBonus, Math.Max(0, 300 - thiefSteal - _stealSessionXp));
        if (grant > 0)
        {
            _stealSessionXp += grant;
            AwardXp(grant);
        }
        _stealXpBonus += 10;
        Transcript($"steal: stole item={item.Pid:X} from {mark.HexTile}");
        return true;
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
        // P78: in steal mode a caught lift nulls _lootContainer mid-loop, so guard on it.
        while (_lootContainer is { } lc && lc.Inventory.Count > 0)
            TakeFromContainer(0);
        _lootContainer = null;
        _stealTarget = null;
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
            // P81: toggle the weapon in the ACTIVE hand. Clear only the active-hand bit on the bag (vacate
            // that hand) + both bits on this item (it leaves any hand), then set the active hand if equipping.
            // With the default right hand + no left-hand weapon ever set, this reduces to the old clear-both/
            // set-right exactly → byte-identical.
            bool equip = (item.Flags & _activeHand) == 0;
            foreach (MapObject other in _dudeInventory)
                other.Flags &= ~_activeHand;
            item.Flags &= ~(MapObject.FlagInLeftHand | MapObject.FlagInRightHand);
            if (equip)
                item.Flags |= _activeHand;
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
        if (_dialog.HeadId >= 0) // P87: report the talking-head art id (heads.lst index) when present
            Console.WriteLine($"HEAD: {_dialog.HeadId}");
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
        // P76-M2: ItemCost.For (item.cc itemGetCost) prices a loaded weapon's rounds + a partial ammo
        // box's fill + container contents, not just the raw proto cost. SafeProto → 0 on a missing proto.
        int cost = Formats.Map.ItemCost.For(item, SafeProto);
        return Formats.Combat.BarterMath.BuyPrice(cost, _barterModifier,
            _barterNpc is { } npc ? NpcBarterSkill(npc) : 0, DudeBarterSkill());
    }

    private int BarterSellPrice(MapObject item)
    {
        try
        {
            return Formats.Combat.BarterMath.SellPrice(Formats.Map.ItemCost.For(item, SafeProto));
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
                // P75-M3: Lifegiver adds +4 max HP per rank at each level-up (stat.cc:771). Inert at rank 0.
                int gain = Formats.Combat.Progression.HpPerLevel(endurance, DudePerkRank(Formats.Perks.PerkId.Lifegiver));
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
            // P72-M1: a white "Level Up" float over the dude (party_member.cc:1554 textObjectAdd font 101,
            // _colorTable[0x7FFF]). Draw-only — mutates the float list, never the console → goldens unchanged.
            if (_dude is not null)
                _floatText.Add(_dude.Dude.HexTile, _elevation, "Level Up", CombatFloatColors.LevelUp);

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


    private void InteractWith(MapObject obj)
    {
        // P0: dialogue_system_enter (0x80F9) — a use_p_proc below may request a dialog with its own object
        // (the terminal/well/computer pattern). Reset the request per interaction; consumed after the proc.
        if (_scriptHost is not null)
            _scriptHost.PendingDialogSpeaker = null;

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

        // P55-M2: a scripted scenery object with a use_p_proc but NO exit-grid Destination — run its
        // script (the Gecko reactor terminal/reactor/valve are scenery). The engine's _obj_use dispatches
        // use_p_proc for ANY usable object, scenery included (scripts.cc SCRIPT_PROC_USE); doors and
        // containers already fire it above. RunObjectProc returns null when the script lacks use_p_proc,
        // so unscripted/look-only scenery falls through to the examine line unchanged.
        if (obj.Sid != -1 && Fid.Type(obj.Fid) is ObjectType.Scenery && IsAdjacentToDude(obj)
            && _scriptHost?.RunObjectProc(obj, _map, _dude?.Dude, "use_p_proc") is { } useResult)
        {
            foreach (string line in useResult.Messages)
                Log(line);
            // State-only info line (like the "picked:" examine line below), so a harness can confirm the
            // scenery's use_p_proc ran instead of falling through to the no-op. Never the dialogue text.
            Console.WriteLine($"scenery-use@{obj.HexTile}: handled overridden={(useResult.Overridden ? 1 : 0)}");
            // P0: dialogue_system_enter — the terminal/well/computer's use_p_proc asked to talk to itself.
            if (_scriptHost?.PendingDialogSpeaker is { } speaker)
            {
                _scriptHost.PendingDialogSpeaker = null;
                Console.WriteLine($"dialogue-system-enter@{obj.HexTile}: opening dialog");
                OpenScriptedDialog(speaker);
            }
            return; // the scenery's use_p_proc ran — it handled the interaction
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
            IsBlocked, Reachable, getGlobal, _dudeLevel, _clock.Hour, _clock.Day, Difficulty,
            fortuneFinder: DudePerkRank(Formats.Perks.PerkId.FortuneFinder) > 0,   // P79: 2× caps
            cautiousNature: DudePerkRank(Formats.Perks.PerkId.CautiousNature) > 0); // P79: +3 spawn distance

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
        // P73: --brawl-watch sets _pendingBrawlSpectator so the dude stays a non-combatant and the
        // factions fight it out on their own (the dude-absent NPC-vs-NPC loop).
        if (encounter.Entry.Situation == "FIGHTING"
            && spawnedCritters.Select(c => c.Team).Distinct().Count() >= 2)
            _combat.StartBrawl(spawnedCritters, dudeSpectator: _pendingBrawlSpectator);
        _pendingBrawlSpectator = false;
    }

    /// <summary>P73: the next FIGHTING encounter starts a dude-ABSENT brawl (--brawl-watch).</summary>
    private bool _pendingBrawlSpectator;

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
        _skilldexOpen = false;
        // P82-M6: if the action menu's "Use Skill" opened the Skilldex on a target, apply the picked
        // skill to it directly instead of arm-and-next-click.
        if (_actionSkillTarget is { } target)
        {
            _actionSkillTarget = null;
            TryUseSkillOn(skill, target);
            return;
        }
        _pendingUseSkill = skill;
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
                if (skill == 10 && Fid.Type(target.Fid) is ObjectType.Critter && !target.IsDead)
                {
                    OpenSteal(target); // P78: the Steal screen — each lift runs the skill check
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
                // P70: Healer — First Aid/Doctor heal +4*rank min / +10*rank max (skill.cc:561; the base is
                // 1-5, so rank 0 -> Next(1,6) unchanged -> byte-identical). The dude is always the healer.
                int healerRank = DudePerkRank(Formats.Perks.PerkId.Healer);
                int heal = Math.Min(_skillRng.Next(1 + 4 * healerRank, 6 + 10 * healerRank), cs.MaxHp - target.CurrentHp);
                target.CurrentHp += heal;
                Log(self ? $"You heal {heal} hit points." : $"You heal the {ObjectName(target)} for {heal} hit points.");
                // P72-M2: a yellow skill-response float over the target (actions.cc:1461 textObjectAdd font
                // 101, _colorTable[32747]). Draw-only — never the console → goldens unchanged.
                _floatText.Add(target.HexTile, _elevation, $"+{heal}", CombatFloatColors.SkillResponse);
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
        // P80: a slot save deferred its thumbnail to here (the render thread); capture it world-only before
        // the normal frame draws, so the picker (still up) isn't in the shot. Restores the backbuffer target.
        if (_pendingThumbnailPath is { } thumbPath)
        {
            _pendingThumbnailPath = null;
            CaptureThumbnail(thumbPath);
        }

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

        // P85: the WORLD layer renders in its own batch under the zoom transform (identity at 1×); the
        // HUD/UI + worldmap follow in a second, native batch so the chrome never scales with zoom.
        if (!_worldmapOpen && _map is not null)
        {
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: WorldZoomMatrix());
            DrawObjects(_flatObjects[_elevation]);
            DrawObjects(_solidObjects[_elevation]);
            DrawProjectiles();
            if (_roofsVisible)
                DrawRoofs();
            DrawCombatText(); // P45: over the world, under the HUD bar
            _spriteBatch.End();
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
                DrawInterfaceBar(); // the world sprites already drew in the zoomed batch above
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
        DrawActionMenu();      // P82-M6: the right-click action menu, above the world/HUD
        DrawMouseCursor();     // P82-M5: the FO2 hex-ring / arrow cursor, above everything
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

        // PERK_AWARENESS (proto_instance.cc:294): examining a LIVE critter reveals its HP/condition + the
        // weapon it wields — but ONLY with the perk. (Hexwaste previously showed HP unconditionally, an over-
        // generous divergence; gating it makes the perk a real choice + matches the engine. No golden
        // examines a critter, so this is byte-identical.) Inert at rank 0 -> the default dude sees just the
        // name + description, like the engine.
        if (obj != _dude?.Dude && !obj.IsDead && DudePerkRank(Formats.Perks.PerkId.Awareness) > 0
            && GetCritterState(obj) is { } state)
        {
            Log($"HP: {state.CurrentHp}/{state.MaxHp}, AC: {state.ArmorClass}");
            if (EquippedWeapon(obj) is { Proto: { } wproto, Item: { } witem })
            {
                string shots = wproto.Weapon is { } w && w.IsGun(wproto.ExtendedFlags)
                    ? $" ({WeaponAmmo(wproto, witem)}/{w.AmmoCapacity} shots)" : "";
                Log($"Wielding the {ObjectName(witem)}{shots}.");
            }
        }
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
        Formats.Proto.CritterProtoStats? src;
        if (_companionStatOverride.TryGetValue(obj, out Formats.Proto.CritterProtoStats? overrideStats))
            src = overrideStats;
        else
        {
            try { src = _protos.Get(obj.Pid).Critter; }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException) { return null; }
        }
        if (src is null)
            return null;
        // P78-M2: an NPC that chemmed up carries a per-instance drug bonus folded into BonusStats (a copy,
        // so the shared proto cache stays pristine — the companion-override anti-aliasing rule).
        if (_npcDrugBonus.TryGetValue(obj, out int[]? db))
            src = src with { BonusStats = src.BonusStats.Select((v, i) => v + (i < db.Length ? db[i] : 0)).ToArray() };
        return new Formats.Combat.CritterState(obj, src, perkRanks: companionPerks);
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
            DrawConversationPanel(_dialog.NpcName, _dialog.Reply, _dialog.Options, _dialog.OptionReactions, EffectiveHeadId());
    }

    /// <summary>The shared conversation panel — reply text + numbered options at the
    /// bottom of the screen. Drives both scripted dialog and the companion-control hub
    /// (phase-10 M4); <see cref="_dialogOptionRects"/> feeds mouse hit-testing.</summary>
    // P87: talking-head animation — the fidget cycles on a wall-time tick (game_dialog.cc head-fidget),
    // a documented presentation choice (no .lip phoneme lip-sync; the .lip timing files aren't shipped).
    private int _headFrame;
    private double _headFrameTimerMs;
    private const double HeadFrameMs = 1000.0 / 8; // heads.lst fps ~8

    /// <summary>P87/P89: render the dialogue talking head in the upper area of the centred 640x480 dialog
    /// frame, at the engine's display anchor. The head FRM is the neutral fidget pose (art.cc anim 4 =
    /// _head1 'n' + _head2 'f', fidget #1, e.g. ELDERNF1); its frames cycle for an idle "living" head.
    /// Falls back silently to a text-only dialog if the head art is absent. ported from fallout2-ce
    /// src/game_dialog.cc gdialogInitFromScript()/_gdSetupFidget() (the head display buffer at window-
    /// local (126,14), gameDialogRenderTalkingHead).</summary>
    private void DrawTalkingHead(int headId, int frameX, int frameY)
    {
        int fid = Formats.Fid.Build(Formats.ObjectType.Head, headId, animType: 4, weaponCode: 1);
        Texture2D head;
        try
        {
            int frames = _frmCache.FrameCount(fid);
            head = _frmCache.GetTexture(fid, frames > 0 ? _headFrame % frames : 0);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or NotSupportedException)
        {
            return; // head art missing -> graceful text-only dialog
        }

        // The engine's head display area is window-local (126,14), ~388px wide; the heads sit centred in
        // the 640 frame. Centre this head's own width within that area and draw it at natural size.
        int x = frameX + 126 + (388 - head.Width) / 2;
        int y = frameY + 14;
        _spriteBatch.Draw(head, new Vector2(x, y), Color.White);
    }

    private void DrawConversationPanel(string name, string reply, IReadOnlyList<string> options,
        IReadOnlyList<int>? reactions = null, int headId = -1)
    {
        if (_fontRenderer is null)
            return;

        // P52-M1: with the Empathy perk the engine tints each dialogue option by the NPC's
        // reaction to it (game_dialog.cc gameDialogOptionOnMouseEnter:2118 / onMouseExit:2162).
        bool empathy = reactions is not null && DudePerkRank(Formats.Perks.PerkId.Empathy) > 0;

        _panelPixel ??= CreatePixel();

        Rectangle viewport = GraphicsDevice.Viewport.Bounds;

        // P89: the dialogue is a screen takeover — dim the world so the head + text read as the FO2 dialog
        // screen (game_dialog.cc darkens the captured scene), instead of floating over live, lit play.
        _spriteBatch.Draw(_panelPixel, viewport, new Color(0, 0, 0, 175));

        // FO2 lays the dialog in a 640x480 frame centred on screen: the head display at window-local
        // (126,14), the reply window at (135,225). With a head we honour that frame; a head-less dialog
        // keeps the simple bottom panel over the dimmed scene.
        int frameX = Math.Max(0, (viewport.Width - 640) / 2);
        int frameY = Math.Max(0, (viewport.Height - 480) / 2);

        int panelWidth = headId >= 0 ? 397 : Math.Min(720, viewport.Width - 40);
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
        // With a head, anchor the reply/options panel in the FO2 lower-frame region (the ~225 reply window)
        // so head + panel form the authentic dialog screen; otherwise pin it to the bottom of the screen.
        int panelX = headId >= 0 ? frameX + 122 : (viewport.Width - panelWidth) / 2;
        int panelY = headId >= 0 ? frameY + 219 : viewport.Height - panelHeight - 16;

        if (headId >= 0) // P89: the talking head sits in the upper frame, over the dimmed scene
            DrawTalkingHead(headId, frameX, frameY);

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

    private Texture2D CreatePixel()
    {
        var pixel = new Texture2D(GraphicsDevice, 1, 1);
        pixel.SetData(new[] { Color.White });
        return pixel;
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


    /// <summary>The front-door state machine: Title → character pick (Create
    /// or a premade) → optional SPECIAL/tags creation → play.</summary>
    private void HandleMenuInput(KeyboardState k)
    {
        switch (_menu)
        {
            case MenuState.Title:
                MoveMenu(k, MainMenuButtons.Length);
                if (MenuActivated(k)) ActivateMainMenuButton(_menuIndex);
                // The engine's letter hotkeys (mainmenu.cc:55-62): i/n/l/o/c/e.
                if (IsKeyPressed(k, Keys.I)) ActivateMainMenuButton(0);
                if (IsKeyPressed(k, Keys.N)) ActivateMainMenuButton(1);
                if (IsKeyPressed(k, Keys.L)) ActivateMainMenuButton(2);
                if (IsKeyPressed(k, Keys.O)) ActivateMainMenuButton(3);
                if (IsKeyPressed(k, Keys.C)) ActivateMainMenuButton(4);
                if (IsKeyPressed(k, Keys.Escape) || IsKeyPressed(k, Keys.E)) ActivateMainMenuButton(5);
                break;

            case MenuState.CharacterPick:
            {
                // Art path (pickchar.frm): ◄─► cycle the premade, Enter = Take, M = Modify, C = Create.
                if (_pickCharBg is not null && _premadeGcds.Count > 0)
                {
                    if (IsKeyPressed(k, Keys.Left)) ActivateSelectorButton("prev");
                    if (IsKeyPressed(k, Keys.Right)) ActivateSelectorButton("next");
                    if (IsKeyPressed(k, Keys.Enter)) ActivateSelectorButton("take");
                    if (IsKeyPressed(k, Keys.M)) ActivateSelectorButton("modify");
                    if (IsKeyPressed(k, Keys.C)) ActivateSelectorButton("create");
                    if (IsKeyPressed(k, Keys.Escape)) ActivateSelectorButton("back");
                    break;
                }
                // Text fallback (no art): the old "Create your own" + premade list.
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
        // PremadeBase, not Path.GetFileNameWithoutExtension: the DAT vpath "premade\combat.gcd" uses '\',
        // which Path won't split on Linux (the P83 trap) — it'd persist "premade\combat" into the save name.
        _activeCharacter = PremadeBase(path);
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
        _activeHand = MapObject.FlagInRightHand; // P81: back to the right hand on a new game
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


    // P80: a small save-slot thumbnail. The capture is deferred to Draw (set by a slot save) so it runs in
    // the render thread like the screenshot path; it renders the WORLD ONLY (no menu panels) to the offscreen
    // target, then downscales into the thumbnail target and writes a sidecar PNG — race-free GetData.
    private RenderTarget2D? _thumbnailTarget;
    private string? _pendingThumbnailPath;
    private const int ThumbW = 224, ThumbH = 133; // the LSGAME preview slot (loadsave.cc)

    private void CaptureThumbnail(string path)
    {
        int bw = GraphicsDevice.PresentationParameters.BackBufferWidth;
        int bh = GraphicsDevice.PresentationParameters.BackBufferHeight;
        _screenshotTarget ??= new RenderTarget2D(GraphicsDevice, bw, bh);
        _thumbnailTarget ??= new RenderTarget2D(GraphicsDevice, ThumbW, ThumbH);

        // 1) the world only (floors → objects → roofs → HUD bar), no menu panels, into the full-size target.
        // P85: thumbnails are always the canonical 1× world view (the object batch below is un-zoomed),
        // so force identity for the capture regardless of the live zoom, then restore it.
        int savedZoom = _zoom;
        _zoom = 1;
        GraphicsDevice.SetRenderTarget(_screenshotTarget);
        GraphicsDevice.Clear(Color.Black);
        if (!_worldmapOpen && _map is not null)
        {
            _dudeUnderRoof = DudeIsUnderRoof();
            DrawFloors();
        }
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        if (_worldmapOpen)
            _worldmapScreen?.Draw(_spriteBatch, GraphicsDevice.Viewport.Bounds, _hoveredArea, WorldFog);
        else if (_map is not null)
        {
            DrawObjects(_flatObjects[_elevation]);
            DrawObjects(_solidObjects[_elevation]);
            DrawProjectiles();
            if (_roofsVisible)
                DrawRoofs();
            DrawInterfaceBar();
        }
        _spriteBatch.End();
        _zoom = savedZoom; // P85: restore the live zoom after the 1× thumbnail capture

        // 2) downscale into the thumbnail target.
        GraphicsDevice.SetRenderTarget(_thumbnailTarget);
        GraphicsDevice.Clear(Color.Black);
        _spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
        _spriteBatch.Draw(_screenshotTarget, new Rectangle(0, 0, ThumbW, ThumbH), Color.White);
        _spriteBatch.End();
        GraphicsDevice.SetRenderTarget(null);

        // 3) readback + write the sidecar PNG.
        var pixels = new Color[ThumbW * ThumbH];
        _thumbnailTarget.GetData(pixels);
        for (int i = 0; i < pixels.Length; i++)
            pixels[i].A = 255;
        using var tex = new Texture2D(GraphicsDevice, ThumbW, ThumbH);
        tex.SetData(pixels);
        try
        {
            using FileStream fs = File.Create(path);
            tex.SaveAsPng(fs, ThumbW, ThumbH);
        }
        catch (IOException) { /* a thumbnail is cosmetic — never fail a save over it */ }
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
