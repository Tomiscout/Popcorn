using System.Text.RegularExpressions;

namespace Popcorn.FilenameParser;

/// <summary>A regex and its replacement, applied as one step of a clean-up pipeline.</summary>
internal sealed class RegexReplace(Regex regex, string replacement)
{
    public string Replace(string input) => regex.Replace(input, replacement);

    /// <summary>Replaces in place, returns whether the pattern matched.</summary>
    public bool TryReplace(ref string input)
    {
        if (!regex.IsMatch(input))
        {
            return false;
        }

        input = regex.Replace(input, replacement);
        return true;
    }
}

/// <summary>Clean-up steps shared by the episode, movie and release group parsers.</summary>
internal static partial class ParserCommon
{
    // Video and container extensions stripped before parsing (anything else, e.g. ".5" in "DD.5", is kept).
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".webm", ".m4v", ".3gp", ".nsv", ".ty", ".strm", ".rm", ".rmvb", ".m3u", ".ifo", ".mov", ".qt", ".divx", ".xvid",
        ".bivx", ".nrg", ".pva", ".wmv", ".asf", ".asx", ".ogm", ".ogv", ".m2v", ".avi", ".bin", ".dat", ".dvr-ms", ".mpg",
        ".mpeg", ".mp4", ".avc", ".vp3", ".svq3", ".nuv", ".viv", ".dv", ".fli", ".flv", ".wpl", ".img", ".iso", ".vob",
        ".mkv", ".mk3d", ".ts", ".wtv", ".m2ts",
        // Usenet
        ".par2", ".nzb",
    };

    [GeneratedRegex(@"\.[a-z0-9]{2,4}$", RegexOptions.IgnoreCase)]
    private static partial Regex FileExtensionRegex { get; }

    // Scene releases that were posted reversed ("p0801.10E10S.eltiT").
    [GeneratedRegex(@"(?:^|[-._ ])(p027|p0801|\d{2,3}E-?\d{2}S)[-._ ]")]
    private static partial Regex ReversedTitleRegex { get; }

    private static readonly Regex[] RejectHashedReleasesRegexes =
    [
        RejectHashedReleases0,
        RejectHashedReleases1,
        RejectHashedReleases2,
        RejectHashedReleases3,
        RejectHashedReleases4,
        RejectHashedReleases5,
        RejectHashedReleases6,
        RejectHashedReleases7,
        RejectHashedReleases8,
        RejectHashedReleases9,
        RejectHashedReleases10,
        RejectHashedReleases11,
        RejectHashedReleases12,
        RejectHashedReleases13,
    ];

    // Generic match for md5 and mixed-case hashes.
    [GeneratedRegex(@"^[0-9a-zA-Z]{32}")]
    private static partial Regex RejectHashedReleases0 { get; }

    // Generic match for shorter lower-case hashes.
    [GeneratedRegex(@"^[a-z0-9]{24}$")]
    private static partial Regex RejectHashedReleases1 { get; }

    // Format seen on some NZBGeek releases
    // Be very strict with these coz they are very close to the valid 101 ep numbering.
    [GeneratedRegex(@"^[A-Z]{11}\d{3}$")]
    private static partial Regex RejectHashedReleases2 { get; }

    [GeneratedRegex(@"^[a-z]{12}\d{3}$")]
    private static partial Regex RejectHashedReleases3 { get; }

    // Backup filename (Unknown origins)
    [GeneratedRegex(@"^Backup_\d{5,}S\d{2}-\d{2}$")]
    private static partial Regex RejectHashedReleases4 { get; }

    // 123 - Started appearing December 2014
    [GeneratedRegex(@"^123$")]
    private static partial Regex RejectHashedReleases5 { get; }

    // abc - Started appearing January 2015
    [GeneratedRegex(@"^abc$", RegexOptions.IgnoreCase)]
    private static partial Regex RejectHashedReleases6 { get; }

    // abc - Started appearing 2020
    [GeneratedRegex(@"^abc[-_. ]xyz", RegexOptions.IgnoreCase)]
    private static partial Regex RejectHashedReleases7 { get; }

    // b00bs - Started appearing January 2015
    [GeneratedRegex(@"^b00bs$", RegexOptions.IgnoreCase)]
    private static partial Regex RejectHashedReleases8 { get; }

    // 170424_26 - Started appearing August 2018
    [GeneratedRegex(@"^\d{6}_\d{2}$")]
    private static partial Regex RejectHashedReleases9 { get; }

    // additional Generic match for mixed-case hashes. - Started appearing Dec 2020
    [GeneratedRegex(@"^[0-9a-zA-Z]{30}")]
    private static partial Regex RejectHashedReleases10 { get; }

    // additional Generic match for mixed-case hashes. - Started appearing Jan 2021
    [GeneratedRegex(@"^[0-9a-zA-Z]{26}")]
    private static partial Regex RejectHashedReleases11 { get; }

    // additional Generic match for mixed-case hashes. - Started appearing Jan 2021
    [GeneratedRegex(@"^[0-9a-zA-Z]{39}")]
    private static partial Regex RejectHashedReleases12 { get; }

    // additional Generic match for mixed-case hashes. - Started appearing Jan 2021
    [GeneratedRegex(@"^[0-9a-zA-Z]{24}")]
    private static partial Regex RejectHashedReleases13 { get; }

    // Anime / Chinese / Korean release layouts rewritten into the common "[Group] Title - 01" / "Title S01E01" forms.
    internal static readonly RegexReplace[] PreSubstitutionRegex =
    [
        new(PreSubstitution0, ".S01E$1.$2"),
        new(PreSubstitution1, "[${subgroup}] ${title} - ${episode} - "),
        new(PreSubstitution2, "[${subgroup}][${title}][${episode}]["),
        new(PreSubstitution3, "[${subgroup}] ${title} S${season} - ${episode} "),
        new(PreSubstitution4, "[${subgroup}] ${title} - ${episode} "),
        new(PreSubstitution5, "${title}S${season}E${episode}.${language}${rest}"),
        new(PreSubstitution6, "${title}S${season}.${language}${rest}"),
        new(PreSubstitution7, "[${subgroup}] ${title} - ${episode} "),
        new(PreSubstitution8, "[${subgroup}] ${title} S${season} - ${episode} "),
        new(PreSubstitution9, "[${subgroup}] ${title} - ${episode} "),
        new(PreSubstitution10, "[${subgroup}] ${title} - ${episode} "),
        new(PreSubstitution11, "[${subgroup}] ${title} - ${episode} "),
        new(PreSubstitution12, "[${subgroup}] ${title} - ${episode} "),
        new(PreSubstitution13, "${title} (${year}) - ${info} "),
    ];

    // Korean series without season number, replace with S01Exxx and remove airdate
    [GeneratedRegex(@"\.E(\d{2,4})\.\d{6}\.(.*-(F1RST|NEXT))$")]
    private static partial Regex PreSubstitution0 { get; }

    // Some Chinese anime releases contain both English and Chinese titles, remove the Chinese title and replace with normal anime pattern
    [GeneratedRegex(@"^\[(?:(?<subgroup>[^\]]+?)(?:[\u4E00-\u9FCC]+)?)\]\[(?<title>[^\]]+?)(?:\s(?<chinesetitle>[\u4E00-\u9FCC][^\]]*?))\]\[(?:(?:[\u4E00-\u9FCC]+?)?(?<episode>\d{1,4})(?:[\u4E00-\u9FCC]+?)?)\]")]
    private static partial Regex PreSubstitution1 { get; }

    // Chinese LoliHouse/ZERO/Lilith-Raws/Skymoon-Raws/orion origin releases don't use the expected brackets, normalize using brackets
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]*?(?:LoliHouse|ZERO|Lilith-Raws|Skymoon-Raws|orion origin)[^\]]*?)\](?<title>[^\[\]]+?)(?: - (?<episode>[0-9-]+)\s*|\[第?(?<episode>[0-9]+(?:-[0-9]+)?)话?(?:END|完)?\])\[")]
    private static partial Regex PreSubstitution2 { get; }

    // Most Chinese anime releases contain additional brackets/separators for chinese and non-chinese titles, remove junk first and if it has S0x as season number, convert it to Sx
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s?★[^\[ -]+\s?)?\[?(?:(?<chinesetitle>(?=[^\]]*?[\u4E00-\u9FCC])[^\]]*?)(?:\]\[|\s*[_/·]\s*)){0,2}(?<title>[^\[\]]+?)(?:\s(?:S?(?<!\d+)((0)(?<season>\d)|(?<season>[1-9]\d))(?!\d+)))\]?(?:\[\d{4}\])?\[第?(?<episode>[0-9]+(?:-[0-9]+)?)(?:话|集)?(?: ?END|完| ?Fin)?\]")]
    private static partial Regex PreSubstitution3 { get; }

    // Some Chinese releases don't include a separation between Chinese and English titles within the same bracketed group
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\]\[(?<chinesetitle>(?<![^a-zA-Z0-9])[^a-zA-Z0-9]+)(?<title>[^\]]+?)\](?:\[\d{4}\])?\[第?(?<episode>[0-9]+(?:-[0-9]+)?)(?:话|集)?(?: ?END|完| ?Fin)?\]")]
    private static partial Regex PreSubstitution4 { get; }

    // Chinese season packs, that may not actually have the full season so treated as multi-episode
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s?★[^\[ -]+\s?)?\[?(?:(?<chinesetitle>(?=[^\]]*?[\u4E00-\u9FCC])[^\]]*?)(?:\]\[|\s*[_/·]\s*)){0,2}(?<title>[^\]]+?)\]?(?:\[\d{4}\])?\[(?:第|全)?(?<episode>[0-9]{1,4}-[0-9]{1,4})(?:话|集)?\](?:\[(?<language>.+?)\+.+?\])[_. ](?<title>.+?)S(?<season>[0-9]{1,2})(?<rest>.+?)$")]
    private static partial Regex PreSubstitution5 { get; }

    // Chinese season packs, that actually have the full season so treated as multi-episode
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s?★[^\[ -]+\s?)?\[?(?:(?<chinesetitle>(?=[^\]]*?[\u4E00-\u9FCC])[^\]]*?)(?:\]\[|\s*[_/·]\s*)){0,2}(?<title>[^\]]+?)\]?(?:\[\d{4}\])?\[(?:全)?(?<episode>[0-9]{1,4})(?:话|集)?\](?:\[(?<language>.+?)\+.+?\])[_. ](?<title>.+?)S(?<season>[0-9]{1,2})(?<rest>.+?)$")]
    private static partial Regex PreSubstitution6 { get; }

    // Most Chinese anime releases contain additional brackets/separators for chinese and non-chinese titles, remove junk and replace with normal anime pattern
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s?★[^\[ -]+\s?)?\[?(?:(?<chinesetitle>(?=[^\]]*?[\u4E00-\u9FCC])[^\]]*?)(?:\]\[|\s*[_/·]\s*)){0,2}(?<title>[^\]]+?)\]?(?:\[\d{4}\])?\[第?(?<episode>[0-9]{1,4}(?:-[0-9]{1,4})?)(?:话|集)?(?: ?END|完| ?Fin)?\]")]
    private static partial Regex PreSubstitution7 { get; }

    // Some Chinese anime releases contain both Chinese and English titles, remove the Chinese title first and if it has S0x as season number, convert it to Sx
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s)(?:(?<chinesetitle>(?=[^\]]*?[\u4E00-\u9FCC])[^\]]*?)(?:\s/\s))(?<title>[^\[\]]+?)(?:\s(?:S?(?<!\d+)((0)(?<season>\d)|(?<season>[1-9]\d))(?!\d+)))(?:[- ]+)(?<episode>[0-9]+(?:-[0-9]+)?)话?(?:END|完)?")]
    private static partial Regex PreSubstitution8 { get; }

    // Some Chinese anime releases contain both English and Chinese titles, remove the Chinese title and replace with normal anime pattern
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s)(?:(?<title>[^\]]+?)(?:\s/\s))(?<chinesetitle>(?=[^\]]*?[\u4E00-\u9FCC])[^\]]*?)(?:[- ]+)(?<episode>[0-9]+(?:-[0-9]+)?(?![a-z]))话?(?:END|完)?")]
    private static partial Regex PreSubstitution9 { get; }

    // Some Chinese anime releases contain both Chinese and English titles, remove the Chinese title and replace with normal anime pattern
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s)(?:(?<chinesetitle>(?=[^\]]*?[\u4E00-\u9FCC])[^\]]*?)(?:\s/\s))(?<title>[^\]]+?)(?:[- ]+)(?<episode>[0-9]+(?:-[0-9]+)?(?![a-z]))话?(?:END|完)?")]
    private static partial Regex PreSubstitution10 { get; }

    // GM-Team releases with lots of square brackets
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:(?<chinesubgroup>\[(?=[^\]]*?[\u4E00-\u9FCC])[^\]]*\])+)\[(?<title>[^\]]+?)\](?<junk>\[[^\]]+\])*\[(?<episode>[0-9]+(?:-[0-9]+)?)( END| Fin)?\]")]
    private static partial Regex PreSubstitution11 { get; }

    // Some Chinese anime releases contain both Chinese and English titles separated by | instead of /, remove the Chinese title and replace with normal anime pattern
    [GeneratedRegex(@"^\[(?<subgroup>[^\]]+)\](?:\s)(?:(?<chinesetitle>(?=[^\]]*?[\u4E00-\u9FCC])[^\]]*?)(?:\s\|\s))(?<title>[^\]]+?)(?:[- ]+)(?<episode>[0-9]+(?:-[0-9]+)?(?![a-z]))话?(?:END|完)?")]
    private static partial Regex PreSubstitution12 { get; }

    // Spanish releases with information in brackets
    [GeneratedRegex(@"^(?<title>.+?(?=[ ._-]\()).+?\((?<year>\d{4})\/(?<info>S[^\/]+)")]
    private static partial Regex PreSubstitution13 { get; }

    // Valid TLDs http://data.iana.org/TLD/tlds-alpha-by-domain.txt
    private static readonly RegexReplace WebsitePrefixRegex = new(WebsitePrefixPattern, string.Empty);

    [GeneratedRegex(@"^(?:(?:\[|\()\s*)?(?:www\.)?[-a-z0-9-]{1,256}\.(?<!Naruto-Kun\.)(?:[a-z]{2,6}\.[a-z]{2,6}|xn--[a-z0-9-]{4,}|[a-z]{2,})\b(?:\s*(?:\]|\))|[ -]{2,})[ -]*", RegexOptions.IgnoreCase)]
    private static partial Regex WebsitePrefixPattern { get; }

    private static readonly RegexReplace WebsitePostfixRegex = new(WebsitePostfixPattern, string.Empty);

    [GeneratedRegex(@"(?:\[\s*)?(?:www\.)?[-a-z0-9-]{1,256}\.(?:xn--[a-z0-9-]{4,}|[a-z]{2,6})\b(?:\s*\])$", RegexOptions.IgnoreCase)]
    private static partial Regex WebsitePostfixPattern { get; }

    private static readonly RegexReplace CleanTorrentSuffixRegex = new(CleanTorrentSuffixPattern, string.Empty);

    [GeneratedRegex(@"\[(?:ettv|rartv|rarbg|cttv|publichd)\]$", RegexOptions.IgnoreCase)]
    private static partial Regex CleanTorrentSuffixPattern { get; }

    [GeneratedRegex(@"\[[a-z0-9 ._-]+\]$", RegexOptions.IgnoreCase)]
    private static partial Regex CleanQualityBracketsRegex { get; }

    // Request info in front of the title: "[REQ] Title".
    [GeneratedRegex(@"^(?:\[.+?\])+")]
    internal static partial Regex RequestInfoRegex { get; }

    internal static readonly string[] NumberWords = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];

    public static string RemoveFileExtension(string title) =>
        FileExtensionRegex.Replace(title, m => MediaExtensions.Contains(m.Value) ? string.Empty : m.Value);

    /// <summary>False for names that cannot be parsed: no letters/digits, obfuscated hashes, encrypted usenet posts.</summary>
    public static bool IsParsable(string title)
    {
        var lower = title.ToLowerInvariant();

        if (lower.Contains("password") && lower.Contains("yenc"))
        {
            return false;
        }

        if (!title.Any(char.IsLetterOrDigit))
        {
            return false;
        }

        var titleWithoutExtension = RemoveFileExtension(title);

        return !RejectHashedReleasesRegexes.Any(r => r.IsMatch(titleWithoutExtension));
    }

    public static string UnreverseTitle(string title)
    {
        if (!ReversedTitleRegex.IsMatch(title))
        {
            return title;
        }

        var reversed = RemoveFileExtension(title).ToCharArray();
        Array.Reverse(reversed);

        return string.Concat(new string(reversed), title.AsSpan(reversed.Length));
    }

    /// <summary>Strips "[www.site.org] - " prefixes, "[site.org]" postfixes and "[rarbg]"-style torrent suffixes.</summary>
    public static string RemoveWebsites(string title)
    {
        title = WebsitePrefixRegex.Replace(title);
        title = WebsitePostfixRegex.Replace(title);
        return CleanTorrentSuffixRegex.Replace(title);
    }

    /// <summary>Strips the website prefix and torrent suffix only (what the release group parser needs).</summary>
    public static string RemoveWebsitePrefixAndTorrentSuffix(string title) =>
        CleanTorrentSuffixRegex.Replace(WebsitePrefixRegex.Replace(title));

    /// <summary>Drops a trailing "[HDTV-720p]"-style bracket if it really is a quality.</summary>
    public static string RemoveQualityBrackets(string title) =>
        CleanQualityBracketsRegex.Replace(title, m => QualityParser.ParseQuality(m.Value).Quality != Quality.Unknown ? string.Empty : m.Value);

    /// <summary>The "[ABCD1234]" CRC that anime releases carry, empty if none.</summary>
    public static string GetReleaseHash(Match match)
    {
        var hash = match.Groups["hash"];

        if (!hash.Success)
        {
            return string.Empty;
        }

        var value = hash.Value.Trim('[', ']', '(', ')');

        return value == "1280x720" ? string.Empty : value;
    }

    public static string? GetSubGroup(Match match) =>
        match.Groups["subgroup"] is { Success: true } group && !string.IsNullOrWhiteSpace(group.Value) ? group.Value : null;
}
