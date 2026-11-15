using Popcorn.Persistence;
using Popcorn.Server.Models.Requests;
using Popcorn.Shared.Logging;
using Popcorn.Shared.Utils;

namespace Popcorn.Server.Services.Disk;

/// <summary>
/// Deletes log files older than <see cref="Domain.Models.SettingsModel.LogRetentionDays"/> (all apps' files in
/// <c>logs/</c>): at start, every few hours (the server runs for weeks) and when the setting changes. Daily files go by
/// the day in their name, others (CEF's <c>cef*.log</c>) by their last write.
/// </summary>
public class LogCleanupService(SettingsRepository settingsRepository, ILogger<LogCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            await CleanupAsync();
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    public async Task CleanupAsync()
    {
        try
        {
            var days = (await settingsRepository.GetSettingsAsync()).LogRetentionDays;
            Cleanup(days);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Log cleanup failed");
        }
    }

    /// <summary>
    /// Settings → Storage, "Clear logs": deletes every log file but today's. Those stay: the running apps hold them
    /// open, and on Linux deleting one would send the rest of the day's logs to an unlinked file.
    /// </summary>
    public CacheClearResult ClearAllButToday() => Cleanup(1);

    private CacheClearResult Cleanup(int retentionDays)
    {
        var folder = DirectoryUtils.LogsFolder;
        if (!Directory.Exists(folder)) return new CacheClearResult();

        // Today counts as one of the days kept.
        var firstKeptDay = DateOnly.FromDateTime(DateTime.Now).AddDays(-(retentionDays - 1));
        var cutoff = firstKeptDay.ToDateTime(TimeOnly.MinValue);
        int deleted = 0, skipped = 0;
        long bytes = 0;
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles())
        {
            var old = LogFileName.TryParse(file.Name, out _, out var day)
                ? day < firstKeptDay
                : file.LastWriteTime < cutoff;
            if (!old) continue;
            try
            {
                var length = file.Length;
                file.Delete();
                deleted++;
                bytes += length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
                logger.LogDebug(ex, "Could not delete log file {File}", file.Name);
            }
        }

        if (deleted > 0)
            logger.LogInformation("Deleted {Count} log files older than {Days} days", deleted, retentionDays);
        return new CacheClearResult { Files = deleted, Bytes = bytes, Skipped = skipped };
    }
}
