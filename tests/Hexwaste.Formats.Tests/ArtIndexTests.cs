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
