using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Party;

/// <summary>The persisted per-companion level-up bookkeeping — the engine's
/// PartyMemberLevelUpInfo (party_member.cc:61-65), three ints saved per member
/// (party_member.cc:520-538). Mutable: <see cref="PartyLevelUp.IncLevel"/> advances it
/// in place, exactly like the engine mutates _partyMemberLevelUpInfoList[idx].</summary>
public sealed class PartyLevelUpState
{
    /// <summary>The companion's level = how many upgrade stages it has taken (0 = base).</summary>
    public int Level;
    /// <summary>How many PC level-ups have happened with this member in the party.</summary>
    public int NumLevelUps;
    /// <summary>The last advance was an "early" probability roll, so we skip until the
    /// next levelMod==0 boundary before allowing another (party_member.cc:1519-1526).</summary>
    public int IsEarly;
}

/// <summary>
/// Companion proto level-up decision logic, ported from fallout2-ce src/party_member.cc
/// _partyMemberIncLevels() (the per-member body, party_member.cc:1487-1539). Pure: the
/// caller supplies the description, the mutable <see cref="PartyLevelUpState"/>, the
/// current PC level, and an <see cref="ICombatRng"/>; the function decides whether the
/// member advances one stage on this PC level-up and returns the stage proto PID to swap
/// to (or null = no advance). The engine calls this once per member each time the player
/// crosses a level (from stat.cc:789 pcAddExperienceWithOptions).
/// </summary>
public static class PartyLevelUp
{
    /// <summary>
    /// Evaluate one PC level-up for one companion. Mutates <paramref name="state"/> and
    /// returns the upgrade-stage proto PID if the member advances, else null.
    /// </summary>
    public static int? IncLevel(PartyMemberDescription desc, PartyLevelUpState state, int pcLevel, ICombatRng rng)
    {
        if (desc.LevelUpEvery == 0)                          // party_member.cc:1487 (level_pids = -1 members)
            return null;
        if (pcLevel < desc.LevelMinimum)                     // :1501
            return null;
        if (state.Level >= desc.LevelPids.Count)             // :1507 (level_pids_num cap)
            return null;

        state.NumLevelUps++;                                 // :1511
        int levelMod = state.NumLevelUps % desc.LevelUpEvery; // :1513

        // A previous "early" advance skips evaluations until the next cycle boundary.
        if (state.IsEarly != 0)                              // :1521
        {
            if (levelMod == 0)
                state.IsEarly = 0;
            return null;
        }

        // levelMod==0 advances unconditionally (no roll drawn). Otherwise an EARLY
        // advance is rolled with probability 1 − levelMod/level_up_every. NOTE the
        // engine's comparison is INVERTED: randomBetween(0,100) > threshold means DO
        // NOT advance (party_member.cc:1528) — randomBetween is inclusive, so the
        // ICombatRng exclusive upper bound is 101.
        if (levelMod != 0 && rng.Next(0, 101) > 100 * levelMod / desc.LevelUpEvery)
            return null;

        state.Level++;                                       // :1532
        if (levelMod != 0)                                   // :1533
            state.IsEarly = 1;

        // DIVERGENCE from party_member.cc:1537: the engine indexes level_pids[level]
        // AFTER the increment, which skips level_pids[0] entirely and reads one element
        // past the array on the final stage (a benign-in-practice but real OOB quirk —
        // copyLevelInfo is never called at recruit, only here). We apply level_pids in
        // order — stage `level-1` after the increment — so every listed proto is used
        // exactly once, capped at the list length. Sane, safe, and the evident intent.
        return desc.LevelPids[state.Level - 1];
    }
}
