using System.Text.RegularExpressions;

namespace Popcorn.FilenameParser;

/// <summary>
/// Movie release names: title + year (+ edition, IMDb/TMDb id, hardcoded subs). Radarr's parser. It assumes the name is
/// a movie and needs a year (or a "[PassThePopcorn]"-style tag) to recognise the title; see <see cref="MediaNameParser"/>.
/// </summary>
public static partial class MovieParser
{
    private const string EditionPattern = @"\(?\b(?<edition>(((Recut.|Extended.|Ultimate.)?(Director.?s|Collector.?s|Theatrical|Ultimate|Extended|Despecialized|(Special|Rouge|Final|Assembly|Imperial|Diamond|Signature|Hunter|Rekall)(?=(.(Cut|Edition|Version)))|\d{2,3}(th)?.Anniversary)(?:.(Cut|Edition|Version))?(.(Extended|Uncensored|Remastered|Unrated|Uncut|Open.?Matte|IMAX|Fan.?Edit))?|((Uncensored|Remastered|Unrated|Uncut|Open?.Matte|IMAX|Fan.?Edit|Restored|((2|3|4)in1))))))\b\)?";

    [GeneratedRegex(@"^.+?" + EditionPattern, RegexOptions.IgnoreCase)]
    private static partial Regex ReportEditionRegex { get; }

