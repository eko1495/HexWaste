using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Map;

/// <summary>The outcome of a step that triggered an encounter: the chosen table
/// entry (spawn groups + situation + optional special map) and its table (the
/// random-map pool to pick a terrain map from).</summary>
public sealed record EncounterResult(EncounterTable Table, EncounterEntry Entry);

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
    /// <paramref name="playerLevel"/>/<paramref name="daysPlayed"/> feed conditions.</summary>
    public EncounterResult? Roll(int worldX, int worldY, int hhmm, Func<int, int> getGlobal,
        int playerLevel, int daysPlayed)
    {
        // Δ3 gate: no encounter until the party has moved ≥3 subtiles in BOTH axes
        // since the last encounter (two separate early returns in the engine).
        if (Math.Abs(worldX - _lastX) < 3 || Math.Abs(worldY - _lastY) < 3)
            return null;

        if (_world.SubtileAt(worldX, worldY) is not { } subtile)
            return null;

        int chance = DaypartChance(subtile, hhmm);
        if (chance <= 0 || _rng.Next(0, 101) >= chance) // randomBetween(0,100) < frequency
            return null;

        if (_world.Table(subtile.EncTable) is not { } table)
            return null;

        EncounterEntry? picked = Pick(table, getGlobal, playerLevel, hhmm, daysPlayed);
        if (picked is null)
            return null;

        _lastX = worldX;
        _lastY = worldY;
        return new EncounterResult(table, picked);
    }

    /// <summary>Weighted pick over the candidates that pass their conditions and have
    /// a non-zero counter — a uniform roll over the SUM of Chance weights, walked
    /// down (worldmap.cc:3557-3654; Luck shift skipped v1).</summary>
    private EncounterEntry? Pick(EncounterTable table, Func<int, int> getGlobal,
        int playerLevel, int hhmm, int daysPlayed)
    {
        var candidates = table.Entries
            .Where(e => e.Chance > 0 && e.Counter != 0
                && EncounterConditions.All(e.Conditions, _rng, getGlobal, playerLevel, hhmm, daysPlayed, 0))
            .ToList();
        int total = candidates.Sum(e => e.Chance);
        if (total <= 0)
            return null;

        int roll = _rng.Next(0, total);
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
