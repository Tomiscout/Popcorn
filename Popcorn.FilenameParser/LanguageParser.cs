using System.Text.RegularExpressions;

namespace Popcorn.FilenameParser;

/// <summary>Audio languages of a release name, and the language/tags/title of external subtitle files.</summary>
public static partial class LanguageParser
{
    // Everything before S01E01 is the series title ("The.Spanish.Princess.S01E01" is not Spanish).
    private static readonly RegexReplace CleanSeriesTitleRegex = new(CleanSeriesTitlePattern, "$1");

    [GeneratedRegex(@".*?[_. ](S\d{2}(?:E\d{2,4})*[_. ].*)", RegexOptions.IgnoreCase)]
    private static partial Regex CleanSeriesTitlePattern { get; }

    // Language names anywhere in the title.
    private static readonly (string Word, Language Language)[] LanguageWords =
    [
        ("english", Language.English), ("spanish", Language.Spanish), ("danish", Language.Danish), ("dutch", Language.Dutch),
        ("japanese", Language.Japanese), ("icelandic", Language.Icelandic), ("mandarin", Language.Chinese),
        ("cantonese", Language.Chinese), ("chinese", Language.Chinese), ("korean", Language.Korean), ("russian", Language.Russian),
        ("romanian", Language.Romanian), ("hindi", Language.Hindi), ("arabic", Language.Arabic), ("thai", Language.Thai),
        ("bulgarian", Language.Bulgarian), ("polish", Language.Polish), ("vietnamese", Language.Vietnamese),
        ("swedish", Language.Swedish), ("norwegian", Language.Norwegian), ("finnish", Language.Finnish),
        ("turkish", Language.Turkish), ("portuguese", Language.Portuguese), ("brazilian", Language.PortugueseBrazil),
        ("dublado", Language.PortugueseBrazil), ("hungarian", Language.Hungarian), ("hebrew", Language.Hebrew),
        ("ukrainian", Language.Ukrainian), ("persian", Language.Persian), ("bengali", Language.Bengali),
        ("slovak", Language.Slovak), ("latvian", Language.Latvian), ("latino", Language.SpanishLatino), ("tamil", Language.Tamil),
        ("telugu", Language.Telugu), ("malayalam", Language.Malayalam), ("kannada", Language.Kannada),
        ("albanian", Language.Albanian), ("afrikaans", Language.Afrikaans), ("marathi", Language.Marathi),
        ("tagalog", Language.Tagalog), ("azerbaijan", Language.Azerbaijani), ("uzbek", Language.Uzbek),
    ];

    // Tags and abbreviations; every group is named after the language it stands for (see GroupLanguages).
    private const string LanguageTagsPattern = @"(?<english>\b(?:ing|eng)\b)|(?<italian>\b(?:ita|italian)\b)|(?<german>(?:swiss)?german\b|videomann|ger[. ]dub|\bger\b)|(?<flemish>flemish)|(?<greek>greek)|(?<french>(?:_|\b)(?:FR|VO|VF|VF2|VFF|VFI|VFQ|TRUEFRENCH|FRENCH|FRE|FRA)(?:_|\b))|(?<russian>\b(?:rus|ru)\b)|(?<hungarian>\b(?:HUNDUB|HUN)\b)|(?<hebrew>\b(?:HebDub|HebDubbed)\b)|(?<polish>\b(?:PL\W?DUB|DUB\W?PL|LEK\W?PL|PL\W?LEK)\b)|(?<chinese>\[(?:CH[ST]|BIG5|GB)\]|简|繁|字幕|国语音轨[.+])|(?<bulgarian>\bbgaudio\b)|(?<brazilian>\b(?:dublado|pt-BR)\b)|(?<spanish>\b(?:español|castellano|esp|spa(?!\(Latino\)))\b)|(?<ukrainian>\b(?:\dx?)?(?:ukr))|(?<thai>\b(?:THAI)\b)|(?<romanian>\b(?:RoDubbed|ROMANIAN)\b)|(?<catalan>[-,. ]cat[. ](?:DD|subs)|\b(?:catalan?|catalán|català)\b)|(?<latvian>\b(?:lat|lav|lv)\b)|(?<turkish>\b(?:tur)\b)|(?<telugu>\btel\b)|(?<vietnamese>\bVIE\b)|(?<korean>\bKOR\b)|(?<urdu>\burdu\b)|(?<romansh>\b(?:romansh|rumantsch|romansch)\b)|(?<mongolian>\b(?:mongolian|khalkha)\b)|(?<georgian>\b(?:geo|ka|kat|georgian)\b)|(?<irish>\b(?:irish|gaeilge)\b)|(?<japanese>\(JA\)|JAP|JPN)|(?<portuguese>[_. ]por[_. ])|(?<original>\b(?:orig|original)\b)";

