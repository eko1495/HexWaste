using System.Text;
using Hexwaste.Formats;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

public class WorldmapFileTests
{
    private const string Sample = """
        [Data]
        Frequent=38%
        Uncommon=12%
        None=0%

        [Tile 0]
        art_idx=339
        encounter_difficulty=0
        0_0=Ocean,Fill_W,None,Uncommon,None,Fish_O
        1_3=Desert,No_Fill,Frequent,Frequent,Uncommon,Arro_M

        [Encounter Table 6]
        lookup_name=Arro_M
        maps=Mountain Encounter 1, Mountain Encounter 2
        enc_00=Chance:9%,Enc:(2-4) ARRO_War_Party AMBUSH Player
        enc_04=Chance:15%,Enc:(2-5) Bounty_Hunter_Low AMBUSH Player, If(Global(1) > 1) And If(Player(Level) < 7)

        [Encounter: ARRO_War_Party]
        type_00=ratio:30%, pid:16777418, Item:280(wielded), Item:(3-6)320, Script:618
        type_01=ratio:20%, pid:16777419, Item:280(wielded), Script:618
        position=wedge, spacing:2

        [Random Maps: Desert]
        map_00=Desert Encounter 1
        map_01=Desert Encounter 2
        """;

    [Fact]
    public void ParsesTilesTablesGroupsAndConditions()
    {
        WorldmapFile wm = WorldmapFile.Parse(Sample);

        Assert.Equal(12, wm.FrequencyPercent("Uncommon"));

        WorldTile tile = wm.Tiles.Single();
        Assert.Equal(0, tile.Difficulty);
        Subtile ocean = tile.Subtiles[0, 0];
        Assert.Equal("Ocean", ocean.Terrain);
        Assert.Equal("Fish_O", ocean.EncTable);
        Assert.Equal(12, ocean.AfternoonChance); // Uncommon → 12
        Assert.Equal(0, ocean.NightChance);       // None → 0
        Assert.Equal("Arro_M", tile.Subtiles[1, 3].EncTable);

        EncounterTable table = wm.Table("Arro_M")!;
        Assert.Equal(2, table.Maps.Count);
        Assert.Equal(2, table.Entries.Count);

        EncounterEntry e0 = table.Entries[0];
        Assert.Equal(9, e0.Chance);
        Assert.Equal("AMBUSH", e0.Situation);
        Assert.Equal(new EncounterSpawn(2, 4, "ARRO_War_Party"), e0.Spawns.Single());

        EncounterEntry e4 = table.Entries[1];
        Assert.Equal(2, e4.Conditions.Count);
        Assert.Contains(e4.Conditions, c => c is { Type: "Global", Param: 1, Op: ">", Value: 1 });
        Assert.Contains(e4.Conditions, c => c is { Type: "Player", Op: "<", Value: 7 });

        EncounterGroup grp = wm.Group("ARRO_War_Party")!;
        Assert.Equal(2, grp.Members.Count);
        GroupMember m0 = grp.Members[0];
        Assert.Equal(30, m0.Ratio);
        Assert.False(m0.Single);
        Assert.Equal(16777418, m0.Pid);
        Assert.Equal(617, m0.ScriptIndex);                 // Script:618 → 617
        Assert.Contains(m0.Items, it => it is { Pid: 280, Wielded: true });
        Assert.Contains(m0.Items, it => it is { Pid: 320, Min: 3, Max: 6 });
        Assert.Equal("wedge", grp.Formation);
        Assert.Equal(2, grp.Spacing);

        Assert.Equal(2, wm.RandomMaps["Desert"].Count);
    }

    [Fact]
    public void TerrainTypesParseAndDriveTravelDifficulty()
    {
        // Phase-17 M1: [Data] terrain_types -> the dot's per-pixel pacing. A subtile's
        // terrain maps to its difficulty (clamped >=1); off-grid falls back to 1.
        WorldmapFile wm = WorldmapFile.Parse("""
            [Data]
            terrain_types=Desert:1, Mountain:2, City:1, Ocean:1
            Forced=100%

            [Tile 0]
            encounter_difficulty=0
            0_0=Mountain,No_Fill,Forced,Forced,Forced,T
            2_2=Desert,No_Fill,Forced,Forced,Forced,T
            """);
        Assert.Equal(2, wm.TerrainDifficulties["Mountain"]);
        Assert.Equal(2, wm.TerrainTravelDifficultyAt(10, 10));     // subtile [0,0] = Mountain
        Assert.Equal(1, wm.TerrainTravelDifficultyAt(110, 110));   // subtile [2,2] = Desert
        Assert.Equal(1, wm.TerrainTravelDifficultyAt(99999, 99999)); // off-grid → clamp 1
    }

    [Fact]
    public void LowercaseIfConditionParsesLikeCapitalIf()
    {
        // Regression for the phase-16 M4 bug: ARRO_Spore_Plants' Dead member uses lowercase
        // "if (Rand(5%))" — a case-sensitive match dropped it, so the member spawned 100%
        // instead of 5%. Both casings must parse into the member's Conditions.
        WorldmapFile wm = WorldmapFile.Parse("""
            [Encounter: GRP]
            type_00=ratio:100%, pid:100, If(Rand(10%))
            type_01=Dead, pid:101, if (Rand(5%))
            position=huddle
            """);
        GroupMember capital = wm.Group("GRP")!.Members[0];
        GroupMember lower = wm.Group("GRP")!.Members[1];
        Assert.Contains(capital.Conditions ?? [], c => c is { Type: "Rand", Param: 10 });
        Assert.Contains(lower.Conditions ?? [], c => c is { Type: "Rand", Param: 5 });
    }

