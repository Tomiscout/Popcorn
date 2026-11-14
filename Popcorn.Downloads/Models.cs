using System.Text.Json.Serialization;
using Popcorn.Domain.Models;

namespace Popcorn.Downloads;

[JsonConverter(typeof(JsonStringEnumConverter<DownloadKind>))]
public enum DownloadKind
{
    Torrent,
    Direct,
}

[JsonConverter(typeof(JsonStringEnumConverter<DownloadState>))]
public enum DownloadState
{
    /// <summary>Torrent: waiting for the metadata of a magnet link. Direct: asking the server about the file.</summary>
    Metadata,
    Hashing,
    Downloading,
    Seeding,
    /// <summary>Stopped by the user.</summary>
    Paused,
    /// <summary>Everything wanted is downloaded and nothing more happens (direct download done, or seed limit reached).</summary>
    Completed,
    Error,
}

/// <summary>
/// Share of the bandwidth a download gets while others download too: lower ones are throttled to a fraction of the
/// rate the highest ones reach (<see cref="DownloadManager"/>). A download a player streams from ranks above all.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DownloadPriority>))]
public enum DownloadPriority
{
    Low,
    Normal,
    High,
}

/// <summary>Order of the files inside a torrent (<see cref="SequentialPieceRequester"/>); the file a player reads goes first.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FilePriority>))]
public enum FilePriority
{
    /// <summary>Not downloaded.</summary>
    Skip,
    Low,
    Normal,
    High,
}

/// <summary>Persisted state of one download (<c>&lt;data&gt;/downloads/downloads.json</c>).</summary>
public class DownloadRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DownloadKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Torrent: magnet URI, or path of the stored .torrent (<c>&lt;data&gt;/downloads/torrents</c>). Direct: the URL.</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>Folder of this download (one per download, inside the download folder).</summary>
    public string SavePath { get; set; } = string.Empty;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    /// <summary>Stopped by the user; not started again on restart.</summary>
    public bool Paused { get; set; }
    /// <summary>Done for good: direct download complete, or torrent reached its seed limit.</summary>
    public bool Finished { get; set; }
    /// <summary>Resumed by the user after the seed limit stopped it: seeds until paused again.</summary>
    public bool IgnoreSeedLimit { get; set; }
    public string? Error { get; set; }
    public DownloadPriority Priority { get; set; } = DownloadPriority.Normal;

    /// <summary>Create a library item for the downloaded video files.</summary>
    public bool AddToLibrary { get; set; }
    public string? MediaTitle { get; set; }
    public string? PosterUrl { get; set; }
    public string? BackdropUrl { get; set; }
    /// <summary>Media page the download was started from: given to the library item (<see cref="MediaItem.Source"/>).</summary>
    public MediaSourceRef? MediaSource { get; set; }
    public Guid? MediaItemId { get; set; }
    /// <summary>
    /// Downloaded for this existing library item: saved in a folder inside the item's folder and its videos are added
    /// to the item (<see cref="MediaItemId"/> is set once they are). The item is not owned by the download.
    /// </summary>
    public Guid? AttachToItemId { get; set; }

    // Torrent
    /// <summary>File index → priority, only files that are not <see cref="FilePriority.Normal"/>.</summary>
    public Dictionary<int, FilePriority> FilePriorities { get; set; } = [];
    /// <summary>Old format of skipped files (moved into <see cref="FilePriorities"/> on load).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<int>? SkippedFiles { get; set; }
    /// <summary>Uploaded bytes of earlier sessions (MonoTorrent's counters restart with every start).</summary>
    public long UploadedBytes { get; set; }
    public double SeedingSeconds { get; set; }

    // Direct
    public string? FileName { get; set; }
    /// <summary>-1 = unknown (server sent no length).</summary>
    public long Length { get; set; } = -1;
    public bool SupportsRanges { get; set; }
    /// <summary>Completed chunks of <see cref="DirectDownload.ChunkSize"/> bytes, bit per chunk (base64 in JSON).</summary>
    public byte[]? Chunks { get; set; }
}

