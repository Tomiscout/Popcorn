using Popcorn.Domain.Interfaces;
using Popcorn.Server.Attributes;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Responses;

namespace Popcorn.Server.Services;

[WebSocketService]
public class HomeWebService(WebSocketBus bus, IMediaItemRepository mediaItemRepository)
{
    [WebSocketEvent(SocketRequests.MediaList)]
    public async Task SendHomeScreenMediaList()
    {
        var allItems = await mediaItemRepository.GetMediaItemsWithHistory();
        foreach (var item in allItems) //TODO this should be done elsewhere
        {
            item.GetDisplayTitle();
        }
                
        var missingItems = allItems?
            .Where(item => !Directory.Exists(item.FolderDirectory))
            .ToList();

        allItems?.RemoveAll(i => missingItems.Contains(i));

        var recentItems = allItems?
            .Where(x => x.History?.LastOrDefault() != null)
            .OrderBy(x => x.History?.LastOrDefault()?.PlaybackTime ?? DateTime.MaxValue)
            .Reverse()
            .ToList();

        var newItems = allItems?
            .Where(x => x.History?.LastOrDefault() == null)
            .ToList();
                
        await bus.SendEventAsync(SocketRequests.MediaList, new MediaListResponse
        {
            NewItems = newItems,
            RecentItems = recentItems,
            RemovedItems = missingItems
        });
    }
}