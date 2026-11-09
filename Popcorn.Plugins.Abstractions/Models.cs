using System.Text.Json.Serialization;

namespace Popcorn.Plugins;

/// <summary>A search result, or a small card on a details page (<see cref="MediaDetails.Sections"/>).</summary>
public class MediaCard
{
    /// <summary>Plugin-defined id, passed back to <see cref="IMediaSourcePlugin.GetDetailsAsync"/>. Any string.</summary>
    public required string Id { get; init; }
    public required string Title { get; init; }
    /// <summary>Short secondary line, e.g. a year or "Season 2 · 10 episodes".</summary>
    public string? Subtitle { get; init; }
    public string? Description { get; init; }
    /// <summary>Absolute URL of a portrait image (poster, profile photo).</summary>
    public string? ImageUrl { get; init; }
    public IReadOnlyList<MediaBadge> Badges { get; init; } = [];
    /// <summary>False when <see cref="Id"/> has no details page (the card is not clickable).</summary>
    public bool HasDetails { get; init; } = true;
    /// <summary>Free-form data for the plugin's own UI components; serialized to JSON (camelCase) as-is.</summary>
    public object? Data { get; init; }
}

public class MediaDetails
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Tagline { get; init; }
    public string? Overview { get; init; }
    public string? PosterUrl { get; init; }
    public string? BackdropUrl { get; init; }
    public IReadOnlyList<MediaBadge> Badges { get; init; } = [];
    /// <summary>Label/value rows, e.g. Release date, Runtime, Director.</summary>
    public IReadOnlyList<MediaField> Fields { get; init; } = [];
    /// <summary>Rows of small cards, e.g. Seasons, Recommendations.</summary>
    public IReadOnlyList<MediaCardSection> Sections { get; init; } = [];
    /// <summary>
    /// How file sources should look this item up (title, year, IMDb id...); shown as a Files section under the
    /// details. Null = search by <see cref="Title"/>.
    /// </summary>
    public FileQuery? FileQuery { get; init; }
    /// <summary>Free-form data for the plugin's own UI components; serialized to JSON (camelCase) as-is.</summary>
    public object? Data { get; init; }
}

/// <param name="Color">Any CSS color or Mantine color name; null = theme default.</param>
public record MediaBadge(string Label, string? Color = null);

public record MediaField(string Label, string Value);

public record MediaCardSection(string Title, IReadOnlyList<MediaCard> Cards)
{
    /// <summary>Rendered as a collapsed, expandable row (secondary content such as similar titles).</summary>
    public bool Collapsed { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<MediaKind>))]
public enum MediaKind
{
    Movie,
    Show,
}

/// <summary>
/// What to look for in a file source. From the search page only <see cref="Text"/> is set (what the user typed);
/// from a media details page it is <see cref="MediaDetails.FileQuery"/> (or the item title when that is null).
/// </summary>
public record FileQuery
{
    /// <summary>Free text, always set: the typed query, or e.g. "The Matrix 1999" for a media item.</summary>
    public required string Text { get; init; }
    public string? Title { get; init; }
    public int? Year { get; init; }
    /// <summary>IMDb id, e.g. "tt0133093".</summary>
    public string? ImdbId { get; init; }
    public MediaKind? Kind { get; init; }
}

/// <summary>A file (or a set of files, e.g. a torrent) found by a <see cref="IFileSourcePlugin"/>.</summary>
public class FileResult
{
    /// <summary>Plugin-defined, unique within one result list.</summary>
    public required string Id { get; init; }
    public required string Title { get; init; }
    /// <summary>Short secondary line, e.g. "1.8 GB · 6 files · 2012-06-01".</summary>
    public string? Subtitle { get; init; }
    public string? Description { get; init; }
    /// <summary>E.g. quality, category, seeders.</summary>
    public IReadOnlyList<MediaBadge> Badges { get; init; } = [];
    /// <summary>Ways to get the file; the app shows an action per link.</summary>
    public IReadOnlyList<FileLink> Links { get; init; } = [];
    /// <summary>
    /// Values of the plugin's <see cref="IFileSourcePlugin.SortFields"/> by <see cref="FileSortField.Key"/>. A missing
    /// key sorts last.
    /// </summary>
    public IReadOnlyDictionary<string, double> SortValues { get; init; } = new Dictionary<string, double>();
    /// <summary>
    /// The provider (site, tracker, index) the result really comes from, as a tag shared by every plugin that can return
    /// its results: lowercase name or domain, e.g. "1337x", "yts". An aggregator sets it per result (its results come
    /// from several providers); a plugin for one site sets its own tag. Results of different plugins with the same
    /// <see cref="Origin"/> and <see cref="OriginId"/> are the same item: the combined list can show it once
    /// (Remove duplicates). Null = unknown, never deduplicated.
    /// </summary>
    public string? Origin { get; init; }
    /// <summary>
    /// The result's id at <see cref="Origin"/> (e.g. the site's torrent id or the info hash), the same whichever plugin
    /// returns it. Null = <see cref="Id"/>.
    /// </summary>
    public string? OriginId { get; init; }
    /// <summary>Free-form data for the plugin's own UI components; serialized to JSON (camelCase) as-is.</summary>
    public object? Data { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<FileLinkKind>))]
