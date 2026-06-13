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
