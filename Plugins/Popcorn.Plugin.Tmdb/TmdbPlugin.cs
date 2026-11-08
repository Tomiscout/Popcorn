using Microsoft.Extensions.Logging;
using Popcorn.Plugins;
using TMDbLib.Client;
using TMDbLib.Objects.Find;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Movies;
using TMDbLib.Objects.Search;
using TMDbLib.Objects.TvShows;

namespace Popcorn.Plugin.Tmdb;

/// <summary>
/// Ids: "movie:{tmdbId}", "tv:{tmdbId}", "person:{tmdbId}" (people have no details page).
/// Details <c>Data</c> = <see cref="TmdbDetailsData"/>, rendered by ui/src/index.tsx.
/// Also an image provider (posters + backdrops) for library items.
/// </summary>
public sealed class TmdbPlugin(ILogger<TmdbPlugin> logger) : IMediaSourcePlugin, IImageProvider
{
    private const int ImageMatches = 5;
    private const int GalleryImages = 20;
    private const int OtherGalleryImages = 6;

    private readonly TMDbClient _client = new("b8af6dd235f0a34dd87d3697acd79b29");
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private Dictionary<int, string> _genres = [];

    private static readonly MediaBadge MovieBadge = new("Movie", "#4caf50");
    private static readonly MediaBadge TvBadge = new("TV Show", "#2196f3");
    private static readonly MediaBadge PersonBadge = new("Person", "#ff9800");

