namespace Popcorn.FilenameParser;

/// <summary>A language a release is in (or <see cref="Unknown"/> / <see cref="Original"/>). Union of Sonarr's and Radarr's lists.</summary>
public sealed record Language(string Name)
{
    public override string ToString() => Name;

    public static readonly Language Unknown = new("Unknown");
    public static readonly Language Original = new("Original");
    public static readonly Language English = new("English");
    public static readonly Language French = new("French");
    public static readonly Language Spanish = new("Spanish");
    public static readonly Language SpanishLatino = new("Spanish (Latino)");
    public static readonly Language German = new("German");
    public static readonly Language Italian = new("Italian");
    public static readonly Language Danish = new("Danish");
    public static readonly Language Dutch = new("Dutch");
    public static readonly Language Flemish = new("Flemish");
    public static readonly Language Japanese = new("Japanese");
    public static readonly Language Icelandic = new("Icelandic");
    public static readonly Language Chinese = new("Chinese");
    public static readonly Language Russian = new("Russian");
    public static readonly Language Polish = new("Polish");
    public static readonly Language Vietnamese = new("Vietnamese");
    public static readonly Language Swedish = new("Swedish");
    public static readonly Language Norwegian = new("Norwegian");
    public static readonly Language Finnish = new("Finnish");
    public static readonly Language Turkish = new("Turkish");
    public static readonly Language Portuguese = new("Portuguese");
    public static readonly Language PortugueseBrazil = new("Portuguese (Brazil)");
    public static readonly Language Greek = new("Greek");
    public static readonly Language Korean = new("Korean");
    public static readonly Language Hungarian = new("Hungarian");
    public static readonly Language Hebrew = new("Hebrew");
    public static readonly Language Lithuanian = new("Lithuanian");
    public static readonly Language Czech = new("Czech");
    public static readonly Language Arabic = new("Arabic");
    public static readonly Language Hindi = new("Hindi");
    public static readonly Language Bulgarian = new("Bulgarian");
    public static readonly Language Malayalam = new("Malayalam");
    public static readonly Language Ukrainian = new("Ukrainian");
    public static readonly Language Slovak = new("Slovak");
    public static readonly Language Thai = new("Thai");
    public static readonly Language Romanian = new("Romanian");
    public static readonly Language Latvian = new("Latvian");
    public static readonly Language Persian = new("Persian");
    public static readonly Language Bengali = new("Bengali");
    public static readonly Language Catalan = new("Catalan");
    public static readonly Language Croatian = new("Croatian");
    public static readonly Language Serbian = new("Serbian");
    public static readonly Language Bosnian = new("Bosnian");
    public static readonly Language Estonian = new("Estonian");
    public static readonly Language Tamil = new("Tamil");
    public static readonly Language Telugu = new("Telugu");
    public static readonly Language Kannada = new("Kannada");
    public static readonly Language Marathi = new("Marathi");
    public static readonly Language Indonesian = new("Indonesian");
    public static readonly Language Macedonian = new("Macedonian");
    public static readonly Language Slovenian = new("Slovenian");
    public static readonly Language Albanian = new("Albanian");
    public static readonly Language Afrikaans = new("Afrikaans");
    public static readonly Language Tagalog = new("Tagalog");
    public static readonly Language Azerbaijani = new("Azerbaijani");
    public static readonly Language Uzbek = new("Uzbek");
    public static readonly Language Malay = new("Malay");
    public static readonly Language Urdu = new("Urdu");
    public static readonly Language Romansh = new("Romansh");
    public static readonly Language Mongolian = new("Mongolian");
    public static readonly Language Georgian = new("Georgian");
    public static readonly Language Irish = new("Irish");

    public static readonly IReadOnlyList<Language> All =
    [
        Unknown, Original, English, French, Spanish, SpanishLatino, German, Italian, Danish, Dutch, Flemish, Japanese,
        Icelandic, Chinese, Russian, Polish, Vietnamese, Swedish, Norwegian, Finnish, Turkish, Portuguese, PortugueseBrazil,
        Greek, Korean, Hungarian, Hebrew, Lithuanian, Czech, Arabic, Hindi, Bulgarian, Malayalam, Ukrainian, Slovak, Thai,
        Romanian, Latvian, Persian, Bengali, Catalan, Croatian, Serbian, Bosnian, Estonian, Tamil, Telugu, Kannada, Marathi,
        Indonesian, Macedonian, Slovenian, Albanian, Afrikaans, Tagalog, Azerbaijani, Uzbek, Malay, Urdu, Romansh, Mongolian,
        Georgian, Irish,
    ];
}

/// <param name="TwoLetterCode">ISO 639-1.</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2, lower case, empty for the language's default.</param>
/// <param name="ThreeLetterCode">ISO 639-2/T.</param>
public sealed record IsoLanguage(string TwoLetterCode, string CountryCode, string ThreeLetterCode, Language Language);

