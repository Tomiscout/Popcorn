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
        await SendFlatFileList(mediaItem);

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

    /// <summary>Files in play order + whether the item gets them from several downloads (the list's default view then).</summary>
    private async Task SendFlatFileList(MediaItem mediaItem)
    {
        List<MediaFile> files;
        try
        {
            files = mediaItem.GetPlayOrder();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not order the files of item {itemId}", mediaItem.Id);
            files = [];
        }
        await bus.SendEventAsync(SocketRequests.MediaItemFlatFileList, new MediaItemFlatFileListResponse
        {
            ItemId = mediaItem.Id,
            Files = files,
            // Its tree has a virtual folder per source then (downloads, merged items).
            MultipleDownloads = mediaItem.Files.Any(f => f.IsVirtual),
        });
    }

    [WebSocketEvent(SocketRequests.MediaItemHistoryDelete)]
    public async Task DeleteMediaItemHistory(DeleteMediaItemHistoryRequest request)
    {
        if (request.HistoryId is { } historyId)
            await mediaItemRepository.DeleteHistoryItem(request.ItemId, historyId);
        else
            await mediaItemRepository.DeleteHistoryAll(request.ItemId);
        // History decides played marks and what Play continues: its pages (this one too) get the ping and refetch.
        itemHistoryService.NotifyChanged(request.ItemId);
    }

    [WebSocketEvent(SocketRequests.RenameMediaItem)]
    public async Task RenameMediaItem(RenameMediaItemRequest request)
    {
        var title = request.Title.Trim();
        if (string.IsNullOrWhiteSpace(title)) return;
        var mediaItem = await mediaItemRepository.GetMediaItem(request.Id);
        if (mediaItem == null) return;

        await mediaItemRepository.SetTitle(mediaItem.Id, title);
        mediaItem.Title = title;
        mediaItem.ShortTitle = title;
        await bus.SendEventAsync(SocketRequests.MediaItem, mediaItem);
    }

    [WebSocketEvent(SocketRequests.MediaItemMatch)]
    public async Task Match(MediaItemMatchRequest request)
    {
        var items = await mediaItemRepository.GetMediaItemsWithHistory();
        var ranked = MediaItemMatcher.Rank(request.Name, items)
            .Select(m => new MediaItemMatchDto(m.Item.Id, m.Item.GetDisplayTitle(), m.Item.ImagePath, Math.Round(m.Score, 3)))
            .ToList();
        await bus.SendEventAsync(SocketRequests.MediaItemMatch, new MediaItemMatchResponse(request.Name, ranked));
    }

    /// <summary>
    /// Merges an item into another: its sources become virtual folders of the target, its downloads feed the target,
    /// its history and intro/outro references move along. The target's page gets the merged item.
    /// </summary>
    [WebSocketEvent(SocketRequests.MediaItemMerge)]
    public async Task Merge(MediaItemMergeRequest request)
    {
        var merged = await mediaItemService.MergeAsync(request.TargetId, request.OtherId);
        if (merged is null) return;
        await downloads.ReassignItemAsync(request.OtherId, request.TargetId);
        segmentFinder.ForgetItem(request.OtherId, []); // its references moved to the target
        mediaImageService.DeleteImages(merged); // the pictures the target did not take
        logger.LogInformation("Merged item {Other} ({Title}) into {Target}", request.OtherId, merged.Title, request.TargetId);
        await SendMediaItems(new MediaItemRequest { Id = request.TargetId });
    }

    [WebSocketEvent(SocketRequests.MediaItemSourceRemove)]
    public async Task RemoveSource(MediaItemSourceRemoveRequest request)
    {
        var sources = await mediaItemRepository.GetSources(request.ItemId);
        if (sources.All(s => s.Id != request.SourceId)) return;
        if (sources.Count <= 1) throw new InvalidOperationException("The item's only source cannot be removed; remove the item instead");

        // Downloads first: stopped before their files are deleted, and not linked again when they finish.
        var released = await downloads.ReleaseSourceAsync(request.ItemId, request.SourceId, request.DeleteFiles);
        var failed = await mediaItemService.RemoveSourceAsync(request.ItemId, request.SourceId, request.DeleteFiles);
        if (failed.Count > 0)
            logger.LogWarning("Could not delete {Count} path(s) of source {Source}: {Paths}", failed.Count, request.SourceId, failed);
        logger.LogInformation("Removed source {Source} from item {Item} ({Downloads} downloads, files deleted: {Deleted})",
            request.SourceId, request.ItemId, released, request.DeleteFiles);
        await SendMediaItems(new MediaItemRequest { Id = request.ItemId });
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