public record DownloadFileDto(int Index, string Path, long Length, double Progress, FilePriority Priority, bool Streaming, bool IsVideo, Guid? MediaFileId)
{
    public bool Wanted => Priority != FilePriority.Skip;
}

public record DownloadDto
{
    public required Guid Id { get; init; }
    public required DownloadKind Kind { get; init; }
    public required string Name { get; init; }
    /// <summary><see cref="Name"/> run through the file name parser (title, episodes, resolution).</summary>
    public required string DisplayName { get; init; }
    public required DownloadState State { get; init; }
    public string? Error { get; init; }
    public required string SavePath { get; init; }
    /// <summary>Bytes of the wanted files, -1 = unknown yet.</summary>
    public long Size { get; init; }
    public long Downloaded { get; init; }
    public double Progress { get; init; }
    public long DownloadRate { get; init; }
    public long UploadRate { get; init; }
    public long Uploaded { get; init; }
    public double Ratio { get; init; }
    /// <summary>Seconds, null = unknown.</summary>
    public long? Eta { get; init; }
    public int Peers { get; init; }
    public int Seeds { get; init; }
    public DateTime AddedAt { get; init; }
    /// <summary>Library item the files are in, or are going to be added to (attached downloads before linking).</summary>
    public Guid? MediaItemId { get; init; }
    public string? MediaItemTitle { get; init; }
    /// <summary>Added to an existing item: deleting its files only drops them from the item.</summary>
    public bool AttachedToItem { get; init; }
    public DownloadPriority Priority { get; init; }
    /// <summary>A player reads from it right now: it ranks above every priority.</summary>
    public bool Streaming { get; init; }
    public IReadOnlyList<DownloadFileDto> Files { get; init; } = [];
}

public record DownloadStatsDto(long DownloadRate, long UploadRate, int Active, PortStatusDto Port);

/// <summary>Whether peers can connect to the torrent listen port (<see cref="DownloadManager.GetPortStatus"/>).</summary>
/// <param name="Port">The listen port setting, 0 = random.</param>
public record PortStatusDto(int Port, PortReachability Reachability, RouterMapping Mapping);

public enum PortReachability
{
    /// <summary>Not enough time with peers yet to tell.</summary>
    Unknown,
    /// <summary>A peer connected to us.</summary>
    Reachable,
    /// <summary>Torrents had peers for a while, none of them ever connected to us.</summary>
    Unreachable,
}

/// <summary>Automatic port forwarding on the router (UPnP / NAT-PMP).</summary>
public enum RouterMapping
{
    /// <summary>Still asking the router.</summary>
    Pending,
    Mapped,
    /// <summary>A router answered and refused the mapping.</summary>
    Failed,
    /// <summary>No router answered: UPnP / NAT-PMP is off or not supported.</summary>
    NoRouter,
}

/// <summary>What the players need to play a file that is still being downloaded.</summary>
/// <param name="StreamPath">Server-relative URL of the waiting stream (<c>api/downloads/{id}/files/{index}</c>).</param>
/// <param name="Ranges">Downloaded parts of the file as [start, end] fractions of its playback time (of its length when the container has no usable index).</param>
public record DownloadPlaybackDto(Guid DownloadId, int FileIndex, bool Complete, string StreamPath, long Length, IReadOnlyList<double[]> Ranges);

public record AddDownloadOptions
{
    public bool AddToLibrary { get; init; }
    public string? MediaTitle { get; init; }
    public string? PosterUrl { get; init; }
    public string? BackdropUrl { get; init; }
    public MediaSourceRef? MediaSource { get; init; }
    /// <summary>Existing library item to add the files to (<see cref="DownloadRecord.AttachToItemId"/>).</summary>
    public Guid? MediaItemId { get; init; }
}
