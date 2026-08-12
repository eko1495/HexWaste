namespace Hexwaste.Formats.Combat;

/// <summary>
/// The AI's two-weapon preference comparison, ported from fallout2-ce src/combat_ai.cc
/// <c>_ai_best_weapon()</c> (:1817), the <c>_weapPrefOrderings</c> table (:269) and
/// <c>_caiHasWeapPrefType()</c> (:1803). Decides which of a critter's carried weapons it
/// should wield, by its ai.txt <c>best_weapon</c> preference, with an avg-damage / item-cost
/// tiebreak. PURE — the caller (<see cref="CombatEngine"/>) supplies each candidate's class /
/// damage / cost; the <c>_ai_search_inven_weap</c> fold lives in the engine where the host
/// accessors are.
/// </summary>
public static class AiBestWeapon
{
    // _weapPrefOrderings[BEST_WEAPON_COUNT + 1][ATTACK_TYPE_COUNT], indexed [best_weapon + 1].
    // Entries are ATTACK_TYPE_* (1 unarmed, 2 melee, 3 throw, 4 ranged); 0 = end/unused.
    // ported from fallout2-ce src/combat_ai.cc _weapPrefOrderings (:269).
    private static readonly int[][] PrefOrderings =
    {
        new[] { 4, 3, 2, 1, 0 }, // best_weapon == -1 (engine default) → RANGED,THROW,MELEE,UNARMED
        new[] { 4, 3, 2, 1, 0 }, // NO_PREF
        new[] { 2, 0, 0, 0, 0 }, // MELEE
        new[] { 2, 4, 0, 0, 0 }, // MELEE_OVER_RANGED
        new[] { 4, 2, 0, 0, 0 }, // RANGED_OVER_MELEE
        new[] { 4, 0, 0, 0, 0 }, // RANGED
        new[] { 1, 0, 0, 0, 0 }, // UNARMED
        new[] { 1, 3, 0, 0, 0 }, // UNARMED_OVER_THROW
        new[] { 0, 0, 0, 0, 0 }, // RANDOM
    };

    private const int BestWeaponUnarmedOverThrow = 6; // BEST_WEAPON_UNARMED_OVER_THROW
    private const int BestWeaponRandom = 7;           // BEST_WEAPON_RANDOM

    /// <summary>One weapon (or the unarmed "punch" seed) as the comparison sees it.</summary>
    /// <param name="AttackType">ATTACK_TYPE_* (<see cref="WeaponClass"/>).</param>
    /// <param name="AvgDamage">(min+max)/2 of the weapon's damage; 0 for the punch seed (the engine
    /// leaves avgDamage1 = 0 when weapon1 is null).</param>
    /// <param name="Cost">itemGetCost — the cost tiebreak when avg damage is within 5.</param>
    /// <param name="Ignore"><c>_combat_safety_invalidate_weapon</c> → order forced to 999 (unusable here).</param>
    /// <param name="IsFlare">pid == PROTO_ID_FLARE (79) — deprioritised when orders differ.</param>
    public readonly record struct Choice(
        int AttackType, int AvgDamage, int Cost, bool Ignore = false, bool IsFlare = false);

    /// <summary>ported from fallout2-ce src/combat_ai.cc _ai_best_weapon (:1857-1870): the candidate's
    /// damage score — the (min+max)/2 midpoint (the SFALL avg-damage fix), DOUBLED when the weapon
    /// carries a weapon perk (:1866). The explosive ×(extrasLength+1) factor (:1861) is NOT applied —
    /// it needs _compute_explosion_on_extras, deferred with the ring-spiral explosion port.</summary>
    public static int AvgDamage(int minDamage, int maxDamage, int weaponPerk)
    {
        int avg = (minDamage + maxDamage) / 2;
        return weaponPerk != -1 ? avg * 2 : avg;
    }

    /// <summary><c>_caiHasWeapPrefType</c>: does this best_weapon preference include this attack type?</summary>
    public static bool HasWeapPrefType(int bestWeapon, int attackType)
    {
        foreach (int t in PrefOrderings[bestWeapon + 1])
            if (t == attackType)
                return true;
        return false;
    }

    private static int OrderOf(int bestWeapon, int attackType, bool ignore)
    {
        if (ignore)
            return 999;
        int[] order = PrefOrderings[bestWeapon + 1];
        for (int i = 0; i < order.Length; i++)
            if (order[i] == attackType)
                return i;
        return 999;
    }

    /// <summary>
    /// <c>_ai_best_weapon</c> pairwise: true if <paramref name="b"/> (the candidate) is preferred
    /// over <paramref name="a"/> (the running best). The engine's "returns null" (both attack types
    /// outside the preference) maps to false here — keep <paramref name="a"/> (which is the unarmed
    /// punch seed at the fold start, so a null ≡ "use fists"). RANDOM is resolved by the caller's
    /// coin via <paramref name="randomFavorsB"/>.
    /// </summary>
    public static bool Prefers(int bestWeapon, in Choice a, in Choice b, bool randomFavorsB = false)
    {
        if (bestWeapon == BestWeaponRandom)
            return randomFavorsB;

        int order1 = OrderOf(bestWeapon, a.AttackType, a.Ignore);
        int order2 = OrderOf(bestWeapon, b.AttackType, b.Ignore);

        if (order1 == order2)
        {
            if (order1 == 999)
                return false; // neither in the preference → keep a (engine returns null ≡ punch)
            if (Math.Abs(b.AvgDamage - a.AvgDamage) <= 5)
                return b.Cost > a.Cost; // within 5 damage → higher item cost wins
            return b.AvgDamage > a.AvgDamage;
        }

        if (a.IsFlare) return true;   // a is a flare and orders differ → prefer the real weapon b
        if (b.IsFlare) return false;  // b is a flare → keep a

        if ((bestWeapon == -1 || bestWeapon >= BestWeaponUnarmedOverThrow)
            && Math.Abs(b.AvgDamage - a.AvgDamage) > 5)
            return b.AvgDamage > a.AvgDamage;

        return order1 > order2; // lower order index = more preferred
    }
}
