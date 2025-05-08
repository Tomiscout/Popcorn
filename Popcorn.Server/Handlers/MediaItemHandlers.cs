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