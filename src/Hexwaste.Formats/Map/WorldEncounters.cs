using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Map;

/// <summary>The outcome of a step that triggered an encounter: the chosen table
/// entry (spawn groups + situation + optional special map) and its table (the
/// random-map pool to pick a terrain map from).</summary>
public sealed record EncounterResult(EncounterTable Table, EncounterEntry Entry)
{
    /// <summary>The worldmap.msg id of this encounter's display name
    /// (worldmap.cc:3511 getmsg(3000 + 50*encounterTableId + encounterEntryId); the
    /// table id is its load-order index, the entry id its position in the table).</summary>
    public int MessageId => 3000 + 50 * Table.Index + Entry.EntryIndex;
}

/// <summary>Game difficulty — skews the encounter occurrence frequency and the
/// weighted pick (phase-10 #12). Normal is the no-op default.</summary>
public enum GameDifficulty { Easy, Normal, Hard }

/// <summary>
/// The random-encounter roll/pick chain, ported from fallout2-ce
/// src/worldmap.cc (wmRndEncounterOccurred :3322 / wmRndEncounterPick :3557 /
/// wmEvalConditional :4096). Pure + seeded off <see cref="ICombatRng"/> so travel
/// gets golden transcripts like combat. The v1 cut SKIPS (all confirmed
/// skippable for the Arroyo→Klamath→Den loop): Horrigan, sfall hooks,
/// outdoorsman-avoid, the special-encounter circle pin, perks/Luck, and the
/// difficulty skew. See docs/phase10-research-report.md M1/M2.
/// </summary>
public sealed class WorldEncounters
{
    private readonly WorldmapFile _world;
    private readonly ICombatRng _rng;

    // The position of the last encounter (or travel start) — the Δ3 gate measures
    // from here; it resets only after an encounter fires (worldmap.cc:3331-3337,3501).
    private int _lastX;
    private int _lastY;

    public WorldEncounters(WorldmapFile world, ICombatRng rng, int startX, int startY)
    {
        _world = world;
        _rng = rng;
        _lastX = startX;
        _lastY = startY;
    }

    /// <summary>The day-part the subtile encounter % is read from
    /// (worldmap.cc:3403-3413). <paramref name="hhmm"/> is the clock's HHMM hour.</summary>
    public static int DaypartChance(Subtile s, int hhmm) =>
        hhmm >= 1800 || hhmm < 600 ? s.NightChance
        : hhmm >= 1200 ? s.AfternoonChance
        : s.MorningChance;

    /// <summary>Roll one travel step at a worldmap pixel position. Returns the chosen
    /// encounter, or null for none. <paramref name="getGlobal"/> reads a GVAR;
    /// <paramref name="playerLevel"/>/<paramref name="daysPlayed"/> feed conditions.
    /// <paramref name="luck"/> shifts the weighted pick (Luck 5 = none), the difficulty
    /// skews the occurrence frequency, and <paramref name="outdoorsman"/> can detect +
    /// avoid the encounter — phase-10 #12 (defaults are no-op: Luck 5, Normal, 0).</summary>
    public EncounterResult? Roll(int worldX, int worldY, int hhmm, Func<int, int> getGlobal,
        int playerLevel, int daysPlayed, int luck = 5, int outdoorsman = 0,
        GameDifficulty difficulty = GameDifficulty.Normal)
    {
        // Δ3 gate: no encounter until the party has moved ≥3 subtiles in BOTH axes
        // since the last encounter (two separate early returns in the engine).
        if (Math.Abs(worldX - _lastX) < 3 || Math.Abs(worldY - _lastY) < 3)
            return null;

        if (_world.SubtileAt(worldX, worldY) is not { } subtile)
            return null;

        // Difficulty skew on the occurrence frequency (worldmap.cc:3404-3414): Easy makes
        // encounters rarer, Hard more common, by freq/15. Normal = unchanged.
        int frequency = DaypartChance(subtile, hhmm);
        if (frequency is > 0 and < 100)
        {
            int modifier = frequency / 15;
            frequency += difficulty switch
            {
                GameDifficulty.Easy => -modifier,
                GameDifficulty.Hard => modifier,
                _ => 0,
            };
        }

        int chance = _rng.Next(0, 101); // randomBetween(0,100)
        if (frequency <= 0 || chance >= frequency)
            return null;

        if (_world.Table(subtile.EncTable) is not { } table)
            return null;

        EncounterEntry? picked = Pick(table, getGlobal, playerLevel, hhmm, daysPlayed, luck, difficulty);
        if (picked is null)
            return null;

        // Outdoorsman-avoid (worldmap.cc:3454-3519): a high-Outdoorsman party detects the
        // encounter ahead and steers around it. The engine pops a yes/no dialog on detect;
        // here detect == avoid (XP + motion-sensor skipped). The Δ3 anchor still resets on
        // an avoided encounter, exactly like the engine (:3501-3502), so the next step
        // doesn't immediately re-roll.
        _lastX = worldX;
        _lastY = worldY;
        int detect = Math.Min(outdoorsman, 95) + _world.TileDifficultyAt(worldX, worldY);
        if (_rng.Next(1, 101) < detect)
            return null;

        return new EncounterResult(table, picked);
    }

    /// <summary>Weighted pick over the candidates that pass their conditions and have a
    /// non-zero counter — a uniform roll over the SUM of Chance weights plus the Luck-5
    /// shift and the ±5 difficulty nudge (clamped), walked down (worldmap.cc:3557-3654;
    /// perks skipped — no perk system).</summary>
    private EncounterEntry? Pick(EncounterTable table, Func<int, int> getGlobal,
        int playerLevel, int hhmm, int daysPlayed, int luck, GameDifficulty difficulty)
    {
        var candidates = table.Entries
            .Where(e => e.Chance > 0 && e.Counter != 0
                && EncounterConditions.All(e.Conditions, _rng, getGlobal, playerLevel, hhmm, daysPlayed, 0))
            .ToList();
        int total = candidates.Sum(e => e.Chance);
        if (total <= 0)
            return null;

        int roll = _rng.Next(0, total) + (luck - 5);
        roll += difficulty switch { GameDifficulty.Easy => 5, GameDifficulty.Hard => -5, _ => 0 };
        roll = Math.Clamp(roll, 0, total);
        foreach (EncounterEntry e in candidates)
        {
            roll -= e.Chance;
            if (roll < 0)
            {
                if (e.Counter > 0)
                    e.Counter--; // one-shot budget
                return e;
            }
        }
        return candidates[^1]; // float-safety fallthrough
    }
}
