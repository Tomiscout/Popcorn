using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Popcorn.Plugins;
using Popcorn.Shared.Utils;

namespace Popcorn.Server.PluginHost;

/// <summary>plugin.json in the plugin folder.</summary>
public class PluginManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int ApiVersion { get; set; }
    /// <summary>Main assembly file name, relative to the plugin folder.</summary>
    public string Assembly { get; set; } = string.Empty;
    /// <summary>Optional ES module with custom UI components, relative to the plugin's <c>web/</c> folder
    /// (served at <c>/plugins/{id}/</c>).</summary>
    public string? Ui { get; set; }
    /// <summary>Optional icon (png/svg/...), relative to the plugin's <c>web/</c> folder.</summary>
    public string? Icon { get; set; }
    /// <summary>Optional settings form; values are stored in settings.json (<see cref="PluginSettingsFile"/>).</summary>
    public List<PluginSettingField> Settings { get; set; } = [];
}

public sealed class LoadedPlugin(PluginManifest manifest, string directory, object instance, PluginLoadContext context)
{
    public PluginManifest Manifest { get; } = manifest;
    public string Directory { get; } = directory;
    /// <summary>Set when the plugin is a media information source.</summary>
    public IMediaSourcePlugin? Media { get; } = instance as IMediaSourcePlugin;
    /// <summary>Set when the plugin provides posters and/or backdrops.</summary>
    public IImageProvider? Images { get; } = instance as IImageProvider;
    /// <summary>
    /// File sources: the plugin itself (<see cref="IFileSourcePlugin"/>, id "", plugin name and icon) or the ones of its
    /// <see cref="IFileSourceProvider"/>. Throws when the provider throws or returns invalid / duplicate ids.
    /// </summary>
    public IReadOnlyList<FileSource> GetFileSources()
    {
        switch (instance)
        {
            case IFileSourcePlugin self:
                return [new FileSource("", Manifest.Name, self) { Icon = Manifest.Icon }];
            case IFileSourceProvider provider:
                var sources = provider.FileSources;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var source in sources)
                {
                    if (!PluginManager.IsValidId(source.Id) || !ids.Add(source.Id))
                        throw new InvalidDataException($"File source id '{source.Id}' is invalid or not unique");
                    ArgumentNullException.ThrowIfNull(source.Source);
                }
                return sources;
            default:
                return [];
        }
    }
    /// <summary>Changes on every load, appended to the UI url so browsers do not reuse a module of a replaced plugin.</summary>
    public long LoadedAt { get; } = DateTime.UtcNow.Ticks;
    /// <summary>Static files of the plugin UI (<c>web/</c>), served at <c>/plugins/{id}/</c>; null when there are none.</summary>
    public IFileProvider? WebFiles { get; } =
        System.IO.Directory.Exists(Path.Combine(directory, "web")) ? new PhysicalFileProvider(Path.Combine(directory, "web")) : null;

    internal void Unload(ILogger logger)
    {
        try
        {
            (instance as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Plugin {id} threw on dispose", Manifest.Id);
        }
        (WebFiles as IDisposable)?.Dispose();
        context.Unload(); // collected once in-flight calls drop their references
    }
}

/// <summary>A plugin folder found in one of the <see cref="PluginManager.PluginRoots"/>.</summary>
public sealed record PluginEntry(
    string Id,
    string Directory,
    bool Bundled,
    bool Enabled,
    PluginManifest? Manifest,
    LoadedPlugin? Loaded,
    string? Error);

/// <summary>
/// Plugins from <c>&lt;data&gt;/plugins/*</c> (user-installed, wins on duplicate ids) and <c>&lt;server dir&gt;/plugins/*</c>
/// (bundled). Plugins can be enabled/disabled and reordered (persisted in <c>&lt;data&gt;/plugins/plugins.json</c>), installed from a
/// zip into the data folder and uninstalled at runtime; each one lives in its own collectible load context.
/// A plugin that fails to load is kept in the list with its error.
/// </summary>
public sealed class PluginManager(IServiceProvider services, ILogger<PluginManager> logger)
{
    public static readonly string UserRoot = Path.Combine(DirectoryUtils.DataFolder, "plugins");
    public static readonly string BundledRoot = Path.Combine(AppContext.BaseDirectory, "plugins");
    public static readonly string[] PluginRoots = [UserRoot, BundledRoot];

