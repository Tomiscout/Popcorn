using Popcorn.Domain;

namespace Popcorn.Downloads;

/// <summary>One file of a download.</summary>
public record DownloadFile(int Index, string FullPath, string RelativePath, long Length, double Progress, FilePriority Priority)
{
    public bool Wanted => Priority != FilePriority.Skip;
    public bool IsVideo => MediaLoader.VideoFormats.Any(ext => FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A torrent or a direct download, as the manager and the UI see it.</summary>
public abstract class Download(DownloadRecord record)
{
    public DownloadRecord Record { get; } = record;
    public Guid Id => Record.Id;

    public abstract DownloadState State { get; }
    /// <summary>Bytes of the wanted files, -1 = unknown.</summary>
    public abstract long Size { get; }
    public abstract long Downloaded { get; }
    public abstract long DownloadRate { get; }
    public virtual long UploadRate => 0;
    public virtual long Uploaded => 0;
    public virtual int Peers => 0;
    public virtual int Seeds => 0;
    /// <summary>Files, empty while unknown (magnet metadata, direct download before the first response).</summary>
    public abstract IReadOnlyList<DownloadFile> Files { get; }
    /// <summary>Everything wanted is on disk.</summary>
    public abstract bool IsComplete { get; }
    /// <summary>Is downloading or seeding (uses the network).</summary>
    public bool IsActive => State is DownloadState.Metadata or DownloadState.Hashing or DownloadState.Downloading or DownloadState.Seeding;

    public abstract Task StartAsync();
    public abstract Task StopAsync();
    /// <summary>Stops and releases everything; the record is dropped by the manager.</summary>
    public abstract Task RemoveAsync();
    public abstract IStreamSource? OpenFile(int index);
    /// <summary>Downloaded parts of a file as [start, end] fractions.</summary>
    public abstract IReadOnlyList<double[]> FileRanges(int index);
    public virtual Task SetFilePriorityAsync(int index, FilePriority priority) => Task.CompletedTask;
    /// <summary>Index of the file a player is reading right now, null = none.</summary>
    public abstract int? StreamingFile { get; }
    /// <summary>Download rate cap from the priorities of all downloads (bytes/s, 0 = none); set every tick.</summary>
    public abstract Task SetThrottleAsync(long bytesPerSecond);
    /// <summary>Called by the manager every second.</summary>
    public virtual Task TickAsync(TimeSpan elapsed) => Task.CompletedTask;

    public double Progress => Size > 0 ? Math.Clamp((double)Downloaded / Size, 0, 1) : IsComplete ? 1 : 0;
}
