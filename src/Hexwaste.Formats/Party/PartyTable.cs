namespace Hexwaste.Formats.Party;

/// <summary>
/// One <c>[Party Member N]</c> section of <c>data\party.txt</c> — the subset that
/// drives proto level-ups. Ported from fallout2-ce src/party_member.cc
/// partyMembersInit() (the <c>level_minimum</c>/<c>level_up_every</c>/<c>level_pids</c>
/// keys); the AI behaviour bools (area_attack_mode, best_weapon, …) are out of scope
/// for the level-up port and skipped.
/// </summary>
/// <param name="SectionIndex">The <c>N</c> in <c>[Party Member N]</c>. The engine
/// keys the per-level flavour message (misc.msg 9000 + 10·index + level − 1) on this,
/// so it is preserved for a future viewer hookup even though the pure logic ignores it.</param>
/// <param name="Pid">The companion proto PID (<c>party_member_pid</c>).</param>
/// <param name="LevelMinimum">Player level the companion must reach before it can level.</param>
/// <param name="LevelUpEvery">Cadence; 0 means the member never levels (level_pids = -1).</param>
/// <param name="LevelPids">Ordered upgrade-stage proto PIDs (capped at
/// <see cref="PartyTable.MaxLevel"/>).</param>
public sealed record PartyMemberDescription(
    int SectionIndex,
    int Pid,
    int LevelMinimum,
    int LevelUpEvery,
    IReadOnlyList<int> LevelPids);

/// <summary>
/// The parsed <c>data\party.txt</c> — companion level-up tables. Pure data layer
/// (the decision logic lives in <see cref="PartyLevelUp"/>); no MonoGame, no game
/// data baked in. Ported from fallout2-ce src/party_member.cc partyMembersInit().
/// </summary>
public sealed class PartyTable
{
    /// <summary>PARTY_MEMBER_MAX_LEVEL (party_member.cc:45) — the level_pids cap.</summary>
    public const int MaxLevel = 6;

    /// <summary>Every parsed member, in section order.</summary>
    public IReadOnlyList<PartyMemberDescription> Members { get; }

    /// <summary>Members keyed by proto PID — the recruit-time lookup
    /// (party_member.cc:1492 gPartyMemberPids match).</summary>
    public IReadOnlyDictionary<int, PartyMemberDescription> ByPid { get; }

    private PartyTable(List<PartyMemberDescription> members)
    {
        Members = members;
        var byPid = new Dictionary<int, PartyMemberDescription>();
        foreach (PartyMemberDescription m in members)
            byPid[m.Pid] = m; // later sections win on a duplicate PID (engine: last match)
        ByPid = byPid;
    }

    /// <summary>The level-up description for a recruited critter's proto PID, or null
    /// if it is not a party.txt member (most critters — they never level).</summary>
    public PartyMemberDescription? ForPid(int pid) => ByPid.GetValueOrDefault(pid);

    public static PartyTable Parse(string text)
    {
        var members = new List<PartyMemberDescription>();

        int sectionIndex = -1;
        int pid = -1, levelMinimum = 0, levelUpEvery = 0;
        List<int> levelPids = [];
        bool inMember = false;

        void Flush()
        {
            if (inMember && pid != -1)
                members.Add(new PartyMemberDescription(sectionIndex, pid, levelMinimum, levelUpEvery, levelPids));
        }

        foreach (string raw in text.Split('\n'))
        {
            string line = StripComment(raw).Trim();
            if (line.Length == 0)
                continue;

            if (line[0] == '[')
            {
                Flush();
                string head = line.Trim('[', ']').Trim();
                // Only "[Party Member N]" (singular + number) carries a level table;
                // "[Party Members]" (the count header) and any other section are skipped.
                inMember = head.StartsWith("Party Member ", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(head["Party Member ".Length..].Trim(), out sectionIndex);
                pid = -1;
                levelMinimum = 0;
                levelUpEvery = 0;
                levelPids = [];
                continue;
            }

            if (!inMember)
                continue;

            int eq = line.IndexOf('=');
            if (eq < 0)
                continue;
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();

            switch (key.ToLowerInvariant())
            {
                case "party_member_pid":
                    int.TryParse(value, out pid);
                    break;
                case "level_minimum":
                    int.TryParse(value, out levelMinimum);
                    break;
                case "level_up_every":
                    int.TryParse(value, out levelUpEvery);
                    break;
                case "level_pids":
                    // Comma/space-separated PIDs (engine strParseInt loop, party_member.cc:253),
                    // capped at MaxLevel. -1 (the "never levels" sentinel) is kept as-is;
                    // level_up_every=0 already gates those out in PartyLevelUp.
                    levelPids = [.. value
                        .Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => int.TryParse(s, out int p) ? p : 0)
                        .Take(MaxLevel)];
                    break;
            }
        }
        Flush();

        return new PartyTable(members);
    }

    private static string StripComment(string line)
    {
        int semi = line.IndexOf(';');
        return semi < 0 ? line : line[..semi];
    }
}
