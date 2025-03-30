using Popcorn.Domain.Interfaces;
using Popcorn.Persistence.Repositories;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;
using Popcorn.Server.Models.Responses;
using PopcornPlayer.Domain.Extensions;

namespace Popcorn.Server.Handlers;

public static class MediaListHandlers
{
    public static void Register(WebSocketBus bus, IServiceScopeFactory scopeFactory)
    {
        bus.RegisterHandler<object>(SocketRequests.MediaList, async request =>
        {
            using var scope = scopeFactory.CreateScope();

            try
            {
                var mediaItemRepository = scope.ServiceProvider.GetRequiredService<IMediaItemRepository>();
                var imageRepository = scope.ServiceProvider.GetRequiredService<ImageRepository>();

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
            catch (Exception ex)
            {
                throw;
            }
            
        });
        
        bus.RegisterHandler<MediaItemFileListRequest>(SocketRequests.MediaItemFileList, async item =>
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IMediaItemRepository>();
            
            // TODO load directories
            service.GetMediaItem(item.Id);
        });
    }
}