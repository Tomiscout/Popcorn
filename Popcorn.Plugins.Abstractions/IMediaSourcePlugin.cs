namespace Popcorn.Plugins;

/// <summary>
/// A media information source (search + details), e.g. TMDb: finds out *what* exists. Results show in the search
/// page's Media tab; a details page can describe how to look the item up in file sources
/// (<see cref="MediaDetails.FileQuery"/>).
/// </summary>
/// <remarks>
/// A plugin assembly contains exactly one public, non-abstract class implementing <see cref="IMediaSourcePlugin"/>,
/// <see cref="IFileSourcePlugin"/>, <see cref="IFileSourceProvider"/>, <see cref="IImageProvider"/> or
/// <see cref="IServicePlugin"/> (one class may implement
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
/// Implemented by a file source (<see cref="IFileSourcePlugin"/>) that returns <see cref="FileLink.Resolve"/> links:
/// turns one into the real link when the user downloads or opens it.
/// </summary>
public interface IFileLinkResolver
{
    /// <summary>The usable link (<see cref="FileLink.Resolve"/> false) for <paramref name="link"/>; throw when there is none (shown as an error).</summary>
    Task<FileLink> ResolveLinkAsync(FileLink link, CancellationToken cancellationToken);
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
/// A plugin without a search role that serves other plugins or the user: it publishes <see cref="IPluginVariables"/>,
/// reports <see cref="IPluginStatus"/>, has <see cref="IPluginActions"/> (e.g. runs a helper program such as a
/// Cloudflare solver and publishes its URL). Implement it when the class implements none of the other plugin kinds.
/// Its instance lives while the plugin is loaded and is disposed (<see cref="IDisposable"/>) when it is unloaded or
/// the server stops.
/// </summary>
public interface IServicePlugin;

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

/// <summary>
/// Buttons in the plugin's settings dialog (Settings → Plugins), e.g. "Test connection", "Log in", "Clear cache".
/// Implement it on the plugin class next to its other interfaces. A plugin with actions gets the settings dialog even
/// without settings fields.
/// </summary>
/// <remarks>
/// Actions run on the loaded instance, so they see the saved settings: the dialog asks the user to save changes
/// before running one. <see cref="Actions"/> is read whenever the dialog opens and after every run, so the list may
/// change (e.g. "Log out" once logged in).
/// </remarks>
public interface IPluginActions
{
    /// <summary>The buttons, in display order.</summary>
    IReadOnlyList<PluginAction> Actions { get; }

    /// <summary>
    /// Runs the action with <see cref="PluginAction.Id"/> <paramref name="actionId"/>. The result's message is shown to
    /// the user; throw to report a failure (the exception message is shown). Cancelled after 2 minutes.
    /// </summary>
    Task<PluginActionResult> RunActionAsync(string actionId, CancellationToken cancellationToken);
}

/// <summary>A button of <see cref="IPluginActions"/>.</summary>
/// <param name="Id">Passed to <see cref="IPluginActions.RunActionAsync"/>; unique within the plugin.</param>
/// <param name="Label">Button text.</param>
public record PluginAction(string Id, string Label)
{
    /// <summary>Shown next to the button.</summary>
    public string? Description { get; init; }
    /// <summary>Settings <c>group</c> (plugin.json) to show the button in; null = after the fields without a group.</summary>
    public string? Group { get; init; }
    /// <summary>Mantine color name (e.g. "red" for something destructive); null = default.</summary>
    public string? Color { get; init; }
    /// <summary>When set, the user confirms this question before the action runs.</summary>
    public string? Confirm { get; init; }
    /// <summary>Shown greyed out (e.g. "Log out" while not logged in).</summary>
    public bool Disabled { get; init; }
}

/// <summary>Outcome of a successful <see cref="IPluginActions.RunActionAsync"/>.</summary>
/// <param name="Message">Shown in a notification; null = a generic "done".</param>
/// <param name="Warning">Shown as a warning instead of a success (it ran, but e.g. found nothing to do).</param>
public record PluginActionResult(string? Message = null, bool Warning = false)
{
    public static PluginActionResult Ok(string? message = null) => new(message);
}

/// <summary>
/// Variables shared between plugins, kept by the app (in memory). A plugin publishes a value under a name
/// (e.g. a proxy plugin sets <c>proxy.url</c> from its settings) and any plugin that knows the name reads it. Ask for it
/// in the plugin class constructor.
/// </summary>
/// <remarks>
/// A value belongs to the plugin that set it last and is removed when that plugin is unloaded (disabled, uninstalled,
/// reloaded after a settings change), so a reloaded plugin sets its values again, typically in its constructor.
/// Plugins load in no particular order: read a value when you use it instead of once in the constructor. Names are
/// case-sensitive; prefix them with a topic (<c>proxy.url</c>) to avoid clashes. Values are
/// strings so that plugins share no types; use JSON for anything structured.
/// </remarks>
public interface IPluginVariables
{
    /// <summary>The value of <paramref name="name"/>, or null when no loaded plugin has set it.</summary>
    string? Get(string name);

    /// <summary>Publishes <paramref name="value"/> under <paramref name="name"/>, replacing any value; null removes it.</summary>
    void Set(string name, string? value);

    /// <summary>All current values by name.</summary>
    IReadOnlyDictionary<string, string> GetAll();
}

/// <summary>
/// Status messages of the plugin, shown in Settings → Plugins (on the plugin's row and in its settings dialog), e.g.
/// "Set the API key" when something is not set up, or "Site unreachable" after failed searches. Ask for it in the
/// plugin class constructor; it can be changed at any time, open pages update right away.
/// </summary>
/// <remarks>
/// Messages are kept by key, so independent problems are set and cleared separately (e.g. key "setup" checked in the
/// constructor, key "connection" set by a failed search and cleared by the next successful one). They belong to the
/// loaded instance: unloading (also the reload after saving settings) clears them, so a constructor sets them again.
/// </remarks>
public interface IPluginStatus
{
    /// <summary>Shows <paramref name="message"/> under <paramref name="key"/>, replacing the message of that key.</summary>
    void Set(PluginStatusLevel level, string message, string key = "");

    /// <summary>Removes the message of <paramref name="key"/>, if any.</summary>
    void Clear(string key = "");
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<PluginStatusLevel>))]
public enum PluginStatusLevel
{
    Info,
    Warning,
    /// <summary>The plugin cannot work as it is (e.g. a required setting is missing).</summary>
    Error,
}

/// <summary>Version of this contract. A plugin's plugin.json must declare the same <c>apiVersion</c> or it is not loaded.</summary>
public static class PluginApi
{
    public const int Version = 1;
}
