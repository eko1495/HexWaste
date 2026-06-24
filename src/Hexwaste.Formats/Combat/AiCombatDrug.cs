namespace Hexwaste.Formats.Combat;

/// <summary>
/// The NPC non-healing combat-drug decision, ported from fallout2-ce src/combat_ai.cc _ai_check_drugs
/// (the <c>if (!drugUsed)</c> branch, :1028): after the heal pass, a critter whose chem_use is
/// sometimes/anytime/always rolls a per-mode chance and (if it passes) quaffs a <c>chem_primary_desire</c>
/// drug — Jet/Psycho/Buffout — to buff itself mid-fight. P78-M2 (the heal branch is P42's AiHealing).
/// </summary>
public static class AiCombatDrug
{
    /// <summary>The chemUseChance for this turn (combat_ai.cc:982-994): SOMETIMES 25% / ANYTIME 75% gated
    /// on combatTurns%3, ALWAYS 100%. Clean + the stims-when-hurt modes don't combat-drug (return 0).</summary>
    public static int UseChance(int chemUse, int combatTurns) => chemUse switch
    {
        3 => combatTurns % 3 == 0 ? 25 : 0, // CHEM_USE_SOMETIMES
        4 => combatTurns % 3 == 0 ? 75 : 0, // CHEM_USE_ANYTIME
        5 => 100,                           // CHEM_USE_ALWAYS
        _ => 0,
    };

    /// <summary>Does the NPC decide to chem up this turn? randomBetween(0,100) &lt; chance (combat_ai.cc:1030).
    /// Short-circuits WITHOUT drawing when the chance is 0 (clean/off-turn) — so a clean enemy is inert.</summary>
    public static bool ShouldUse(int chemUse, int combatTurns, ICombatRng rng)
    {
        int chance = UseChance(chemUse, combatTurns);
        return chance > 0 && rng.Next(0, 101) < chance; // rng.Next(0,101) == randomBetween(0,100)
    }

    /// <summary>How many drugs the NPC may take this turn before stopping (combat_ai.cc:1122): SOMETIMES 1,
    /// ANYTIME 2, ALWAYS unbounded (AP-limited). Used to cap the engine's quaff loop.</summary>
    public static int MaxPerTurn(int chemUse) => chemUse switch { 3 => 1, 4 => 2, _ => int.MaxValue };

    /// <summary>Pick a carried non-healing drug pid: a <c>chem_primary_desire</c> match first (in desire
    /// order), else the first other non-healing drug; −1 if none. The engine randomizes within the bucket
    /// (a CE tweak) — we take the first match for determinism (documented).</summary>
    public static int Pick(IReadOnlyList<int> carriedDrugPids, int[]? primaryDesire)
    {
        var nonHealing = carriedDrugPids.Where(p => !AiHealing.IsHealingItem(p)).ToList();
        if (nonHealing.Count == 0)
            return -1;
        if (primaryDesire is not null)
            foreach (int want in primaryDesire)
                if (nonHealing.Contains(want))
                    return want;
        return nonHealing[0];
    }
}
