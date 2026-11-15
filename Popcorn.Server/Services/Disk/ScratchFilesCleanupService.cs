using Popcorn.Shared.Utils;

namespace Popcorn.Server.Services.Disk;

public class ScratchFilesCleanupService(ILogger<ScratchFilesCleanupService> logger)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(1);
    public const long MaxTotalSizeBytes = 10L * 1024 * 1024 * 1024; // 10 GB

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
        CleanupThumbnails(DirectoryUtils.ThumbnailsFolder);
        // Left over by a process killed while rendering; a day old, nobody is still writing them.
        CleanupThumbnails(DirectoryUtils.ThumbnailsRenderingFolder);
        DeleteLegacyPlayerThumbnails();
        return Task.CompletedTask;
    }

    private void CleanupThumbnails(string folder)
    {
        if (!Directory.Exists(folder))
            return;

        var now = DateTime.UtcNow;

        var files = new DirectoryInfo(folder).GetFiles(
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
    }

    /// <summary>The native players' old thumbnail folder (they use <c>ThumbnailsCache/</c> now): only a cache, dropped.</summary>
    private void DeleteLegacyPlayerThumbnails()
    {
        var folder = DirectoryUtils.LegacyPlayerThumbnailsFolder;
        if (!Directory.Exists(folder))
            return;
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not delete the old player thumbnails folder {folder}", folder);
        }
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