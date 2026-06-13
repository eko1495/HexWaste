using Hexwaste.Formats;
using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

public class CompanionTests
{
    private static MapObject Critter(int pid, bool dead = false, bool hidden = false) => new()
    {
        Id = -1, HexTile = 100, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = Fid.Build(ObjectType.Critter, 1, 0, 0), Flags = hidden ? 0x01 : 0, Pid = pid,
        CombatResults = dead ? 0x80 : 0,
    };

    [Fact]
    public void PartyMemberCountIsOnePlusLiveVisibleCritters()
    {
        // metarule(16) = 1 (the dude, slot 0) + live, visible, recruited critters.
        var members = new List<MapObject>
        {
            Critter(0x0100000B),               // live critter → counts
            Critter(0x0100000C),               // live critter → counts
            Critter(0x0100000D, dead: true),   // dead → excluded (party_member.cc:900)
            Critter(0x0100000E, hidden: true), // hidden → excluded
            Critter(0x00000029),               // non-critter pid (item) → excluded
        };

        Assert.Equal(3, ScriptHost.PartyMemberCount(members)); // dude + 2 live critters
        Assert.Equal(1, ScriptHost.PartyMemberCount([]));      // empty roster = just the dude
    }
}
