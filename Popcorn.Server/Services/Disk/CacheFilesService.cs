using Popcorn.Domain.Interfaces;
using Popcorn.Server.Models.Requests;
using Popcorn.Shared.Logging;
using Popcorn.Shared.Utils;

namespace Popcorn.Server.Services.Disk;

/// <summary>
/// Reads what is in the cache folders (Settings → Storage): seek bar thumbnails (web and native players share them), and the
/// stream cache (remuxes, converted audio, subtitles); and the size of the logs folder. Every cached file's name starts with the id of the media file
/// it was made from (<c>&lt;fileId&gt;-...</c>); the files are grouped per media file and kind.
/// </summary>
public class CacheFilesService(IMediaFileRepository mediaFiles, IMediaItemRepository mediaItems, ILogger<CacheFilesService> logger)
{
    private static readonly string[] ThumbnailFolders = [DirectoryUtils.ThumbnailsFolder];

    private record CachedFile(CacheKind Kind, Guid? FileId, FileInfo Info, bool InProgress);

    private List<CachedFile> ListCachedFiles() =>
        ThumbnailFolders.SelectMany(f => ListFiles(f).Select(i => Classify(i, CacheKind.Thumbnails)))
            .Concat(ListFiles(DirectoryUtils.StreamCache).Select(i => Classify(i, null)))
            .ToList();

    /// <summary>
    /// Deletes the cached files of <paramref name="kind"/> made from <paramref name="fileId"/> (null: of every kind /
    /// media file). Files still being written are left alone. Whoever needs a deleted file makes it again: every user
    /// checks that it exists first (thumbnails, <c>FileStreamService</c>, <c>AudioTrackService</c>).
    /// </summary>
    public CacheClearResult Clear(CacheKind? kind, Guid? fileId)
    {
        int files = 0, skipped = 0;
        long bytes = 0;
        foreach (var file in ListCachedFiles())
        {
            if (kind != null && file.Kind != kind) continue;
            if (fileId != null && file.FileId != fileId) continue;
            if (file.InProgress)
            {
                skipped++;
                continue;
            }
            try
            {
                var length = file.Info.Length;
                file.Info.Delete();
                files++;
                bytes += length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
                logger.LogDebug(ex, "Could not delete cached file {File}", file.Info.FullName);
            }
        }

        logger.LogInformation("Cleared cache ({Kind}, {FileId}): {Files} files, {Bytes} bytes, {Skipped} skipped",
            kind?.ToString() ?? "all", fileId?.ToString() ?? "all files", files, bytes, skipped);
        return new CacheClearResult { Files = files, Bytes = bytes, Skipped = skipped };
    }

    public async Task<CacheOverview> GetOverviewAsync()
    {
        var files = ListCachedFiles();

        var fileIds = files.Where(f => f.FileId != null).Select(f => f.FileId!.Value.ToString()).Distinct().ToArray();
        var known = fileIds.Length == 0
            ? []
            : (await mediaFiles.GetFilesByIds(fileIds)).ToDictionary(f => f.Id);

        var itemTitles = new Dictionary<Guid, string?>();
        foreach (var itemId in known.Values.Select(f => f.ItemId).Distinct())
        {
            itemTitles[itemId] = (await mediaItems.GetMediaItem(itemId))?.GetDisplayTitle();
        }

        var entries = files
            .GroupBy(f => (f.Kind, f.FileId))
            .Select(g =>
            {
                var mediaFile = g.Key.FileId is { } id ? known.GetValueOrDefault(id) : null;
                return new CacheEntry
                {
                    Kind = g.Key.Kind,
                    FileId = g.Key.FileId,
                    ItemId = mediaFile?.ItemId,
                    ItemTitle = mediaFile == null ? null : itemTitles.GetValueOrDefault(mediaFile.ItemId),
                    FilePath = mediaFile?.Path,
                    Files = g.Count(),
                    Bytes = g.Sum(f => f.Info.Length),
                    LastWrite = g.Max(f => f.Info.LastWriteTimeUtc),
                    InProgress = g.Any(f => f.InProgress),
                };
            })
            .OrderByDescending(e => e.Bytes)
            .ToList();

        var kinds = Enum.GetValues<CacheKind>().Select(kind => new CacheKindSummary
        {
            Kind = kind,
            Folders = kind == CacheKind.Thumbnails ? [..ThumbnailFolders] : [DirectoryUtils.StreamCache],
            Files = entries.Where(e => e.Kind == kind).Sum(e => e.Files),
            Bytes = entries.Where(e => e.Kind == kind).Sum(e => e.Bytes),
        }).ToList();

        return new CacheOverview
        {
            Kinds = kinds,
            Entries = entries,
            MaxAgeDays = (int)ScratchFilesCleanupService.MaxAge.TotalDays,
            StreamCacheLimitBytes = ScratchFilesCleanupService.MaxTotalSizeBytes,
            Logs = GetLogs(),
        };
    }

    private LogFolderSummary GetLogs()
    {
        var logs = ListFiles(DirectoryUtils.LogsFolder).ToList();
        return new LogFolderSummary
        {
            Folder = DirectoryUtils.LogsFolder,
            Files = logs.Count,
            Bytes = logs.Sum(f => f.Length),
            LargestFileBytes = logs.Count == 0 ? 0 : logs.Max(f => f.Length),
            MaxFileBytes = DailyLogFileSink.MaxFileBytes,
        };
    }

    private IEnumerable<FileInfo> ListFiles(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? new DirectoryInfo(folder).GetFiles("*", SearchOption.TopDirectoryOnly)
                : [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not list cache folder {Folder}", folder);
            return [];
        }
    }

    /// <summary>Kind by name in the stream cache (<paramref name="kind"/> null): see <see cref="CacheKind"/>.</summary>
    private static CachedFile Classify(FileInfo file, CacheKind? kind)
    {
        var name = file.Name;
        var inProgress = name.EndsWith(".part", StringComparison.OrdinalIgnoreCase);
        if (inProgress) name = name[..^".part".Length];

        Guid? fileId = name.Length >= 36 && Guid.TryParse(name.AsSpan(0, 36), out var id) ? id : null;

        kind ??= Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".mp4" => CacheKind.Remux,
            ".m4a" => CacheKind.Audio,
            _ => CacheKind.Subtitles,
        };
        return new CachedFile(kind.Value, fileId, file, inProgress);
    }
}
