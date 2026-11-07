using System.Globalization;
using Popcorn.Server.Models;

public class LanguageService
{
    public List<Language> GetLanguages()
    {
        // NeutralCultures gives us distinct languages (no en-US vs en-UK duplicates)
        var cultures = CultureInfo
            .GetCultures(CultureTypes.NeutralCultures)
            .Where(c => !c.IsNeutralCulture || !string.IsNullOrWhiteSpace(c.ThreeLetterISOLanguageName))
            .DistinctBy(c => c.ThreeLetterISOLanguageName)
            .OrderBy(c => c.EnglishName);

        var languages = cultures
            .Select(c => new Language
            {
                Name = c.EnglishName,
                Iso3 = c.ThreeLetterISOLanguageName,
                Iso2 = c.TwoLetterISOLanguageName
            })
            .ToList();

        return languages;
    }
}