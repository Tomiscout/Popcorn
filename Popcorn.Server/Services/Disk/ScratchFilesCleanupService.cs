using Popcorn.Shared.Utils;

namespace Popcorn.Server.Services.Disk;

public class ScratchFilesCleanupService(ILogger<ScratchFilesCleanupService> logger)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);
    private const long MaxTotalSizeBytes = 10L * 1024 * 1024 * 1024; // 10 GB

    public void InitialCleanup()
    {
        Task.WhenAll(
            CleanupVideoCache(),
            CleanupThumbnailsCache()
        );
    }
    
    private Task CleanupVideoCache()
    {
        if (!Directory.Exists(DirectoryUtils.StreamCache))
            return Task.CompletedTask;

        var now = DateTime.UtcNow;

        var files = new DirectoryInfo(DirectoryUtils.StreamCache).GetFiles(
            "*",
            SearchOption.TopDirectoryOnly
        );

        // 1. Delete old files
        foreach (var file in files)
        {
            if (now - file.LastWriteTimeUtc >= MaxAge)
            {
                TryDelete(file);
            }
        }

        // Refresh file list after deletion
        files = new DirectoryInfo(DirectoryUtils.StreamCache).GetFiles(
            "*",
            SearchOption.TopDirectoryOnly
        );

        // 2. Enforce size limit
        long totalSize = files.Sum(f => f.Length);

        if (totalSize > MaxTotalSizeBytes)
        {
            foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc))
            {
                TryDelete(file);
                totalSize -= file.Length;

                if (totalSize <= MaxTotalSizeBytes)
                    break;
            }
        }

        return Task.CompletedTask;
    }
    
    private Task CleanupThumbnailsCache()
    {
        if (!Directory.Exists(DirectoryUtils.ThumbnailsFolder))
            return Task.CompletedTask;

        var now = DateTime.UtcNow;

        var files = new DirectoryInfo(DirectoryUtils.ThumbnailsFolder).GetFiles(
            "*",
            SearchOption.TopDirectoryOnly
        );

        foreach (var file in files)
        {
            if (now - file.LastWriteTimeUtc >= MaxAge)
            {
                TryDelete(file);
            }
        }

        return Task.CompletedTask;
    }

    private void TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch(Exception ex)
        {
            logger.LogError(ex, "Could not delete file {fileName}", file.FullName);
        }
    }
}