using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hexwaste.Formats;

/// <summary>
/// The PoC's JSON save snapshot (the phase-4/5 reports' honest alternative to
/// the original 27-handler binary format): current map + dude position,
/// clock, session GVARs, the dude's inventory, per-script LVAR slices, and
/// per-visited-map deltas keyed by load-order ordinals (MAP object Ids are
/// NOT unique — phase-5 probes found hundreds of duplicate-Id groups per
/// map). On load/revisit: pristine map → import LVARs → map_enter with
/// firstRun=0 → apply delta.
/// </summary>
public sealed class SaveState
{
    /// <summary>Bump on any shape change. Loads refuse mismatches (no silent
    /// misreads of ordinal-keyed deltas); pre-versioning saves deserialize as 0.
    /// V2: SavedItem ammo fields + MapDelta.MovedOrdinals (NPC positions).</summary>
    public const int CurrentVersion = 2;

    public int Version { get; set; }

    public string Map { get; set; } = "artemple.map";
    public int DudeTile { get; set; }
    public int DudeRotation { get; set; }
    public int DudeLevel { get; set; } = 1;
    public int DudeXp { get; set; }

    /// <summary>Banked, unspent skill points (P8-M1; additive — old saves = 0).</summary>
    public int UnspentSkillPoints { get; set; }

    /// <summary>The active premade name whose base sheet to restore ("player" =
    /// blank default); null on very old saves → player.</summary>
    public string? Character { get; set; }

    /// <summary>The dude's current base skill points (18; null = use the
    /// premade as-is). Captures level-up spends over the base sheet.</summary>
    public int[]? DudeSkills { get; set; }

    /// <summary>The dude's base stat block (35; null = reload the named
    /// premade). Self-contained so a created character round-trips without a
    /// .gcd file. Level-up HP lives in bonus stats and is replayed from level.</summary>
    public int[]? DudeBaseStats { get; set; }

    /// <summary>The dude's tagged skills (4; -1 padded). Pairs with DudeBaseStats.</summary>
    public int[]? DudeTaggedSkills { get; set; }

    /// <summary>Current HP; -1 = full (pre-progression saves).</summary>
    public int DudeHp { get; set; } = -1;
    public int Elevation { get; set; }
    public long ClockTicks { get; set; }
    public Dictionary<int, int> GlobalVars { get; set; } = [];
    public List<SavedItem> DudeInventory { get; set; } = [];

    /// <summary>Per-map world deltas, keyed by header map name (e.g. "DENBUS1.MAP").</summary>
    public Dictionary<string, MapDelta> VisitedMaps { get; set; } = [];

    /// <summary>Per-map LVAR slices: mapName → sid → values.</summary>
    public Dictionary<string, Dictionary<int, int[]>> LocalVars { get; set; } = [];

    /// <summary>The party roster (travels outside any map's delta).</summary>
    public List<PartyMemberState> Party { get; set; } = [];

    /// <summary>Flags carries the equip bits (in-hand 0x3000000, worn 0x4000000).
    /// Ammo sentinels: -1 = derive from the prototype on load (V2).</summary>
    public sealed record SavedItem(int Pid, int Count, int Flags = 0,
        int AmmoQuantity = -1, int AmmoTypePid = -1);

    public sealed record SavedDoor(int HexTile, int Pid, bool Open, bool Locked);

    /// <summary>An object a script or the player added to the world.</summary>
    public sealed record CreatedObject(int Pid, int Tile, int Elevation, int Count);

    /// <summary>A pristine object's new position (V2).</summary>
    public sealed record MovedObject(int Ordinal, int Tile, int Elevation, int Rotation);

    /// <summary>A recruited companion traveling with the dude (additive V2
    /// field; absent in older saves → empty roster).</summary>
    public sealed record PartyMemberState(int Pid, int ScriptListIndex, int Hp, int Team,
        int AiPacket, List<SavedItem> Inventory);

    public sealed class MapDelta
    {
        public List<SavedDoor> Doors { get; set; } = [];

        /// <summary>Pristine objects removed from the world (picked up / destroyed), by load-order ordinal.</summary>
        public List<int> TakenOrdinals { get; set; } = [];

        /// <summary>Killed critters by ordinal: replayed as sid=-1 + DAM_DEAD
        /// before map_enter (dead scripts never run — combat.cc:4876) and a
        /// corpse conversion after.</summary>
        public List<int> DeadOrdinals { get; set; } = [];

        /// <summary>Objects that drifted from their pristine spot (wandering
        /// NPCs, script moves): replayed BEFORE map_enter like a .SAV reload.</summary>
        public List<MovedObject> MovedOrdinals { get; set; } = [];

        /// <summary>Player-dropped or script-created objects still on the map.</summary>
        public List<CreatedObject> Created { get; set; } = [];

        /// <summary>Full container inventory snapshots (ordinal → items) — overwrites
        /// whatever map_enter restocked, killing the restock quirk.</summary>
        public Dictionary<int, List<SavedItem>> ContainerInventories { get; set; } = [];

        public int[] MapVars { get; set; } = [];

        /// <summary>Game-day this delta was captured (P8-M5). Script-stocked
        /// merchant containers restock from pristine map data once this is
        /// older than the restock window; 0 = pre-M5 (treated as "today").</summary>
        public int SnapshotDay { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));

    public static SaveState? Load(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<SaveState>(File.ReadAllText(path), Options) : null;

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static SaveState? FromJson(string json) =>
        JsonSerializer.Deserialize<SaveState>(json, Options);
}
