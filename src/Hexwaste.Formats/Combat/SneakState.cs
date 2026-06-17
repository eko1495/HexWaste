namespace Hexwaste.Formats.Combat;

/// <summary>
/// The dude's two-layer sneak state, ported from fallout2-ce src/critter.cc. The engine separates the
/// SNEAKING FLAG (<c>dudeHasState</c> — the toggle the player/Skilldex sets, stored in the proto critter
/// flags) from <c>_sneak_working</c>, the REAL active state set by a periodic SKILL_SNEAK roll
/// (<c>sneakEventProcess</c>). "Really sneaking" (<c>dudeIsSneaking</c>, critter.cc:1236) = flag AND
/// working; a script's <c>using_skill(dude, SKILL_SNEAK)</c> reads only the FLAG (interpreter_extra.cc:589).
///
/// PoC: the engine stores the flag in the proto's critter flag bits and <c>_sneak_working</c> in the
/// critter data block (saved as int32, critter.cc:205/218); we hold the two as plain bools and persist
/// them additively in the save (P29 A-M2). A fresh/trait-less dude has both false → not sneaking.
/// </summary>
public sealed class SneakState
{
    /// <summary>The sneaking FLAG (dudeHasState, DUDE_STATE_SNEAKING) — the Skilldex/S toggle.</summary>
    public bool FlagSet { get; set; }

    /// <summary>The active result of the last periodic SKILL_SNEAK roll (<c>_sneak_working</c>).
    /// Set by the A-M2 periodic roll; meaningless until the flag is set.</summary>
    public bool Working { get; set; }

    /// <summary>dudeIsSneaking (critter.cc:1236): really hidden only when the flag is set AND the last
    /// periodic sneak roll succeeded.</summary>
    public bool IsSneaking => FlagSet && Working;

    /// <summary>Ticks until the next periodic sneak re-check, ported verbatim from sneakEventProcess
    /// (critter.cc:1195-1221): a success reschedules in 600; a failure retries SOONER the higher the
    /// Sneak skill (>250→100, >200→120, >170→150, >135→200, >100→300, >80→400, else 600).</summary>
    public static int RescheduleTicks(int sneakSkill, bool rollSucceeded)
    {
        if (rollSucceeded)
            return 600;
        return sneakSkill switch
        {
            > 250 => 100,
            > 200 => 120,
            > 170 => 150,
            > 135 => 200,
            > 100 => 300,
            > 80 => 400,
            _ => 600,
        };
    }
}
