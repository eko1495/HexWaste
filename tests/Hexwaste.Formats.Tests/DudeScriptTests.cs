using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

public class DudeScriptTests
{
    [GameDataFact]
    public void DudeScriptLocalVarsSurviveMapChange()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        var host = new ScriptHost(vfs, ScriptList.Load(vfs), protos);
        var dude = new MapObject { Id = 18000, HexTile = 16908, X = 0, Y = 0, Frame = 0, Rotation = 0, Fid = 0x01000000, Flags = 0, Pid = 0x01000001, Sid = -1 };

        MapFile Load(string name)
        {
            using Stream stream = vfs.OpenRead($@"maps\{name}");
            return MapFile.Load(stream, protos);
        }

        MapFile first = Load("denbus1.map");
        host.BindDudeScript(first, dude);
        Assert.Equal(ScriptHost.DudeSid, dude.Sid);
        Assert.True(host.SetObjectLocalVar(first, dude, 0, 7)); // obj_dude.int has 5 LVARs

        MapFile second = Load("denbus2.map");
        host.BindDudeScript(second, dude);
        Assert.Equal(7, host.ReactionValue(second, dude)); // ReactionValue reads LVAR[0]
    }
}
