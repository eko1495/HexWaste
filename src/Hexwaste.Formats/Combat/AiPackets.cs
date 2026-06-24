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
    string Disposition,
    /// <summary>The DAM_* damage-flags mask parsed from ai.txt's <c>hurt_too_much</c> column;
    /// an enemy with <c>(CombatResults &amp; HurtTooMuch) != 0</c> flees (combat_ai.cc:3076). 0 = never.</summary>
    int HurtTooMuch = 0,
    /// <summary>ai.txt <c>chem_use</c> mode (gChemUseKeys order): 0 clean, 1 stims_when_hurt_little,
    /// 2 stims_when_hurt_lots, 3 sometimes, 4 anytime, 5 always (combat_ai.cc:192). Drives the AI's
    /// mid-fight healing (P42); the non-healing combat-drug branch is a documented residual.</summary>
    int ChemUse = 0,
    /// <summary>ai.txt <c>best_weapon</c> preference (gBestWeaponKeys index, combat_ai.cc:180):
    /// 0 no_pref, 1 melee, 2 melee_over_ranged, 3 ranged_over_melee, 4 ranged, 5 unarmed,
    /// 6 unarmed_over_thrown, 7 random. -1 = absent (the engine's pre-parse default, which uses the
    /// same RANGED,THROW,MELEE,UNARMED ordering as no_pref). Drives the AI inventory weapon switch
    /// (<see cref="AiBestWeapon"/>) when the wielded weapon becomes unusable.</summary>
    int BestWeapon = -1,
    /// <summary>ai.txt <c>chance</c>: the % chance to emit a combat taunt (_combatai_msg, combat_ai.cc:3322 —
    /// randomBetween(1,100) &gt; chance skips). 0 = never taunts (e.g. the Scorpion packet). P72-M3.</summary>
    int Chance = 0,
    /// <summary>ai.txt <c>color</c>: the palette index for the taunt float text (combat_ai.cc:3401
    /// textObjectAdd colour). P72-M3.</summary>
    int TauntColor = 0,
    /// <summary>ai.txt <c>attack_start</c>/<c>attack_end</c>: the combatai.msg id range the attacker
    /// picks from when it attacks (AI_MESSAGE_TYPE_ATTACK, actions.cc:630). P72-M3.</summary>
    int AttackStart = 0, int AttackEnd = -1,
    /// <summary>ai.txt <c>run_start</c>/<c>run_end</c>: the combatai.msg id range a critter picks from
    /// when it flees (AI_MESSAGE_TYPE_RUN, combat_ai.cc:1209). P72-M3.</summary>
    int RunStart = 0, int RunEnd = -1,
    /// <summary>ai.txt <c>called_freq</c>: a 1/called_freq chance to make an AIMED (called) shot at a
    /// random body part (_ai_called_shot, combat_ai.cc:2634). 10000 ≈ never (the golden packets); 0/absent
    /// = never. P75-M4.</summary>
    int CalledFreq = 0);

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
                    S("distance"), S("disposition"), ParseHurt(S("hurt_too_much")), ParseChemUse(S("chem_use")),
                    ParseBestWeapon(S("best_weapon")),
                    Chance: I("chance"), TauntColor: I("color"),         // P72-M3 taunt fields
                    AttackStart: I("attack_start"), AttackEnd: I("attack_end"),
                    RunStart: I("run_start"), RunEnd: I("run_end"),
                    CalledFreq: I("called_freq")));                      // P75-M4
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

    /// <summary>
    /// ported from fallout2-ce src/combat_ai.cc _parse_hurt_str(): the comma-delimited
    /// keyword list in ai.txt's <c>hurt_too_much</c> column, OR-ing each keyword's DAM_* mask.
    /// NOTE "crippled" is legs+arms ONLY (0x3C), NOT the obj_types.h DAM_CRIP macro that also
    /// includes blind — blind is its own keyword. An empty/absent field → 0 (never flee on hurt).
    /// </summary>
    internal static int ParseHurt(string list)
    {
        int mask = 0;
        foreach (string token in list.ToLowerInvariant().Split(','))
        {
            mask |= token.Trim() switch
            {
                "blind" => CriticalTables.DamBlind,             // 0x40
                "crippled" => CriticalTables.DamCripLimbs,      // 0x3C (legs + arms, NOT blind)
                "crippled_legs" => CriticalTables.DamCripLegAny, // 0x0C
                "crippled_arms" => CriticalTables.DamCripArmAny, // 0x30
                _ => 0, // unrecognized / empty token — the engine logs and skips
            };
        }
        return mask;
    }

    /// <summary>ai.txt <c>chem_use</c> string → the gChemUseKeys index (combat_ai.cc:192). Absent/
    /// unknown → 0 (clean). _cai_match_str_to_list (combat_ai.cc:475).</summary>
    internal static int ParseChemUse(string value) => value.Trim().ToLowerInvariant() switch
    {
        "stims_when_hurt_little" => 1,
        "stims_when_hurt_lots" => 2,
        "sometimes" => 3,
        "anytime" => 4,
        "always" => 5,
        _ => 0, // "clean" / absent
    };

    /// <summary>ai.txt <c>best_weapon</c> string → the gBestWeaponKeys index (combat_ai.cc:180).
    /// Absent/unknown → -1 (the engine's pre-parse default; _cai_match_str_to_list leaves the value
    /// untouched on no match). Note "unarmed_over_thrown" is the spelling in ai.txt.</summary>
    internal static int ParseBestWeapon(string value) => value.Trim().ToLowerInvariant() switch
    {
        "no_pref" => 0,
        "melee" => 1,
        "melee_over_ranged" => 2,
        "ranged_over_melee" => 3,
        "ranged" => 4,
        "unarmed" => 5,
        "unarmed_over_thrown" => 6,
        "random" => 7,
        _ => -1, // absent / unknown
    };
}
