using System.Text.Json;
using Popcorn.Domain.Enum;
using Popcorn.Plugins;
using Popcorn.Server.PluginHost;

namespace Popcorn.Server.Models.Requests;

public class PluginInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    /// <summary>Server-relative URL of the plugin's UI module, null when it has none.</summary>
    public string? UiUrl { get; init; }
    /// <summary>Server-relative URL of the plugin's icon, null when it has none.</summary>
    public string? IconUrl { get; init; }
    /// <summary>Loaded and a media information source (search page Media tab).</summary>
    public bool Media { get; init; }
    /// <summary>File sources of a loaded plugin (search page Files tab, Files section of details pages); empty = none.</summary>
    public IReadOnlyList<FileSourceInfo> FileSources { get; init; } = [];
    /// <summary>Image kinds a loaded image provider supplies (posters/backdrops of library items); empty = none.</summary>
    public IReadOnlyList<ImageKind> ImageKinds { get; init; } = [];
    /// <summary>plugin.json declares settings or the loaded plugin has actions: it has a settings dialog (<see cref="PluginSettingsResponse"/>).</summary>
    public bool HasSettings { get; init; }
    public bool Enabled { get; init; }
    /// <summary>Enabled and loaded without errors: usable for search.</summary>
    public bool Loaded { get; init; }
    /// <summary>Ships with the program (can be disabled, not uninstalled).</summary>
    public bool Bundled { get; init; }
    /// <summary>Did not load (or plugin.json is unreadable).</summary>
    public string? Error { get; init; }
    /// <summary>Messages of the loaded plugin (<see cref="IPluginStatus"/>); empty = none.</summary>
    public IReadOnlyList<PluginStatusMessage> Status { get; init; } = [];
}

/// <summary>A <see cref="FileSource"/>; a plugin that is itself the file source has one with id "".</summary>
public class FileSourceInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Server-relative URL of the source's icon (else the plugin's), null when there is none.</summary>
    public string? IconUrl { get; init; }
    /// <summary>Sort columns (<see cref="IFileSourcePlugin.SortFields"/>), the first one is its default.</summary>
    public IReadOnlyList<FileSortField> SortFields { get; init; } = [];
}

public class PluginSetEnabledRequest
{
    public string PluginId { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public class PluginSetOrderRequest
{
    /// <summary>Plugin ids in the new order.</summary>
    public List<string> PluginIds { get; set; } = [];
}

public class PluginUninstallRequest
{
    public string PluginId { get; set; } = string.Empty;
}

public class PluginSettingsRequest
{
    public string PluginId { get; set; } = string.Empty;
}

public class PluginSettingsResponse
{
    public required string PluginId { get; init; }
    public IReadOnlyList<PluginSettingField> Fields { get; init; } = [];
    /// <summary>Key → current value or default. Password fields are never included, see <see cref="SecretsSet"/>.</summary>
    public IReadOnlyDictionary<string, JsonElement> Values { get; init; } = new Dictionary<string, JsonElement>();
    /// <summary>Keys of the password fields that have a value.</summary>
    public IReadOnlyList<string> SecretsSet { get; init; } = [];
    /// <summary>Buttons of a loaded plugin (<see cref="IPluginActions"/>); empty = none.</summary>
    public IReadOnlyList<PluginAction> Actions { get; init; } = [];
    public string? Error { get; init; }
}

public class PluginActionRequest
{
    public string PluginId { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
}

public class PluginActionResponse
{
    public required string PluginId { get; init; }
    public required string ActionId { get; init; }
    /// <summary><see cref="PluginActionResult.Message"/> of a successful run.</summary>
    public string? Message { get; init; }
    public bool Warning { get; init; }
    /// <summary>Set when the action failed (threw, timed out, plugin not loaded).</summary>
    public string? Error { get; init; }
    /// <summary>The plugin's actions after the run (they may change).</summary>
    public IReadOnlyList<PluginAction> Actions { get; init; } = [];
}

public class PluginSettingsSaveRequest
{
    public string PluginId { get; set; } = string.Empty;
    /// <summary>Key → new value. A key left out stays as it is; null or blank removes the value.</summary>
    public Dictionary<string, JsonElement> Values { get; set; } = [];
}

public class PluginSettingsSaveResponse
{
    public required string PluginId { get; init; }
    public string? Error { get; init; }
}

public class PluginSearchRequest
{
    public string PluginId { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
}

public class PluginSearchResponse
{
    public required string PluginId { get; init; }
    public required string Query { get; init; }
    public IReadOnlyList<MediaCard> Results { get; init; } = [];
    public string? Error { get; init; }
}

public class PluginDetailsRequest
{
    public string PluginId { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
}

public class PluginDetailsResponse
{
    public required string PluginId { get; init; }
    public required string Id { get; init; }
    public MediaDetails? Details { get; init; }
    public string? Error { get; init; }
}

public class PluginFileSearchRequest
{
    public string PluginId { get; set; } = string.Empty;
    /// <summary><see cref="FileSourceInfo.Id"/>; "" for a plugin that is itself the file source.</summary>
    public string SourceId { get; set; } = string.Empty;
    /// <summary>Client-chosen, echoed in the response so it can tell its searches apart.</summary>
    public string RequestId { get; set; } = string.Empty;
    public FileQuery? Query { get; set; }
}

public class PluginFileSearchResponse
{
    public required string PluginId { get; init; }
    public required string SourceId { get; init; }
    public required string RequestId { get; init; }
    public IReadOnlyList<FileResult> Results { get; init; } = [];
    public string? Error { get; init; }
}

/// <summary><see cref="FileLink.Resolve"/> links of one file source to turn into usable ones.</summary>
public class PluginResolveLinksRequest
{
    public string PluginId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    /// <summary>Echoed in the response.</summary>
    public string RequestId { get; set; } = string.Empty;
    public List<FileLink> Links { get; set; } = [];
}

public class PluginResolveLinksResponse
{
    public required string RequestId { get; init; }
    /// <summary>Same order as the request; null where it failed (see <see cref="Errors"/>).</summary>
    public IReadOnlyList<FileLink?> Links { get; init; } = [];
    /// <summary>Per link, null where it worked.</summary>
    public IReadOnlyList<string?> Errors { get; init; } = [];
}

public class OpenLinkRequest
{
    public string Url { get; set; } = string.Empty;
}

public class MediaImageSearchRequest
{
    /// <summary>Item name as typed or the folder name; parsed for title/year.</summary>
    public string Name { get; set; } = string.Empty;
    public MediaImageKind Kind { get; set; }
    /// <summary>Echoed in the response so the client can drop answers to older searches.</summary>
    public string? RequestId { get; set; }
}

public class MediaImageSearchResponse
{
    public MediaImageKind Kind { get; init; }
    public string? RequestId { get; init; }
    /// <summary>One entry per enabled image provider of <see cref="Kind"/>, in the plugin order.</summary>
    public IReadOnlyList<MediaImageProviderResult> Providers { get; init; } = [];
}

public class MediaImageProviderResult
{
    public required string PluginId { get; init; }
    public required string PluginName { get; init; }
    public IReadOnlyList<ImageResult> Images { get; init; } = [];
    public string? Error { get; init; }
}
