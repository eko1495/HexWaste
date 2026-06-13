using Hexwaste.Formats.Combat;

namespace Hexwaste.Formats.Tests;

public class CriticalTablesTests
{
    [Fact]
    public void GeneratedDataMatchesChecksum()
    {
        // Guards CriticalTables.g.cs against hand-corruption; regenerate via
        // tools/gen_critical_tables.py if the table legitimately changes.
        Assert.Equal(CriticalTables.DataChecksum, CriticalTables.ComputeChecksum());
    }

    [Fact]
    public void TableDimensionsAreFull()
    {
        Assert.Equal(19, CriticalTables.KillTypeCount);
        Assert.Equal(9, CriticalTables.LocationCount);
        Assert.Equal(6, CriticalTables.SeverityCount);
    }

    [Fact]
    public void ManHeadColumnMatchesSource()
    {
        // KILL_TYPE_MAN (0), HIT_LOCATION_HEAD (0), the six severities — verbatim
        // from combat.cc:191-198.
        Assert.Equal(new CriticalEffect(4, 0), CriticalTables.Lookup(0, 0, 0, false));
        Assert.Equal(new CriticalEffect(4, CriticalTables.DamBypass), CriticalTables.Lookup(0, 0, 1, false));
        // sev3: { 5, DAM_KNOCKED_DOWN | DAM_BYPASS, ... }
        CriticalEffect kd = CriticalTables.Lookup(0, 0, 3, false);
        Assert.Equal(5, kd.DamageMultiplier);
        Assert.True((kd.Flags & CriticalTables.DamKnockedDown) != 0);
        Assert.True((kd.Flags & CriticalTables.DamBypass) != 0);
        // sev5: { 6, DAM_DEAD, ... }
        CriticalEffect dead = CriticalTables.Lookup(0, 0, 5, false);
        Assert.Equal(6, dead.DamageMultiplier);
        Assert.True((dead.Flags & CriticalTables.DamDead) != 0);
    }

    [Fact]
    public void OutOfRangeKillTypeFallsBackToMan()
    {
        Assert.Equal(CriticalTables.Lookup(0, 0, 5, false), CriticalTables.Lookup(999, 0, 5, false));
    }

    [Fact]
    public void PlayerTableIsUsedForTheDude()
    {
        // The player table (combat.cc:1791) differs from the MAN table; just assert
        // it resolves and is independent of killType.
        CriticalEffect a = CriticalTables.Lookup(5, 0, 5, defenderIsDude: true);
        CriticalEffect b = CriticalTables.Lookup(0, 0, 5, defenderIsDude: true);
        Assert.Equal(a, b);
        Assert.True(a.DamageMultiplier > 0);
    }
}
