namespace Hexwaste.Formats.Perks;

/// <summary>
/// One perk's definition, ported from fallout2-ce src/perk.cc PerkDescription (P28-M2). The
/// table itself is generated into PerkTable.g.cs by tools/gen_perk_table.py. <see cref="Stat"/>
/// (−1 = none) + <see cref="StatModifier"/> encode the data-driven stat perks (Toughness → DR,
/// Action Boy → AP, More Criticals → crit chance, …) applied per rank; the gate fields
/// (level/stat/skill/gvar requirements) drive <see cref="PerkRules.CanAdd"/>.
/// </summary>
/// <param name="Index">The perk's enum index (perk_defs.h order).</param>
/// <param name="FrmId">Skilldex/perk-window art id (also keys the perk.msg name/description).</param>
/// <param name="MaxRank">Max rank; −1 = not selectable at level-up (granted by other means).</param>
/// <param name="MinLevel">Minimum PC level.</param>
/// <param name="Stat">Critter stat raised per rank, or −1 for none.</param>
/// <param name="StatModifier">Per-rank modifier to <see cref="Stat"/>.</param>
/// <param name="Param1">Skill index, or (with bit 0x4000000) a global-var number; −1 = none.</param>
/// <param name="Value1">Required value of param1 (negative = "at most").</param>
/// <param name="ParamMode">0 = first-only, 1 = OR, 2 = AND (PerkParamMode).</param>
/// <param name="Param2">Second skill/gvar gate (see Param1).</param>
/// <param name="Value2">Required value of param2 (negative = "at most").</param>
/// <param name="StatReqs">Per-SPECIAL requirement [S,P,E,C,I,A,L]: positive = minimum, negative = "at most".</param>
public sealed record PerkData(int Index, int FrmId, int MaxRank, int MinLevel, int Stat, int StatModifier,
    int Param1, int Value1, int ParamMode, int Param2, int Value2, int[] StatReqs);

public static partial class PerkTable
{
    /// <summary>All 119 perk definitions in enum order (PerkTable.g.cs).</summary>
    public static IReadOnlyList<PerkData> All => Entries;

    public static PerkData Get(int index) => Entries[index];
}