public static class IsoLanguages
{
    private static readonly IsoLanguage[] All =
    [
        new("en", "", "eng", Language.English),
        new("fr", "fr", "fra", Language.French),
        new("es", "", "spa", Language.Spanish),
        new("de", "de", "deu", Language.German),
        new("it", "", "ita", Language.Italian),
        new("da", "", "dan", Language.Danish),
        new("nl", "", "nld", Language.Dutch),
        new("ja", "", "jpn", Language.Japanese),
        new("is", "", "isl", Language.Icelandic),
        new("zh", "cn", "zho", Language.Chinese),
        new("ru", "", "rus", Language.Russian),
        new("pl", "", "pol", Language.Polish),
        new("vi", "", "vie", Language.Vietnamese),
        new("sv", "", "swe", Language.Swedish),
        new("no", "", "nor", Language.Norwegian),
        new("nb", "", "nob", Language.Norwegian), // Norwegian Bokmål
        new("fi", "", "fin", Language.Finnish),
        new("tr", "", "tur", Language.Turkish),
        new("pt", "pt", "por", Language.Portuguese),
        new("nl", "", "nld", Language.Flemish),
        new("el", "", "ell", Language.Greek),
        new("ko", "", "kor", Language.Korean),
        new("hu", "", "hun", Language.Hungarian),
        new("he", "", "heb", Language.Hebrew),
        new("lt", "", "lit", Language.Lithuanian),
        new("cs", "", "ces", Language.Czech),
        new("ar", "", "ara", Language.Arabic),
        new("hi", "", "hin", Language.Hindi),
        new("bg", "", "bul", Language.Bulgarian),
        new("ml", "", "mal", Language.Malayalam),
        new("uk", "", "ukr", Language.Ukrainian),
        new("sk", "", "slk", Language.Slovak),
        new("th", "th", "tha", Language.Thai),
        new("pt", "br", "por", Language.PortugueseBrazil),
        new("es", "mx", "spa", Language.SpanishLatino),
        new("ro", "", "ron", Language.Romanian),
        new("lv", "", "lav", Language.Latvian),
        new("fa", "", "fas", Language.Persian),
        new("bn", "", "ben", Language.Bengali),
        new("ca", "", "cat", Language.Catalan),
        new("hr", "", "hrv", Language.Croatian),
        new("sr", "", "srp", Language.Serbian),
        new("bs", "", "bos", Language.Bosnian),
        new("et", "", "est", Language.Estonian),
        new("ta", "", "tam", Language.Tamil),
        new("te", "", "tel", Language.Telugu),
        new("kn", "", "kan", Language.Kannada),
        new("mr", "", "mar", Language.Marathi),
        new("id", "", "ind", Language.Indonesian),
        new("mk", "", "mkd", Language.Macedonian),
        new("sl", "", "slv", Language.Slovenian),
        new("sq", "", "sqi", Language.Albanian),
        new("af", "", "afr", Language.Afrikaans),
        new("tl", "", "tgl", Language.Tagalog),
        new("az", "", "aze", Language.Azerbaijani),
        new("uz", "", "uzb", Language.Uzbek),
        new("ms", "", "msa", Language.Malay),
        new("ur", "", "urd", Language.Urdu),
        new("rm", "", "roh", Language.Romansh),
        new("mn", "", "mon", Language.Mongolian),
        new("ka", "", "kat", Language.Georgian),
        new("ga", "", "gle", Language.Irish),
    ];

    private static readonly Dictionary<string, Language> AlternateCodes = new()
    {
        { "cn", Language.Chinese },
        { "yue", Language.Chinese },
        { "zhtw", Language.Chinese },
    };

    // ISO 639-2/B (bibliographic) and individual-language codes -> the 639-2/T code used above.
    private static readonly Dictionary<string, string> ThreeLetterAliases = new()
    {
        { "alb", "sqi" }, { "arm", "hye" }, { "baq", "eus" }, { "bur", "mya" }, { "chi", "zho" }, { "cze", "ces" },
        { "dut", "nld" }, { "fre", "fra" }, { "geo", "kat" }, { "ger", "deu" }, { "gre", "ell" }, { "gsw", "deu" },
        { "ice", "isl" }, { "mac", "mkd" }, { "mao", "mri" }, { "may", "msa" }, { "per", "fas" }, { "rum", "ron" },
        { "slo", "slk" }, { "tib", "bod" }, { "wel", "cym" }, { "khk", "mon" }, { "mvf", "mon" },
    };

    /// <summary>Looks up "en", "eng", "en-US", "pt-BR", ... (case-insensitive); null if unknown.</summary>
    public static IsoLanguage? Find(string isoCode)
    {
        var parts = isoCode.ToLowerInvariant().Split('-');
        var langCode = parts[0];

        if (AlternateCodes.TryGetValue(isoCode.ToLowerInvariant(), out var alternate))
        {
            return Get(alternate);
        }

        if (langCode.Length == 2)
        {
            var candidates = All.Where(l => l.TwoLetterCode == langCode).ToList();

            if (parts.Length > 1)
            {
                var country = parts[1];
                candidates = candidates.Any(l => l.CountryCode == country)
                    ? candidates.Where(l => l.CountryCode == country).ToList()
                    : candidates.Where(l => l.CountryCode == "").ToList();
            }

            return candidates.FirstOrDefault();
        }

        if (langCode.Length == 3)
        {
            langCode = ThreeLetterAliases.GetValueOrDefault(langCode, langCode);
            return All.FirstOrDefault(l => l.ThreeLetterCode == langCode);
        }

        return null;
    }

    public static IsoLanguage? Get(Language language) => All.FirstOrDefault(l => l.Language == language);

    public static IsoLanguage? FindByName(string name) =>
        All.FirstOrDefault(l => l.Language.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase));
}
