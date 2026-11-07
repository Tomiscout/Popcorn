using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Server.Attributes;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Responses;

namespace Popcorn.Server.Services;

[WebSocketService]
public class HomeWebService(
    WebSocketBus bus,
    IMediaItemRepository mediaItemRepository,
    IMediaFileRepository mediaFileRepository,
    ILogger<HomeWebService> logger)
{
    [WebSocketEvent(SocketRequests.MediaList)]
    public async Task SendHomeScreenMediaList()
    {
        var allItems = await mediaItemRepository.GetMediaItemsWithHistory();
        foreach (var item in allItems) //TODO this should be done elsewhere
        {
            item.GetDisplayTitle();
        }
                
        var missingItems = allItems
            .Where(item => !Directory.Exists(item.FolderDirectory))
            .ToList();

        allItems.RemoveAll(i => missingItems.Contains(i));

        var recentItems = allItems
            .Where(x => x.History?.LastOrDefault() != null)
            .OrderBy(x => x.History?.LastOrDefault()?.PlaybackTime ?? DateTime.MaxValue)
            .Reverse()
            .ToList();

        var newItems = allItems
            .Where(x => x.History?.LastOrDefault() == null)
            .ToList();

        var continueWatching = new Dictionary<Guid, ContinueWatching>();
        foreach (var item in allItems)
        {
            if (await GetContinueWatching(item) is { } info) continueWatching[item.Id] = info;
        }
                
        await bus.SendEventAsync(SocketRequests.MediaList, new MediaListResponse
        {
            NewItems = newItems,
            RecentItems = recentItems,
            RemovedItems = missingItems,
            Continue = continueWatching,
        });
    }

    private async Task<ContinueWatching?> GetContinueWatching(MediaItem item)
    {
        try
        {
            item.Files = await mediaFileRepository.GetFilesForItemId(item.Id);
            return ContinueWatching.For(item);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not pick the file to continue for item {itemId}", item.Id);
            return null;
        }
        finally
        {
            item.Files = null!; // the list sends items without their files
        }
    }
}