    [Fact]
    public void TableIndexAndEntryIndexDriveTheEncounterNameMessageId()
    {
        // Phase-16 M0: the encounter-name lookup is getmsg(3000 + 50*tableId + entryId),
        // where tableId is the table's load-order index (the "[Encounter Table N]" number)
        // and entryId is the enc_NN number itself (so a gap can't shift it).
        WorldmapFile wm = WorldmapFile.Parse(Sample);
        EncounterTable table = wm.Table("Arro_M")!;
        Assert.Equal(6, table.Index);

        EncounterEntry e0 = table.Entries[0]; // enc_00
        EncounterEntry e4 = table.Entries[1]; // enc_04 (note the gap)
        Assert.Equal(0, e0.EntryIndex);
        Assert.Equal(4, e4.EntryIndex);

        Assert.Equal(3300, new EncounterResult(table, e0).MessageId); // 3000 + 50*6 + 0
        Assert.Equal(3304, new EncounterResult(table, e4).MessageId); // 3000 + 50*6 + 4
    }

    [Fact]
    public void CountersExportOnlyChangedTablesAndImportRoundTrips()
    {
        const string w = """
            [Encounter Table 0]
            lookup_name=Limited
            enc_00=Chance:50%,Counter:2,Enc:(1-1) A AMBUSH Player
            enc_01=Chance:50%,Enc:(1-1) B AMBUSH Player

            [Encounter Table 1]
            lookup_name=Unlimited
            enc_00=Chance:50%,Enc:(1-1) C AMBUSH Player
            """;
        WorldmapFile wm = WorldmapFile.Parse(w);

        // Pristine (nothing consumed): export is empty — no redundant arrays.
        Assert.Empty(wm.ExportCounters());

        // Spend the one-shot: only the changed table is emitted, dense per-entry
        // (the -1 unlimited sibling tags along); the untouched table stays absent.
        wm.Table("Limited")!.Entries[0].Counter = 0;
        Dictionary<string, int[]> spent = wm.ExportCounters();
        Assert.Equal([0, -1], Assert.Contains("Limited", (IReadOnlyDictionary<string, int[]>)spent));
        Assert.DoesNotContain("Unlimited", spent.Keys);

        // Re-apply over a freshly parsed (pristine) map.
        WorldmapFile reload = WorldmapFile.Parse(w);
        Assert.Equal(2, reload.Table("Limited")!.Entries[0].Counter); // pristine again
        reload.ImportCounters(spent);
        Assert.Equal(0, reload.Table("Limited")!.Entries[0].Counter); // restored
        Assert.Equal(-1, reload.Table("Limited")!.Entries[1].Counter); // untouched

        // And a re-export from the reloaded map reproduces the same delta.
        Assert.Equal([0, -1], Assert.Contains("Limited", (IReadOnlyDictionary<string, int[]>)reload.ExportCounters()));
    }

    [Fact]
    public void ImportCountersIgnoresUnknownTablesAndOutOfRangeIndices()
    {
        const string w = """
            [Encounter Table 0]
            lookup_name=T
            enc_00=Chance:50%,Counter:3,Enc:(1-1) A AMBUSH Player
            """;
        WorldmapFile wm = WorldmapFile.Parse(w);
        // Unknown table skipped; the index past Entries.Count is ignored (no throw).
        wm.ImportCounters(new Dictionary<string, int[]>
        {
            ["Nonexistent"] = [9, 9],
            ["T"] = [1, 7, 7],
        });
        Assert.Single(wm.Table("T")!.Entries);
        Assert.Equal(1, wm.Table("T")!.Entries[0].Counter);
    }

    [Fact]
    public void ImportCountersIgnoresNullArrayFromHandEditedSave()
    {
        const string w = """
            [Encounter Table 0]
            lookup_name=T
            enc_00=Chance:50%,Counter:3,Enc:(1-1) A AMBUSH Player
            """;
        WorldmapFile wm = WorldmapFile.Parse(w);
        // A hand-edited/corrupt save with {"T": null} must degrade to pristine,
        // honouring the documented non-throwing contract (a present JSON null
        // deserializes to a null array, not a skipped key).
        wm.ImportCounters(new Dictionary<string, int[]> { ["T"] = null! });
        Assert.Equal(3, wm.Table("T")!.Entries[0].Counter);
    }

    [GameDataFact]
    public void RealWorldmapTxtParsesToTheKnownCounts()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        WorldmapFile wm = WorldmapFile.Parse(Encoding.Latin1.GetString(vfs.ReadAllBytes(@"data\worldmap.txt")));

        // Verified this session: 20 [Tile], 76 [Encounter Table], 110 [Encounter:].
        Assert.Equal(20, wm.Tiles.Count);
        Assert.Equal(76, wm.Tables.Count);
        Assert.Equal(110, wm.Groups.Count);

        // Frequency table from [Data].
        Assert.Equal(38, wm.FrequencyPercent("Frequent"));
        Assert.Equal(4, wm.FrequencyPercent("Rare"));

        // Every tile carries a full 7×6 = 42-subtile grid.
        foreach (WorldTile t in wm.Tiles)
            Assert.Equal(WorldmapFile.SubtileGridWidth * WorldmapFile.SubtileGridHeight,
                t.Subtiles.Cast<Subtile?>().Count(s => s is not null));

        // Spot-check the Arroyo mountain table (early-loop) + a real group.
        EncounterTable arroM = wm.Table("Arro_M")!;
        Assert.Contains(arroM.Entries, e => e.Spawns.Any(s => s.Group == "ARRO_War_Party"));
        EncounterGroup war = wm.Group("ARRO_War_Party")!;
        Assert.Contains(war.Members, m => m.Items.Any(i => i is { Pid: 280, Wielded: true })); // Sharp Spear
    }
}
