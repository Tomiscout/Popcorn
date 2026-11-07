using System.Text.RegularExpressions;

namespace Popcorn.FilenameParser;

/// <summary>What a media file's name says about it.</summary>
public sealed record ParsedMediaName
{
    public string? Title { get; init; }
    public int? Year { get; init; }

    /// <summary>The name looks like an episode, otherwise it is treated as a movie.</summary>
    public bool IsTv { get; init; }
    public IReadOnlyList<int> Seasons { get; init; } = [];

    /// <summary>Episode numbers within the season, or absolute numbers (anime) when the name has no season.</summary>
    public IReadOnlyList<int> Episodes { get; init; } = [];

    /// <summary>Episode name written after the episode number ("Show.S01E08.Hello.World.1080p" -> "Hello World"), null if none.</summary>
    public string? EpisodeTitle { get; init; }

    /// <summary>
    /// The name without its release tags: everything up to the first tag after the episode number / year, leading
    /// "[Group]" removed ("Show.S01E08.Hello.World.1080p.WEB.h264-GRP[site]" -> "Show S01E08 Hello World"). Null if the path is empty.
    /// </summary>
    public string? CleanName { get; init; }

    /// <summary>Vertical resolution (1080 for 1080p), null if the name does not say.</summary>
    public int? Resolution { get; init; }
    /// <summary>Release source as written in the name ("WEB", "WEB-DL", "BluRay"...), null if none.</summary>
    public string? Source { get; init; }
    public string? VideoCodec { get; init; }
    public string? ReleaseGroup { get; init; }
}

/// <summary>
/// Entry point of the library: runs the episode and movie parsers on a path and picks the one that fits.
/// The parsers (<see cref="EpisodeParser"/>, <see cref="MovieParser"/>, <see cref="QualityParser"/>,
/// <see cref="LanguageParser"/>, <see cref="ReleaseGroupParser"/>) can be used directly for the full detail.
/// </summary>
public static partial class MediaNameParser
{
    // The quality parser only uses the codec to infer the source; report what the name says.
    private static readonly (string Name, Regex Regex)[] Codecs =
    [
        ("H.265", Codecs0),
        ("H.264", Codecs1),
        ("AV1", Codecs2),
        ("VP9", Codecs3),
        ("XviD", Codecs4),
        ("DivX", Codecs5),
        ("VC-1", Codecs6),
        ("MPEG-2", Codecs7),
    ];

