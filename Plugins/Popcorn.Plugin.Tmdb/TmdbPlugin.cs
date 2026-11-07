using Microsoft.Extensions.Logging;
using Popcorn.Plugins;
using TMDbLib.Client;
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

    /// <summary>
    /// The best match's own image, then its gallery (posters in English first, backdrops without text first), then the
    /// images of the next few matches.
    /// </summary>
    public async Task<IReadOnlyList<ImageResult>> SearchImagesAsync(ImageQuery query, ImageKind kind, CancellationToken cancellationToken)
    {
        await EnsureConfig(cancellationToken);
        var matches = await FindMatches(query, cancellationToken);
        if (matches.Count == 0) return [];

        var poster = kind == ImageKind.Poster;
        string? MainPath(SearchBase m) => m switch
        {
            SearchMovie movie => poster ? movie.PosterPath : movie.BackdropPath,
            SearchTv tv => poster ? tv.PosterPath : tv.BackdropPath,
            _ => null,
        };
        string Label(SearchBase m) => m switch
        {
            SearchMovie movie => $"{movie.Title}{(movie.ReleaseDate is { } d ? $" ({d.Year})" : "")}",
            SearchTv tv => $"{tv.Name}{(tv.FirstAirDate is { } d ? $" ({d.Year})" : "")}",
            _ => string.Empty,
        };

        var best = matches[0];
        var images = best switch
        {
            SearchMovie movie => await _client.GetMovieImagesAsync(movie.Id, "en", "en,null", cancellationToken),
            SearchTv tv => await _client.GetTvShowImagesAsync(tv.Id, "en", "en,null", cancellationToken),
            _ => null,
        };
        var gallery = (poster ? images?.Posters : images?.Backdrops) ?? [];
        var galleryPaths = gallery
            .OrderBy(i => poster ? (i.Iso_639_1 == "en" ? 0 : 1) : (string.IsNullOrEmpty(i.Iso_639_1) ? 0 : 1))
            .ThenByDescending(i => i.VoteAverage)
            .Take(GalleryImages)
            .Select(i => (Path: i.FilePath, Title: Label(best)));

        return new[] { (Path: MainPath(best), Title: Label(best)) }
            .Concat(galleryPaths)
            .Concat(matches.Skip(1).Select(m => (Path: MainPath(m), Title: Label(m))))
            .Where(i => !string.IsNullOrEmpty(i.Path))
            .DistinctBy(i => i.Path)
            .Select(i => new ImageResult
            {
                Url = ImageUrl(i.Path, poster ? "w780" : "w1280")!,
                ThumbnailUrl = ImageUrl(i.Path, poster ? "w185" : "w300"),
                Title = i.Title,
            })
            .ToList();
    }

    /// <summary>Movies and shows matching the query; ones of the query's year first.</summary>
    private async Task<List<SearchBase>> FindMatches(ImageQuery query, CancellationToken ct)
    {
        if (query.Kind == MediaKind.Show)
        {
            var shows = await _client.SearchTvShowAsync(query.Title, 1, false, query.Year ?? 0, ct);
            return (shows?.Results ?? []).Take(ImageMatches).Cast<SearchBase>().ToList();
        }

        var result = await _client.SearchMultiAsync(query.Title, cancellationToken: ct);
        int? YearOf(SearchBase m) => m switch
        {
            SearchMovie movie => movie.ReleaseDate?.Year,
            SearchTv tv => tv.FirstAirDate?.Year,
            _ => null,
        };
        return (result?.Results ?? [])
            .Where(m => m is SearchMovie or SearchTv)
            .OrderBy(m => query.Year is { } y && YearOf(m) == y ? 0 : 1) // stable: keeps TMDb's relevance order
            .Take(ImageMatches)
            .ToList();
    }

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
