namespace Hexwaste.Formats.Combat;

/// <summary>
/// The Doctor skill's crippled-limb / blindness mending, ported from fallout2-ce
/// src/skill.cc SKILL_DOCTOR (skill.cc:675-752). First Aid heals HP only — only Doctor
/// (and Repair on robots, out of slice) clears the DAM_CRIP_* / DAM_BLIND flags.
/// </summary>
public static class SkillHealing
{
    /// <summary>The healable damage flags in the engine's order (gHealableDamageFlags,
    /// skill.cc:69-75): blind, then left arm, right arm, right leg, left leg.</summary>
    public static readonly (int Flag, string Name)[] HealableLimbs =
    [
        (CriticalTables.DamBlind, "eyes"),
        (CriticalTables.DamCripArmLeft, "left arm"),
        (CriticalTables.DamCripArmRight, "right arm"),
        (CriticalTables.DamCripLegRight, "right leg"),
        (CriticalTables.DamCripLegLeft, "left leg"),
    ];

    public static bool IsCrippled(int combatResults) => (combatResults & CriticalTables.DamHealable) != 0;

    /// <summary>Roll <paramref name="skillPercent"/> (d100) against each present crippled
    /// limb / blindness in engine order, clearing the flag on success. Returns the new
    /// CombatResults; <paramref name="healed"/> names the limbs mended (in order).</summary>
    public static int HealLimbs(int combatResults, int skillPercent, ICombatRng rng, out List<string> healed)
    {
        healed = [];
        foreach ((int flag, string name) in HealableLimbs)
        {
            if ((combatResults & flag) == 0)
                continue; // only roll for limbs that are actually crippled
            if (rng.Next(1, 101) <= skillPercent)
            {
                combatResults &= ~flag;
                healed.Add(name);
            }
        }
        return combatResults;
    }
}