    // A language tag directly followed by "subs" (possibly after more language tags) names a subtitle, not the audio.
    [GeneratedRegex(@$"(?:{LanguageTagsPattern})(?!(?:[-_. ](?:{LanguageTagsPattern}))*[-_. ]subs?)", RegexOptions.IgnoreCase)]
    private static partial Regex LanguageRegex { get; }

    [GeneratedRegex(@"(?:(?i)(?<!SUB[\W|_|^]))(?:(?<english>\bEN\b)|(?<lithuanian>\bLT\b)|(?<czech>\bCZ\b)|(?<polish>\bPL\b)|(?<bulgarian>\bBG\b)|(?<slovak>\bSK\b)|(?<german>\bDE\b)|(?<spanish>\b(?<!DTS[._ -])ES\b))(?:(?i)(?![\W|_|^]SUB))")]
    private static partial Regex CaseSensitiveLanguageRegex { get; }

    private static readonly Dictionary<string, Language> GroupLanguages = new()
    {
        { "english", Language.English }, { "italian", Language.Italian }, { "german", Language.German },
        { "flemish", Language.Flemish }, { "greek", Language.Greek }, { "french", Language.French },
        { "russian", Language.Russian }, { "hungarian", Language.Hungarian }, { "hebrew", Language.Hebrew },
        { "polish", Language.Polish }, { "chinese", Language.Chinese }, { "bulgarian", Language.Bulgarian },
        { "brazilian", Language.PortugueseBrazil }, { "spanish", Language.Spanish }, { "ukrainian", Language.Ukrainian },
        { "thai", Language.Thai }, { "romanian", Language.Romanian }, { "catalan", Language.Catalan },
        { "latvian", Language.Latvian }, { "turkish", Language.Turkish }, { "telugu", Language.Telugu },
        { "vietnamese", Language.Vietnamese }, { "korean", Language.Korean }, { "urdu", Language.Urdu },
        { "romansh", Language.Romansh }, { "mongolian", Language.Mongolian }, { "georgian", Language.Georgian },
        { "irish", Language.Irish }, { "japanese", Language.Japanese }, { "portuguese", Language.Portuguese },
        { "original", Language.Original }, { "lithuanian", Language.Lithuanian }, { "czech", Language.Czech },
        { "slovak", Language.Slovak },
    };

    [GeneratedRegex(@"(?<!WEB[-_. ]?)\bDL\b", RegexOptions.IgnoreCase)]
    private static partial Regex GermanDualLanguageRegex { get; }
    [GeneratedRegex(@"\bML\b", RegexOptions.IgnoreCase)]
    private static partial Regex GermanMultiLanguageRegex { get; }

    private const string SubtitleTags = "forced|foreign|default|cc|psdh|sdh";

    [GeneratedRegex($".+?([-_. ](?<tags>{SubtitleTags}))*[-_. ](?<iso_code>[a-z]{{2,3}})([-_. ](?<tags>{SubtitleTags}))*$", RegexOptions.IgnoreCase)]
    private static partial Regex SubtitleLanguageRegex { get; }

    [GeneratedRegex($@".+?(\.((?<tags1>{SubtitleTags})|(?<iso_code>[a-z]{{2,3}})))*[-_. ](?<title>[^.]*)(\.((?<tags2>{SubtitleTags})|(?<iso_code>[a-z]{{2,3}})))*$", RegexOptions.IgnoreCase)]
    private static partial Regex SubtitleLanguageTitleRegex { get; }

