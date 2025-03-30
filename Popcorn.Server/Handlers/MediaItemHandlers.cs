using Popcorn.Domain.Interfaces;
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
            
            
            await bus.SendEventAsync(SocketRequests.MediaItemHistory, mediaItem.History);

            mediaItem.Files = null!;
            mediaItem.History = null!;
            await bus.SendEventAsync(SocketRequests.MediaItem, mediaItem);
            if (!string.IsNullOrWhiteSpace(mediaItem.ImagePath))
            {
                var imageData = await imageRepository.LoadImageBlob(mediaItem.ImagePath);
                if (imageData != null)
                {
                    await bus.SendBinaryAsync(SocketRequests.ImageFile, imageData, mediaItem.ImagePath);
                }
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