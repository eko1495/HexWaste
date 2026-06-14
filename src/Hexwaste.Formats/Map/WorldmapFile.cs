using System.Text.RegularExpressions;
using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Map;

/// <summary>
/// The parsed <c>worldmap.txt</c> — random-encounter tables, the per-tile subtile
/// grid, encounter groups, and the random-map pools. Ported from fallout2-ce
/// src/worldmap.cc (wmConfigInit / wmReadEncounterType / wmReadEncBaseType). The
/// roll/pick chain that consumes this lives in <see cref="WorldEncounters"/>
/// (phase-10 M1); this is the pure data layer. See docs/phase10-research-report.md.
/// </summary>
public sealed class WorldmapFile
{
    public const int SubtileGridWidth = 7;  // SUBTILE_GRID_WIDTH (worldmap.cc:64)
    public const int SubtileGridHeight = 6; // SUBTILE_GRID_HEIGHT (worldmap.cc:65)

    /// <summary>Frequency name → percentage (e.g. "Uncommon" → 12) from [Data].</summary>
    public IReadOnlyDictionary<string, int> Frequencies { get; }
    public IReadOnlyList<WorldTile> Tiles { get; }
    /// <summary>Encounter tables keyed by lookup_name (the subtile pointer).</summary>
    public IReadOnlyDictionary<string, EncounterTable> Tables { get; }
    /// <summary>Encounter groups keyed by name (the Enc: pointer).</summary>
    public IReadOnlyDictionary<string, EncounterGroup> Groups { get; }
    /// <summary>Terrain → ordered random-map lookup-names ([Random Maps: TERRAIN]).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> RandomMaps { get; }

    // The pristine per-entry counters captured at parse time (before any pick
    // decrements them) — ExportCounters diffs against this so only the handful of
    // actually-consumed one-shots reach the save, not all 60+ Counter:1 tables.
    private readonly Dictionary<string, int[]> _pristineCounters;

    private WorldmapFile(Dictionary<string, int> freq, List<WorldTile> tiles,
        Dictionary<string, EncounterTable> tables, Dictionary<string, EncounterGroup> groups,
        Dictionary<string, IReadOnlyList<string>> randomMaps)
    {
        Frequencies = freq;
        Tiles = tiles;
        Tables = tables;
        Groups = groups;
        RandomMaps = randomMaps;
        _pristineCounters = tables.ToDictionary(kv => kv.Key,
            kv => kv.Value.Entries.Select(e => e.Counter).ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The encounter % a frequency name resolves to (None/unknown → 0).</summary>
    public int FrequencyPercent(string name) => Frequencies.GetValueOrDefault(name, 0);

    public EncounterTable? Table(string lookupName) => Tables.GetValueOrDefault(lookupName);
    public EncounterGroup? Group(string name) => Groups.GetValueOrDefault(name);

    /// <summary>Snapshot the one-shot encounter counters consumed this session, as
    /// table lookup_name → per-entry counter array (phase-10 M2). Only tables whose
    /// counters have actually changed from their parsed-pristine values are emitted
    /// — worldmap.txt carries 375 <c>Counter:1</c> entries across ~60 tables, so an
    /// unconsumed game saves an empty dict, not 60 redundant arrays. The emitted
    /// arrays are dense and index-aligned with <see cref="EncounterTable.Entries"/>;
    /// on a re-parse the entry order is stable (enc_NN, ordered), so import lines up.</summary>
    public Dictionary<string, int[]> ExportCounters()
    {
        var result = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, EncounterTable table) in Tables)
        {
            int[] pristine = _pristineCounters[name];
            bool changed = false;
            for (int i = 0; i < table.Entries.Count; i++)
                if (table.Entries[i].Counter != pristine[i]) { changed = true; break; }
            if (changed)
                result[name] = [.. table.Entries.Select(e => e.Counter)];
        }
        return result;
    }

    /// <summary>Restore consumed one-shot counters over the freshly parsed pristine
    /// tables (phase-10 M2, the load side of <see cref="ExportCounters"/>). Unknown
    /// table names, out-of-range indices, and a null array (hand-edited save) are
    /// ignored so a stale save degrades to pristine counters instead of throwing.</summary>
    public void ImportCounters(IReadOnlyDictionary<string, int[]> saved)
    {
        foreach ((string name, int[] counters) in saved)
            if (counters is not null && Tables.TryGetValue(name, out EncounterTable? table))
                for (int i = 0; i < counters.Length && i < table.Entries.Count; i++)
                    table.Entries[i].Counter = counters[i];
    }

