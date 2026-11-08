using NUnit.Framework;
using Popcorn.Shared.Languages;

namespace Popcorn.Domain.Tests;

public class LanguageCodesTests
{
    [TestCase("eng", "English")]
    [TestCase("en", "English")]
    [TestCase("jpn", "Japanese")]
    [TestCase("ger", "German")]
    [TestCase("deu", "German")]
    [TestCase("fre", "French")]
    [TestCase("chi", "Chinese")]
    [TestCase("lit", "Lithuanian")]
    [TestCase("pt-BR", "Portuguese")]
    [TestCase("xyz", "xyz")]
    public void DisplayName(string tag, string expected) => Assert.That(LanguageCodes.DisplayName(tag), Is.EqualTo(expected));

    [TestCase("")]
    [TestCase(null)]
    [TestCase("und")]
    public void NoLanguage(string? tag) => Assert.That(LanguageCodes.DisplayName(tag), Is.Null);

    [TestCase("ger", "deu")]
    [TestCase("en", "eng")]
    [TestCase("ENG", "eng")]
    [TestCase("cze", "ces")]
    public void Matches(string a, string b) => Assert.That(LanguageCodes.Matches(a, b), Is.True);

    [Test]
    public void PickPreferredFollowsPreferenceOrder()
    {
        var tracks = new[] { "eng", "jpn", "ger" };
        Assert.That(LanguageCodes.PickPreferred(tracks, t => t, ["fra", "de", "eng"]), Is.EqualTo("ger"));
        Assert.That(LanguageCodes.PickPreferred(tracks, t => t, ["fra"]), Is.Null);
    }

    [Test]
    public void AllHasUniqueCodes()
    {
        Assert.That(LanguageCodes.All.Count, Is.GreaterThan(100));
        Assert.That(LanguageCodes.All.Select(l => l.Code), Is.Unique);
        Assert.That(LanguageCodes.All.Any(l => l.Code == "eng" && l.Name == "English"));
    }
}
