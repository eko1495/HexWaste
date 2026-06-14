using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

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

        // Walk the tree picking the LAST option each round — conventionally the
        // "goodbye" exit — which must terminate cleanly within a bounded number of
        // rounds. (Picking the FIRST option no longer terminates now that multi-round
        // dialog works — #10 M0 — so the last-option goodbye is the reliable exit.)
        int rounds = 0;
        while (session.Active && rounds++ < 50)
            session.Choose(session.Options.Count - 1);

        Assert.False(session.Active, "conversation did not terminate via the goodbye option");
        Assert.True(rounds < 50, "conversation looped");
    }

    [GameDataFact]
    public void DialogContinuesPastTheFirstChoice()
    {
        // The multi-round regression (#10 M0): a non-blocking gsay_end means
        // talk_p_proc's trailing end_dialogue used to set a sticky SessionEnded that
        // killed the first Choose. Metzger (dcMetzge.int, scripts.lst index 45, on
        // denbus2) has a deep tree — choosing a continuing option must advance to a
        // genuinely new round, not end the conversation.
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        var host = new ScriptHost(vfs, ScriptList.Load(vfs), protos) { NameResolver = _ => "Metzger" };

        using Stream stream = vfs.OpenRead(@"maps\denbus2.map");
        MapFile map = MapFile.Load(stream, protos);
        MapObject metzger = map.Elevations[0]!.Objects.First(o =>
            o.Sid != -1 && map.ScriptsBySid.TryGetValue(o.Sid, out MapScriptRecord? r) && r.ScriptListIndex == 45);

        ScriptHost.DialogSession? session = host.StartDialog(metzger, map, null, out _);
        Assert.NotNull(session);
        string firstReply = session!.Reply;
        Assert.NotEmpty(session.Options);

        // A continuing (non-goodbye) option advances the conversation.
        Assert.True(session.Choose(0));
        Assert.True(session.Active, "dialog ended after the first choice (the multi-round bug)");
        Assert.NotEmpty(session.Options);
        Assert.NotEqual(firstReply, session.Reply);

        // The last option ("Never mind. Bye.") still terminates cleanly.
        ScriptHost.DialogSession? bye = host.StartDialog(metzger, map, null, out _);
        Assert.NotNull(bye);
        bye!.Choose(bye.Options.Count - 1);
        Assert.False(bye.Active, "the goodbye option did not end the conversation");
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
