using System.Text;
using Hexwaste.Formats.Perks;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Perk infrastructure (P28-M2): the generated 119-perk table (PerkTable.g.cs from
/// tools/gen_perk_table.py) + the perkCanAdd gate port + the every-3-levels cadence + the
/// data-driven stat modifiers. The inert-by-default invariant (zero ranks → 0) keeps goldens stable.
/// </summary>
public class PerkTests
{
    [Fact]
    public void TableHasAllPerksAndMatchesGeneratorChecksum()
    {
        Assert.Equal(119, PerkTable.Count);
        Assert.Equal(119, PerkTable.All.Count);

        // Recompute the FNV-1a checksum over the same flattened field stream the Python generator
        // hashes (frmId..value2 + the 7 stat reqs, in order) — guards the table against drift.
        var sb = new StringBuilder();
        foreach (PerkData p in PerkTable.All)
        {
            int[] f = [p.FrmId, p.MaxRank, p.MinLevel, p.Stat, p.StatModifier,
                p.Param1, p.Value1, p.ParamMode, p.Param2, p.Value2, .. p.StatReqs];
            foreach (int n in f)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(n);
            }
        }
        uint h = 0x811c9dc5u;
        foreach (byte b in Encoding.UTF8.GetBytes(sb.ToString()))
            h = (h ^ b) * 0x01000193u;
        Assert.Equal(PerkTable.Checksum, h);
    }

    [Fact]
    public void KnownPerkEntriesParsedCorrectly()
    {
        // Bonus HtH Damage (idx 2): +2 melee/rank, maxRank 3, level 3, needs ST6 & AG6.
        PerkData hth = PerkTable.Get(2);
        Assert.Equal(11, hth.Stat); // MELEE_DAMAGE
        Assert.Equal(2, hth.StatModifier);
        Assert.Equal(3, hth.MaxRank);
        Assert.Equal(3, hth.MinLevel);
        Assert.Equal(6, hth.StatReqs[0]); // ST
        Assert.Equal(6, hth.StatReqs[5]); // AG
        // Toughness (idx 12): +10 DR/rank.
        Assert.Equal(24, PerkTable.Get(12).Stat); // DAMAGE_RESISTANCE
        Assert.Equal(10, PerkTable.Get(12).StatModifier);
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 4)]
    public void ProgressionIsThreeOrFourWithSkilled(bool skilled, int expected) =>
        Assert.Equal(expected, PerkRules.Progression(skilled));

    [Theory]
    [InlineData(2, false, 0)]
    [InlineData(3, false, 1)]
    [InlineData(99, false, 33)]
    [InlineData(12, true, 3)]    // skilled → every 4
    [InlineData(200, false, 37)] // capped at 37
    public void PicksEarnedFollowsCadence(int level, bool skilled, int expected) =>
        Assert.Equal(expected, PerkRules.PicksEarned(level, skilled));

    [Fact]
    public void StatModifierIsInertWithoutRanks()
    {
        Assert.Equal(0, PerkRules.StatModifier(24, null));
        Assert.Equal(0, PerkRules.StatModifier(24, new int[PerkTable.Count]));
    }

    [Fact]
    public void StatModifierSumsRankedPerks()
    {
        var ranks = new int[PerkTable.Count];
        ranks[12] = 2; // Toughness rank 2 → +20 DR
        Assert.Equal(20, PerkRules.StatModifier(24, ranks));
        Assert.Equal(0, PerkRules.StatModifier(8, ranks)); // unrelated stat unaffected
    }

    // --- perkGetSkillModifier (P70 skill-perk family) --------------------

    [Fact]
    public void SkillModifierIsInertWithoutRanks()
    {
        Assert.Equal(0, PerkRules.SkillModifier(0, null));
        Assert.Equal(0, PerkRules.SkillModifier(13, new int[PerkTable.Count]));
    }

    [Theory]
    [InlineData(PerkId.Medic, 6, 10)]                 // Medic → +10 First Aid
    [InlineData(PerkId.Medic, 7, 10)]                 // Medic → +10 Doctor
    [InlineData(PerkId.MrFixit, 12, 10)]              // Mr.Fixit → +10 Science
    [InlineData(PerkId.MrFixit, 13, 10)]              // Mr.Fixit → +10 Repair
    [InlineData(PerkId.MasterThief, 9, 15)]           // Master Thief → +15 Lockpick
    [InlineData(PerkId.MasterThief, 10, 15)]          // Master Thief → +15 Steal
    [InlineData(PerkId.Harmless, 10, 20)]             // Harmless → +20 Steal
    [InlineData(PerkId.Speaker, 14, 20)]              // Speaker → +20 Speech
    [InlineData(PerkId.Salesman, 15, 20)]             // Salesman → +20 Barter
    [InlineData(PerkId.Gambler, 16, 20)]              // Gambler → +20 Gambling
    [InlineData(PerkId.Survivalist, 17, 25)]          // Survivalist → +25 Outdoorsman
    [InlineData(PerkId.Ranger, 17, 15)]               // Ranger → +15 Outdoorsman
    [InlineData(PerkId.Thief, 8, 10)]                 // Thief → +10 Sneak
    [InlineData(PerkId.VaultCityTraining, 6, 5)]      // VC Training → +5 First Aid
    public void SkillModifierMatchesEngineTable(int perk, int skill, int expected)
    {
        var ranks = new int[PerkTable.Count];
        ranks[perk] = 1;
        Assert.Equal(expected, PerkRules.SkillModifier(skill, ranks));
        Assert.Equal(0, PerkRules.SkillModifier(0, ranks)); // unrelated skill (Small Guns) unaffected
    }

    [Fact]
    public void SkillModifierStacksOverlappingPerks()
    {
        var ranks = new int[PerkTable.Count];
        ranks[PerkId.Medic] = 1;             // +10 Doctor
        ranks[PerkId.LivingAnatomy] = 1;     // +10 Doctor
        ranks[PerkId.VaultCityTraining] = 1; // +5 Doctor
        Assert.Equal(25, PerkRules.SkillModifier(7, ranks));
    }

    // --- perkCanAdd gates ------------------------------------------------

    private static int[] Stats(int s = 6, int p = 6, int e = 6, int c = 6, int i = 6, int a = 6, int l = 6) =>
        [s, p, e, c, i, a, l];

    private static PerkData Perk(int maxRank = 1, int minLevel = 1, int[]? reqs = null,
        int param1 = -1, int value1 = 0, int paramMode = 0, int param2 = -1, int value2 = 0) =>
        new(0, 0, maxRank, minLevel, -1, 0, param1, value1, paramMode, param2, value2, reqs ?? new int[7]);

    [Fact]
    public void LevelGateBlocksBelowMinLevel()
    {
        PerkData perk = Perk(minLevel: 6);
        Assert.False(PerkRules.CanAdd(perk, null, 5, _ => 10, _ => 100, _ => 0));
        Assert.True(PerkRules.CanAdd(perk, null, 6, _ => 10, _ => 100, _ => 0));
    }

    [Fact]
    public void MaxRankCaps()
    {
        PerkData perk = Perk(maxRank: 2);
        var ranks = new int[PerkTable.Count];
        ranks[0] = 2;
        Assert.False(PerkRules.CanAdd(perk, ranks, 99, _ => 10, _ => 100, _ => 0));
        Assert.False(PerkRules.CanAdd(PerkTable.Get(0) with { MaxRank = -1 }, null, 99, _ => 10, _ => 100, _ => 0));
    }

    [Fact]
    public void SpecialRequirementsMinAndMax()
    {
        // positive req = minimum: ST 6 needed.
        PerkData min = Perk(reqs: Stats(s: 6, p: 0, e: 0, c: 0, i: 0, a: 0, l: 0));
        Assert.False(PerkRules.CanAdd(min, null, 99, s => s == 0 ? 5 : 10, _ => 0, _ => 0)); // ST 5 < 6
        Assert.True(PerkRules.CanAdd(min, null, 99, s => s == 0 ? 6 : 10, _ => 0, _ => 0));  // ST 6
        // negative req = "at most": LK at most 4 (req -5 → fail if LK >= 5).
        PerkData max = Perk(reqs: [0, 0, 0, 0, 0, 0, -5]);
        Assert.True(PerkRules.CanAdd(max, null, 99, s => s == 6 ? 4 : 10, _ => 0, _ => 0));  // LK 4 ok
        Assert.False(PerkRules.CanAdd(max, null, 99, s => s == 6 ? 5 : 10, _ => 0, _ => 0)); // LK 5 too high
    }

    [Fact]
    public void SkillParamGate()
    {
        // Param1 = skill 6 (First Aid), needs >= 40.
        PerkData perk = Perk(param1: 6, value1: 40);
        Assert.False(PerkRules.CanAdd(perk, null, 99, _ => 10, sk => sk == 6 ? 39 : 0, _ => 0));
        Assert.True(PerkRules.CanAdd(perk, null, 99, _ => 10, sk => sk == 6 ? 40 : 0, _ => 0));
    }

    [Fact]
    public void OrAndParamModes()
    {
        // OR (mode 1): skill A>=50 OR skill B>=50.
        PerkData or = Perk(param1: 1, value1: 50, paramMode: 1, param2: 2, value2: 50);
        Assert.True(PerkRules.CanAdd(or, null, 99, _ => 10, sk => sk == 2 ? 50 : 0, _ => 0));  // only B
        Assert.False(PerkRules.CanAdd(or, null, 99, _ => 10, _ => 0, _ => 0));                  // neither
        // AND (mode 2): both required.
        PerkData and = Perk(param1: 1, value1: 50, paramMode: 2, param2: 2, value2: 50);
        Assert.False(PerkRules.CanAdd(and, null, 99, _ => 10, sk => sk == 2 ? 50 : 0, _ => 0)); // only B
        Assert.True(PerkRules.CanAdd(and, null, 99, _ => 10, sk => 50, _ => 0));                // both
    }
}
