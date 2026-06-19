namespace Hexwaste.Formats.Combat;

/// <summary>
/// Critical FAILURE resolution, ported from fallout2-ce combat.cc attackComputeCriticalFailure
/// (:4178) + the _cf_table (:1875). On a MISSED attack the roll can upgrade to a critical failure —
/// the symmetric mirror of the crit-SUCCESS upgrade (random.cc randomTranslateRoll: gated on day ≥ 1,
/// a d100 ≤ −delta/10) — and the EFFECT is this Luck-bucketed severity → the _cf_table DAM_* flags
/// (drop / destroy / explode / hit-self / lose-ammo / random-hit / cripple / lose-turn / on-fire).
///
/// The trigger (the upgrade roll) and the effect APPLICATION (with the dude's separate day ≥ 6 gate
/// and the invalid-flag mask) live in CombatEngine; this is the pure severity + table lookup.
/// </summary>
public static class CriticalFailure
{
    /// <summary>Luck-modified severity bucket (combat.cc:4203-4216): chance = d100 − 5·(Luck − 5), then
    /// ≤20→0, ≤50→1, ≤75→2, ≤95→3, else 4. Higher Luck shifts the fumble toward the milder column 0.</summary>
    public static int Severity(int chance) =>
        chance <= 20 ? 0 : chance <= 50 ? 1 : chance <= 75 ? 2 : chance <= 95 ? 3 : 4;

    /// <summary>Roll the critical-failure effect flags for an attacker fumbling a weapon of the given
    /// criticalFailureType (the _cf_table row; -1/none → row 0, unarmed/default). Draws ONE d100 for the
    /// Luck-modified severity, then looks up the flags. 0 ⇒ no effect (the milder rows' column 0).</summary>
    public static int Resolve(int failureType, int luck, ICombatRng rng)
    {
        int chance = rng.Next(1, 101) - 5 * (luck - 5); // randomBetween(1,100) − 5*(LUCK−5)
        return CriticalTables.CritFailFlags(failureType, Severity(chance));
    }
}