    public async Task<IReadOnlyList<MediaCard>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        await EnsureConfig(cancellationToken);
        var result = await _client.SearchMultiAsync(query, cancellationToken: cancellationToken);
        return (result?.Results ?? []).Select(ToCard).OfType<MediaCard>().ToList();
    }

    public async Task<MediaDetails?> GetDetailsAsync(string id, CancellationToken cancellationToken)
    {
        await EnsureConfig(cancellationToken);
        var (kind, tmdbId) = ParseId(id);
        return kind switch
        {
            "movie" => await GetMovie(tmdbId, cancellationToken),
            "tv" => await GetTvShow(tmdbId, cancellationToken),
            _ => null,
        };
    }

    private async Task<MediaDetails?> GetMovie(int id, CancellationToken ct)
    {
        var m = await _client.GetMovieAsync(id, MovieMethods.Credits | MovieMethods.Recommendations, ct);
        if (m is null) return null;

        var fields = new List<MediaField>();
        if (m.ReleaseDate is { } release) fields.Add(new("Release date", release.ToString("yyyy-MM-dd")));
        if (m.Runtime is > 0) fields.Add(new("Runtime", $"{m.Runtime / 60}h {m.Runtime % 60}m"));
        if (m.VoteCount > 0) fields.Add(new("Rating", $"{m.VoteAverage:0.0} / 10 ({m.VoteCount} votes)"));
        var directors = m.Credits?.Crew?.Where(c => c.Job == "Director").Select(c => c.Name).ToList();
        if (directors is { Count: > 0 }) fields.Add(new("Director", string.Join(", ", directors)));
        if (!string.IsNullOrEmpty(m.Status)) fields.Add(new("Status", m.Status));

        return new MediaDetails
        {
            Id = $"movie:{m.Id}",
            Title = m.Title ?? m.OriginalTitle ?? string.Empty,
            Tagline = m.Tagline,
            Overview = m.Overview,
            PosterUrl = PosterUrl(m.PosterPath),
            BackdropUrl = OriginalUrl(m.BackdropPath),
            Badges = [MovieBadge, .. GenreBadges(m.Genres)],
            Fields = fields,
            Sections = Sections(Similar(m.Recommendations?.Results)),
            FileQuery = FileQueryFor(m.Title ?? m.OriginalTitle, m.ReleaseDate?.Year, m.ImdbId, MediaKind.Movie),
            Data = new TmdbDetailsData(
                (m.Credits?.Cast ?? []).OrderBy(c => c.Order).Take(20)
                    .Select(c => new TmdbCastMember(c.Name, c.Character, ProfileUrl(c.ProfilePath))).ToList(),
                m.ImdbId is { Length: > 0 } imdb ? $"https://www.imdb.com/title/{imdb}/" : null,
                $"https://www.themoviedb.org/movie/{m.Id}"),
        };
    }

    private async Task<MediaDetails?> GetTvShow(int id, CancellationToken ct)
    {
        var t = await _client.GetTvShowAsync(id, TvShowMethods.Credits | TvShowMethods.Recommendations | TvShowMethods.ExternalIds, cancellationToken: ct);
        if (t is null) return null;

        var fields = new List<MediaField>();
        if (t.FirstAirDate is { } first)
            fields.Add(new("Aired", $"{first:yyyy-MM-dd} – {(t.InProduction ? "now" : t.LastAirDate?.ToString("yyyy-MM-dd") ?? "?")}"));
        fields.Add(new("Seasons", $"{t.NumberOfSeasons} ({t.NumberOfEpisodes} episodes)"));
        if (t.VoteCount > 0) fields.Add(new("Rating", $"{t.VoteAverage:0.0} / 10 ({t.VoteCount} votes)"));
        if (t.CreatedBy is { Count: > 0 }) fields.Add(new("Created by", string.Join(", ", t.CreatedBy.Select(c => c.Name))));
        if (t.Networks is { Count: > 0 }) fields.Add(new("Network", string.Join(", ", t.Networks.Select(n => n.Name))));

        var seasons = (t.Seasons ?? []).Select(s => new MediaCard
        {
            Id = $"tv:{t.Id}:season:{s.SeasonNumber}",
            Title = s.Name ?? $"Season {s.SeasonNumber}",
            Subtitle = $"{s.EpisodeCount} episodes{(s.AirDate is { } d ? $" · {d.Year}" : "")}",
            ImageUrl = PosterUrl(s.PosterPath),
            HasDetails = false,
        });

        return new MediaDetails
        {
            Id = $"tv:{t.Id}",
            Title = t.Name ?? t.OriginalName ?? string.Empty,
            Tagline = t.Tagline,
            Overview = t.Overview,
            PosterUrl = PosterUrl(t.PosterPath),
            BackdropUrl = OriginalUrl(t.BackdropPath),
            Badges = [TvBadge, .. GenreBadges(t.Genres)],
            Fields = fields,
            Sections = Sections(Section("Seasons", seasons), Similar(t.Recommendations?.Results)),
            FileQuery = FileQueryFor(t.Name ?? t.OriginalName, null, t.ExternalIds?.ImdbId, MediaKind.Show),
            Data = new TmdbDetailsData(
                (t.Credits?.Cast ?? []).OrderBy(c => c.Order).Take(20)
                    .Select(c => new TmdbCastMember(c.Name, c.Character, ProfileUrl(c.ProfilePath))).ToList(),
                null,
                $"https://www.themoviedb.org/tv/{t.Id}"),
        };
    }

    public IReadOnlyList<ImageKind> ImageKinds { get; } = [ImageKind.Poster, ImageKind.Backdrop];

    /// <summary>A movie or show the images can be of.</summary>
    private sealed record Match(bool IsTv, int Id, string Title, string? OriginalTitle, int? Year, string? PosterPath, string? BackdropPath, int VoteCount)
    {
        public string Label => $"{Title}{(Year is { } y ? $" ({y})" : "")}";
        public string Group => $"{Label} · {(IsTv ? "TV show" : "Movie")}";

        public static Match? From(SearchBase result) => result switch
        {
            SearchMovie m => new Match(false, m.Id, m.Title ?? m.OriginalTitle ?? "", m.OriginalTitle, m.ReleaseDate?.Year, m.PosterPath, m.BackdropPath, m.VoteCount),
            SearchTv t => new Match(true, t.Id, t.Name ?? t.OriginalName ?? "", t.OriginalName, t.FirstAirDate?.Year, t.PosterPath, t.BackdropPath, t.VoteCount),
            _ => null,
        };
    }

    /// <summary>
    /// Images grouped per matching title, the best match first: its own image + gallery (posters in English first,
    /// backdrops without text first), then the other matches with a few images each, so a wrong guess is one click
    /// away. The best match is the query's media id (added from this plugin's page), else its IMDb id, else the
    /// search result whose title fits the query best (<see cref="RankMatches"/>).
    /// </summary>
    public async Task<IReadOnlyList<ImageResult>> SearchImagesAsync(ImageQuery query, ImageKind kind, CancellationToken cancellationToken)
    {
        await EnsureConfig(cancellationToken);
        var known = await FindKnown(query, cancellationToken);
        var searched = await SearchMatches(query, cancellationToken);
        var matches = (known is null ? searched : searched.Where(m => m.IsTv != known.IsTv || m.Id != known.Id).Prepend(known))
            .Take(ImageMatches)
            .ToList();
        if (matches.Count == 0) return [];

        var poster = kind == ImageKind.Poster;
        var galleries = await Task.WhenAll(matches.Select((m, i) => Gallery(m, poster, i == 0 ? GalleryImages : OtherGalleryImages, cancellationToken)));

        return matches
            .SelectMany((m, i) => new[] { poster ? m.PosterPath : m.BackdropPath }
                .Concat(galleries[i])
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(path => (Path: path!, Match: m)))
            .DistinctBy(i => i.Path)
            .Select(i => new ImageResult
            {
                Url = ImageUrl(i.Path, poster ? "w780" : "w1280")!,
                ThumbnailUrl = ImageUrl(i.Path, poster ? "w185" : "w300"),
                Title = i.Match.Label,
                Group = matches.Count > 1 ? i.Match.Group : null,
            })
            .ToList();
    }

    private async Task<IEnumerable<string>> Gallery(Match match, bool poster, int count, CancellationToken ct)
    {
        try
        {
            var images = match.IsTv
                ? await _client.GetTvShowImagesAsync(match.Id, "en", "en,null", ct)
                : await _client.GetMovieImagesAsync(match.Id, "en", "en,null", ct);
            return ((poster ? images?.Posters : images?.Backdrops) ?? [])
                .OrderBy(i => poster ? (i.Iso_639_1 == "en" ? 0 : 1) : (string.IsNullOrEmpty(i.Iso_639_1) ? 0 : 1))
                .ThenByDescending(i => i.VoteAverage)
                .Take(count)
                .Select(i => i.FilePath)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "TMDb images of {Match} failed", match.Label);
            return [];
        }
    }

    /// <summary>The title the query identifies: this plugin's media id, else the IMDb id. Null = search by name.</summary>
    private async Task<Match?> FindKnown(ImageQuery query, CancellationToken ct)
    {
        try
        {
            if (query.MediaId is { } mediaId)
            {
                var (kind, id) = ParseId(mediaId);
                if (kind == "movie" && await _client.GetMovieAsync(id, cancellationToken: ct) is { } m)
                    return new Match(false, m.Id, m.Title ?? m.OriginalTitle ?? "", m.OriginalTitle, m.ReleaseDate?.Year, m.PosterPath, m.BackdropPath, m.VoteCount);
                if (kind == "tv" && await _client.GetTvShowAsync(id, cancellationToken: ct) is { } t)
                    return new Match(true, t.Id, t.Name ?? t.OriginalName ?? "", t.OriginalName, t.FirstAirDate?.Year, t.PosterPath, t.BackdropPath, t.VoteCount);
            }
            if (query.ImdbId is { Length: > 0 } imdbId)
            {
                var found = await _client.FindAsync(FindExternalSource.Imdb, imdbId, ct);
                var hit = (found?.MovieResults ?? []).Cast<SearchBase>().Concat(found?.TvResults ?? []).Select(Match.From).FirstOrDefault(m => m != null);
                if (hit != null) return hit;
                // an episode's id: its show
                if (found?.TvEpisode?.FirstOrDefault() is { } episode && await _client.GetTvShowAsync(episode.ShowId, cancellationToken: ct) is { } show)
                    return new Match(true, show.Id, show.Name ?? show.OriginalName ?? "", show.OriginalName, show.FirstAirDate?.Year, show.PosterPath, show.BackdropPath, show.VoteCount);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "TMDb lookup of {MediaId} / {ImdbId} failed", query.MediaId, query.ImdbId);
        }
        return null;
    }

    /// <summary>Movies and shows found for the query's title, best fitting first (<see cref="RankMatches"/>).</summary>
    private async Task<List<Match>> SearchMatches(ImageQuery query, CancellationToken ct)
    {
        IEnumerable<SearchBase> results = query.Kind == MediaKind.Show
            ? (await _client.SearchTvShowAsync(query.Title, 1, false, query.Year ?? 0, ct))?.Results ?? []
            : (await _client.SearchMultiAsync(query.Title, cancellationToken: ct))?.Results ?? [];
        return RankMatches(query, results.Select(Match.From).OfType<Match>()).ToList();
    }

    /// <summary>
    /// TMDb orders search results by popularity, so a new or upcoming title that merely contains the words beats the
    /// one named exactly so ("Avatar The Last Airbender" → "Aang: The Last Airbender" (2026) before the 2005 show).
    /// Ranked instead by: same title (punctuation and case ignored, original title too) and year, then how much of
    /// the title the query covers, then vote count — of two titles named the same (an animated show and its
    /// live-action remake) the established one wins; the other is right after it.
    /// </summary>
    private static IEnumerable<Match> RankMatches(ImageQuery query, IEnumerable<Match> matches)
    {
        var wanted = Words(query.Title);
        double Fit(Match m)
        {
            var best = 0.0;
            foreach (var title in new[] { m.Title, m.OriginalTitle })
            {
                if (string.IsNullOrWhiteSpace(title)) continue;
                var words = Words(title);
                if (words.SequenceEqual(wanted)) return 2;
                var shared = words.Intersect(wanted).Count();
                best = Math.Max(best, shared == 0 ? 0 : (double)shared / words.Union(wanted).Count());
            }
            return best;
        }
        return matches
            .Select(m => (Match: m, Score: Fit(m) + (query.Year is { } y && m.Year == y ? 1 : 0)))
            .OrderByDescending(r => Math.Round(r.Score, 1))
            .ThenByDescending(r => r.Match.VoteCount)
            .Select(r => r.Match);
    }

    private static List<string> Words(string title) =>
        System.Text.RegularExpressions.Regex.Split(title.ToLowerInvariant().Replace("&", " and "), @"[^\p{L}\p{N}]+")
            .Where(w => w.Length > 0)
            .ToList();

    private MediaCard? ToCard(SearchBase result) => result switch
    {
        SearchMovie m => new MediaCard
        {
            Id = $"movie:{m.Id}",
            Title = m.Title ?? m.OriginalTitle ?? string.Empty,
            Subtitle = m.ReleaseDate?.Year.ToString(),
            Description = m.Overview,
            ImageUrl = PosterUrl(m.PosterPath),
            Badges = [MovieBadge, .. GenreBadges(m.GenreIds)],
        },
        SearchTv t => new MediaCard
        {
            Id = $"tv:{t.Id}",
            Title = t.Name ?? t.OriginalName ?? string.Empty,
            Subtitle = t.FirstAirDate?.Year.ToString(),
            Description = t.Overview,
            ImageUrl = PosterUrl(t.PosterPath),
            Badges = [TvBadge, .. GenreBadges(t.GenreIds)],
        },
        SearchPerson p => new MediaCard
        {
            Id = $"person:{p.Id}",
            Title = p.Name ?? string.Empty,
            ImageUrl = ProfileUrl(p.ProfilePath),
            Badges = [PersonBadge],
            HasDetails = false,
        },
        _ => null,
    };

    /// <summary>Shows are searched without the year: their files are usually named "Show S01E02".</summary>
    private static FileQuery? FileQueryFor(string? title, int? year, string? imdbId, MediaKind kind) =>
        string.IsNullOrWhiteSpace(title)
            ? null
            : new FileQuery
            {
                Text = year is { } y ? $"{title} {y}" : title,
                Title = title,
                Year = year,
                ImdbId = string.IsNullOrEmpty(imdbId) ? null : imdbId,
                Kind = kind,
            };

    private static MediaCardSection Section(string title, IEnumerable<MediaCard?>? cards) =>
        new(title, (cards ?? []).OfType<MediaCard>().ToList());

    private MediaCardSection Similar(IEnumerable<SearchBase>? results) =>
        Section("See similar", results?.Select(ToCard)) with { Collapsed = true };

    private static IReadOnlyList<MediaCardSection> Sections(params MediaCardSection[] sections) =>
        sections.Where(s => s.Cards.Count > 0).ToList();

    private IEnumerable<MediaBadge> GenreBadges(IEnumerable<int>? ids) =>
        (ids ?? []).Select(id => _genres.GetValueOrDefault(id)).OfType<string>().Select(g => new MediaBadge(g));

    private static IEnumerable<MediaBadge> GenreBadges(IEnumerable<Genre>? genres) =>
        (genres ?? []).Where(g => !string.IsNullOrEmpty(g.Name)).Select(g => new MediaBadge(g.Name!));

    private static (string Kind, int Id) ParseId(string id)
    {
        var parts = id.Split(':');
        return parts.Length == 2 && int.TryParse(parts[1], out var tmdbId) ? (parts[0], tmdbId) : ("", 0);
    }

    private string? PosterUrl(string? path) => ImageUrl(path, _client.Config.Images?.PosterSizes?.ElementAtOrDefault(2) ?? "w342");
    private string? ProfileUrl(string? path) => ImageUrl(path, "w185");
    private string? OriginalUrl(string? path) => ImageUrl(path, "original");

    private string? ImageUrl(string? path, string size) =>
        string.IsNullOrEmpty(path) ? null : $"{_client.Config.Images!.SecureBaseUrl}{size}{path}";

    private async Task EnsureConfig(CancellationToken ct)
    {
        if (_client.HasConfig) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_client.HasConfig) return;
            await _client.GetConfigAsync();
            var genres = new Dictionary<int, string>();
            foreach (var g in (await _client.GetMovieGenresAsync(ct) ?? []).Concat(await _client.GetTvGenresAsync(ct) ?? []))
                if (!string.IsNullOrEmpty(g.Name)) genres.TryAdd(g.Id, g.Name);
            _genres = genres;
            logger.LogInformation("TMDb configuration loaded ({count} genres)", genres.Count);
        }
        finally
        {
            _initLock.Release();
        }
    }
}

public record TmdbDetailsData(IReadOnlyList<TmdbCastMember> Cast, string? ImdbUrl, string TmdbUrl);

public record TmdbCastMember(string? Name, string? Character, string? ProfileUrl);
