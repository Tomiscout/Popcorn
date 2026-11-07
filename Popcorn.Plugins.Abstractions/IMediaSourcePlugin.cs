namespace Popcorn.Plugins;

/// <summary>
/// A media information source (search + details), e.g. TMDb: finds out *what* exists. Results show in the search
/// page's Media tab; a details page can describe how to look the item up in file sources
/// (<see cref="MediaDetails.FileQuery"/>).
/// </summary>
/// <remarks>
/// A plugin assembly contains exactly one public, non-abstract class implementing <see cref="IMediaSourcePlugin"/>,
/// <see cref="IFileSourcePlugin"/>, <see cref="IFileSourceProvider"/> or <see cref="IImageProvider"/> (one class may implement
/// several; not both file kinds). It is created once (singleton) through DI: the constructor may ask for
/// host services such as <c>ILogger&lt;T&gt;</c>. Calls can run concurrently.
/// </remarks>
public interface IMediaSourcePlugin
{
    /// <summary>Search the source. Return an empty list for no results; throw on failure (shown to the user as an error).</summary>
    Task<IReadOnlyList<MediaCard>> SearchAsync(string query, CancellationToken cancellationToken);

    /// <summary>Details for a <see cref="MediaCard.Id"/> returned by this plugin, or null when it does not exist.</summary>
    Task<MediaDetails?> GetDetailsAsync(string id, CancellationToken cancellationToken);
}

/// <summary>
/// A file source: finds *where to get* files (a torrent site, a web page, an FTP server, an archive...). Results show
/// in the search page's Files tab and under a media details page. The plugin only returns links
/// (<see cref="FileLink"/>); Popcorn handles them by kind (hands torrents to the system's torrent program, opens
/// direct links, ...).
/// </summary>
public interface IFileSourcePlugin
{
    /// <summary>
    /// Search the source. Use the most precise thing it supports, e.g. <see cref="FileQuery.ImdbId"/> when set, else
    /// <see cref="FileQuery.Text"/>. Return an empty list for no results; throw on failure (shown as an error).
    /// </summary>
    Task<IReadOnlyList<FileResult>> SearchFilesAsync(FileQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Columns the results can be sorted by (values in <see cref="FileResult.SortValues"/>); the first one is the
    /// default sort. Use the <see cref="FileSortField"/> presets (or the same keys) where they fit, so results of
    /// different plugins sort together in the combined list.
    /// </summary>
    IReadOnlyList<FileSortField> SortFields => [];
}

/// <summary>
/// A plugin with several file sources (e.g. one per site, or one per indexer the user configured), each shown and
/// searched as a provider of its own. Implement this instead of <see cref="IFileSourcePlugin"/> (not both).
/// </summary>
public interface IFileSourceProvider
{
    /// <summary>
    /// The sources, in display order. Read whenever the app lists plugins (page load, plugin changes), so it may change
    /// at runtime; a search names the source by <see cref="FileSource.Id"/>.
    /// </summary>
    IReadOnlyList<FileSource> FileSources { get; }
}

/// <summary>One file source of an <see cref="IFileSourceProvider"/>.</summary>
/// <param name="Id">Unique within the plugin, stable (remembered UI state is keyed by it). Letters, digits, '-', '_', '.'.</param>
/// <param name="Name">Shown in the UI instead of the plugin name.</param>
/// <param name="Source">Does the searching.</param>
public record FileSource(string Id, string Name, IFileSourcePlugin Source)
{
    /// <summary>Image relative to the plugin's <c>web/</c> folder (like plugin.json <c>icon</c>); null = the plugin icon.</summary>
    public string? Icon { get; init; }
}

/// <summary>
/// An image provider: posters and/or backdrops for library items (Add to library dialog, item details page). The
/// user picks among the results of all enabled image providers; the dialog preselects the first result of the first
/// provider in the plugin order.
/// </summary>
public interface IImageProvider
{
    /// <summary>The kinds this plugin provides, e.g. only <see cref="ImageKind.Poster"/>.</summary>
    IReadOnlyList<ImageKind> ImageKinds { get; }

    /// <summary>
    /// Images of <paramref name="kind"/> (one of <see cref="ImageKinds"/>) for the query, best match first. Return an
    /// empty list for no results; throw on failure.
    /// </summary>
    Task<IReadOnlyList<ImageResult>> SearchImagesAsync(ImageQuery query, ImageKind kind, CancellationToken cancellationToken);
}

/// <summary>
/// The plugin's settings (fields declared in plugin.json <c>settings</c>, edited in Settings → Plugins, stored in its
/// settings.json). Ask for it in the plugin class constructor. The plugin is reloaded when the user saves settings,
/// so values can be cached for the lifetime of the instance. Getters return the field's default when there is no
/// value, null when there is none either or the value has another type.
/// </summary>
public interface IPluginSettings
{
    /// <summary>A <c>text</c>, <c>password</c> or <c>select</c> value.</summary>
    string? GetString(string key);
    /// <summary>A <c>number</c> (whole) or <c>float</c> value.</summary>
    double? GetNumber(string key);
    bool? GetBool(string key);
    /// <summary>Value of a <c>"list": true</c> text or password field.</summary>
    IReadOnlyList<string>? GetStringList(string key);
    /// <summary>Value of a <c>"list": true</c> number or float field.</summary>
    IReadOnlyList<double>? GetNumberList(string key);
}

/// <summary>Version of this contract. A plugin's plugin.json must declare the same <c>apiVersion</c> or it is not loaded.</summary>
public static class PluginApi
{
    public const int Version = 1;
}
