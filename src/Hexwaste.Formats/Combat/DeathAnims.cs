namespace Hexwaste.Formats.Combat;

/// <summary>
/// Gory death-animation selection (P26), ported from fallout2-ce src/actions.cc _pick_death
/// (0x410378). On a kill the corpse takes a gore variant — sliced / charred / electrified /
/// chunks / big-hole / exploded — chosen by the weapon's damage type, the damage dealt, and the
/// attack animation, gated by the violence-level preference. The host applies the engine's
/// _check_death art-exists fallback (a critter without the gore art falls back to FALL_BACK).
///
/// PoC scope: the BLOODY_MESS trait + the Pyromaniac/Flameboy/Pyro perks + the Molotov special
/// case + the per-critter CRITTER_SPECIAL_DEATH flag are out (no trait/perk system, no molotov in
/// the slice); the hit-from-front flip is host-side (FALL_BACK vs FALL_FRONT by art existence).
/// </summary>
public static class DeathAnims
{
    // Death animation codes (animation.h Anim enum). The static single-frame corpse is at
    // the death anim + CorpseOffset (FALL_BACK 20 -> FALL_BACK_SF 48).
    public const int FallBack = 20, FallFront = 21;
    public const int BigHole = 23, CharredBody = 24, ChunksOfFlesh = 25, DancingAutofire = 26;
    public const int Electrify = 27, SlicedInHalf = 28;
    public const int ElectrifiedToNothing = 30, ExplodedToNothing = 31, MeltedToNothing = 32, FireDance = 33;
    public const int CorpseOffset = 28;

    // Attacker animation codes (animation.h) that _pick_death branches on.
    public const int ThrowPunch = 16, KickLeg = 17, ThrowAnim = 18, ThrustAnim = 41,
        SwingAnim = 42, FireSingle = 45, FireBurst = 46;

    // Violence-level preference (the engine's preferences.violence_level). The viewer fixes this
    // at NORMAL (no preferences screen) — enough to show gNormalDeathAnimations gore on solid hits
    // without the MAX_BLOOD obliteration deaths.
    public const int ViolenceNone = 0, ViolenceMinimal = 1, ViolenceNormal = 2, ViolenceMaxBlood = 3;

    private const int DamageTypeNormal = 0, DamageTypeExplosion = 6;

    // gNormalDeathAnimations / gMaximumBloodDeathAnimations, indexed by DAMAGE_TYPE 0..6
    // (NORMAL,LASER,FIRE,PLASMA,ELECTRICAL,EMP,EXPLOSION) — actions.cc:55-74.
    private static readonly int[] NormalByDamage =
        [DancingAutofire, SlicedInHalf, CharredBody, CharredBody, Electrify, FallBack, BigHole];
    private static readonly int[] MaxBloodByDamage =
        [ChunksOfFlesh, SlicedInHalf, FireDance, MeltedToNothing, ElectrifiedToNothing, FallBack, ExplodedToNothing];

    /// <summary>The attacker animation a single attack presents for the gore picker: a gunshot
    /// is FIRE_SINGLE, a melee weapon SWING, a bare fist PUNCH. (Bursts pass FIRE_BURST, thrown
    /// weapons THROW_ANIM directly.)</summary>
    public static int AttackAnimFor(bool isGun, bool hasWeapon) =>
        isGun ? FireSingle : hasWeapon ? SwingAnim : ThrowPunch;

    /// <summary>The DESIRED death animation for a kill (the host then applies the art-exists
    /// fallback). Ported from _pick_death; bloodyMess defaults false (no trait system).</summary>
    public static int Pick(int damageType, int damage, int attackerAnim, int violenceLevel, bool bloodyMess = false)
    {
        const int normalThreshold = 15, maxThreshold = 45;
        if (damageType < 0 || damageType >= NormalByDamage.Length)
            return FallBack;

        int deathAnim = FallBack;

        bool meleeLike = (attackerAnim == ThrowPunch && damageType == DamageTypeNormal)
            || attackerAnim == KickLeg || attackerAnim == ThrustAnim || attackerAnim == SwingAnim
            || (attackerAnim == ThrowAnim && damageType != DamageTypeExplosion);

        if (meleeLike)
        {
            // Melee/thrown-non-explosive: only a BLOODY_MESS dude big-holes (out of scope) → FALL_BACK.
            if (violenceLevel == ViolenceMaxBlood && bloodyMess)
                deathAnim = BigHole;
        }
        else if (attackerAnim == FireSingle && damageType == DamageTypeNormal)
        {
            // A single normal-damage shot only big-holes at max blood (bloody-mess or big damage).
            if (violenceLevel == ViolenceMaxBlood && (bloodyMess || maxThreshold <= damage))
                deathAnim = BigHole;
        }
        else if (violenceLevel > ViolenceMinimal && (bloodyMess || normalThreshold <= damage))
        {
            deathAnim = violenceLevel > ViolenceNormal && (bloodyMess || maxThreshold <= damage)
                ? MaxBloodByDamage[damageType]
                : NormalByDamage[damageType];
        }

        return deathAnim;
    }
}