    [GeneratedRegex(@"\b((?<hcsub>(\w+(?<!SOFT|MULTI|HORRIBLE)SUBS?))|(?<hc>(HC|SUBBED)))\b", RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex HardcodedSubsRegex { get; }

    private static readonly Regex[] ReportMovieTitleRegex =
    [
        ReportMovieTitle0,
        ReportMovieTitle1,
        ReportMovieTitle2,
        ReportMovieTitle3,
        ReportMovieTitle4,
        ReportMovieTitle5,
        ReportMovieTitle6,
        ReportMovieTitle7,
        ReportMovieTitle8,
        ReportMovieTitle9,
        ReportMovieTitle10,
    ];

    // Anime [Subgroup] with year explicitly in parentheses, e.g. [Sub] Title 1993 (1993) [VHS]
    // This must come before the generic anime+year regex to correctly handle titles that contain a year as part of the name
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+?)\s*\((?<year>(1(8|9)|20)\d{2})\).*?(?<hash>\[\w{8}\])?(?:$|\.)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportMovieTitle0 { get; }

    // Anime [Subgroup] and Year
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|x|\d+|\]|\W\d+)))+.*?(?<hash>\[\w{8}\])?(?:$|\.)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportMovieTitle1 { get; }

    // Anime [Subgroup] no year, versioned title, hash
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+?)((v)(?:\d{1,2})(?:([-_. ])))(\[.*)?(?:[\[(][^])])?.*?(?<hash>\[\w{8}\])(?:$|\.)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportMovieTitle2 { get; }

    // Anime [Subgroup] no year, info in double sets of brackets, hash
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+?)(\[.*).*?(?<hash>\[\w{8}\])(?:$|\.)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportMovieTitle3 { get; }

    // Anime [Subgroup] no year, info in parentheses or brackets, hash
    [GeneratedRegex(@"^(?:\[(?<subgroup>.+?)\][-_. ]?)(?<title>(?![(\[]).+)(?:[\[(][^])]).*?(?<hash>\[\w{8}\])(?:$|\.)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportMovieTitle4 { get; }

    // Some german or french tracker formats (missing year, ...) (Only applies to german and TrueFrench releases) - see ParserFixture for examples and tests - french removed as it broke all movies w/ french titles
    [GeneratedRegex(@"^(?<title>(?![(\[]).+?)((\W|_))(" + EditionPattern + @".{1,3})?(?:(?<!(19|20)\d{2}.*?)(?<!(?:Good|The)[_ .-])(German|TrueFrench))(.+?)(?=((19|20)\d{2}|$))(?<year>(19|20)\d{2}(?!p|i|\d+|\]|\W\d+))?(\W+|_|$)(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportMovieTitle5 { get; }

    // Special, Despecialized, etc. Edition Movies, e.g: Mission.Impossible.3.Special.Edition.2011
    [GeneratedRegex(@"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*" + EditionPattern + @".{1,3}(?<year>(1(8|9)|20)\d{2}(?!p|i|\d+|\]|\W\d+)))+(\W+|_|$)(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportMovieTitle6 { get; }

    // Special, Despecialized, etc. Edition Movies, e.g: Mission.Impossible.3.2011.Special.Edition //TODO: Seems to slow down parsing heavily!
    /*new Regex(@"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(19|20)\d{2}(?!p|i|(19|20)\d{2}|\]|\W(19|20)\d{2})))+(\W+|_|$)(?!\\)\(?(?<edition>(((Extended.|Ultimate.)?(Director.?s|Collector.?s|Theatrical|Ultimate|Final(?=(.(Cut|Edition|Version)))|Extended|Rogue|Special|Despecialized|\d{2,3}(th)?.Anniversary)(.(Cut|Edition|Version))?(.(Extended|Uncensored|Remastered|Unrated|Uncut|IMAX|Fan.?Edit))?|((Uncensored|Remastered|Unrated|Uncut|IMAX|Fan.?Edit|Edition|Restored|((2|3|4)in1))))))\)?",
    RegexOptions.IgnoreCase | RegexOptions.Compiled),*/

    // Normal movie format, e.g: Mission.Impossible.3.2011
    [GeneratedRegex(@"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|(1(8|9)|20)\d{2}|\]|\W(1(8|9)|20)\d{2})))+(\W+|_|$)(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportMovieTitle7 { get; }

    // PassThePopcorn Torrent names: Star.Wars[PassThePopcorn]
    [GeneratedRegex(@"^(?<title>.+?)?(?:(?:[-_\W](?<![()\[!]))*(?<year>(\[\w *\])))+(\W+|_|$)(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportMovieTitle8 { get; }

    // That did not work? Maybe some tool uses [] for years. Who would do that?
    [GeneratedRegex(@"^(?<title>(?![(\[]).+?)?(?:(?:[-_\W](?<![)!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|\d+|\W\d+)))+(\W+|_|$)(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportMovieTitle9 { get; }

    // As a last resort for movies that have ( or [ in their title.
    [GeneratedRegex(@"^(?<title>.+?)?(?:(?:[-_\W](?<![)\[!]))*(?<year>(1(8|9)|20)\d{2}(?!p|i|\d+|\]|\W\d+)))+(\W+|_|$)(?!\\)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportMovieTitle10 { get; }

    private static readonly Regex[] ReportMovieTitleFolderRegex =
    [
        ReportMovieTitleFolder0,
    ];

    // When year comes first.
    [GeneratedRegex(@"^(?:(?:[-_\W](?<![)!]))*(?<year>(19|20)\d{2}(?!p|i|\d+|\W\d+)))+(\W+|_|$)(?<title>.+?)?$")]
    private static partial Regex ReportMovieTitleFolder0 { get; }

    // Regex to split movie titles that contain `AKA`.
    [GeneratedRegex(@"[ ]+(?:AKA|\/)[ ]+", RegexOptions.IgnoreCase)]
    private static partial Regex AlternativeTitleRegex { get; }

    // Regex to unbracket alternative titles.
    [GeneratedRegex(@"(.*) \([ ]*AKA[ ]+(.*)\)", RegexOptions.IgnoreCase)]
    private static partial Regex BracketedAlternativeTitleRegex { get; }

    [GeneratedRegex(@"[ ]+(?:A\.K\.A\.)[ ]+", RegexOptions.IgnoreCase)]
    private static partial Regex NormalizeAlternativeTitleRegex { get; }

    [GeneratedRegex(@"(?<imdbid>tt\d{7,8})", RegexOptions.IgnoreCase)]
    private static partial Regex ReportImdbId { get; }
    [GeneratedRegex(@"tmdb(id)?-(?<tmdbid>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ReportTmdbId { get; }

    // Quality tags that would otherwise read as part of the title or as a year.
    private static readonly RegexReplace SimpleTitleRegex = new(SimpleTitlePattern, string.Empty);

    [GeneratedRegex(@"(?:(480|540|576|720|1080|2160)[ip]|[xh][\W_]?26[45]|DD\W?5\W1|[<>?*]|848x480|1280x720|1920x1080|3840x2160|4096x2160|(8|10)b(it)?|10-bit)\s*?(?![a-b0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex SimpleTitlePattern { get; }

    [GeneratedRegex(@"\s*(?:[<>?*|])", RegexOptions.IgnoreCase)]
    private static partial Regex SimpleReleaseTitleRegex { get; }

    /// <summary>Parses the file name, falling back to "folder + file name" and the folder name ("Movie (2010)/video.mkv").</summary>
    public static ParsedMovieInfo? ParsePath(string path)
    {
        var fileInfo = new FileInfo(path);
        var result = ParseTitle(fileInfo.Name, true);
        var directoryName = fileInfo.Directory?.Name;

        if (result != null || directoryName == null)
        {
            return result;
        }

        return ParseTitle(directoryName + " " + fileInfo.Name) ?? ParseTitle(directoryName + fileInfo.Extension);
    }

    /// <param name="title">Release or file name.</param>
    /// <param name="isDir">Also accept "2010 Movie Title" (year first), as folders are often named.</param>
    public static ParsedMovieInfo? ParseTitle(string title, bool isDir = false)
    {
        try
        {
            if (!ParserCommon.IsParsable(title))
            {
                return null;
            }

            title = ParserCommon.UnreverseTitle(title);

            var releaseTitle = ParserCommon.RemoveFileExtension(title).Trim('-', '_').Replace("【", "[").Replace("】", "]");

            var simpleTitle = SimpleTitleRegex.Replace(releaseTitle);
            simpleTitle = ParserCommon.RemoveWebsites(simpleTitle);
            simpleTitle = ParserCommon.RemoveQualityBrackets(simpleTitle);

            foreach (var regex in isDir ? ReportMovieTitleRegex.Concat(ReportMovieTitleFolderRegex) : ReportMovieTitleRegex)
            {
                var matches = regex.Matches(simpleTitle);

                if (matches.Count == 0)
                {
                    continue;
                }

                var result = ParseMovieMatch(matches[0]);

                if (result == null)
                {
                    continue;
                }

                // Replace the title with a placeholder so that words in it do not read as group / language / edition.
                var simpleReleaseTitle = SimpleReleaseTitleRegex.Replace(releaseTitle, string.Empty);
                var titleGroup = matches[0].Groups["title"];
                var movieTitle = titleGroup.Success ? titleGroup.Value : result.PrimaryMovieTitle;

                if (!string.IsNullOrWhiteSpace(movieTitle))
                {
                    var placeholder = movieTitle.Contains('.') ? "A.Movie" : "A Movie";

                    simpleReleaseTitle = titleGroup.Success
                        ? simpleReleaseTitle.Remove(titleGroup.Index, titleGroup.Length).Insert(titleGroup.Index, placeholder)
                        : simpleReleaseTitle.Replace(movieTitle, placeholder);
                }

                result.ReleaseGroup = ParserCommon.GetSubGroup(matches[0]) ?? ReleaseGroupParser.ParseReleaseGroup(simpleReleaseTitle);
                result.HardcodedSubs = ParseHardcodedSubs(title);
                result.Languages = LanguageParser.ParseLanguages(string.IsNullOrWhiteSpace(result.ReleaseGroup)
                    ? simpleReleaseTitle
                    : simpleReleaseTitle.Replace(result.ReleaseGroup, "RlsGrp"));
                result.Quality = QualityParser.ParseQuality(title);

                if (string.IsNullOrWhiteSpace(result.Edition))
                {
                    result.Edition = ParseEdition(simpleReleaseTitle);
                }

                result.ReleaseHash = ParserCommon.GetReleaseHash(matches[0]);
                result.ReleaseTitle = releaseTitle;
                result.ImdbId = ParseImdbId(simpleReleaseTitle);
                result.TmdbId = ParseTmdbId(simpleReleaseTitle);

                return result;
            }
        }
        catch (Exception)
        {
            // File names are arbitrary input, never fail on one.
        }

        return null;
    }

    public static string ParseImdbId(string title)
    {
        var match = ReportImdbId.Match(title);

        return match.Success && match.Groups["imdbid"].Length is 9 or 10 ? match.Groups["imdbid"].Value : "";
    }

    public static int ParseTmdbId(string title)
    {
        var match = ReportTmdbId.Match(title);

        return match.Success && int.TryParse(match.Groups["tmdbid"].Value, out var tmdbId) ? tmdbId : 0;
    }

    public static string ParseEdition(string title)
    {
        var match = ReportEditionRegex.Match(title);

        return match.Success ? match.Groups["edition"].Value.Replace(".", " ") : "";
    }

    public static string? ParseHardcodedSubs(string title)
    {
        var match = HardcodedSubsRegex.Matches(title).LastOrDefault();

        if (match == null)
        {
            return null;
        }

        return match.Groups["hcsub"].Success ? match.Groups["hcsub"].Value : "Generic Hardcoded Subs";
    }

    private static ParsedMovieInfo? ParseMovieMatch(Match match)
    {
        var titleGroup = match.Groups["title"];

        if (!titleGroup.Success || titleGroup.Value == "(")
        {
            return null;
        }

        var movieName = titleGroup.Value.Replace('_', ' ');
        movieName = NormalizeAlternativeTitleRegex.Replace(movieName, " AKA ");
        movieName = ParserCommon.RequestInfoRegex.Replace(movieName, "").Trim(' ');
        movieName = JoinDottedTitle(movieName);

        int.TryParse(match.Groups["year"].Value, out var year);

        var result = new ParsedMovieInfo
        {
            Year = year,
            Edition = match.Groups["edition"].Success ? match.Groups["edition"].Value.Replace(".", " ") : "",
        };

        // The title, then its alternatives: "Title AKA Other", "Title (AKA Other)", "Title / Other".
        var unbracketedName = BracketedAlternativeTitleRegex.Replace(movieName, "$1 AKA $2");

        result.MovieTitles.Add(movieName);
        result.MovieTitles.AddRange(AlternativeTitleRegex.Split(unbracketedName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && name != movieName));

        return result;
    }

    // "Mission.Impossible.3" -> "Mission Impossible 3", keeping acronyms ("S.H.I.E.L.D", "Dr.") dotted.
    private static string JoinDottedTitle(string title)
    {
        var parts = title.Split('.');
        var result = "";
        var previousAcronym = false;

        for (var n = 0; n < parts.Length; n++)
        {
            var part = parts[n];
            var nextPart = n + 1 < parts.Length ? parts[n + 1] : "";
            var lower = part.ToLowerInvariant();

            var isAcronymLetter = part.Length == 1 && lower != "a" && !int.TryParse(part, out _) &&
                                  (previousAcronym || n < parts.Length - 1) &&
                                  (previousAcronym || nextPart.Length != 1 || !int.TryParse(nextPart, out _));

            if (isAcronymLetter || (lower == "a" && (previousAcronym || nextPart.Length == 1)) || lower == "dr")
            {
                result += part + ".";
                previousAcronym = true;
            }
            else
            {
                if (previousAcronym)
                {
                    result += " ";
                    previousAcronym = false;
                }

                result += part + " ";
            }
        }

        return result.Trim(' ');
    }
}
