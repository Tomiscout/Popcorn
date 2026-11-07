using System.Text.RegularExpressions;

namespace Popcorn.FilenameParser;

/// <summary>The scene/anime group that made a release ("-SPARKS", "[SubsPlease]").</summary>
public static partial class ReleaseGroupParser
{
    [GeneratedRegex(@"-(?<releasegroup>[A-Za-zÀ-ÖØ-öø-ÿ0-9]+(?<part2>-[A-Za-zÀ-ÖØ-öø-ÿ0-9]+)?(?!.+?(?:HDTV|SDTV|480p|576p|720p|1080p|2160p)))(?<!(?:HDTV|SDTV|WEB-(DL|Rip)|Blu-Ray|480p|576p|720p|1080p|2160p|DTS-HD|DTS-X|DTS-MA|DTS-ES|-ES|-EN|-CAT|-ENG|-JAP|-GER|-FRA|-FRE|-ITA|-HDRip|\d{1,2}-bit|[ ._]\d{4}-\d{2}|-\d{2}|tmdb(id)?-(?<tmdbid>\d+)|(?<imdbid>tt\d{7,8}))(?:\k<part2>)?)(?:\b|[-._ ]|$)|[-._ ]\[(?<releasegroup>[A-Za-zÀ-ÖØ-öø-ÿ0-9]+)\]$", RegexOptions.IgnoreCase)]
    private static partial Regex ReleaseGroupRegex { get; }

    [GeneratedRegex(@"^([se]\d+|[0-9a-f]{8})$", RegexOptions.IgnoreCase)]
    private static partial Regex InvalidReleaseGroupRegex { get; }

    [GeneratedRegex(@"^(?:\[(?<subgroup>(?!\s).+?(?<!\s))\](?:_|-|\s|\.)?)", RegexOptions.IgnoreCase)]
    private static partial Regex AnimeReleaseGroupRegex { get; }

    // Handle Exception Release Groups that don't follow -RlsGrp; Manual List
    // name only...be very careful with this last; high chance of false positives
    [GeneratedRegex(@"\b(?<releasegroup>KRaLiMaRKo|E\.N\.D|D\-Z0N3|Koten_Gars|BluDragon|ZØNEHD|HQMUX|VARYG|YIFY|YTS(.(MX|LT|AG))?|TMd|Eml HDTeam|LMain|DarQ|BEN[_. ]THE[_. ]MEN|TAoE|QxR|126811|Fight-BB|KCRT|Vialle)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ExceptionReleaseGroupRegexExact { get; }

    // groups whose releases end with RlsGroup) or RlsGroup]
    [GeneratedRegex(@"(?<=[._ \[])(?<releasegroup>(Silence|afm72|Panda|Ghost|MONOLITH|Tigole|Joy|ImE|UTR|t3nzin|Anime Time|Project Angel|Hakata Ramen|HONE|GiLG|Vyndros|SEV|Garshasp|Kappa|Natty|RCVR|SAMPA|YOGI|r00t|EDGE2020|RZeroX|FreetheFish|Anna|Bandi|Qman|theincognito|HDO|DusIctv|DHD|CtrlHD|-ZR-|ADC|XZVN|RH|Kametsu|Celdra)(?=\]|\)))", RegexOptions.IgnoreCase)]
    private static partial Regex ExceptionReleaseGroupRegex { get; }

    // Indexer / reposter junk after the real group, and everything up to S01E01 (the series title can contain dashes).
    private static readonly RegexReplace CleanReleaseGroupRegex = new(CleanReleaseGroupPattern, string.Empty);

    [GeneratedRegex(@"^(.*?[-._ ](S\d+E\d+)[-._ ])|(-(RP|1|NZBGeek|\[N-Z-B\]|Obfuscated|Obfuscation|Scrambled|sample|Pre|postbot|xpost|Rakuv[a-z0-9]*|WhiteRev|BUYMORE|AsRequested|AlternativeToRequested|GEROV|Z0iDS3N|Chamele0n|4P|4Planet|AlteZachen|RePACKPOST))+$", RegexOptions.IgnoreCase)]
    private static partial Regex CleanReleaseGroupPattern { get; }

    public static string? ParseReleaseGroup(string title)
    {
        title = ParserCommon.RemoveFileExtension(title.Trim());

        foreach (var replace in ParserCommon.PreSubstitutionRegex)
        {
            if (replace.TryReplace(ref title))
            {
                break;
            }
        }

        title = ParserCommon.RemoveWebsitePrefixAndTorrentSuffix(title);

        var animeMatch = AnimeReleaseGroupRegex.Match(title);

        if (animeMatch.Success)
        {
            return animeMatch.Groups["subgroup"].Value;
        }

        title = CleanReleaseGroupRegex.Replace(title);

        // Exact names first: "(... Tigole) [QxR]" is QxR's re-encode of Tigole's release.
        var exactMatches = ExceptionReleaseGroupRegexExact.Matches(title);

        if (exactMatches.Count != 0)
        {
            return exactMatches.Last().Groups["releasegroup"].Value;
        }

        var exceptionMatches = ExceptionReleaseGroupRegex.Matches(title);

        if (exceptionMatches.Count != 0)
        {
            return exceptionMatches.Last().Groups["releasegroup"].Value;
        }

        var matches = ReleaseGroupRegex.Matches(title);

        if (matches.Count == 0)
        {
            return null;
        }

        var group = matches.Last().Groups["releasegroup"].Value;

        return int.TryParse(group, out _) || InvalidReleaseGroupRegex.IsMatch(group) ? null : group;
    }
}
