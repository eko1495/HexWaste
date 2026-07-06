using Hexwaste.Formats.Combat;
using Hexwaste.Formats.Int;
using Hexwaste.Formats.Map;
using Hexwaste.Formats.Proto;

namespace Hexwaste.Formats.Tests;

public class GcdAndStatsTests
{
    [GameDataFact]
    public void PlayerGcdParsesFully()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        using Stream stream = vfs.OpenRead(@"premade\player.gcd");
        GcdFile gcd = GcdFile.Load(stream);

        // The blank premade character: all SPECIAL at 5.
        for (int stat = 0; stat <= 6; stat++)
            Assert.InRange(gcd.Stats.BaseStats[stat] + gcd.Stats.BonusStats[stat], 1, 10);
        Assert.Equal(100, gcd.Stats.BaseStats[29]); // forced EMP resistance (gcdLoad)
        Assert.Equal(4, gcd.TaggedSkills.Length);
        Assert.Equal(2, gcd.Traits.Length);
        Assert.Equal(-1, stream.ReadByte()); // 432 bytes fully consumed (stream is non-seekable zlib)
    }

    [GameDataFact]
    public void CritterStatValueUsesOverrideAndPseudostats()
    {
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        var protos = new ProtoDatabase(vfs);
        var host = new ScriptHost(vfs, ScriptList.Load(vfs), protos);

        var dude = new MapObject
        {
            Id = -1,
            HexTile = 100,
            X = 0,
            Y = 0,
            Frame = 0,
            Rotation = 0,
            Fid = 0x01000000,
            Pid = 0x01000001,
            Flags = 0,
            Sid = -1,
        };
        dude.CurrentHp = 17;
        dude.Poison = 3;

        using Stream stream = vfs.OpenRead(@"premade\player.gcd");
        GcdFile gcd = GcdFile.Load(stream);
        host.StatsResolver = obj => obj == dude ? gcd.Stats : null;

        int agility = gcd.Stats.BaseStats[5] + gcd.Stats.BonusStats[5];
        Assert.Equal(agility, host.CritterStatValue(dude, 5));
        Assert.Equal(17, host.CritterStatValue(dude, 35)); // STAT_CURRENT_HIT_POINTS
        Assert.Equal(3, host.CritterStatValue(dude, 36)); // poison
        Assert.Equal(-1, host.CritterStatValue(dude, 99));

        // Non-overridden critters fall back to their prototype.
        var peasant = new MapObject
        {
            Id = 2,
            HexTile = 200,
            X = 0,
            Y = 0,
            Frame = 0,
            Rotation = 0,
            Fid = 0x01000000,
            Pid = 0x01000041, // Average Peasant (verified phase 6 M2 research)
            Flags = 0,
            Sid = -1,
        };
        CritterProtoStats proto = protos.Get(peasant.Pid).Critter!;
        Assert.Equal(proto.BaseStats[7] + proto.BonusStats[7], host.CritterStatValue(peasant, 7));
    }
}

public class RotationToTests
{
    [Fact]
    public void RotationToMatchesStepDirections()
    {
        // Walking one hex in rotation r, the rotation back to start is (r+3)%6
        // and toward the destination is r — tileGetRotationTo round-trip.
        int start = 100 * Hexwaste.Formats.Hex.HexGrid.Width + 100;
        for (int rotation = 0; rotation < 6; rotation++)
        {
            int next = Hexwaste.Formats.Hex.HexGrid.TileInDirection(start, rotation);
            Assert.Equal(rotation, Hexwaste.Formats.Hex.HexGrid.RotationTo(start, next));
            Assert.Equal((rotation + 3) % 6, Hexwaste.Formats.Hex.HexGrid.RotationTo(next, start));
        }
    }
}

public class GcdCreateTests
{
    [Fact]
    public void CreateComputesDerivedStatsLikeTheEngine()
    {
        // ST8 PE5 EN9 CH3 IN4 AG7 LK4, female, tags Small Guns/Melee/Throwing.
        int[] special = [8, 5, 9, 3, 4, 7, 4];
        Hexwaste.Formats.Combat.GcdFile g =
            Hexwaste.Formats.Combat.GcdFile.Create(special, [0, 4, 5], gender: 1);

        int[] bs = g.Stats.BaseStats;
        Assert.Equal(8 + 2 * 9 + 15, bs[7]);   // MaxHP = ST + 2*EN + 15 = 41
        Assert.Equal(7 / 2 + 5, bs[8]);        // MaxAP = AG/2 + 5 = 8
        Assert.Equal(7, bs[9]);                // AC = AG
        Assert.Equal(Math.Max(8 - 5, 1), bs[11]); // Melee = max(ST-5,1) = 3
        Assert.Equal(2 * 5, bs[13]);           // Sequence = 2*PE = 10
        Assert.Equal(4, bs[15]);               // CritChance = LK
        Assert.Equal(1, bs[34]);               // gender female
        Assert.Equal(100, bs[29]);             // EMP resist forced
        Assert.Equal([0, 4, 5, -1], g.TaggedSkills);

        // The tag bonus flows through SkillSet for the created dude.
        Assert.Equal(5 + 4 * 7 + 20, Hexwaste.Formats.Combat.SkillSet.Value(
            bs, g.Stats.BonusStats, g.Stats.Skills, g.TaggedSkills, 0)); // Small Guns tagged = 53
    }

