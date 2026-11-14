using System.Text.Json;
using System.Text.Json.Serialization;

namespace Popcorn.Domain.Models;

public enum ItemSourceKind
{
    /// <summary>A folder on disk added to the library (or the folder of an item from before sources existed).</summary>
    Folder,
    /// <summary>One or more downloads of the built-in downloader (a batch shares one source and its folder).</summary>
    Download,
}

/// <summary>
/// Where a set of an item's files came from: a library folder, a download or a batch of downloads. Every
/// <see cref="MediaFile"/> belongs to one (<see cref="MediaFile.SourceId"/>). An item fed by several sources (a download
/// attached to it, items merged) shows one virtual folder per source (<see cref="Domain.MediaTree"/>);
/// the files stay where they are on disk.
/// </summary>
public class ItemSource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ItemId { get; set; }
    public ItemSourceKind Kind { get; set; }
    /// <summary>Name of its virtual folder: the folder / release name.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Folder on disk its files are in (download folder, batch folder, library folder).</summary>
    public string RootPath { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Downloads that filled it, with the file source result they were picked from.</summary>
    public List<SourceOrigin> Origins { get; set; } = [];
    /// <summary>Library item it belonged to before that item was merged into this one (the first merge).</summary>
    public Guid? MergedFromItemId { get; set; }
    public string? MergedFromTitle { get; set; }

    /// <summary>DB column of <see cref="Origins"/>.</summary>
    [JsonIgnore]
    public string? OriginsJson
    {
        get => Origins.Count == 0 ? null : JsonSerializer.Serialize(Origins);
        set => Origins = string.IsNullOrWhiteSpace(value) ? [] : JsonSerializer.Deserialize<List<SourceOrigin>>(value) ?? [];
    }
}

/// <summary>A download that filled an <see cref="ItemSource"/>; kept after the download is removed.</summary>
public record SourceOrigin
{
    public Guid? DownloadId { get; init; }
    /// <summary>Torrent / file name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Magnet or http(s) link; null for .torrent files.</summary>
    public string? Link { get; init; }
    public DateTime AddedAt { get; init; } = DateTime.UtcNow;
    /// <summary>File source result the download was started from (<see cref="FileOrigin"/>), null when added by hand.</summary>
    public FileOrigin? File { get; init; }
}

/// <summary>The file source plugin result a download was picked from.</summary>
public record FileOrigin
{
    public string? PluginId { get; init; }
    /// <summary>Source of a multi-source plugin ("" / null = the plugin itself).</summary>
    public string? SourceId { get; init; }
    /// <summary>The result's title as the plugin listed it.</summary>
    public string? Title { get; init; }
    /// <summary>Site an aggregator got it from (<c>FileResult.Origin</c>).</summary>
    public string? Origin { get; init; }

    public static FileOrigin? Clean(FileOrigin? origin) =>
        origin is null || (string.IsNullOrWhiteSpace(origin.PluginId) && string.IsNullOrWhiteSpace(origin.Title)) ? null : origin;
}
