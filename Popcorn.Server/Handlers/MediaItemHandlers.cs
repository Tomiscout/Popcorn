using Popcorn.Application.Services;
using Popcorn.Domain;
using Popcorn.Domain.Enum;
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
            var itemHistoryService = scope.ServiceProvider.GetRequiredService<ItemHistoryService>();
            
            var mediaItem = await mediaItemRepository.GetMediaItemFull(item.Id);
            if (mediaItem == null) return;

            mediaItem.GetDisplayTitle();

            // This is wonky way to process tags, since library is JS only. Would need to port it to c# or use somthing else
            var unprocessedFiles = mediaItem.Files
                .Where(f => f is { Type: FileType.File, TagsProcessed: false })
                .ToDictionary(f => f.Id!.Value, f => f.Title);
            if (unprocessedFiles.Count > 0)
            {
                await bus.SendEventAsync(SocketRequests.ParseTags, unprocessedFiles);
            }
            
            var currentFiles = mediaItem.Files.GetFilesForDirectory(mediaItem.FolderDirectory)
                .OrderBy(f => f.Title)
                .ToList();
            await bus.SendEventAsync(SocketRequests.MediaItemFileList, currentFiles);

            var historyItems = await itemHistoryService.GetItemHistoryAsync(mediaItem.Id, mediaItem.History);
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
        
        bus.RegisterBinaryHandler(SocketRequests.CreateMediaItem, async (metadata, fileBytes) =>
        {
            using var scope = scopeFactory.CreateScope();
            var mediaItemService = scope.ServiceProvider.GetRequiredService<MediaItemService>();
            var mediaFilesRepository = scope.ServiceProvider.GetRequiredService<IMediaFileRepository>();

            var newItem = new MediaItem
            {
                FolderDirectory = metadata[0],
                Title = metadata[1],
                Files = new ()
            };
            
            var mediaScanResult = await MediaLoader.ScanDirectorySingle(newItem.FolderDirectory);
            if (mediaScanResult == null) return;

            var newItemId = await mediaItemService.CreateMediaItemFromScan(newItem, mediaScanResult);
            var mediaFiles = await mediaFilesRepository.GetFilesForItemId(newItemId);
            
            // This is wonky way to process tags, since library is JS only. Would need to port it to c# or use somthing else
            var unprocessedFiles = mediaFiles
                .Where(f => f is { Type: FileType.File, TagsProcessed: false })
                .ToDictionary(f => f.Id!.Value, f => f.Title);
            if (unprocessedFiles.Count > 0)
            {
                await bus.SendEventAsync(SocketRequests.ParseTags, unprocessedFiles);
            }
        });
        
        bus.RegisterHandler<Guid>(SocketRequests.RemoveMediaItem, async (mediaItemId) =>
        {
            using var scope = scopeFactory.CreateScope();
            var mediaItemRepository = scope.ServiceProvider.GetRequiredService<IMediaItemRepository>();

            await mediaItemRepository.DeleteMediaItem(mediaItemId);
        });
    }
}