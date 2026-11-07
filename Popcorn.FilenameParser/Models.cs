namespace Popcorn.FilenameParser;

/// <summary>Result of <see cref="EpisodeParser"/>.</summary>
public sealed class ParsedEpisodeInfo
{
    public string ReleaseTitle { get; set; } = "";
    public string SeriesTitle { get; set; } = "";
    public SeriesTitleInfo SeriesTitleInfo { get; set; } = new();
    public ParsedQuality Quality { get; set; } = new();
    public List<Language> Languages { get; set; } = [];
    public string? ReleaseGroup { get; set; }

    /// <summary>The "[ABCD1234]" CRC of anime releases, empty if none.</summary>
    public string ReleaseHash { get; set; } = "";

    /// <summary>0 for absolute (anime) and daily numbering.</summary>
    public List<int> SeasonNumbers { get; set; } = [];
    public int[] EpisodeNumbers { get; set; } = [];
    public int[] AbsoluteEpisodeNumbers { get; set; } = [];

    /// <summary>Absolute specials numbered between episodes ("Show - 12.5").</summary>
    public decimal[] SpecialAbsoluteEpisodeNumbers { get; set; } = [];

    /// <summary>yyyy-MM-dd for daily shows, null otherwise.</summary>
    public string? AirDate { get; set; }
    public int? DailyPart { get; set; }

    /// <summary>A whole season (or several, see <see cref="IsMultiSeason"/>) without episode numbers.</summary>
    public bool FullSeason { get; set; }
    public bool IsPartialSeason { get; set; }
    public int SeasonPart { get; set; }
    public bool IsSeasonExtra { get; set; }

    /// <summary>Anime season pack named only after the season's own title (<see cref="EpisodeParser.ParseSeasonTitle"/>).</summary>
    public bool IsSeasonTitle { get; set; }
    public bool IsSplitEpisode { get; set; }

    /// <summary>Episode numbers without a season ("Part 1"); <see cref="SeasonNumbers"/> is then [1].</summary>
    public bool IsMiniSeries { get; set; }
    public bool Special { get; set; }

    /// <summary>The part of the release title after the season/episode numbers (quality, languages, group).</summary>
    internal string ReleaseTokens { get; set; } = "";

    public bool IsMultiSeason => SeasonNumbers.Count > 1;
    public bool IsDaily => !string.IsNullOrWhiteSpace(AirDate);
    public bool IsAbsoluteNumbering => AbsoluteEpisodeNumbers.Length > 0;

    public bool IsPossibleSpecialEpisode =>
        (string.IsNullOrWhiteSpace(AirDate) && string.IsNullOrWhiteSpace(SeriesTitle) && (EpisodeNumbers.Length == 0 || SeasonNumbers.FirstOrDefault(-1) == 0)) ||
        (!string.IsNullOrWhiteSpace(SeriesTitle) && Special) ||
        EpisodeNumbers is [0];

    public bool IsPossibleSceneSeasonSpecial => SeasonNumbers.FirstOrDefault() != 0 && EpisodeNumbers is [0];

    public override string ToString()
    {
        var episode = IsDaily && EpisodeNumbers.Length == 0 ? AirDate
            : FullSeason ? FormatSeasons()
            : EpisodeNumbers.Length > 0 ? $"S{SeasonNumbers.FirstOrDefault():00}E{string.Join("-", EpisodeNumbers.Select(e => e.ToString("00")))}"
            : AbsoluteEpisodeNumbers.Length > 0 ? string.Join("-", AbsoluteEpisodeNumbers.Select(e => e.ToString("000")))
            : Special ? "[Special]"
            : "[Unknown Episode]";

        return $"{SeriesTitle} - {episode} {Quality}";
    }

    // "Season 02", "Seasons 01-03", "Seasons 01, 03, 05".
    private string FormatSeasons()
    {
        var seasons = SeasonNumbers.Order().ToList();

        if (seasons.Count == 0)
        {
            return "[Unknown Season]";
        }

        if (seasons.Count == 1)
        {
            return $"Season {seasons[0]:00}";
        }

        var consecutive = seasons[^1] - seasons[0] == seasons.Count - 1;

        return consecutive ? $"Seasons {seasons[0]:00}-{seasons[^1]:00}" : "Seasons " + string.Join(", ", seasons.Select(s => s.ToString("00")));
    }
}

public sealed class SeriesTitleInfo
{
    public string Title { get; set; } = "";
    public string TitleWithoutYear { get; set; } = "";

    /// <summary>0 if the title has no year.</summary>
    public int Year { get; set; }

    /// <summary>"Title (Alt)", "Title AKA Alt", "Title / Alt" split into their parts; null if there is only one.</summary>
    public string[]? AllTitles { get; set; }
}

/// <summary>Result of <see cref="MovieParser"/>.</summary>
public sealed class ParsedMovieInfo
{
    /// <summary>The title, then its "AKA" / "/" alternatives.</summary>
    public List<string> MovieTitles { get; set; } = [];
    public string? PrimaryMovieTitle => MovieTitles.Count > 0 ? MovieTitles[0] : null;
    public int Year { get; set; }
    public string ReleaseTitle { get; set; } = "";
    public ParsedQuality Quality { get; set; } = new();
    public List<Language> Languages { get; set; } = [];
    public string? ReleaseGroup { get; set; }
    public string ReleaseHash { get; set; } = "";

    /// <summary>"Director's Cut", "Extended", "IMAX", ...; empty if none.</summary>
    public string Edition { get; set; } = "";

    /// <summary>"tt1234567" if the name carries an IMDb id, empty otherwise.</summary>
    public string ImdbId { get; set; } = "";
    public int TmdbId { get; set; }

    /// <summary>Burned-in subtitles ("KORSUB", or "Generic Hardcoded Subs" for HC/SUBBED); null if none.</summary>
    public string? HardcodedSubs { get; set; }

    public override string ToString() => $"{PrimaryMovieTitle} - {Year} {Quality}";
}

/// <summary>Result of <see cref="LanguageParser.ParseSubtitleLanguageInformation"/>.</summary>
public sealed class SubtitleTitleInfo
{
    public List<string> LanguageTags { get; set; } = [];
    public Language Language { get; set; } = Language.Unknown;
    public string? RawTitle { get; set; }
    public string? Title { get; set; }

    /// <summary>The "- 2" suffix of "Commentary - 2" (second track of the same title), 0 if none.</summary>
    public int Copy { get; set; }
    public bool TitleFirst { get; set; }
}