    [Fact]
    public void CreateCarriesTheChargenNameAndAge()
    {
        // P121: the name field + age spinner ride the created sheet — age into base stat 33
        // (STAT_AGE), the name verbatim; defaults are "Wanderer"/25.
        int[] special = [5, 5, 5, 5, 5, 5, 5];
        var named = Hexwaste.Formats.Combat.GcdFile.Create(special, [0, 1, 2], gender: 0,
            name: "Sulik-Fan", age: 31);
        Assert.Equal("Sulik-Fan", named.Name);
        Assert.Equal(31, named.Stats.BaseStats[33]);

        var defaulted = Hexwaste.Formats.Combat.GcdFile.Create(special, [0, 1, 2], gender: 0);
        Assert.Equal("Wanderer", defaulted.Name);
        Assert.Equal(25, defaulted.Stats.BaseStats[33]);
    }

    private static MapObject NewDude() => new()
    {
        Id = 1, HexTile = 100, X = 0, Y = 0, Frame = 0, Rotation = 0,
        Fid = 0x01000000, Pid = 0x01000001, Flags = 0, Sid = -1,
    };

    [Fact]
    public void CreateBakesGiftedPropagationButKeepsBasePrimaryUnmodified()
    {
        // P29-M3: Gifted (+1 to every SPECIAL) propagates into the derived BASE at creation, while the
        // base primary stays unmodified — the engine adds the trait modifier LIVE on each stat read.
        int[] special = [5, 5, 5, 5, 5, 5, 5];
        GcdFile g = GcdFile.Create(special, [0, 4, 5], gender: 0, traits: [TraitModifiers.Gifted]);
        int[] bs = g.Stats.BaseStats;

        Assert.Equal(5, bs[0]);                 // base ST UNMODIFIED (Gifted is live, not baked)
        Assert.Equal(6 + 2 * 6 + 15, bs[7]);    // HP from the +1 (6) primaries = 33
        Assert.Equal(6 / 2 + 5, bs[8]);         // AP from AG 6 = 8
        Assert.Equal(6, bs[9]);                 // AC = AG 6
        Assert.Equal(2 * 6, bs[13]);            // Sequence = 2*PE(6) = 12
        Assert.Equal(6, bs[15]);                // Crit % = LK 6
        Assert.Equal([TraitModifiers.Gifted, -1], g.Traits);

        // A live read adds the trait modifier on top of the unmodified base (no double count).
        var cs = new CritterState(NewDude(), g.Stats, g.TaggedSkills, g.Traits);
        Assert.Equal(6, cs.Stat(CritterStat.Strength)); // base 5 + Gifted 1
    }

    [Fact]
    public void CreateBakesBruiserStrengthButLeavesMaxApPenaltyLive()
    {
        // Bruiser is a PROPAGATION trait for ST (+2) but a DIRECT modifier for MaxAP (−2): the ST raise
        // bakes into HP/melee/carry; the AP penalty is added live, so the base AP stays AG-derived.
        int[] special = [5, 5, 5, 5, 5, 5, 5];
        GcdFile g = GcdFile.Create(special, [0, 4, 5], gender: 0, traits: [TraitModifiers.Bruiser]);
        int[] bs = g.Stats.BaseStats;

        Assert.Equal(7 + 2 * 5 + 15, bs[7]);      // HP from ST 7 = 32
        Assert.Equal(Math.Max(7 - 5, 1), bs[11]); // Melee from ST 7 = 2
        Assert.Equal(25 * 7 + 25, bs[12]);        // Carry from ST 7 = 200
        Assert.Equal(5 / 2 + 5, bs[8]);           // base AP from AG 5 = 7 (Bruiser −2 is a live modifier)

        var cs = new CritterState(NewDude(), g.Stats, g.TaggedSkills, g.Traits);
        Assert.Equal(7, cs.Stat(CritterStat.Strength));                  // base 5 + Bruiser 2
        Assert.Equal(7 - 2, cs.Stat(CritterStat.MaximumActionPoints));   // base 7 + Bruiser −2 = 5
    }

    [Fact]
    public void CreateWithNoTraitsIsUnchanged()
    {
        // The inert default: a trait-less created character matches the pre-P29 derived stats exactly.
        int[] special = [6, 5, 7, 3, 4, 8, 5];
        GcdFile baseline = GcdFile.Create(special, [0, 4, 5], gender: 0);
        GcdFile explicitNone = GcdFile.Create(special, [0, 4, 5], gender: 0, traits: []);
        Assert.Equal(baseline.Stats.BaseStats, explicitNone.Stats.BaseStats);
        Assert.Equal([-1, -1], baseline.Traits);
    }
}
