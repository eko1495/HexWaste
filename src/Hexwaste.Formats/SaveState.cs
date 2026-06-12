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
    /// misreads of ordinal-keyed deltas); pre-versioning saves deserialize as 0.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; }

    public string Map { get; set; } = "artemple.map";
    public int DudeTile { get; set; }
    public int DudeRotation { get; set; }
    public int DudeLevel { get; set; } = 1;
    public int DudeXp { get; set; }

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

    public sealed record SavedItem(int Pid, int Count);

    public sealed record SavedDoor(int HexTile, int Pid, bool Open, bool Locked);

    /// <summary>An object a script or the player added to the world.</summary>
    public sealed record CreatedObject(int Pid, int Tile, int Elevation, int Count);

    public sealed class MapDelta
    {
        public List<SavedDoor> Doors { get; set; } = [];

        /// <summary>Pristine objects removed from the world (picked up / destroyed), by load-order ordinal.</summary>
        public List<int> TakenOrdinals { get; set; } = [];

        /// <summary>Killed critters by ordinal: replayed as sid=-1 + DAM_DEAD
        /// before map_enter (dead scripts never run — combat.cc:4876) and a
        /// corpse conversion after.</summary>
        public List<int> DeadOrdinals { get; set; } = [];

        /// <summary>Player-dropped or script-created objects still on the map.</summary>
        public List<CreatedObject> Created { get; set; } = [];

        /// <summary>Full container inventory snapshots (ordinal → items) — overwrites
        /// whatever map_enter restocked, killing the restock quirk.</summary>
        public Dictionary<int, List<SavedItem>> ContainerInventories { get; set; } = [];

        public int[] MapVars { get; set; } = [];
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
