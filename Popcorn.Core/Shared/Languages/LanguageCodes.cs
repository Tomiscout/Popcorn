using System.Globalization;

namespace Popcorn.Shared.Languages;

/// <summary>A language the user can pick: <see cref="Code"/> = ISO 639-2/T (what containers and mpv mostly report), English name.</summary>
public sealed record LanguageOption(string Code, string Name);

/// <summary>
/// Track language tags (ISO 639-1 "en", ISO 639-2/T "deu", ISO 639-2/B "ger", BCP 47 "pt-BR") → one canonical
/// ISO 639-2/T code and its English name, from .NET's culture data (ICU).
/// </summary>
public static class LanguageCodes
{
    // ISO 639-2/B codes (used by Matroska and many release groups) → their /T code, which CultureInfo reports.
    private static readonly Dictionary<string, string> Bibliographic = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alb"] = "sqi", ["arm"] = "hye", ["baq"] = "eus", ["bur"] = "mya", ["chi"] = "zho", ["cze"] = "ces",
        ["dut"] = "nld", ["fre"] = "fra", ["geo"] = "kat", ["ger"] = "deu", ["gre"] = "ell", ["ice"] = "isl",
        ["mac"] = "mkd", ["mao"] = "mri", ["may"] = "msa", ["per"] = "fas", ["rum"] = "ron", ["slo"] = "slk",
        ["tib"] = "bod", ["wel"] = "cym",
        // Not in ISO 639-2 but seen in files.
        ["scc"] = "srp", ["scr"] = "hrv", ["fil"] = "fil",
    };

    // Tags that say "no particular language".
    private static readonly HashSet<string> NoLanguage = new(StringComparer.OrdinalIgnoreCase) { "und", "mul", "zxx", "mis", "unk", "none" };

    private static readonly Lazy<(Dictionary<string, LanguageOption> ByCode, IReadOnlyList<LanguageOption> All)> Data = new(Build);

    private static (Dictionary<string, LanguageOption>, IReadOnlyList<LanguageOption>) Build()
    {
        var byCode = new Dictionary<string, LanguageOption>(StringComparer.OrdinalIgnoreCase);
        // Shortest name first: "zh" (Chinese) wins over "zh-Hans" for "zho".
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures).OrderBy(c => c.Name.Length))
        {
            var three = culture.ThreeLetterISOLanguageName;
            if (string.IsNullOrEmpty(culture.Name) || string.IsNullOrEmpty(three) || three.Length != 3 || NoLanguage.Contains(three)) continue;

            var option = new LanguageOption(three.ToLowerInvariant(), culture.EnglishName);
            byCode.TryAdd(three, option);
            byCode.TryAdd(culture.TwoLetterISOLanguageName, option);
            byCode.TryAdd(culture.Name, option);
        }

        var all = byCode.Values.DistinctBy(o => o.Code).OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return (byCode, all);
    }

    /// <summary>Every known language, by name.</summary>
    public static IReadOnlyList<LanguageOption> All => Data.Value.All;

    /// <summary>The language of a tag, null when it names none (empty, "und") or is unknown.</summary>
    public static LanguageOption? Find(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var code = tag.Trim();
        var cut = code.IndexOfAny(['-', '_']);
        if (cut > 0) code = code[..cut]; // pt-BR, en_US
        if (NoLanguage.Contains(code)) return null;
        if (Bibliographic.TryGetValue(code, out var terminology)) code = terminology;
        return Data.Value.ByCode.GetValueOrDefault(code);
    }

    /// <summary>Canonical code for comparing tags: ISO 639-2/T, or the tag itself (lower case) when unknown.</summary>
    public static string? Normalize(string? tag) =>
        Find(tag)?.Code ?? (string.IsNullOrWhiteSpace(tag) || NoLanguage.Contains(tag.Trim()) ? null : tag.Trim().ToLowerInvariant());

    /// <summary>English name of the tag's language ("eng" → "English"); the tag itself when unknown, null when empty.</summary>
    public static string? DisplayName(string? tag) =>
        Find(tag)?.Name ?? (string.IsNullOrWhiteSpace(tag) || NoLanguage.Contains(tag.Trim()) ? null : tag.Trim());

    public static bool Matches(string? a, string? b)
    {
        var left = Normalize(a);
        return left is not null && left == Normalize(b);
    }

    /// <summary>First track in the first preferred language that any track has; null when none matches.</summary>
    public static T? PickPreferred<T>(IEnumerable<T> tracks, Func<T, string?> language, IEnumerable<string>? preferred) where T : class
    {
        if (preferred is null) return null;
        var list = tracks.ToList();
        foreach (var wanted in preferred)
        {
            var match = list.FirstOrDefault(t => Matches(language(t), wanted));
            if (match is not null) return match;
        }
        return null;
    }
}
