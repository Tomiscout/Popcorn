using Popcorn.Application.Extensions;
using Popcorn.Application.Services;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;
using Popcorn.Server.Models.Responses;
using Popcorn.Domain.Enum;
using Popcorn.Domain.Models;
using Popcorn.Server.Attributes;
using Popcorn.SegmentFinder;
using Popcorn.Downloads;

namespace Popcorn.Server.Services;

[WebSocketService]
public class MediaItemWebService(
    WebSocketBus bus,
    IMediaItemRepository mediaItemRepository,
    MediaItemService mediaItemService,
    MediaImageService mediaImageService,
    ItemHistoryService itemHistoryService,
    IMediaFileRepository mediaFileRepository,
    IFileService fileService,
    SegmentFinderService segmentFinder,
    DownloadManager downloads,
    ILogger<MediaItemWebService> logger
    )
{
    
    [WebSocketEvent(SocketRequests.MediaItem)]
    public async Task SendMediaItems(MediaItemRequest item)
    {            
        var mediaItem = await mediaItemRepository.GetMediaItemFull(item.Id);
        if (mediaItem == null) return;

        mediaItem.GetDisplayTitle();

        // Files are tagged when scanned; this catches rows stored before that.
        foreach (var file in mediaItem.Files.Where(f => f is { Type: FileType.File, TagsProcessed: false }))
        {
            file.ParseTags();
            await mediaFileRepository.UpdateMediaFile(file);
        }
            
        var currentFiles = mediaItem.Files.GetFilesForDirectory(mediaItem.FolderDirectory)
            .OrderByDescending(f => f.Type)
            .ThenBy(f => f.Title)
            .ToList();
        await bus.SendEventAsync(SocketRequests.MediaItemFileList, currentFiles);

        var historyItems = await itemHistoryService.GetItemHistoryAsync(mediaItem.Id, mediaItem.History);
        await bus.SendEventAsync(SocketRequests.MediaItemHistory, historyItems);
        ContinueWatching? continueWatching = null;
        try
        {
            continueWatching = ContinueWatching.For(mediaItem);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not pick the file to continue for item {itemId}", mediaItem.Id);
        }

        mediaItem.Files = null!;
        mediaItem.History = null!;
        await bus.SendEventAsync(SocketRequests.MediaItem, mediaItem);
        await bus.SendEventAsync(SocketRequests.MediaItemContinue, continueWatching);
    }

    [WebSocketEvent(SocketRequests.MediaItemFileList)]
    public async Task SendMediaItemFileList(MediaItemFileListRequest item)
    {
        // TODO load directories
        // TODO Unused
        await mediaItemRepository.GetMediaItem(item.Id);
    }
    
    [WebSocketEvent(SocketRequests.CreateMediaItem)]
    public async Task CreateMediaItem(CreateMediaItemRequest request)
    {
        var newItem = new MediaItem
        {
            FolderDirectory = request.FolderPath,
            Title = request.Title.Trim(),
            Files = new ()
        };
        if (string.IsNullOrWhiteSpace(newItem.Title)) return;

        var mediaScanResult = await MediaLoader.ScanDirectorySingle(newItem.FolderDirectory);
        if (mediaScanResult == null) return;

        await mediaItemService.CreateMediaItemFromScan(newItem, mediaScanResult);
        await mediaImageService.TrySetImageAsync(newItem, MediaImageKind.Poster, request.PosterUrl);
        await mediaImageService.TrySetImageAsync(newItem, MediaImageKind.Backdrop, request.BackdropUrl);
    }

    [WebSocketEvent(SocketRequests.SetMediaItemImage)]
    public async Task SetMediaItemImage(SetMediaItemImageRequest request)
    {
        var mediaItem = await mediaItemRepository.GetMediaItem(request.Id);
        if (mediaItem == null) return;

        await mediaImageService.SetImageAsync(mediaItem, request.Kind, request.Url); // failures -> ErrorWhenHandlingEvent
        mediaItem.GetDisplayTitle();
        await bus.SendEventAsync(SocketRequests.MediaItem, mediaItem);
    }

    [WebSocketEvent(SocketRequests.RemoveMediaItem)]
    public async Task DeleteMediaItem(RemoveMediaItemRequest request)
    {
        var mediaItemId = request.Id;
        var mediaItem = await mediaItemRepository.GetMediaItemWithSegments(mediaItemId);
        if (request.DeleteFiles && mediaItem != null)
        {
            // Stop the item's downloads first so nothing writes into the folders being deleted.
            foreach (var download in (await downloads.GetListAsync()).Where(d => d.MediaItemId == mediaItemId))
                await downloads.RemoveAsync(download.Id, deleteFiles: true);
            var failed = await mediaItemService.DeleteItemFilesFromDisk(mediaItem);
            if (failed.Count > 0)
                logger.LogWarning("Could not delete {Count} path(s) of item {ItemId}: {Paths}", failed.Count, mediaItemId, failed);
        }
        await mediaItemRepository.DeleteMediaItem(mediaItemId);
        segmentFinder.ForgetItem(mediaItemId, mediaItem?.Segments ?? []);
        if (mediaItem != null) mediaImageService.DeleteImages(mediaItem);
    }
    
    [WebSocketEvent(SocketRequests.RefreshMediaItem)]
    public async Task RescanMediaItem(Guid mediaItemId)
    {
        await mediaItemService.RescanItem(mediaItemId);
        await SendMediaItems(new MediaItemRequest { Id = mediaItemId });
    }
    
    [WebSocketEvent(SocketRequests.OpenMediaItemFolder)]
    public async Task OpenMediaItemFolder(Guid mediaItemId)
    {
        var mediaItem = await mediaItemRepository.GetMediaItem(mediaItemId);
        if (mediaItem == null) return;
        
        fileService.OpenDirectoryExplorer(mediaItem.FolderDirectory);
    }
}