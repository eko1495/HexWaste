using System.Text.RegularExpressions;

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

    private WorldmapFile(Dictionary<string, int> freq, List<WorldTile> tiles,
        Dictionary<string, EncounterTable> tables, Dictionary<string, EncounterGroup> groups,
        Dictionary<string, IReadOnlyList<string>> randomMaps)
    {
        Frequencies = freq;
        Tiles = tiles;
        Tables = tables;
        Groups = groups;
        RandomMaps = randomMaps;
    }

    /// <summary>The encounter % a frequency name resolves to (None/unknown → 0).</summary>
    public int FrequencyPercent(string name) => Frequencies.GetValueOrDefault(name, 0);

    public EncounterTable? Table(string lookupName) => Tables.GetValueOrDefault(lookupName);
    public EncounterGroup? Group(string name) => Groups.GetValueOrDefault(name);

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

        return new EncounterEntry(chance, counter, map, spawns, situation, conditions);
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
            members.Add(new GroupMember(ratio, single, dead, pid, scriptIndex, items));
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

/// <summary>A weighted candidate: Chance weight, one-shot Counter (-1 = unlimited),
/// an optional special Map override, the spawn groups, the situation, and the If
/// conditions that must all pass.</summary>
public sealed record EncounterEntry(int Chance, int Counter, string? Map,
    IReadOnlyList<EncounterSpawn> Spawns, string Situation, IReadOnlyList<EncCondition> Conditions);

public sealed record EncounterSpawn(int Min, int Max, string Group);

/// <summary>An If(Type(Param) Op Value) condition; Rand(N%) carries N in Param with
/// an empty Op.</summary>
public sealed record EncCondition(string Type, int Param, string Op, int Value);

public sealed record EncounterGroup(string Name, IReadOnlyList<GroupMember> Members,
    string Formation, int Spacing, int Distance);

public sealed record GroupMember(int Ratio, bool Single, bool Dead, int Pid, int ScriptIndex,
    IReadOnlyList<EncItem> Items);

public sealed record EncItem(int Min, int Max, int Pid, bool Wielded, bool Worn);