    /// <summary>The subtile under a worldmap pixel position. The map is 4 tiles wide
    /// (×350) × 5 tall (×300); each tile is a 7×6 grid of 50px subtiles indexed
    /// [row=(x%350)/50][col=(y%300)/50] (worldmap.cc:3533-3543).</summary>
    public Subtile? SubtileAt(int worldX, int worldY)
    {
        int tileIndex = worldY / 300 * 4 + worldX / 350;
        WorldTile? tile = Tiles.FirstOrDefault(t => t.Index == tileIndex);
        if (tile is null)
            return null;
        int row = worldX % 350 / 50;
        int col = worldY % 300 / 50;
        return row < SubtileGridWidth && col < SubtileGridHeight ? tile.Subtiles[row, col] : null;
    }

    public static WorldmapFile Parse(string text)
    {
        var freq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tiles = new List<WorldTile>();
        var tables = new Dictionary<string, EncounterTable>(StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<string, EncounterGroup>(StringComparer.OrdinalIgnoreCase);
        var randomMaps = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        string section = "";
        string arg = "";                 // [Tile N] N, [Random Maps: TERRAIN] TERRAIN, [Encounter: NAME] NAME
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // ordered keys captured raw

        void Flush()
        {
            if (section == "tile" && int.TryParse(arg, out int idx))
                tiles.Add(BuildTile(idx, fields, freq));
            else if (section == "table")
                BuildTable(fields, tables);
            else if (section == "group")
                groups[arg] = BuildGroup(arg, fields);
            else if (section == "randommaps")
                randomMaps[arg] = fields.Where(kv => kv.Key.StartsWith("map_", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
            fields.Clear();
        }

        foreach (string raw in text.Split('\n'))
        {
            string line = StripComment(raw).Trim();
            if (line.Length == 0)
                continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                Flush();
                string head = line[1..^1].Trim();
                if (head.Equals("Data", StringComparison.OrdinalIgnoreCase)) { section = "data"; arg = ""; }
                else if (head.StartsWith("Tile ", StringComparison.OrdinalIgnoreCase)) { section = "tile"; arg = head[5..].Trim(); }
                else if (head.StartsWith("Encounter Table ", StringComparison.OrdinalIgnoreCase)) { section = "table"; arg = head[16..].Trim(); }
                else if (head.StartsWith("Encounter:", StringComparison.OrdinalIgnoreCase)) { section = "group"; arg = head["Encounter:".Length..].Trim(); }
                else if (head.StartsWith("Random Maps:", StringComparison.OrdinalIgnoreCase)) { section = "randommaps"; arg = head["Random Maps:".Length..].Trim(); }
                else { section = "other"; arg = ""; }
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();

            if (section == "data")
            {
                // "Uncommon=12%" — a frequency name. Skip terrain_* config lines.
                if (value.EndsWith('%') && int.TryParse(value[..^1], out int pct))
                    freq[key] = pct;
            }
            else
            {
                fields[key] = value; // captured raw; built at Flush
            }
        }
        Flush();

        return new WorldmapFile(freq, tiles, tables, groups, randomMaps);
    }

    private static string StripComment(string line)
    {
        int semi = line.IndexOf(';');
        return semi >= 0 ? line[..semi] : line;
    }

    // ---- section builders ------------------------------------------------

    private static WorldTile BuildTile(int index, Dictionary<string, string> fields, Dictionary<string, int> freq)
    {
        int difficulty = fields.TryGetValue("encounter_difficulty", out string? d) && int.TryParse(d, out int dv) ? dv : 0;
        var grid = new Subtile[SubtileGridWidth, SubtileGridHeight];
        foreach ((string k, string v) in fields)
        {
            // subtile keys are "R_C" (R 0..6, C 0..5): terrain,fill,morning,afternoon,night,encTable
            string[] rc = k.Split('_');
            if (rc.Length != 2 || !int.TryParse(rc[0], out int r) || !int.TryParse(rc[1], out int c)
                || r < 0 || r >= SubtileGridWidth || c < 0 || c >= SubtileGridHeight)
                continue;
            string[] f = v.Split(',');
            if (f.Length < 6)
                continue;
            int Pct(string name) => freq.GetValueOrDefault(name.Trim(), 0);
            grid[r, c] = new Subtile(f[0].Trim(), f[5].Trim(), Pct(f[2]), Pct(f[3]), Pct(f[4]));
        }
        return new WorldTile(index, difficulty, grid);
    }

    private static readonly Regex SpawnRx = new(@"\((\d+)-(\d+)\)\s*([A-Za-z0-9_]+)", RegexOptions.Compiled);
    private static readonly Regex CondRx = new(@"If\s*\(\s*(\w+)\(([^)]*)\)\s*(==|!=|<|>)?\s*(-?\d+)?", RegexOptions.Compiled);

    private static void BuildTable(Dictionary<string, string> fields, Dictionary<string, EncounterTable> tables)
    {
        if (!fields.TryGetValue("lookup_name", out string? lookup))
            return;
        var maps = fields.TryGetValue("maps", out string? m)
            ? m.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList()
            : [];
        var entries = new List<EncounterEntry>();
        foreach ((string k, string v) in fields.Where(kv => kv.Key.StartsWith("enc_", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(kv => kv.Key))
        {
            entries.Add(ParseEntry(v));
        }
        tables[lookup.Trim()] = new EncounterTable(lookup.Trim(), maps, entries);
    }

    private static EncounterEntry ParseEntry(string v)
    {
        int chance = ReadInt(v, @"Chance:\s*(\d+)%");
        int counter = ReadInt(v, @"Counter:\s*(\d+)", -1);
        string? map = Match(v, @"Map:\s*([^,]+)")?.Trim();

        // Enc:(min-max) GROUP [AND (min-max) GROUP2] SITUATION   OR   Enc:Special1
        var spawns = new List<EncounterSpawn>();
        string situation = "AMBUSH";
        string encPart = Match(v, @"Enc:\s*(.+?)(?:,\s*If|$)") ?? "";
        foreach (Match sm in SpawnRx.Matches(encPart))
            spawns.Add(new EncounterSpawn(int.Parse(sm.Groups[1].Value), int.Parse(sm.Groups[2].Value), sm.Groups[3].Value));
        foreach (string token in new[] { "AMBUSH", "FIGHTING", "OUTLINE", "FACING" })
            if (encPart.Contains(token, StringComparison.OrdinalIgnoreCase)) { situation = token; break; }

        var conditions = new List<EncCondition>();
        foreach (Match cm in CondRx.Matches(v))
            conditions.Add(ParseCondition(cm));

        return new EncounterEntry(chance, map, spawns, situation, conditions) { Counter = counter };
    }

    private static EncCondition ParseCondition(Match cm)
    {
        string type = cm.Groups[1].Value;                       // Global / Player / Rand / time_of_day ...
        string paramStr = cm.Groups[2].Value.Trim();            // 1 / Level / 5%
        string op = cm.Groups[3].Value;                         // == != < > or ""
        int value = cm.Groups[4].Success ? int.Parse(cm.Groups[4].Value) : 0;
        int param = int.TryParse(paramStr.TrimEnd('%'), out int p) ? p : 0; // Level → 0; Rand(5%) → 5
        return new EncCondition(type, param, op, value);
    }

    private static readonly Regex RatioRx = new(@"ratio:\s*(\d+)%", RegexOptions.Compiled);
    private static readonly Regex PidRx = new(@"pid:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex ScriptRx = new(@"Script:\s*(\d+)", RegexOptions.Compiled);
    private static readonly Regex ItemRx = new(@"Item:\s*(?:\((\d+)-(\d+)\))?(\d+)(\([^)]*\)|\{[^}]*\})?", RegexOptions.Compiled);

    private static EncounterGroup BuildGroup(string name, Dictionary<string, string> fields)
    {
        var members = new List<GroupMember>();
        foreach ((string _, string v) in fields.Where(kv => kv.Key.StartsWith("type_", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(kv => kv.Key))
        {
            Match rm = RatioRx.Match(v);
            int ratio = rm.Success ? int.Parse(rm.Groups[1].Value) : 0;
            bool single = !rm.Success;                          // omitted ratio = SINGLE (one leader)
            bool dead = Regex.IsMatch(v, @"\bDead\b", RegexOptions.IgnoreCase);
            Match pm = PidRx.Match(v);
            int pid = pm.Success ? int.Parse(pm.Groups[1].Value) : 0;
            Match scr = ScriptRx.Match(v);
            int scriptIndex = scr.Success ? int.Parse(scr.Groups[1].Value) - 1 : -1; // Script:N binds N-1
            var items = new List<EncItem>();
            foreach (Match im in ItemRx.Matches(v))
            {
                int min = im.Groups[1].Success ? int.Parse(im.Groups[1].Value) : 1;
                int max = im.Groups[2].Success ? int.Parse(im.Groups[2].Value) : min;
                string flag = im.Groups[4].Value;
                items.Add(new EncItem(min, max, int.Parse(im.Groups[3].Value),
                    flag.Contains("wielded", StringComparison.OrdinalIgnoreCase),
                    flag.Contains("worn", StringComparison.OrdinalIgnoreCase)));
            }
            // Per-member overrides (phase-10 #7): a surrounding distance, a tilenum
            // placement, and trailing If() conditions that gate the member's spawn.
            int memberDistance = ReadInt(v, @"[Dd]istance:\s*(\d+)", 0);
            int memberTile = ReadInt(v, @"[Tt]ilenum:\s*(\d+)", -1);
            var memberConds = new List<EncCondition>();
            foreach (Match cm in CondRx.Matches(v))
                memberConds.Add(ParseCondition(cm));
            members.Add(new GroupMember(ratio, single, dead, pid, scriptIndex, items,
                memberDistance, memberTile, memberConds));
        }

        string formation = "surrounding";
        int spacing = 1, distance = -1;
        if (fields.TryGetValue("position", out string? pos))
        {
            string[] parts = pos.Split(',');
            formation = parts[0].Trim().ToLowerInvariant();
            spacing = ReadInt(pos, @"[Ss]pacing:\s*(\d+)", 1);
            distance = ReadInt(pos, @"[Dd]istance:\s*(\d+)", -1);
        }
        return new EncounterGroup(name, members, formation, spacing, distance);
    }

    // ---- small helpers ---------------------------------------------------

    private static int ReadInt(string s, string pattern, int fallback = 0)
    {
        Match m = Regex.Match(s, pattern);
        return m.Success && int.TryParse(m.Groups[1].Value, out int v) ? v : fallback;
    }

    private static string? Match(string s, string pattern)
    {
        Match m = Regex.Match(s, pattern);
        return m.Success ? m.Groups[1].Value : null;
    }
}

public sealed record WorldTile(int Index, int Difficulty, Subtile[,] Subtiles);

/// <summary>A worldmap subtile: terrain, the encounter table it points at, and the
/// per-daypart encounter % (resolved from the frequency names).</summary>
public sealed record Subtile(string Terrain, string EncTable, int MorningChance, int AfternoonChance, int NightChance);

public sealed record EncounterTable(string LookupName, IReadOnlyList<string> Maps, IReadOnlyList<EncounterEntry> Entries);

/// <summary>A weighted candidate: Chance weight, an optional special Map override,
/// the spawn groups, the situation, and the If conditions that must all pass.</summary>
public sealed record EncounterEntry(int Chance, string? Map,
    IReadOnlyList<EncounterSpawn> Spawns, string Situation, IReadOnlyList<EncCondition> Conditions)
{
    /// <summary>One-shot budget (-1 = unlimited); decremented on selection and
    /// persisted in the save (phase-10 M2). Mutable runtime state.</summary>
    public int Counter { get; set; } = -1;
}

public sealed record EncounterSpawn(int Min, int Max, string Group);

/// <summary>An If(Type(Param) Op Value) condition; Rand(N%) carries N in Param with
/// an empty Op.</summary>
public sealed record EncCondition(string Type, int Param, string Op, int Value);

/// <summary>The If() condition evaluator shared by the encounter pick (entry
/// conditions) and the group spawn (per-member conditions), ported from
/// wmEvalConditional (worldmap.cc:4096-4169): AND-only across sub-conditions,
/// operators == != &lt; &gt; only, Rand(N%) draws the rng, enctr(num_critters)
/// compares the spawn's rolled count.</summary>
public static class EncounterConditions
{
    public static bool All(IReadOnlyList<EncCondition> conditions, ICombatRng rng,
        Func<int, int> getGlobal, int playerLevel, int hhmm, int daysPlayed, int critterCount)
    {
        foreach (EncCondition c in conditions)
            if (!Evaluate(c, rng, getGlobal, playerLevel, hhmm, daysPlayed, critterCount))
                return false;
        return true;
    }

    private static bool Evaluate(EncCondition c, ICombatRng rng, Func<int, int> getGlobal,
        int playerLevel, int hhmm, int daysPlayed, int critterCount)
    {
        // Rand(N%) is a probability gate with no operator.
        if (c.Type.Equals("Rand", StringComparison.OrdinalIgnoreCase))
            return rng.Next(1, 101) <= c.Param;

        int lhs = c.Type.ToLowerInvariant() switch
        {
            "global" => getGlobal(c.Param),
            "player" => playerLevel,         // Player(Level)
            "time_of_day" => hhmm,
            "days_played" => daysPlayed,
            "enctr" => critterCount,         // enctr(num_critters)
            _ => 0,
        };
        return c.Op switch
        {
            "==" => lhs == c.Value,
            "!=" => lhs != c.Value,
            "<" => lhs < c.Value,
            ">" => lhs > c.Value,
            _ => true,                       // unknown/empty operator → permissive
        };
    }
}

public sealed record EncounterGroup(string Name, IReadOnlyList<GroupMember> Members,
    string Formation, int Spacing, int Distance);

/// <summary>Distance (per-member surrounding-ring radius; 0 = use Perception±2),
/// Tile (per-member placement override; -1 = none), Conditions (the trailing If()
/// that gate whether this member spawns) — all phase-10 #7.</summary>
public sealed record GroupMember(int Ratio, bool Single, bool Dead, int Pid, int ScriptIndex,
    IReadOnlyList<EncItem> Items, int Distance = 0, int Tile = -1,
    IReadOnlyList<EncCondition>? Conditions = null);

public sealed record EncItem(int Min, int Max, int Pid, bool Wielded, bool Worn);
