using Hexwaste.Formats;
using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

public class TaggedSkillTests
{
    private static MapObject Dude() => new()
    {
        Id = -1, HexTile = 100, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = 0x01000000, Flags = 0, Pid = 0x01000001, Sid = -1,
    };

    [GameDataFact]
    public void CombatPremadeGetsTagBonusOnSmallGuns()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        using Stream s = vfs.OpenRead(@"premade\combat.gcd");
        GcdFile gcd = GcdFile.Load(s); // Narg: AG 4, tags Small Guns(0)/Melee(4)/Throwing(5)

        var dude = Dude();
        var untagged = new CritterState(dude, gcd.Stats);                  // NPC view (no tags)
        var tagged = new CritterState(dude, gcd.Stats, gcd.TaggedSkills);  // dude view

        int ag = gcd.Stats.BaseStats[5];  // STAT_AGILITY
        int st = gcd.Stats.BaseStats[0];  // STAT_STRENGTH

        // Untagged value is the plain engine formula (base points are 0 in premades).
        Assert.Equal(5 + 4 * ag + gcd.Stats.Skills[0], untagged.SmallGunsSkill);
        // Tag adds the spent points again (0 here) plus a flat +20.
        Assert.Equal(untagged.SmallGunsSkill + 20, tagged.SmallGunsSkill);
        Assert.Equal(5 + 4 * ag + 20, tagged.SmallGunsSkill);

        // Melee is tagged too.
        Assert.Equal(20 + 2 * (ag + st) + 20, tagged.MeleeWeaponsSkill);

        // Unarmed is NOT tagged — unchanged either way.
        Assert.Equal(untagged.UnarmedSkill, tagged.UnarmedSkill);
    }

    [GameDataFact]
    public void DiplomatPremadeIsFemaleAndTagsBarter()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        using Stream s = vfs.OpenRead(@"premade\diplomat.gcd");
        GcdFile gcd = GcdFile.Load(s); // Chitsa: CHA 10, gender 1, tags FirstAid/Speech/Barter(15)

        Assert.Equal(1, gcd.Stats.BaseStats[34]); // STAT_GENDER = female

        var tagged = new CritterState(Dude(), gcd.Stats, gcd.TaggedSkills);
        Assert.Equal(4 * 10 + 20, tagged.BarterSkill); // 60, with the tag bonus
        Assert.Equal(60, BarterMath.BarterSkill(tagged));
    }
}
