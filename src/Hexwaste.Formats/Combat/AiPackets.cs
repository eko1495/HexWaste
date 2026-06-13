namespace Hexwaste.Formats.Combat;

/// <summary>
/// One AI behaviour packet from <c>data\ai.txt</c> (fallout2-ce src/combat_ai.cc
/// aiInit(), the <c>AiPacket</c> struct). A critter's instance/proto
/// <c>aiPacket</c> is the integer <see cref="PacketNum"/> key. Only the fields
/// phase-9 M1 consumes are typed strongly; the rest of the INI is ignored.
/// </summary>
public sealed record AiPacket(
    int PacketNum,
    string Name,
    /// <summary>Lowest acceptable to-hit %: the AI won't fire below it, closing the
    /// gap or fleeing instead (combat_ai.cc:2705/2845).</summary>
    int MinToHit,
    /// <summary>RAW current-HP threshold to flee (combat_ai.cc:3077 — NOT the
    /// run_away_mode percentage table, which only pre-computes this value).</summary>
    int MinHp,
    int MaxDist,
    string Distance,
    string Disposition);

/// <summary>
/// The parsed <c>data\ai.txt</c> table, keyed by <c>packet_num</c>. Built once and
/// queried by the combat engine via the host. Ported from combat_ai.cc aiInit()
/// (370-470): one INI <c>[Section]</c> per packet.
/// </summary>
public sealed class AiPacketTable
{
    private readonly Dictionary<int, AiPacket> _byNum;

    public AiPacketTable(IEnumerable<AiPacket> packets)
    {
        _byNum = [];
        foreach (AiPacket p in packets)
            _byNum[p.PacketNum] = p; // last definition wins on a duplicate key
    }

    public int Count => _byNum.Count;

    public AiPacket? Get(int packetNum) => _byNum.GetValueOrDefault(packetNum);

    public static AiPacketTable Parse(string text)
    {
        var packets = new List<AiPacket>();
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string name = "";

        int I(string k) => fields.TryGetValue(k, out string? v) && int.TryParse(v, out int n) ? n : 0;
        string S(string k) => fields.GetValueOrDefault(k, "");
        void Flush()
        {
            if (fields.TryGetValue("packet_num", out string? pn) && int.TryParse(pn, out int num))
                packets.Add(new AiPacket(num, name, I("min_to_hit"), I("min_hp"), I("max_dist"),
                    S("distance"), S("disposition")));
            fields.Clear();
        }

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line.StartsWith("//"))
                continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                Flush();
                name = line[1..^1].Trim();
                continue;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            string value = line[(eq + 1)..];
            int comment = value.IndexOf(';'); // strip trailing inline comment
            if (comment >= 0)
                value = value[..comment];
            fields[line[..eq].Trim()] = value.Trim();
        }
        Flush();

        return new AiPacketTable(packets);
    }
}
