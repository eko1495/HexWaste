using Hexwaste.Formats.Map;
using Hexwaste.Formats.Sound;

namespace Hexwaste.Formats.Tests;

public class SfxNameTests
{
    [Fact]
    public void ComposesDoorNames()
    {
        Assert.Equal("SODOORSA", SfxName.Door(SfxName.SceneryAction.Open, (byte)'A'));
        Assert.Equal("SCDOORSB", SfxName.Door(SfxName.SceneryAction.Close, (byte)'b'));
        Assert.Equal("SODOORSA", SfxName.Door(SfxName.SceneryAction.Open, 0)); // missing -> 'A'
        Assert.Equal(@"sound\sfx\SODOORSA.acm", SfxName.Path("SODOORSA"));
    }
}

public class SoundRealGameDataTests
{
    [GameDataFact]
    public void DoorSfxFilesExistAndMapsHaveMusic()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        Assert.True(vfs.Exists(SfxName.Path("SODOORSA")), "SODOORSA.acm missing");
        Assert.True(vfs.Exists(SfxName.Path("FOOTSTEP")), "FOOTSTEP.acm missing");

        MapList maps = MapList.Load(vfs);
        Assert.False(string.IsNullOrEmpty(maps.GetMusic("artemple.map")));
        Assert.False(string.IsNullOrEmpty(maps.GetMusic("denbus1.map")));
    }
}
