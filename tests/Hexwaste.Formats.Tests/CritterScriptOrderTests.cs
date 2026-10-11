using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

public class CritterScriptOrderTests
{
    [GameDataFact]
    public void MapCritterSidsAreTheCritterSectionInFileOrder()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        MapFile map;
        using (Stream stream = vfs.OpenRead(@"maps\kladwtwn.map"))
            map = MapFile.Load(stream, protos);

        Assert.NotEmpty(map.CritterSids);
        Assert.All(map.CritterSids, sid => Assert.Equal(4, sid >> 24));
        Assert.Equal(map.ScriptsBySid.Keys.Where(sid => sid >> 24 == 4).Order(), map.CritterSids.Order());
    }
}
