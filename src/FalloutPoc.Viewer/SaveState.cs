using System.Text.Json;
using System.Text.Json.Serialization;

namespace FalloutPoc.Viewer;

/// <summary>
/// The PoC's JSON save snapshot (the phase-4 report's honest alternative to
/// the original 27-handler binary format): current map + dude position,
/// clock, session GVARs, the dude's inventory (pid + count), and per-door
/// open/lock deltas keyed by hex tile. Maps reload pristine and re-run their
/// entry scripts on load; containers therefore restock — a documented cut.
/// </summary>
public sealed class SaveState
{
    public string Map { get; set; } = "artemple.map";
    public int DudeTile { get; set; }
    public int DudeRotation { get; set; }
    public int Elevation { get; set; }
    public long ClockTicks { get; set; }
    public Dictionary<int, int> GlobalVars { get; set; } = [];
    public List<SavedItem> DudeInventory { get; set; } = [];
    public List<SavedDoor> Doors { get; set; } = [];

    public sealed record SavedItem(int Pid, int Count);

    public sealed record SavedDoor(int HexTile, int Pid, bool Open, bool Locked);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));

    public static SaveState? Load(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<SaveState>(File.ReadAllText(path), Options) : null;
}
