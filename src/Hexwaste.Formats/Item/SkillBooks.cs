namespace Hexwaste.Formats.Item;

/// <summary>
/// Skill books, ported from fallout2-ce src/item.cc booksInitVanilla (:3280) +
/// src/proto_instance.cc _obj_use_book (:754). Reading a book raises the matching skill by a
/// diminishing-returns amount (more for a low skill, nothing once it reaches 100), boosted by the
/// Comprehension perk, at a game-time cost that shrinks with Intelligence.
///
/// Pure: the pid→skill table + the gain/time math. The viewer reads the dude's EFFECTIVE skill % to
/// size the gain but writes the BASE skill-points array (the engine's skillGetValue / skillAddForce
/// split), refuses in combat, and advances the clock.
/// </summary>
public static class SkillBooks
{
    // ported from fallout2-ce src/item.cc booksInitVanilla (:3283) — {bookPid → (skill index, proto.msg id)}.
    // Skill indices are the skill_defs.h enum (SMALL_GUNS=0, FIRST_AID=6, SCIENCE=12, REPAIR=13, OUTDOORSMAN=17).
    private static readonly Dictionary<int, (int Skill, int MessageId)> Books = new()
    {
        [73] = (12, 802),  // Big Book of Science → Science
        [76] = (13, 803),  // Dean's Electronics  → Repair
        [80] = (6, 804),   // First Aid Book      → First Aid
        [86] = (17, 806),  // Scout Handbook      → Outdoorsman
        [102] = (0, 805),  // Guns and Bullets    → Small Guns
    };

    /// <summary>The skill a book trains + its proto.msg "you learn…" id, or false if the pid isn't a
    /// book (booksGetInfo, item.cc:3354 → the use falls through to other misc-item handling).</summary>
    public static bool TryGet(int pid, out int skill, out int messageId)
    {
        if (Books.TryGetValue(pid, out (int Skill, int MessageId) b))
        {
            (skill, messageId) = b;
            return true;
        }
        (skill, messageId) = (-1, -1);
        return false;
    }

    /// <summary>The skill-point gain from reading a book (proto_instance.cc:776): (100 − effective)/10
    /// — diminishing returns that hit ZERO once the effective skill reaches 100 (the de-facto cap; the
    /// engine's increase&lt;=0 branch grants nothing). The Comprehension perk multiplies the gain by 1.5
    /// (150×/100, floored). <paramref name="currentEffectiveSkill"/> is the skillGetValue %.</summary>
    public static int Increase(int currentEffectiveSkill, bool hasComprehension)
    {
        int increase = (100 - currentEffectiveSkill) / 10;
        if (increase <= 0)
            return 0;
        if (hasComprehension)
            increase = 150 * increase / 100;
        return increase;
    }

    /// <summary>Game-seconds a read costs (proto_instance.cc:792): 3600 × (11 − Intelligence) — INT 10
    /// reads in an hour, INT 1 takes ten.</summary>
    public static int ReadSeconds(int intelligence) => 3600 * (11 - intelligence);
}
