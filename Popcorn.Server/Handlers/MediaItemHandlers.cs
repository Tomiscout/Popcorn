using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Persistence.Repositories;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;
using PopcornPlayer.Domain.Extensions;

namespace Popcorn.Server.Handlers;

public static class MediaItemHandlers
{
    public static void Register(WebSocketBus bus, IServiceScopeFactory scopeFactory)
    {
        bus.RegisterHandler<MediaItemRequest>(SocketRequests.MediaItem, async item =>
        {
            using var scope = scopeFactory.CreateScope();
            var mediaItemRepository = scope.ServiceProvider.GetRequiredService<IMediaItemRepository>();
            var imageRepository = scope.ServiceProvider.GetRequiredService<ImageRepository>();
            
            var mediaItem = await mediaItemRepository.GetMediaItemFull(item.Id);
            if (mediaItem == null) return;
            
            var currentFiles = mediaItem.Files.GetFilesForDirectory(mediaItem.FolderDirectory)
                .OrderBy(f => f.Title)
                .ToList();
            await bus.SendEventAsync(SocketRequests.MediaItemFileList, currentFiles);

            var historyItems = mediaItem.History.Select(h =>
            {
                var file = mediaItem.Files.FirstOrDefault();
                return new ItemHistoryDto
                {
                    Title = file?.ShortTitle ?? file?.Title ?? string.Empty,
                    PlaybackTime = h.PlaybackTime,
                    FileId = h.FileId,
                };
            });
            await bus.SendEventAsync(SocketRequests.MediaItemHistory, historyItems);

            mediaItem.Files = null!;
            mediaItem.History = null!;
            await bus.SendEventAsync(SocketRequests.MediaItem, mediaItem);
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