    [GeneratedRegex(@"^((?<title>.+) - )?(?<copy>(?<!\d+)\d{1,3}(?!\d+))$")]
    private static partial Regex SubtitleTitleRegex { get; }

    /// <summary>Audio languages the name mentions; [<see cref="Language.Unknown"/>] if none.</summary>
    public static List<Language> ParseLanguages(string title)
    {
        CleanSeriesTitleRegex.TryReplace(ref title);

        var lowerTitle = title.ToLowerInvariant();
        var languages = LanguageWords.Where(w => lowerTitle.Contains(w.Word)).Select(w => w.Language).ToList();

        foreach (var match in CaseSensitiveLanguageRegex.Matches(title).Concat(LanguageRegex.Matches(title)))
        {
            languages.AddRange(GroupLanguages.Where(g => match.Groups[g.Key].Success).Select(g => g.Value));
        }

        if (languages.Count == 0)
        {
            return [Language.Unknown];
        }

        // German releases: DL = German + original audio, ML = German + original + English.
        if (languages.All(l => l == Language.German))
        {
            if (GermanDualLanguageRegex.IsMatch(title))
            {
                languages.Add(Language.Original);
            }
            else if (GermanMultiLanguageRegex.IsMatch(title))
            {
                languages.Add(Language.Original);
                languages.Add(Language.English);
            }
        }

        return languages.Distinct().ToList();
    }

    /// <summary>Language of a subtitle file from its "Name.en.forced.srt"-style suffix.</summary>
    public static Language ParseSubtitleLanguage(string fileName)
    {
        var simpleFilename = Path.GetFileNameWithoutExtension(fileName);
        var languageMatch = SubtitleLanguageRegex.Match(simpleFilename);

        if (languageMatch.Success)
        {
            return IsoLanguages.Find(languageMatch.Groups["iso_code"].Value)?.Language ?? Language.Unknown;
        }

        return Language.All.FirstOrDefault(l => simpleFilename.EndsWith(l.Name, StringComparison.OrdinalIgnoreCase)) ?? Language.Unknown;
    }

    /// <summary>Language, tags (forced, sdh, ...) and track title of a subtitle file ("Name.Commentary.en.forced.srt").</summary>
    public static SubtitleTitleInfo ParseSubtitleLanguageInformation(string fileName)
    {
        var simpleFilename = Path.GetFileNameWithoutExtension(fileName);
        var matchTitle = SubtitleLanguageTitleRegex.Match(simpleFilename);

        if (!matchTitle.Groups["title"].Success || matchTitle.Groups["iso_code"].Captures.Count != 1)
        {
            return new SubtitleTitleInfo
            {
                LanguageTags = ParseLanguageTags(fileName),
                Language = ParseSubtitleLanguage(fileName),
            };
        }

        var rawTitle = matchTitle.Groups["title"].Value;
        var copyMatch = SubtitleTitleRegex.Match(rawTitle);

        return new SubtitleTitleInfo
        {
            TitleFirst = matchTitle.Groups["tags1"].Captures.Count == 0,
            LanguageTags = matchTitle.Groups["tags1"].Captures
                .Concat(matchTitle.Groups["tags2"].Captures)
                .Where(tag => tag.Value != "")
                .Select(tag => tag.Value.ToLowerInvariant())
                .ToList(),
            RawTitle = rawTitle,
            Title = copyMatch.Success ? (copyMatch.Groups["title"].Success ? copyMatch.Groups["title"].Value : null) : rawTitle,
            Copy = copyMatch.Success ? int.Parse(copyMatch.Groups["copy"].Value) : 0,
            Language = IsoLanguages.Find(matchTitle.Groups["iso_code"].Value)?.Language ?? Language.Unknown,
        };
    }

    public static List<string> ParseLanguageTags(string fileName)
    {
        var match = SubtitleLanguageRegex.Match(Path.GetFileNameWithoutExtension(fileName));

        return match.Groups["tags"].Captures
            .Where(tag => tag.Value != "")
            .Select(tag => tag.Value.ToLowerInvariant())
            .ToList();
    }
}
