using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Combat;

/// <summary>
/// Pure target-lookup helpers ported from fallout2-ce src/combat_ai.cc, used by the
/// <c>_ai_danger_source</c> retaliation-target port. Both take a list the caller has already
/// sorted nearest-first — the reference calls <c>_ai_sort_list_distance</c> at the top of each
/// helper before scanning, so a pre-sorted list is exactly equivalent, and it keeps these
/// functions host-free and unit-testable. PURE — nothing calls these yet (Task 1 of the port).
/// </summary>
public static class AiTargets
{
    private const int DamDead = 0x80; // DAM_DEAD, obj_types.h

    /// <summary>ported from fallout2-ce src/combat_ai.cc aiFindAttackers() (:1457-1525): scans
    /// <paramref name="distanceSorted"/> (nearest first, self excluded by identity) for up to
    /// three retaliation candidates:
    /// <list type="bullet">
    /// <item>WhoHitMe — a living candidate whose whoHitMe is <paramref name="self"/> (:1487-1493).</item>
    /// <item>WhoHitFriend — a living candidate on self's own team whose whoHitMe is a living
    /// cross-team critter (not self); the slot stores that whoHitMe (the actual attacker), not
    /// the friend candidate (:1495-1506).</item>
    /// <item>WhoHitByFriend — a living candidate on a different team whose whoHitMe is on self's
    /// team; the slot stores the candidate itself (:1508-1518).</item>
    /// </list>
    /// Each candidate can fill AT MOST ONE slot: the reference's per-match `continue` is a
    /// documented SFALL fix for one candidate being reported in more than one category
    /// (SFALL fix, combat_ai.cc:1481-1482) — ported here as the same continue-after-match
    /// control flow.</summary>
    public static (MapObject? WhoHitMe, MapObject? WhoHitFriend, MapObject? WhoHitByFriend)
        FindAttackers(MapObject self, IReadOnlyList<MapObject> distanceSorted)
    {
        MapObject? whoHitMe = null;
        MapObject? whoHitFriend = null;
        MapObject? whoHitByFriend = null;

        int team = self.Team;
        int foundTargetCount = 0;

        for (int index = 0; foundTargetCount < 3 && index < distanceSorted.Count; index++)
        {
            MapObject candidate = distanceSorted[index];
            if (candidate == self)
            {
                continue;
            }

            if (whoHitMe == null)
            {
                if (!candidate.IsDead && candidate.WhoHitMe == self)
                {
                    foundTargetCount++;
                    whoHitMe = candidate;
                    continue;
                }
            }

            if (whoHitFriend == null)
            {
                if (team == candidate.Team)
                {
                    MapObject? whoHitCandidate = candidate.WhoHitMe;
                    if (whoHitCandidate != null
                        && whoHitCandidate != self
                        && team != whoHitCandidate.Team
                        && !whoHitCandidate.IsDead)
                    {
                        foundTargetCount++;
                        whoHitFriend = whoHitCandidate;
                        continue;
                    }
                }
            }

            if (whoHitByFriend == null)
            {
                if (candidate.Team != team && !candidate.IsDead)
                {
                    MapObject? whoHitCandidate = candidate.WhoHitMe;
                    if (whoHitCandidate != null && whoHitCandidate.Team == team)
                    {
                        foundTargetCount++;
                        whoHitByFriend = candidate;
                        continue;
                    }
                }
            }
        }

        return (whoHitMe, whoHitFriend, whoHitByFriend);
    }

    /// <summary>ported from fallout2-ce src/combat_ai.cc _ai_find_nearest_team() (:1397-1423):
    /// scans <paramref name="distanceSorted"/> (nearest first) for the first living critter,
    /// other than <paramref name="self"/>, on the same team as <paramref name="reference"/> when
    /// <paramref name="sameTeam"/> is true (reference flags bit 0x01), or on a different team when
    /// false (flags bit 0x02).</summary>
    public static MapObject? FindNearestTeam(MapObject self, MapObject reference,
        bool sameTeam, IReadOnlyList<MapObject> distanceSorted)
    {
        int team = reference.Team;

        foreach (MapObject candidate in distanceSorted)
        {
            if (self != candidate
                && !candidate.IsDead
                && ((!sameTeam && team != candidate.Team) || (sameTeam && team == candidate.Team)))
            {
                return candidate;
            }
        }

        return null;
    }
}
