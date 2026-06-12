using FalloutPoc.Formats.Int;
using FalloutPoc.Formats.Map;
using FalloutPoc.Formats.Proto;

namespace FalloutPoc.Formats.Tests;

public class DialogRealGameDataTests
{
    [GameDataFact]
    public void ConversesWithADenNpcThroughRealScripts()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        var host = new ScriptHost(vfs, ScriptList.Load(vfs), protos) { NameResolver = _ => "npc" };

        using Stream stream = vfs.OpenRead(@"maps\denbus1.map");
        MapFile map = MapFile.Load(stream, protos);

        // Find any critter whose script opens a real dialog.
        ScriptHost.DialogSession? session = null;
        foreach (MapObject critter in map.Elevations[0]!.Objects
            .Where(o => Fid.PidType(o.Pid) == (int)ObjectType.Critter && o.Sid != -1))
        {
            session = host.StartDialog(critter, map, null, out _);
            if (session is not null)
                break;
        }

        Assert.NotNull(session);
        Assert.True(session.Active);
        Assert.False(string.IsNullOrWhiteSpace(session.Reply), "dialog opened with an empty reply");
        Assert.NotEmpty(session.Options);
        Assert.All(session.Options, o => Assert.False(string.IsNullOrWhiteSpace(o)));

        // Walk the tree: always pick the first option; must terminate cleanly
        // well within a bounded number of rounds.
        int rounds = 0;
        while (session.Active && rounds++ < 25)
            session.Choose(0);

        Assert.False(session.Active, "conversation did not terminate");
        Assert.True(rounds < 25, "conversation looped");
    }

    [GameDataFact]
    public void FloaterOnlyNpcsYieldLinesWithoutADialog()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        var host = new ScriptHost(vfs, ScriptList.Load(vfs), protos);

        using Stream stream = vfs.OpenRead(@"maps\klamall.map");
        MapFile map = MapFile.Load(stream, protos);

        // Klamath citizens are the archetypal float-text NPCs.
        int floaterNpcs = 0;
        foreach (MapObject critter in map.Elevations[0]!.Objects
            .Where(o => Fid.PidType(o.Pid) == (int)ObjectType.Critter && o.Sid != -1))
        {
            ScriptHost.DialogSession? session = host.StartDialog(critter, map, null, out var floaters);
            if (session is null && floaters.Count > 0)
                floaterNpcs++;
        }

        Assert.True(floaterNpcs > 0, "no floater-only NPCs found on klamall");
    }
}
