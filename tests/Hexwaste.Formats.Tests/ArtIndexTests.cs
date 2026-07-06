using Hexwaste.Formats.Art;
using Hexwaste.Formats.Frm;

namespace Hexwaste.Formats.Tests;

public class AnimationCodeTests
{
    [Theory]
    [InlineData(0, 0, 'a', 'a')] // stand, unarmed -> "aa"
    [InlineData(1, 0, 'a', 'b')] // walk, unarmed -> "ab"
    [InlineData(19, 0, 'a', 't')] // running -> "at"
    [InlineData(0, 1, 'd', 'a')] // stand with knife -> "da"
    [InlineData(1, 6, 'i', 'b')] // walk with SMG -> "ib"
    [InlineData(16, 0, 'a', 'q')] // throw punch -> "aq"
    [InlineData(20, 0, 'b', 'a')] // fall back (first knockdown) -> "ba"
    [InlineData(48, 0, 'r', 'a')] // fall back single-frame -> "ra"
    [InlineData(38, 1, 'd', 'c')] // take out knife -> "dc"
    [InlineData(13, 0, 'a', 'n')] // dodge unarmed -> "an"
    [InlineData(13, 2, 'e', 'e')] // dodge with club -> "ee"
    public void ComposesAnimationCodes(int anim, int weapon, char expectedWeaponChar, char expectedAnimChar)
    {
        Assert.Equal((expectedWeaponChar, expectedAnimChar), ArtIndex.GetAnimationCode(anim, weapon));
    }
}

public class CritterArtRealGameDataTests
{
    [GameDataFact]
    public void CritterShouldRunParsesTheListFlags()
    {
        // P117: the critters.lst third comma field is the AI run gate (art.cc artInit :238-251
        // + artCritterFidShouldRun :894). Empirical flags confirmed live via --npc-run/--npc-walk
        // on modmain (Miria fid index 0x24 runs; Davin index 0x30 does not).
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var art = new ArtIndex(vfs);

        Assert.True(art.CritterShouldRun(Fid.Build(ObjectType.Critter, 0x24)));
        Assert.False(art.CritterShouldRun(Fid.Build(ObjectType.Critter, 0x30)));
        Assert.False(art.CritterShouldRun(Fid.Build(ObjectType.Scenery, 1))); // non-critters never run
    }

    [GameDataFact]
    public void CritterAliasParsesTheSecondListField()
    {
        // P119 (called-shot window): the critters.lst 2nd comma field is the hit-location
        // name-set alias (art.cc artInit :233-245 + _art_alias_num :888). Numeric fixtures read
        // from the shipped list: index 1 aliases 21; index 47 (a two-field line) aliases 11;
        // index 59 (the radscorpion seen live via --aim-open) aliases itself; the bare
        // comma-less line 1 (index 0) falls back to the "hmwarr" row (index 62, the
        // _art_vault_guy_num default, art.cc:224).
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var art = new ArtIndex(vfs);

        Assert.Equal(21, art.CritterAlias(Fid.Build(ObjectType.Critter, 1)));
        Assert.Equal(11, art.CritterAlias(Fid.Build(ObjectType.Critter, 47)));
        Assert.Equal(59, art.CritterAlias(Fid.Build(ObjectType.Critter, 59)));
        Assert.Equal(62, art.CritterAlias(Fid.Build(ObjectType.Critter, 0)));
        Assert.Equal(0, art.CritterAlias(Fid.Build(ObjectType.Scenery, 1))); // non-critters
    }

    [GameDataFact]
    public void ElevatorInterfaceFrmsAllResolve()
    {
        // P119: every FRM the elevator tables reference must exist in art\intrface (a bad list
        // index would silently drop the picker to the text fallback), and the gauge strip must
        // slice cleanly into its 13 sub-images (elevator.cc:384 divides the height by 13).
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var art = new ArtIndex(vfs);

        var ids = new HashSet<int>
        {
            Map.ElevatorTables.ButtonDownFrmId,
            Map.ElevatorTables.ButtonUpFrmId,
            Map.ElevatorTables.GaugeFrmId,
        };
        foreach ((int bg, int panel) in Map.ElevatorTables.Backgrounds)
        {
            ids.Add(bg);
            if (panel >= 0)
                ids.Add(panel);
        }
        foreach (int id in ids)
        {
            string path = art.GetFrmPath(Fid.Build(ObjectType.Interface, id));
            Assert.True(vfs.Exists(path), $"interface FRM {id} -> {path} not found");
        }

        FrmFile gauge = FrmFile.Load(vfs.ReadAllBytes(
            art.GetFrmPath(Fid.Build(ObjectType.Interface, Map.ElevatorTables.GaugeFrmId))));
        Assert.Equal(0, gauge.Directions[0][0].Height % 13);
    }

    [GameDataFact]
    public void ResolvesAndLoadsStandingCritterFrm()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var artIndex = new ArtIndex(vfs);

        // Critter index 4 = hfjmps (vault jumpsuit female) in critters.lst; stand unarmed.
        int fid = Fid.Build(ObjectType.Critter, 4, 0, 0);
        string path = artIndex.GetFrmPath(fid);
        Assert.EndsWith("aa.frm", path);
        Assert.True(vfs.Exists(path), $"{path} not found");

        FrmFile frm = FrmFile.Load(vfs.ReadAllBytes(path));
        // Standing critters have art for all 6 directions.
        var distinct = new HashSet<FrmFrame[]>(frm.Directions);
        Assert.Equal(6, distinct.Count);
    }
}

public class HeadArtRealGameDataTests
{
    // P87: talking-head FRM names compose like critters — heads.lst base + _head1(emotion) + _head2(kind),
    // with a fidget number for the 'f' kind. heads.lst index 3 = elder (reser,mrcus,myron,elder,...).
    [GameDataFact]
    public void ResolvesNeutralFidgetHeadFrm()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var artIndex = new ArtIndex(vfs);

        // anim 4 = _head1 'n' + _head2 'f' (neutral fidget), fidget #1 -> ELDERNF1.FRM
        string path = artIndex.GetFrmPath(Fid.Build(ObjectType.Head, 3, animType: 4, weaponCode: 1));
        Assert.EndsWith(@"heads\eldernf1.frm", path.ToLowerInvariant());
        Assert.True(vfs.Exists(path), $"{path} not found");
        Assert.True(FrmFile.Load(vfs.ReadAllBytes(path)).Directions[0].Length > 0);
    }

    [GameDataFact]
    public void ResolvesNeutralTalkHeadFrm()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var artIndex = new ArtIndex(vfs);

        // anim 10 = _head1 'n' + _head2 'p' (neutral phoneme/talk) -> ELDERNP.FRM (no fidget number)
        string path = artIndex.GetFrmPath(Fid.Build(ObjectType.Head, 3, animType: 10));
        Assert.EndsWith(@"heads\eldernp.frm", path.ToLowerInvariant());
        Assert.True(vfs.Exists(path), $"{path} not found");
    }
}
