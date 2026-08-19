using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>
/// Tests for <see cref="AiTargets"/>, ported from fallout2-ce src/combat_ai.cc
/// <c>aiFindAttackers()</c> (:1457-1525) and <c>_ai_find_nearest_team()</c> (:1397-1423).
/// AiTargets is a new, uncalled file (Task 1 of the ai_danger_source port) — these tests only
/// exercise the pure helpers in isolation; nothing in the engine wires them in yet.
/// </summary>
public class AiTargetsTests
{
    [Fact]
    public void FindAttackers_WhoHitMeSlot_ReturnsLivingCandidateThatHitSelf()
    {
        MapObject self = NewCritter(1, team: 0);
        MapObject attacker = NewCritter(2, team: 1);
        attacker.WhoHitMe = self;

        var result = AiTargets.FindAttackers(self, new[] { self, attacker });

        Assert.Same(attacker, result.WhoHitMe);
        Assert.Null(result.WhoHitFriend);
        Assert.Null(result.WhoHitByFriend);
    }

    [Fact]
    public void FindAttackers_WhoHitFriendSlot_ReturnsTheAttackerNotTheFriend()
    {
        // combat_ai.cc:1495-1506 — the friend slot stores the friend's whoHitMe (the actual
        // attacker), never the friend candidate itself.
        MapObject self = NewCritter(1, team: 0);
        MapObject friend = NewCritter(2, team: 0);
        MapObject attacker = NewCritter(3, team: 1);
        friend.WhoHitMe = attacker;

        var result = AiTargets.FindAttackers(self, new[] { self, friend, attacker });

        Assert.Null(result.WhoHitMe);
        Assert.Same(attacker, result.WhoHitFriend);
        Assert.NotSame(friend, result.WhoHitFriend);
        Assert.Null(result.WhoHitByFriend);
    }

    [Fact]
    public void FindAttackers_WhoHitByFriendSlot_ReturnsTheEnemyHitByASelfTeamMember()
    {
        MapObject self = NewCritter(1, team: 0);
        MapObject teammate = NewCritter(2, team: 0);
        MapObject enemy = NewCritter(3, team: 1);
        enemy.WhoHitMe = teammate;

        var result = AiTargets.FindAttackers(self, new[] { self, teammate, enemy });

        Assert.Null(result.WhoHitMe);
        Assert.Null(result.WhoHitFriend);
        Assert.Same(enemy, result.WhoHitByFriend);
    }

    [Fact]
    public void FindAttackers_SfallRule_OneCandidateFillsAtMostOneSlot()
    {
        // combat_ai.cc:1481-1482 (SFALL fix): without the `continue`, a single candidate can be
        // reported in more than one category. Construct an enemy whose whoHitMe == self: it
        // qualifies for the WhoHitMe slot (whoHitMe == critter) AND, because whoHitMe(self)'s
        // team == self's own team trivially, for the WhoHitByFriend slot too. It must land in
        // exactly one.
        MapObject self = NewCritter(1, team: 0);
        MapObject enemy = NewCritter(2, team: 1);
        enemy.WhoHitMe = self;

        var result = AiTargets.FindAttackers(self, new[] { self, enemy });

        Assert.Same(enemy, result.WhoHitMe);
        Assert.Null(result.WhoHitByFriend); // not double-booked into the third slot
    }

    [Fact]
    public void FindAttackers_DeadCandidatesAreSkipped()
    {
        MapObject self = NewCritter(1, team: 0);
        MapObject deadAttacker = NewCritter(2, team: 1);
        deadAttacker.WhoHitMe = self;
        deadAttacker.CombatResults = 0x80; // DAM_DEAD

        var result = AiTargets.FindAttackers(self, new[] { self, deadAttacker });

        Assert.Null(result.WhoHitMe);
        Assert.Null(result.WhoHitFriend);
        Assert.Null(result.WhoHitByFriend);
    }

    [Fact]
    public void FindAttackers_SelfIsNeverAssignedToASlot()
    {
        MapObject self = NewCritter(1, team: 0);
        self.WhoHitMe = self; // degenerate, but must never fill whoHitMe with self

        var result = AiTargets.FindAttackers(self, new[] { self });

        Assert.Null(result.WhoHitMe);
        Assert.Null(result.WhoHitFriend);
        Assert.Null(result.WhoHitByFriend);
    }

    [Fact]
    public void FindNearestTeam_SameTeamTrue_ReturnsNearestLivingTeammate()
    {
        MapObject self = NewCritter(1, team: 0);
        MapObject reference = NewCritter(2, team: 0);
        MapObject nearTeammate = NewCritter(3, team: 0);
        MapObject farTeammate = NewCritter(4, team: 0);
        MapObject enemy = NewCritter(5, team: 1);

        // pre-distance-sorted: nearTeammate before farTeammate/enemy.
        var sorted = new[] { nearTeammate, enemy, farTeammate };

        MapObject? result = AiTargets.FindNearestTeam(self, reference, sameTeam: true, sorted);

        Assert.Same(nearTeammate, result);
    }

    [Fact]
    public void FindNearestTeam_SameTeamFalse_ReturnsNearestLivingEnemy()
    {
        MapObject self = NewCritter(1, team: 0);
        MapObject reference = NewCritter(2, team: 0);
        MapObject teammate = NewCritter(3, team: 0);
        MapObject nearEnemy = NewCritter(4, team: 1);
        MapObject farEnemy = NewCritter(5, team: 1);

        var sorted = new[] { teammate, nearEnemy, farEnemy };

        MapObject? result = AiTargets.FindNearestTeam(self, reference, sameTeam: false, sorted);

        Assert.Same(nearEnemy, result);
    }

    [Fact]
    public void FindNearestTeam_ReturnsNullWhenNoneQualify()
    {
        MapObject self = NewCritter(1, team: 0);
        MapObject reference = NewCritter(2, team: 0);
        MapObject deadTeammate = NewCritter(3, team: 0);
        deadTeammate.CombatResults = 0x80; // DAM_DEAD

        MapObject? result = AiTargets.FindNearestTeam(self, reference, sameTeam: true, new[] { self, deadTeammate });

        Assert.Null(result);
    }

    private static MapObject NewCritter(int id, int team) => new()
    {
        Id = id, HexTile = id, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = 0x01000000, Flags = 0, Pid = 0x01000001, Sid = -1,
        Team = team,
    };
}
