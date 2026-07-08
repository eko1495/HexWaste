using Hexwaste.Formats;
using Hexwaste.Formats.Map;

namespace Hexwaste.Formats.Tests;

/// <summary>P131 (gap batch C): the localization path helper + the worldmap tab label FRM
/// index parsed from city.txt.</summary>
public class LocalizationTests
{
    [Fact]
    public void LocalizeIsANoOpForEnglishAndSubstitutesOtherwise()
    {
        string original = Localization.Language;
        try
        {
            Localization.Language = "english";
            Assert.Equal(@"text\english\game\perk.msg", Localization.Localize(@"text\english\game\perk.msg"));

            Localization.Language = "french";
            Assert.Equal(@"text\french\game\perk.msg", Localization.Localize(@"text\english\game\perk.msg"));
            Assert.Equal(@"text\french\cuts\nar_amon.txt", Localization.Localize(@"text\english\cuts\nar_amon.txt"));
        }
        finally
        {
            Localization.Language = original; // shared static — don't leak into other tests
        }
    }

    [GameDataFact]
    public void CityListParsesTownmapLabelArtIdx()
    {
        // P131: townmap_label_art_idx is the worldmap tab's label FRM — Arroyo ships 370,
        // the Den 372 (city.txt). Shadow areas that reuse a name (Destroyed Arroyo) have none.
        using var vfs = GameFileSystem.Open(GameData.RequiredDir);
        CityList cities = CityList.Load(vfs);

        Assert.Equal(370, cities.Areas.First(a => a.Index == 0).LabelArtIdx); // Arroyo
        Assert.Equal(372, cities.Areas.First(a => a.Index == 1).LabelArtIdx); // The Den
        // Every present label index is a plausible art\intrface index.
        Assert.All(cities.Areas.Where(a => a.LabelArtIdx >= 0),
            a => Assert.InRange(a.LabelArtIdx, 300, 500));
    }
}
