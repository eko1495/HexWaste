namespace Hexwaste.Formats.Int;

/// <summary>
/// The player-character meta-stat indices read by <c>get_pc_stat</c> (interpreter_extra.cc
/// op_get_pc_stat 0x80A6), ported from fallout2-ce src/stat_defs.h <c>PcStat</c> enum. These are NOT the
/// critter SPECIAL/derived stats — they live in the engine's <c>gPcStatValues</c> array. Reputation (3)
/// and karma (4) are the home of P31; the engine never auto-updates karma (no kill/quest hook), so it is
/// read-only display data driven by scripts (set_global_var on the reputation GVARs) or the harness.
/// </summary>
public static class PcStat
{
    public const int UnspentSkillPoints = 0;
    public const int Level = 1;
    public const int Experience = 2;
    public const int Reputation = 3;
    public const int Karma = 4;
    public const int Count = 5;
}