    private const long MaxUnpackedSize = 1L << 30;
    private const string SettingsFileName = "settings.json";
    private static readonly string StateFile = Path.Combine(UserRoot, "plugins.json");
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    private readonly Lock _gate = new();
    private PluginState _state = new();
    private HashSet<string> _disabled = new(StringComparer.Ordinal);
    private volatile IReadOnlyList<PluginEntry> _entries = [];

    /// <summary>All plugin folders (first one per id), including disabled and broken ones, in the user's order.</summary>
    public IReadOnlyList<PluginEntry> Entries => _entries;

    public IEnumerable<LoadedPlugin> Plugins => _entries.Select(e => e.Loaded).OfType<LoadedPlugin>();

    public LoadedPlugin? Get(string id) => _entries.FirstOrDefault(e => e.Id == id)?.Loaded;

    public void LoadAll()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(UserRoot); // so users can find where to drop plugins
            _state = ReadState();
            _disabled = new HashSet<string>(_state.Disabled, StringComparer.Ordinal);
            Refresh();
        }
    }

    public void SetEnabled(string id, bool enabled)
    {
        lock (_gate)
        {
            if (enabled ? !_disabled.Remove(id) : !_disabled.Add(id)) return;
            WriteState();
            Refresh();
        }
    }

    /// <summary>Sets the plugin order (search result groups follow it). Ids not listed keep their place after these, by name.</summary>
    public void SetOrder(IReadOnlyList<string> ids)
    {
        lock (_gate)
        {
            _state.Order = ids.Distinct(StringComparer.Ordinal).ToList();
            WriteState();
            _entries = Sorted(_entries);
        }
    }

    private List<PluginEntry> Sorted(IEnumerable<PluginEntry> entries)
    {
        var index = _state.Order.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);
        return entries
            .OrderBy(e => index.GetValueOrDefault(e.Id, int.MaxValue))
            .ThenBy(e => e.Manifest?.Name ?? e.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Extracts a plugin zip (plugin.json at the root or in a single top-level folder) to <c>&lt;data&gt;/plugins/&lt;id&gt;</c>,
    /// replacing an installed plugin with the same id, and loads it. Returns the plugin id.
    /// </summary>
    public string Install(Stream zip)
    {
        lock (_gate)
        {
            var staging = Path.Combine(UserRoot, $".install-{Guid.NewGuid():N}");
            try
            {
                ZipArchive archive;
                try
                {
                    archive = new ZipArchive(zip, ZipArchiveMode.Read);
                }
                catch (Exception ex) when (ex is not InvalidDataException)
                {
                    throw new InvalidDataException("Not a zip archive", ex); // e.g. seek before start of a tiny file
                }
                using (archive)
                {
                    if (archive.Entries.Sum(e => e.Length) > MaxUnpackedSize)
                        throw new InvalidDataException("The archive is too large");
                    archive.ExtractToDirectory(staging); // rejects entries that would land outside staging
                }

                var root = File.Exists(Path.Combine(staging, "plugin.json"))
                    ? staging
                    : Directory.GetDirectories(staging) is [var single] && Directory.GetFiles(staging).Length == 0
                      && File.Exists(Path.Combine(single, "plugin.json"))
                        ? single
                        : throw new InvalidDataException("plugin.json not found at the root of the archive");
                var manifest = ReadManifest(root);

                var target = Path.Combine(UserRoot, manifest.Id);
                var current = _entries.FirstOrDefault(e => e.Directory == target || e.Id == manifest.Id);
                current?.Loaded?.Unload(logger);
                if (current is not null) _entries = _entries.Select(e => e == current ? e with { Loaded = null } : e).ToList();
                var userSettings = Path.Combine(target, SettingsFileName);
                if (File.Exists(userSettings)) File.Copy(userSettings, Path.Combine(root, SettingsFileName), overwrite: true); // updates keep them
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true); // unlink: a mapped dll stays valid
                Directory.Move(root, target);

                if (_disabled.Remove(manifest.Id)) WriteState();
                logger.LogInformation("Installed plugin {id} {version} to {dir}", manifest.Id, manifest.Version, target);
                Refresh();
                return manifest.Id;
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
        }
    }

    /// <summary>Deletes a user-installed plugin (a bundled one with the same id takes its place).</summary>
    public void Uninstall(string id)
    {
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == id) ?? throw new KeyNotFoundException($"Plugin '{id}' is not installed");
            if (entry.Bundled) throw new InvalidOperationException($"Plugin '{id}' is built in, it can only be disabled");

            entry.Loaded?.Unload(logger);
            _entries = _entries.Where(e => e != entry).ToList();
            Directory.Delete(entry.Directory, recursive: true);
            logger.LogInformation("Uninstalled plugin {id} from {dir}", id, entry.Directory);
            Refresh();
        }
    }

    /// <summary>The settings of a plugin, also of a disabled or failed one (settings may be what it is missing).</summary>
    public PluginSettingsFile GetSettings(string id)
    {
        var entry = _entries.FirstOrDefault(e => e.Id == id) ?? throw new KeyNotFoundException($"Plugin '{id}' is not installed");
        return SettingsOf(entry);
    }

    /// <summary>Stores settings and reloads the plugin, so it starts over with them (e.g. logs in with a new password).</summary>
    public void SaveSettings(string id, IReadOnlyDictionary<string, JsonElement> values)
    {
        lock (_gate)
        {
            var entry = _entries.FirstOrDefault(e => e.Id == id) ?? throw new KeyNotFoundException($"Plugin '{id}' is not installed");
            SettingsOf(entry).Save(values);
            entry.Loaded?.Unload(logger);
            _entries = _entries.Select(e => e == entry ? e with { Loaded = null } : e).ToList();
            Refresh();
        }
    }

    private static PluginSettingsFile SettingsOf(PluginEntry entry)
    {
        if (entry.Manifest is not { Settings.Count: > 0 } manifest)
            throw new InvalidOperationException($"Plugin '{entry.Id}' has no settings");
        return SettingsFor(entry.Id, entry.Directory, entry.Bundled, manifest);
    }

    /// <summary>
    /// settings.json in the plugin folder. A bundled plugin's folder is replaced on every app install and may be
    /// read-only, so its file is in <c>&lt;data&gt;/plugins/&lt;id&gt;</c> (not a plugin folder without plugin.json).
    /// </summary>
    private static PluginSettingsFile SettingsFor(string id, string directory, bool bundled, PluginManifest manifest) =>
        new(Path.Combine(bundled ? Path.Combine(UserRoot, id) : directory, SettingsFileName), manifest.Settings);

    /// <summary>Rescans the plugin folders and makes the loaded set match them and the enabled state.</summary>
    private void Refresh()
    {
        var previous = _entries.Where(e => e.Loaded is not null).ToDictionary(e => e.Directory);
        var entries = new List<PluginEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var root in PluginRoots.Where(Directory.Exists))
        {
            var bundled = root == BundledRoot;
            // Folders without plugin.json are not plugins (e.g. plugin sources kept next to them)
            foreach (var dir in Directory.EnumerateDirectories(root)
                         .Where(d => !Path.GetFileName(d).StartsWith('.') && File.Exists(Path.Combine(d, "plugin.json")))
                         .Order())
            {
                PluginManifest manifest;
                try
                {
                    manifest = ReadManifest(dir);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Invalid plugin in {dir}", dir);
                    entries.Add(new PluginEntry(Path.GetFileName(dir), dir, bundled, false, null, null, ex.Message));
                    continue;
                }

                if (!seen.Add(manifest.Id))
                {
                    logger.LogInformation("Plugin {id} in {dir} skipped, overridden by {other}",
                        manifest.Id, dir, entries.First(e => e.Id == manifest.Id).Directory);
                    continue;
                }

                var enabled = !_disabled.Contains(manifest.Id);
                LoadedPlugin? loaded = null;
                string? error = null;
                if (previous.Remove(dir, out var running) && running.Loaded!.Manifest.Version == manifest.Version && enabled)
                {
                    loaded = running.Loaded;
                }
                else
                {
                    running?.Loaded!.Unload(logger);
                    if (enabled)
                    {
                        try
                        {
                            loaded = Load(dir, manifest, SettingsFor(manifest.Id, dir, bundled, manifest));
                            logger.LogInformation("Loaded plugin {id} {version} from {dir}", manifest.Id, manifest.Version, dir);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Failed to load plugin from {dir}", dir);
                            error = ex.Message;
                        }
                    }
                }
                entries.Add(new PluginEntry(manifest.Id, dir, bundled, enabled, manifest, loaded, error));
            }
        }

        foreach (var gone in previous.Values) gone.Loaded!.Unload(logger); // folder deleted or now overridden
        _entries = Sorted(entries);
    }

    private static PluginManifest ReadManifest(string dir)
    {
        var manifestPath = Path.Combine(dir, "plugin.json");
        if (!File.Exists(manifestPath)) throw new InvalidDataException("plugin.json not found");
        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), Json)
                       ?? throw new InvalidDataException("plugin.json is empty");

        if (!IsValidId(manifest.Id))
            throw new InvalidDataException("plugin.json: id must be non-empty and contain only letters, digits, '-', '_', '.' (not first)");
        if (manifest.ApiVersion != PluginApi.Version)
            throw new InvalidDataException($"plugin.json: apiVersion {manifest.ApiVersion} is not supported (server: {PluginApi.Version})");
        if (string.IsNullOrWhiteSpace(manifest.Name)) manifest.Name = manifest.Id;
        manifest.Settings ??= [];
        PluginSettingField.Normalize(manifest.Settings);
        return manifest;
    }

    /// <summary>Plugin and file source ids: used in URLs and storage keys.</summary>
    internal static bool IsValidId(string? id) =>
        !string.IsNullOrEmpty(id) && !id.StartsWith('.') && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    private LoadedPlugin Load(string dir, PluginManifest manifest, IPluginSettings settings)
    {
        var assemblyPath = ResolveInside(dir, manifest.Assembly, "assembly");
        if (manifest.Ui is not null && !File.Exists(ResolveInside(Path.Combine(dir, "web"), manifest.Ui, "ui", mustExist: false)))
        {
            logger.LogWarning("{dir}: ui module web/{ui} not found, using the built-in UI", dir, manifest.Ui);
            manifest.Ui = null;
        }
        if (manifest.Icon is not null && !File.Exists(ResolveInside(Path.Combine(dir, "web"), manifest.Icon, "icon", mustExist: false)))
        {
            logger.LogWarning("{dir}: icon web/{icon} not found", dir, manifest.Icon);
            manifest.Icon = null;
        }

        var context = new PluginLoadContext(assemblyPath);
        try
        {
            var assembly = context.LoadFromAssemblyPath(assemblyPath);
            var types = assembly.GetExportedTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false }
                            && (typeof(IMediaSourcePlugin).IsAssignableFrom(t) || typeof(IFileSourcePlugin).IsAssignableFrom(t)
                                || typeof(IFileSourceProvider).IsAssignableFrom(t) || typeof(IImageProvider).IsAssignableFrom(t)))
                .ToList();
            if (types.Count != 1)
                throw new InvalidDataException(
                    $"{manifest.Assembly} must contain exactly one public IMediaSourcePlugin/IFileSourcePlugin/IFileSourceProvider/IImageProvider implementation, found {types.Count}");
            if (typeof(IFileSourcePlugin).IsAssignableFrom(types[0]) && typeof(IFileSourceProvider).IsAssignableFrom(types[0]))
                throw new InvalidDataException($"{types[0].Name} implements both IFileSourcePlugin and IFileSourceProvider, pick one");

            return new LoadedPlugin(manifest, dir, ActivatorUtilities.CreateInstance(new PluginServices(services, settings), types[0]), context);
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    private static string ResolveInside(string dir, string relative, string field, bool mustExist = true)
    {
        var full = Path.GetFullPath(Path.Combine(dir, relative));
        if (!full.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.Ordinal) || (mustExist && !File.Exists(full)))
            throw new InvalidDataException($"plugin.json: {field} '{relative}' is not a file inside {dir}");
        return full;
    }

    private PluginState ReadState()
    {
        try
        {
            if (File.Exists(StateFile))
                return JsonSerializer.Deserialize<PluginState>(File.ReadAllText(StateFile), Json) ?? new PluginState();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not read {file}, all plugins enabled", StateFile);
        }
        return new PluginState();
    }

    private void WriteState()
    {
        _state.Disabled = _disabled.Order().ToList();
        File.WriteAllText(StateFile, JsonSerializer.Serialize(_state, Json));
    }

    /// <summary>The host's services + what is per plugin, for the plugin class constructor.</summary>
    private sealed class PluginServices(IServiceProvider inner, IPluginSettings settings) : IServiceProvider, IServiceProviderIsService
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IPluginSettings) ? settings
            : serviceType == typeof(IServiceProviderIsService) ? this
            : inner.GetService(serviceType);

        public bool IsService(Type serviceType) =>
            serviceType == typeof(IPluginSettings) || inner.GetService<IServiceProviderIsService>()?.IsService(serviceType) == true;
    }

    private sealed class PluginState
    {
        public List<string> Disabled { get; set; } = [];
        /// <summary>Plugin ids in the user's order; others follow by name.</summary>
        public List<string> Order { get; set; } = [];
    }
}
