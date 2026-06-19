namespace Hexwaste.Formats.Combat;

/// <summary>
/// AI mid-fight healing, ported from fallout2-ce src/combat_ai.cc _ai_check_drugs (:955) — a hurt
/// BIPED enemy quaffs healing items from its inventory before attacking. Pure: the healing-item set
/// + the chem_use → HP-ratio map. The trigger/loop (BIPED gate, AP cost, while-below-ratio) lives in
/// CombatEngine; the actual heal (find item, roll, consume) is host-side.
/// </summary>
public static class AiHealing
{
    /// <summary>The healing items the AI will use (itemIsHealing, item.cc:3592 gHealingItemPids):
    /// Stimpak (40), Super Stimpak (144), Healing Powder (273).</summary>
    public static bool IsHealingItem(int pid) => pid is 40 or 144 or 273;

    /// <summary>The HP ratio (% of max) below which the AI heals, by chem_use mode (combat_ai.cc:971-991):
    /// clean → 0 (never heals), stims_when_hurt_little → 60, stims_when_hurt_lots → 30, else (the chance
    /// modes sometimes/anytime/always, which also heal at the default while pursuing the combat-drug
    /// branch) → 50.</summary>
    public static int HealHpRatio(int chemUse) => chemUse switch
    {
        0 => 0,   // clean — no chems
        1 => 60,  // stims_when_hurt_little
        2 => 30,  // stims_when_hurt_lots
        _ => 50,  // sometimes / anytime / always — default heal threshold
    };
}
