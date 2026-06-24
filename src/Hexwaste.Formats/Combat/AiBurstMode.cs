namespace Hexwaste.Formats.Combat;

/// <summary>
/// The AI single-vs-burst decision, ported from fallout2-ce src/combat_ai.cc _ai_pick_hit_mode()
/// (:2285): an NPC whose weapon has a burst (secondary) mode picks burst by its ai.txt
/// <c>area_attack_mode</c> + <c>secondary_freq</c>. The caller must first confirm the weapon is
/// burst-capable (the engine's pre-RNG "no secondary attack type → primary" short-circuit) so a
/// single-mode enemy never reaches the rng draw. P76-M1.
/// </summary>
public static class AiBurstMode
{
    /// <summary>Parse the ai.txt area_attack_mode string into the shared <see cref="AreaAttack"/> enum;
    /// null = absent (the INT&lt;6/dist&lt;10 default branch). "no_pref"/unknown → Never (no burst).</summary>
    public static AreaAttack? Parse(string mode) => mode.Trim().ToLowerInvariant() switch
    {
        "" => null, // absent → default branch
        "always" => AreaAttack.Always,
        "sometimes" => AreaAttack.Sometimes,
        "be_careful" => AreaAttack.BeCareful,
        "be_sure" => AreaAttack.BeSure,
        "be_absolutely_sure" => AreaAttack.BeAbsolutelySure,
        _ => AreaAttack.Never, // no_pref / unrecognised → the switch falls through to single
    };

    /// <summary>Should this NPC burst this attack? <paramref name="secondaryToHit"/> is the to-hit with
    /// the burst mode. The rng (1/secondary_freq) is drawn ONLY for SOMETIMES and the default branch.</summary>
    public static bool ShouldBurst(AiPacket ai, int attackerIntelligence, int distance, int secondaryToHit, ICombatRng rng)
    {
        AreaAttack? mode = Parse(ai.AreaAttackMode);
        if (mode is { } m)
            return m == AreaAttack.Sometimes
                ? ai.SecondaryFreq >= 1 && rng.Next(1, ai.SecondaryFreq + 1) == 1
                : CompanionAi.ShouldAreaAttack(m, secondaryToHit);
        // area_attack_mode absent (-1): low INT or a close target sprays (combat_ai.cc:2313).
        return (attackerIntelligence < 6 || distance < 10)
            && ai.SecondaryFreq >= 1 && rng.Next(1, ai.SecondaryFreq + 1) == 1;
    }
}