    [GeneratedRegex(@"\b(?:[xh][-_. ]?265|HEVC)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Codecs0 { get; }

    [GeneratedRegex(@"\b(?:[xh][-_. ]?264|AVC)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Codecs1 { get; }

    [GeneratedRegex(@"\bAV1\b", RegexOptions.IgnoreCase)]
    private static partial Regex Codecs2 { get; }

    [GeneratedRegex(@"\bVP9\b", RegexOptions.IgnoreCase)]
    private static partial Regex Codecs3 { get; }

    [GeneratedRegex(@"\bX-?vid(?:HD)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex Codecs4 { get; }

    [GeneratedRegex(@"\bDivX\b", RegexOptions.IgnoreCase)]
    private static partial Regex Codecs5 { get; }

    [GeneratedRegex(@"\bVC[-_. ]?1\b", RegexOptions.IgnoreCase)]
    private static partial Regex Codecs6 { get; }

    [GeneratedRegex(@"\bMPEG[-_. ]?2\b", RegexOptions.IgnoreCase)]
    private static partial Regex Codecs7 { get; }

    // Release sources, most specific first; looked for in the tag part only (after the episode number / year).
    private static readonly (string Name, Regex Regex)[] Sources =
    [
        ("WEB-DL", Sources0),
        ("WEBRip", Sources1),
        ("WEB", Sources2),
        ("Remux", Sources3),
        ("BluRay", Sources4),
        ("BDRip", Sources5),
        ("BRRip", Sources6),
        ("HDTV", Sources7),
        ("HDRip", Sources8),
        ("DVDRip", Sources9),
    ];

    [GeneratedRegex(@"\bWEB[-_. ]?DL\b", RegexOptions.IgnoreCase)]
    private static partial Regex Sources0 { get; }

    [GeneratedRegex(@"\bWEB[-_. ]?Rip\b", RegexOptions.IgnoreCase)]
    private static partial Regex Sources1 { get; }

    [GeneratedRegex(@"\bWEB\b", RegexOptions.IgnoreCase)]
    private static partial Regex Sources2 { get; }

    [GeneratedRegex(@"\b(?:BD)?Remux\b", RegexOptions.IgnoreCase)]
    private static partial Regex Sources3 { get; }

    [GeneratedRegex(@"\bBlu[-_. ]?Ray\b", RegexOptions.IgnoreCase)]
    private static partial Regex Sources4 { get; }

    [GeneratedRegex(@"\bBDRip\b", RegexOptions.IgnoreCase)]
    private static partial Regex Sources5 { get; }

    [GeneratedRegex(@"\bBRRip\b", RegexOptions.IgnoreCase)]
    private static partial Regex Sources6 { get; }

    [GeneratedRegex(@"\bHDTV\b", RegexOptions.IgnoreCase)]
    private static partial Regex Sources7 { get; }

    [GeneratedRegex(@"\bHDRip\b", RegexOptions.IgnoreCase)]
    private static partial Regex Sources8 { get; }

    [GeneratedRegex(@"\bDVDRip\b", RegexOptions.IgnoreCase)]
    private static partial Regex Sources9 { get; }

    // The episode number the episode name follows: S01E08 (E09...), 1x08, Part 1, or a leading "08 - " (season in the folder).
    [GeneratedRegex(@"\bS\d{1,4}(?:[ ._-]?E\d{1,4})+\b|\b\d{1,2}x\d{2,4}(?:[x-]\d{2,4})*\b|\bPart[ ._]?\d{1,2}\b|^\d{1,4}(?=[ ._]*-[ ._])", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeToken { get; }

    // Where the title / episode name ends: the first release tag (quality, source, streaming service, codec, audio) or bracket.
    [GeneratedRegex(@"[\[\(]|\b(?:\d{3,4}[pi]|4k|uhd|web(?:[-_. ]?(?:dl|rip))?|blu-?ray|bdrip|brrip|bdremux|remux|hdtv|hdrip|dvdrip|amzn|nf|dsnp|hmax|atvp|hulu|repack|proper|internal|multi|[xh][-_. ]?26[45]|hevc|avc|10bit|ddp?\d?|aac\d?|ac3|dts|atmos|hdr|dv)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReleaseTagStart { get; }

    // A movie title ends with its year; brackets included so "Movie (2010) 1080p" keeps "(2010)" whole.
    [GeneratedRegex(@"[\(\[]?\b(?:19|20)\d{2}\b[\)\]]?")]
    private static partial Regex YearToken { get; }

    [GeneratedRegex(@"^(?:\s*(?:\[[^\]]*\]|\([^\)]*\)))+[\s._-]*")]
    private static partial Regex LeadingBrackets { get; }

    /// <param name="path">File path; parent folder names are used when the file name alone is not enough (Season 1/01.mkv).</param>
    public static ParsedMediaName Parse(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new ParsedMediaName();
        }

        var fileName = Path.GetFileName(path);
        var quality = QualityParser.ParseQuality(fileName);
        var resolution = quality.ResolutionFromName && quality.Quality.Resolution > 0 ? quality.Quality.Resolution : (int?)null;
        var codec = Codecs.FirstOrDefault(c => c.Regex.IsMatch(fileName)).Name;
        var name = LeadingBrackets.Replace(Path.GetFileNameWithoutExtension(path), "");
        var tagsStart = TagsStart(name);
        var source = Sources.FirstOrDefault(s => s.Regex.IsMatch(name, tagsStart)).Name;
        var cleanName = ParseCleanName(name, tagsStart);

        // Each parser assumes it is given its own kind of release, so each parses some names of the other kind.
        var episode = EpisodeParser.ParsePath(path);
        var movie = MovieParser.ParsePath(path);

        if (episode != null && IsEpisode(episode, movie))
        {
            return new ParsedMediaName
            {
                Title = episode.SeriesTitle,
                Year = episode.SeriesTitleInfo.Year > 0 ? episode.SeriesTitleInfo.Year : null,
                IsTv = true,
                // Season 0 is what absolute (anime) and daily numbering get, it would read as specials.
                Seasons = episode.EpisodeNumbers.Length > 0 || episode.FullSeason ? episode.SeasonNumbers : [],
                Episodes = episode.EpisodeNumbers.Length > 0 ? episode.EpisodeNumbers : episode.AbsoluteEpisodeNumbers,
                EpisodeTitle = ParseEpisodeTitle(Path.GetFileNameWithoutExtension(path)),
                CleanName = cleanName,
                Resolution = resolution,
                Source = source,
                VideoCodec = codec,
                ReleaseGroup = episode.ReleaseGroup,
            };
        }

        return new ParsedMediaName
        {
            Title = movie?.PrimaryMovieTitle,
            Year = movie?.Year > 0 ? movie.Year : null,
            CleanName = cleanName,
            Resolution = resolution,
            Source = source,
            VideoCodec = codec,
            ReleaseGroup = movie != null ? movie.ReleaseGroup : ReleaseGroupParser.ParseReleaseGroup(fileName),
        };
    }

    private static string? ParseEpisodeTitle(string name)
    {
        var token = EpisodeToken.Match(name);
        if (!token.Success) return null;

        var rest = name[(token.Index + token.Length)..];
        var end = ReleaseTagStart.Match(rest);
        if (end.Success) rest = rest[..end.Index];

        var title = Tidy(rest, dotsAreSpaces: !name.Contains(' '));
        return title.Length > 0 ? title : null;
    }

    /// <summary>Where release tags may start: after the episode number, else after the first year, else anywhere.
    /// Keeps title words that look like tags ("The Proper Way") when the name has one of those.</summary>
    private static int TagsStart(string name)
    {
        var token = EpisodeToken.Match(name);
        if (token.Success) return token.Index + token.Length;
        var year = YearToken.Match(name);
        return year.Success ? year.Index + year.Length : 0;
    }

    private static string? ParseCleanName(string name, int tagsStart)
    {
        if (name.Length == 0) return null;
        var end = ReleaseTagStart.Match(name, tagsStart);
        var clean = Tidy(end.Success ? name[..end.Index] : name, dotsAreSpaces: !name.Contains(' '));
        // Nothing before the first tag: better the whole name than an empty one.
        return clean.Length > 0 ? clean : Tidy(name, dotsAreSpaces: !name.Contains(' '));
    }

    /// <summary>Dotted scene names use dots as spaces; a name with spaces keeps its dots ("Mr. Robot").</summary>
    private static string Tidy(string text, bool dotsAreSpaces)
    {
        if (dotsAreSpaces) text = text.Replace('.', ' ');
        return string.Join(' ', text.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '-', '.');
    }

    private static bool IsEpisode(ParsedEpisodeInfo episode, ParsedMovieInfo? movie)
    {
        if (episode.EpisodeNumbers.Length == 0 && episode.AbsoluteEpisodeNumbers.Length == 0 && !episode.FullSeason && !episode.IsDaily)
        {
            return false;
        }

        // The episode parser reads a bare year as season + episode ("Inception.2010.1080p" -> S20E10).
        var yearReadAsEpisode = movie != null && episode.SeasonNumbers.Count == 1 && episode.EpisodeNumbers.Length == 1 &&
                                episode.SeasonNumbers[0] * 100 + episode.EpisodeNumbers[0] == movie.Year;

        return !yearReadAsEpisode;
    }
}