public enum FileLinkKind
{
    /// <summary>magnet: URI, passed to the torrent program.</summary>
    Magnet,
    /// <summary>URL of a .torrent file, or the file itself as <c>data:application/x-bittorrent;base64,…</c>.</summary>
    Torrent,
    /// <summary>URL of the file itself (http, https, ftp).</summary>
    Direct,
    /// <summary>A web page about the file (opened in the browser).</summary>
    Page,
}

/// <param name="Label">Button text; null = the app's default for the kind.</param>
public record FileLink(FileLinkKind Kind, string Url, string? Label = null)
{
    /// <summary>
    /// True = not the link yet: <see cref="Url"/> is whatever the plugin's <see cref="IFileLinkResolver"/> needs to get
    /// it (e.g. the page the magnet link is on), asked for only when the user uses the link, instead of fetching it for
    /// every result of a search. <see cref="Kind"/> is the kind of the link it resolves to.
    /// </summary>
    public bool Resolve { get; init; }
}

/// <summary>A numeric column file results can be sorted by.</summary>
/// <param name="Key">Key in <see cref="FileResult.SortValues"/>.</param>
/// <param name="Label">Shown in the sort menu.</param>
/// <param name="Descending">Default direction (largest first).</param>
public record FileSortField(string Key, string Label, bool Descending = true)
{
    /// <summary>Seeders, key <c>seeders</c>.</summary>
    public static readonly FileSortField Seeders = new("seeders", "Seeders");
    /// <summary>Leechers, key <c>leechers</c>.</summary>
    public static readonly FileSortField Leechers = new("leechers", "Leechers");
    /// <summary>Size in bytes, key <c>size</c>.</summary>
    public static readonly FileSortField Size = new("size", "Size");
    /// <summary>Upload time as Unix seconds, key <c>added</c>.</summary>
    public static readonly FileSortField Added = new("added", "Date added");
}

[JsonConverter(typeof(JsonStringEnumConverter<ImageKind>))]
public enum ImageKind
{
    /// <summary>Portrait cover (2:3).</summary>
    Poster,
    /// <summary>Wide background (16:9).</summary>
    Backdrop,
}

/// <summary>What to find images for: a library item, usually named after its folder.</summary>
public record ImageQuery
{
    /// <summary>Title, cleaned of release tags when the name could be parsed (else the name as typed).</summary>
    public required string Title { get; init; }
    public int? Year { get; init; }
    /// <summary>Set when the name looks like an episode (<see cref="MediaKind.Show"/>); null = unknown.</summary>
    public MediaKind? Kind { get; init; }
    /// <summary>
    /// IMDb id ("tt1234567") of the item: known from the media page it was added from, or written in its name. When
    /// set it identifies the title; use it before any name search.
    /// </summary>
    public string? ImdbId { get; init; }
    /// <summary>
    /// This plugin's own media id (<see cref="MediaDetails.Id"/>) when the item was added from this plugin's media
    /// page: the exact title, wins over everything else. Only ever set for the plugin that produced it.
    /// </summary>
    public string? MediaId { get; init; }
}

/// <summary>An image found by an <see cref="IImageProvider"/>.</summary>
public class ImageResult
{
    /// <summary>Absolute http(s) URL of the full-size image; downloaded and stored by the app when picked.</summary>
    public required string Url { get; init; }
    /// <summary>Smaller version for the picker; null = <see cref="Url"/>.</summary>
    public string? ThumbnailUrl { get; init; }
    /// <summary>What the image is of, e.g. "The Matrix (1999)"; shown as a tooltip.</summary>
    public string? Title { get; init; }
    /// <summary>
    /// Heading for the title the image belongs to when a search finds several (e.g. "The Matrix (1999) · Movie"):
    /// consecutive images with the same group are shown together under it. Null = no heading.
    /// </summary>
    public string? Group { get; init; }
}
