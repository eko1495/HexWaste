using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Int;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Save.Vanilla;

/// <summary>Game-data-derived counts SAVE.DAT does not store but its readers depend on.</summary>
public sealed record VanillaSaveContext(int GlobalVarCount, int PartyDescriptionCount,
    IReadOnlyList<int> PartyDescriptionPids, Func<int, bool> HasCustomAiPacket, ProtoDatabase? Protos)
{
    public static VanillaSaveContext FromGameData(GameFileSystem vfs, ProtoDatabase protos)
    {
        // ported from fallout2-ce src/game.cc globalVarsRead("data\\vault13.gam") (:1044)
        int gvars = GameGlobalVars.Parse(ReadText(vfs, @"data\vault13.gam")).Count;
        List<int> pids = PartyDescriptionPidsFrom(ReadText(vfs, @"data\party.txt"));
        AiPacketTable ai = AiPacketTable.Parse(ReadText(vfs, @"data\ai.txt"));
        bool Custom(int pid)
        {
            // ported from fallout2-ce src/combat_ai.cc aiLoad() (:573): critter pid whose proto AI
            // packet has disposition "custom" (index 0 after the keyword shift, :222-229/:480-482).
            if (pid == -1 || Fid.PidType(pid) != 1)
                return false;
            // The dude pid 0x1000000 resolves to the in-memory gDudeProto (proto.cc protoGetProto :2133),
            // initialised by proto_critter_init (proto.cc:502) with aiPacket = 1 — not a critters.lst row.
            int packet = pid == 0x1000000 ? 1 : protos.Get(pid).Critter?.AiPacket ?? -1;
            return ai.Get(packet) is { } p && p.Disposition.Equals("custom", StringComparison.OrdinalIgnoreCase);
        }
        return new VanillaSaveContext(gvars, pids.Count, pids, Custom, protos);
    }

    /// <summary>ported from fallout2-ce src/party_member.cc partyMembersInit() (:133-143): consecutive
    /// "[Party Member N]" sections from N=0 that carry party_member_pid; index 0 is the dude.</summary>
    public static List<int> PartyDescriptionPidsFrom(string text)
    {
        var pids = new List<int>();
        var sections = new Dictionary<int, int>();
        int current = -1;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Split(';')[0].Trim();
            if (line.StartsWith("[Party Member ", StringComparison.OrdinalIgnoreCase) && line.EndsWith(']'))
                current = int.TryParse(line[14..^1].Trim(), out int n) ? n : -1;
            else if (line.StartsWith('['))
                current = -1;
            else if (current >= 0 && line.StartsWith("party_member_pid", StringComparison.OrdinalIgnoreCase)
                     && line.Split('=') is [_, var v] && int.TryParse(v.Trim(), out int pid))
                sections[current] = pid;
        }
        for (int n = 0; sections.TryGetValue(n, out int pid); n++)
            pids.Add(pid);
        return pids;
    }

    private static string ReadText(GameFileSystem vfs, string path)
    {
        using var reader = new StreamReader(vfs.OpenRead(path));
        return reader.ReadToEnd();
    }
}
