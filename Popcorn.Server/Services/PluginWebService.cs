using Popcorn.Domain;
using Popcorn.Domain.Enum;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.FilenameParser;
using Popcorn.Plugins;
using Popcorn.Server.Attributes;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;
using Popcorn.Server.PluginHost;
using Popcorn.Shared.Utils;

namespace Popcorn.Server.Services;

[WebSocketService]
public class PluginWebService(WebSocketBus bus, PluginManager pluginManager, IFileService fileService, IMediaItemRepository mediaItemRepository, ILogger<PluginWebService> logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromMinutes(2);

    [WebSocketEvent(SocketRequests.PluginList)]
    public async Task SendPluginList()
    {
        var plugins = pluginManager.Entries
            .Select(e => new PluginInfo
            {
                Id = e.Id,
                Name = e.Manifest?.Name ?? e.Id,
                Version = e.Manifest?.Version ?? string.Empty,
                UiUrl = e.Loaded is { } plugin ? WebUrl(plugin, plugin.Manifest.Ui) : null,
                IconUrl = e.Loaded is { } loaded ? WebUrl(loaded, loaded.Manifest.Icon) : null,
                Media = e.Loaded?.Media is not null,
                FileSources = FileSources(e),
                ImageKinds = ImageKinds(e.Loaded),
                HasSettings = e.Manifest?.Settings.Count > 0 || e.Loaded?.Actions is not null,
                Enabled = e.Enabled,
                Loaded = e.Loaded is not null,
                Bundled = e.Bundled,
                Error = e.Error,
                Status = e.Loaded?.Status.Messages ?? [],
            })
            .ToList();
        await bus.SendEventAsync(SocketRequests.PluginList, plugins);
    }

    [WebSocketEvent(SocketRequests.PluginSetEnabled)]
    public async Task SetEnabled(PluginSetEnabledRequest request)
    {
        pluginManager.SetEnabled(request.PluginId, request.Enabled);
        await SendPluginList();
    }

    [WebSocketEvent(SocketRequests.PluginSetOrder)]
    public async Task SetOrder(PluginSetOrderRequest request)
    {
        pluginManager.SetOrder(request.PluginIds);
        await SendPluginList();
    }

    [WebSocketEvent(SocketRequests.PluginUninstall)]
    public async Task Uninstall(PluginUninstallRequest request)
    {
        try
        {
            pluginManager.Uninstall(request.PluginId);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning("Uninstall of plugin {plugin} refused: {message}", request.PluginId, ex.Message);
        }
        await SendPluginList();
    }

    [WebSocketEvent(SocketRequests.PluginSettings)]
    public async Task GetSettings(PluginSettingsRequest request)
    {
        PluginSettingsResponse response;
        try
        {
            var entry = pluginManager.Entries.FirstOrDefault(e => e.Id == request.PluginId)
                        ?? throw new KeyNotFoundException($"Plugin '{request.PluginId}' is not installed");
            var actions = ActionsOf(entry.Loaded);
            if (entry.Manifest is { Settings.Count: > 0 })
            {
                var settings = pluginManager.GetSettings(request.PluginId);
                response = new PluginSettingsResponse
                {
                    PluginId = request.PluginId,
                    Fields = settings.Fields,
                    Values = settings.PublicValues(),
                    SecretsSet = settings.SecretsSet(),
                    Actions = actions,
                };
            }
            else if (actions.Count > 0)
            {
                response = new PluginSettingsResponse { PluginId = request.PluginId, Actions = actions };
            }
            else
            {
                throw new InvalidOperationException($"Plugin '{request.PluginId}' has no settings");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not read settings of plugin {plugin}", request.PluginId);
            response = new PluginSettingsResponse { PluginId = request.PluginId, Error = ex.Message };
        }
        await bus.SendEventAsync(SocketRequests.PluginSettings, response);
    }

    [WebSocketEvent(SocketRequests.PluginSettingsSave)]
    public async Task SaveSettings(PluginSettingsSaveRequest request)
    {
        string? error = null;
        try
        {
            pluginManager.SaveSettings(request.PluginId, request.Values);
        }
        catch (Exception ex) // all of them: the bus logs the payload of a failed event, and this one has passwords
        {
            logger.LogWarning("Settings of plugin {plugin} not saved: {message}", request.PluginId, ex.Message);
            error = ex.Message;
        }
        await bus.SendEventAsync(SocketRequests.PluginSettingsSave, new PluginSettingsSaveResponse { PluginId = request.PluginId, Error = error });
        await SendPluginList();
    }

    [WebSocketEvent(SocketRequests.PluginAction)]
    public Task RunAction(PluginActionRequest request)
    {
        // May take minutes: not on the socket's message loop, the answer comes as its own event.
        _ = Task.Run(async () =>
        {
            var response = await RunActionAsync(request);
            try
            {
                await bus.SendEventAsync(SocketRequests.PluginAction, response);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Plugin action answer not delivered (page closed?)");
            }
        });
        return Task.CompletedTask;
    }

    private async Task<PluginActionResponse> RunActionAsync(PluginActionRequest request)
    {
        IPluginActions? actions = null;
        try
        {
            actions = GetPlugin(request.PluginId).Actions ?? throw new InvalidOperationException($"Plugin '{request.PluginId}' has no actions");
            var action = actions.Actions.FirstOrDefault(a => a.Id == request.ActionId)
                         ?? throw new KeyNotFoundException($"Plugin '{request.PluginId}' has no action '{request.ActionId}'");
            if (action.Disabled) throw new InvalidOperationException($"{action.Label} is not available right now");
            using var cts = new CancellationTokenSource(ActionTimeout);
            logger.LogInformation("Running action {action} of plugin {plugin}", request.ActionId, request.PluginId);
            var result = await actions.RunActionAsync(request.ActionId, cts.Token);
            return new PluginActionResponse
            {
                PluginId = request.PluginId,
                ActionId = request.ActionId,
                Message = result.Message,
                Warning = result.Warning,
                Actions = ActionsOf(actions),
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Action {action} of plugin {plugin} failed", request.ActionId, request.PluginId);
            var error = ex is OperationCanceledException ? "The action took too long and was cancelled" : ex.Message;
            return new PluginActionResponse { PluginId = request.PluginId, ActionId = request.ActionId, Error = error, Actions = ActionsOf(actions) };
        }
    }

    [WebSocketEvent(SocketRequests.PluginSearch)]
    public async Task Search(PluginSearchRequest request)
    {
        var query = request.Query.Trim();
        if (query.Length == 0) return;

        PluginSearchResponse response;
        try
        {
            var media = GetPlugin(request.PluginId).Media ?? throw NotA(request.PluginId, "media");
            using var cts = new CancellationTokenSource(Timeout);
            var results = await media.SearchAsync(query, cts.Token);
            response = new PluginSearchResponse { PluginId = request.PluginId, Query = request.Query, Results = results };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Plugin {plugin} search failed for {query}", request.PluginId, query);
            response = new PluginSearchResponse { PluginId = request.PluginId, Query = request.Query, Error = ex.Message };
        }
        await bus.SendEventAsync(SocketRequests.PluginSearch, response);
    }

    [WebSocketEvent(SocketRequests.PluginDetails)]
    public async Task Details(PluginDetailsRequest request)
    {
        PluginDetailsResponse response;
        try
        {
            var media = GetPlugin(request.PluginId).Media ?? throw NotA(request.PluginId, "media");
            using var cts = new CancellationTokenSource(Timeout);
            var details = await media.GetDetailsAsync(request.Id, cts.Token);
            response = new PluginDetailsResponse
            {
                PluginId = request.PluginId, Id = request.Id, Details = details,
                Error = details is null ? "Not found" : null,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Plugin {plugin} details failed for {id}", request.PluginId, request.Id);
            response = new PluginDetailsResponse { PluginId = request.PluginId, Id = request.Id, Error = ex.Message };
        }
        await bus.SendEventAsync(SocketRequests.PluginDetails, response);
    }

    [WebSocketEvent(SocketRequests.PluginFileSearch)]
    public async Task FileSearch(PluginFileSearchRequest request)
    {
        var query = request.Query is { } q ? q with { Text = q.Text.Trim() } : null;
        if (string.IsNullOrEmpty(query?.Text)) return;

        PluginFileSearchResponse response;
        try
        {
            var source = GetPlugin(request.PluginId).GetFileSources().FirstOrDefault(s => s.Id == request.SourceId)
                         ?? throw new KeyNotFoundException($"Plugin '{request.PluginId}' has no file source '{request.SourceId}'");
            using var cts = new CancellationTokenSource(Timeout);
            var results = await source.Source.SearchFilesAsync(query, cts.Token);
            response = new PluginFileSearchResponse
            {
                PluginId = request.PluginId, SourceId = request.SourceId, RequestId = request.RequestId, Results = results,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Plugin {plugin} file source '{source}' search failed for {query}", request.PluginId, request.SourceId, query);
            response = new PluginFileSearchResponse
            {
                PluginId = request.PluginId, SourceId = request.SourceId, RequestId = request.RequestId, Error = ex.Message,
            };
        }
        await bus.SendEventAsync(SocketRequests.PluginFileSearch, response);
    }

    /// <summary>
    /// Posters or backdrops for a library item from every enabled image provider of that kind (queried in parallel,
    /// answered together in the plugin order). The name is parsed like a release name for title/year/kind (and an IMDb
    /// id written in it); the item's <see cref="MediaItem.Source"/> gives the IMDb id and, to the plugin it came from,
    /// its media id.
    /// </summary>
    [WebSocketEvent(SocketRequests.MediaImageSearch)]
    public async Task ImageSearch(MediaImageSearchRequest request)
    {
        var name = request.Name.Trim();
        if (name.Length == 0) return;

        var parsed = MediaNameParser.Parse(name);
        var source = request.ItemId is { } itemId ? (await mediaItemRepository.GetMediaItem(itemId))?.Source : null;
        var imdbId = source?.ImdbId ?? MovieParser.ParseImdbId(name);
        var query = new ImageQuery
        {
            Title = string.IsNullOrWhiteSpace(parsed.Title) ? name : parsed.Title,
            Year = parsed.Year,
            Kind = parsed.IsTv ? MediaKind.Show : null,
            ImdbId = string.IsNullOrEmpty(imdbId) ? null : imdbId,
        };
        var kind = request.Kind == MediaImageKind.Poster ? ImageKind.Poster : ImageKind.Backdrop;

        var providers = pluginManager.Plugins.Where(p => ImageKinds(p).Contains(kind)).ToList();
        var results = await Task.WhenAll(providers.Select(async plugin =>
        {
            try
            {
                using var cts = new CancellationTokenSource(Timeout);
                var own = source?.PluginId == plugin.Manifest.Id ? query with { MediaId = source.MediaId } : query;
                var images = await plugin.Images!.SearchImagesAsync(own, kind, cts.Token);
                return new MediaImageProviderResult
                {
                    PluginId = plugin.Manifest.Id, PluginName = plugin.Manifest.Name,
                    Images = images.Where(i => Uri.TryCreate(i.Url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https").ToList(),
                };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Plugin {plugin} image search failed for {query}", plugin.Manifest.Id, query);
                return new MediaImageProviderResult { PluginId = plugin.Manifest.Id, PluginName = plugin.Manifest.Name, Error = ex.Message };
            }
        }));
        await bus.SendEventAsync(SocketRequests.MediaImageSearch,
            new MediaImageSearchResponse { Kind = request.Kind, RequestId = request.RequestId, Providers = results });
    }

    /// <summary>Opens a file link on the server machine (desktop app: the UI cannot hand magnet: links to the OS).</summary>
    /// <summary>
    /// Fetches <see cref="FileLink.Resolve"/> links (e.g. a magnet read from a torrent page) when they
    /// are used. Off the socket loop like actions: it may take a while and searches must not wait for it.
    /// </summary>
    [WebSocketEvent(SocketRequests.PluginResolveLinks)]
    public Task ResolveLinks(PluginResolveLinksRequest request)
    {
        _ = Task.Run(async () =>
        {
            var response = await ResolveLinksAsync(request);
            try
            {
                await bus.SendEventAsync(SocketRequests.PluginResolveLinks, response);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Resolved links not delivered (page closed?)");
            }
        });
        return Task.CompletedTask;
    }

    private async Task<PluginResolveLinksResponse> ResolveLinksAsync(PluginResolveLinksRequest request)
    {
        IFileLinkResolver resolver;
        try
        {
            var source = GetPlugin(request.PluginId).GetFileSources().FirstOrDefault(s => s.Id == request.SourceId)
                         ?? throw new KeyNotFoundException($"Plugin '{request.PluginId}' has no file source '{request.SourceId}'");
            resolver = source.Source as IFileLinkResolver
                       ?? throw new InvalidOperationException($"Plugin '{request.PluginId}' cannot fetch links");
        }
        catch (Exception ex)
        {
            return new PluginResolveLinksResponse
            {
                RequestId = request.RequestId,
                Links = request.Links.Select(_ => (FileLink?)null).ToList(),
                Errors = request.Links.Select(_ => (string?)ex.Message).ToList(),
            };
        }

        using var cts = new CancellationTokenSource(Timeout);
        var results = await Task.WhenAll(request.Links.Select(async link =>
        {
            try
            {
                var resolved = await resolver.ResolveLinkAsync(link with { Resolve = true }, cts.Token);
                if (resolved.Resolve) throw new InvalidOperationException("The plugin returned a link that still needs fetching");
                return (Link: (FileLink?)resolved, Error: (string?)null);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Plugin {plugin} could not fetch link {url}", request.PluginId, link.Url);
                return (null, ex is OperationCanceledException ? "Fetching the link took too long" : ex.Message);
            }
        }));
        return new PluginResolveLinksResponse
        {
            RequestId = request.RequestId,
            Links = results.Select(r => r.Link).ToList(),
            Errors = results.Select(r => r.Error).ToList(),
        };
    }

    [WebSocketEvent(SocketRequests.OpenLink)]
    public Task OpenLink(OpenLinkRequest request)
    {
        try
        {
            fileService.OpenUrl(request.Url);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not open link {url}", TorrentDataUrl.ForLog(request.Url));
        }
        return Task.CompletedTask;
    }

    private IReadOnlyList<FileSourceInfo> FileSources(PluginEntry entry)
    {
        if (entry.Loaded is not { } plugin) return [];
        try
        {
            return plugin.GetFileSources()
                .Select(s => new FileSourceInfo
                {
                    Id = s.Id,
                    Name = string.IsNullOrWhiteSpace(s.Name) ? plugin.Manifest.Name : s.Name,
                    IconUrl = WebUrl(plugin, s.Icon ?? plugin.Manifest.Icon),
                    SortFields = SortFields(entry, s),
                })
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Plugin {plugin} FileSources failed", entry.Id);
            return [];
        }
    }

    private IReadOnlyList<ImageKind> ImageKinds(LoadedPlugin? plugin)
    {
        if (plugin?.Images is not { } images) return [];
        try
        {
            return images.ImageKinds.Distinct().ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Plugin {plugin} ImageKinds failed", plugin.Manifest.Id);
            return [];
        }
    }

    private IReadOnlyList<FileSortField> SortFields(PluginEntry entry, FileSource source)
    {
        try
        {
            return source.Source.SortFields;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Plugin {plugin} file source '{source}' SortFields failed", entry.Id, source.Id);
            return [];
        }
    }

    /// <summary>URL of a file in the plugin's <c>web/</c> folder; the version suffix keeps browsers off a replaced plugin's copy.</summary>
    private static string? WebUrl(LoadedPlugin plugin, string? path) =>
        path is null ? null : $"/plugins/{plugin.Manifest.Id}/{path.TrimStart('/')}?v={plugin.LoadedAt}";

    private IReadOnlyList<PluginAction> ActionsOf(LoadedPlugin? plugin) => ActionsOf(plugin?.Actions);

    /// <summary>The plugin's buttons; a plugin that throws listing them gets none (logged).</summary>
    private IReadOnlyList<PluginAction> ActionsOf(IPluginActions? actions)
    {
        if (actions is null) return [];
        try
        {
            var list = actions.Actions;
            if (list.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != list.Count)
                throw new InvalidDataException("Action ids are not unique");
            return list;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not list plugin actions");
            return [];
        }
    }

    private static InvalidOperationException NotA(string id, string kind) => new($"Plugin '{id}' is not a {kind} source");

    private LoadedPlugin GetPlugin(string id) =>
        pluginManager.Get(id) ?? throw new KeyNotFoundException($"Plugin '{id}' is not installed");
}
