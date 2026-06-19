namespace Hexwaste.Formats.Item;

/// <summary>
/// Drug addiction roll + the drug→addiction-GVAR map, ported from fallout2-ce src/item.cc
/// (the _item_d_take_drug addiction tail :2822, gDrugDescriptions :144, drugGetAddictionGvarByPid
/// :3091). Pure logic; the timed withdrawal STAT penalty is the maxRank==-1 perk fold in
/// <see cref="Perks.PerkRules.MaxRankPerkEffect"/> (perkAddEffect, perk.cc), keyed by the proto's
/// WithdrawalEffect (the addiction-perk index).
///
/// Inert by default: <see cref="GvarForPid"/> returns -1 for a non-drug pid and the roll fires
/// only from a dude UseDrug — so nothing addicts a fresh / un-drugged dude.
/// </summary>
public static class DrugAddiction
{
    // ported from fallout2-ce src/item.cc gDrugDescriptions (:144) — {drugPid → addiction GVAR}.
    // GVAR indices are the game_vars.h positional enum values (verified: NUKA_COLA_ADDICT=21 …
    // ALCOHOL_ADDICT=26, ADDICT_TRAGIC=293, ADDICT_JET=294). The struct's field_8 is unused here.
    private static readonly Dictionary<int, int> AddictionGvar = new()
    {
        [106] = 21,  // Nuka-Cola → GVAR_NUKA_COLA_ADDICT
        [87] = 22,   // Buffout   → GVAR_BUFF_OUT_ADDICT
        [53] = 23,   // Mentats   → GVAR_MENTATS_ADDICT
        [110] = 24,  // Psycho    → GVAR_PSYCHO_ADDICT
        [48] = 25,   // RadAway   → GVAR_RADAWAY_ADDICT
        [124] = 26,  // Beer      → GVAR_ALCOHOL_ADDICT
        [125] = 26,  // Booze     → GVAR_ALCOHOL_ADDICT
        [259] = 294, // Jet       → GVAR_ADDICT_JET
        [304] = 293, // Deck of Tragic Cards → GVAR_ADDICT_TRAGIC
    };

    /// <summary>The char-sheet "::: Addictions :::" display rows, ported from character_editor.cc
    /// gAddictionReputationVars (:540) — the addiction GVAR + its editor.msg name id (1004 + index,
    /// :4625), in the engine's order. The display shows each row whose GVAR is non-zero.</summary>
    public static readonly (int Gvar, int EditorMsgId)[] ReputationVars =
    [
        (21, 1004),  // Nuka-Cola
        (22, 1005),  // Buffout
        (23, 1006),  // Mentats
        (24, 1007),  // Psycho
        (25, 1008),  // RadAway
        (26, 1009),  // Alcohol
        (294, 1010), // Jet
        (293, 1011), // Deck of Tragic Cards
    ];

    /// <summary>The addiction GVAR index for a drug pid, or -1 if the drug isn't addictive
    /// (drugGetAddictionGvarByPid, item.cc:3091).</summary>
    public static int GvarForPid(int drugPid) =>
        AddictionGvar.TryGetValue(drugPid, out int gvar) ? gvar : -1;

    /// <summary>Whether a drug pid can cause addiction at all.</summary>
    public static bool IsAddictive(int drugPid) => AddictionGvar.ContainsKey(drugPid);

    /// <summary>The faithful addiction roll (item.cc:2823-2840): chance = addictionChance, ×2 for
    /// the Chem Reliant trait, ÷2 for Chem Resistant, ÷2 for the Flower Child perk (in that order,
    /// integer division), then addicted iff <paramref name="roll"/> (a 1..100 draw) ≤ chance —
    /// inclusive (randomBetween, random.cc:134). The trait/perk mods are dude-only at the call site.</summary>
    public static bool Roll(int addictionChance, bool chemReliant, bool chemResistant, bool flowerChild, int roll)
    {
        int chance = addictionChance;
        if (chemReliant) chance *= 2;
        if (chemResistant) chance /= 2;
        if (flowerChild) chance /= 2;
        return roll <= chance;
    }
}
