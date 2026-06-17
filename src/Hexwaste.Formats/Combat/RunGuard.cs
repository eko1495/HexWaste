namespace Hexwaste.Formats.Combat;

/// <summary>
/// The dude's movement anim-code decision: run by default, walk under any of the engine's three
/// guards. ported from fallout2-ce src/animation.cc animationRegisterRunToTile() (the identical
/// block is in animationRegisterRunToObject()): walk if a crippled leg, or sneaking without Silent
/// Running, or the run art is missing; otherwise run. Everything else (per-rotation offsets,
/// FRM-driven speed) is anim-code-independent, so only the FID's anim-code differs walk vs run.
/// </summary>
public static class RunGuard
{
    public const int AnimWalk = 1;      // ANIM_WALK (animation.h:24)
    public const int AnimRunning = 19;  // ANIM_RUNNING (animation.h:42)

    /// <summary>
    /// <paramref name="combatResults"/> = the critter's DAM_* flags (a crippled-leg bit forces walk);
    /// <paramref name="sneakFlag"/> = the dude's sneaking FLAG; <paramref name="silentRunning"/> = the
    /// PERK_SILENT_RUNNING rank &gt; 0; <paramref name="runArtExists"/> = the run FRM is present.
    /// </summary>
    public static int MovementAnimCode(int combatResults, bool sneakFlag, bool silentRunning, bool runArtExists)
    {
        if ((combatResults & CriticalTables.DamCripLegAny) != 0) // guard 1: crippled leg (DAM_CRIP_LEG_ANY 0x0C)
            return AnimWalk;
        if (sneakFlag && !silentRunning) // guard 2: sneaking can't run unless Silent Running (dude-only)
            return AnimWalk;
        if (!runArtExists) // guard 3: no run art for this critter → walk
            return AnimWalk;
        return AnimRunning;
    }
}
