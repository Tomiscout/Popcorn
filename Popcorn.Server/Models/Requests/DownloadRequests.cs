using Popcorn.Downloads;
using System.Text.Json.Serialization;

namespace Popcorn.Server.Models.Requests;

[JsonConverter(typeof(JsonStringEnumConverter<DownloadLinkKind>))]
public enum DownloadLinkKind
{
    /// <summary>magnet: URI or http(s) URL of a .torrent file.</summary>
    Torrent,
    /// <summary>http(s) URL of the file itself.</summary>
    Direct,
}

public class DownloadAddRequest
{
    public DownloadLinkKind Kind { get; set; }
    public string Url { get; set; } = string.Empty;
    /// <summary>Create a library item for the downloaded videos (downloads started from Search).</summary>
    public bool AddToLibrary { get; set; }
    /// <summary>Library item title, e.g. the media page's title; null = the download name.</summary>
    public string? MediaTitle { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    /// <summary>Media page the download was started from (plugin + media id, IMDb id), kept by the library item.</summary>
    public Popcorn.Domain.Models.MediaSourceRef? MediaSource { get; set; }
    /// <summary>Existing library item to add the files to (saved inside its folder); overrides the title/poster.</summary>
    public Guid? MediaItemId { get; set; }
}

public class DownloadLinkRequest
{
    public DownloadLinkKind Kind { get; set; }
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// Several downloads for one library item (e.g. the episodes of a season): <see cref="MediaItemId"/>, or a new item
/// titled <see cref="MediaTitle"/> that each download is added to.
/// </summary>
public class DownloadAddBatchRequest
{
    public List<DownloadLinkRequest> Links { get; set; } = [];
    public Guid? MediaItemId { get; set; }
    public string? MediaTitle { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    public Popcorn.Domain.Models.MediaSourceRef? MediaSource { get; set; }
}

/// <summary>Answer to <see cref="SocketRequests.DownloadAdd"/> / <see cref="SocketRequests.DownloadAddTorrentFile"/>.</summary>
public record DownloadAddedResponse(Guid? Id, string? Name, string? Error);

public class DownloadIdRequest
{
    public Guid Id { get; set; }
}

public class DownloadRemoveRequest
{
    public Guid Id { get; set; }
    public bool DeleteFiles { get; set; }
}

public class DownloadSetFilePriorityRequest
{
    public Guid Id { get; set; }
    public int Index { get; set; }
    public FilePriority Priority { get; set; }
}

public class DownloadSetPriorityRequest
{
    public Guid Id { get; set; }
    public DownloadPriority Priority { get; set; }
}
