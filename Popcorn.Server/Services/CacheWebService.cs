using Popcorn.Server.Attributes;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;
using Popcorn.Server.Services.Disk;

namespace Popcorn.Server.Services;

/// <summary>Settings → Storage: what the thumbnail and stream caches hold, and clearing them.</summary>
[WebSocketService]
public class CacheWebService(WebSocketBus bus, CacheFilesService cacheFiles, LogCleanupService logCleanup,
    ILogger<CacheWebService> logger)
{
    [WebSocketEvent(SocketRequests.CacheList)]
    public Task SendList()
    {
        // Thousands of thumbnails to stat: not on the socket's message loop.
        _ = Task.Run(async () =>
        {
            try
            {
                await bus.SendEventAsync(SocketRequests.CacheList, await cacheFiles.GetOverviewAsync());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not list the cache folders");
            }
        });
        return Task.CompletedTask;
    }

    /// <summary>Deletes cached files, answers with what was deleted, then the new <c>CacheList</c>.</summary>
    [WebSocketEvent(SocketRequests.CacheClear)]
    public Task Clear(CacheClearRequest request)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var result = cacheFiles.Clear(request.Kind, request.FileId);
                await bus.SendEventAsync(SocketRequests.CacheClear, result);
                await bus.SendEventAsync(SocketRequests.CacheList, await cacheFiles.GetOverviewAsync());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not clear the cache");
            }
        });
        return Task.CompletedTask;
    }

    /// <summary>Deletes all log files but today's; answers like <see cref="Clear"/>.</summary>
    [WebSocketEvent(SocketRequests.CacheClearLogs)]
    public Task ClearLogs()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await bus.SendEventAsync(SocketRequests.CacheClearLogs, logCleanup.ClearAllButToday());
                await bus.SendEventAsync(SocketRequests.CacheList, await cacheFiles.GetOverviewAsync());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not clear the logs");
            }
        });
        return Task.CompletedTask;
    }
}
