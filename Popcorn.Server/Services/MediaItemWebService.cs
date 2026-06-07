using Popcorn.Application.Extensions;
using Popcorn.Application.Services;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;
using Popcorn.Domain.Enum;
using Popcorn.Domain.Models;
using Popcorn.Server.Attributes;

namespace Popcorn.Server.Services;

[WebSocketService]
public class MediaItemWebService(
    WebSocketBus bus,
    IMediaItemRepository mediaItemRepository,
    MediaItemService mediaItemService,
    ItemHistoryService itemHistoryService,
    IMediaFileRepository mediaFileRepository,
    IFileService fileService
    )
{
    
    [WebSocketEvent(SocketRequests.MediaItem)]
    public async Task SendMediaItems(MediaItemRequest item)
    {            
        var mediaItem = await mediaItemRepository.GetMediaItemFull(item.Id);
        if (mediaItem == null) return;

        mediaItem.GetDisplayTitle();

        // This is wonky way to process tags, since library is JS only. Would need to port it to c# or use somthing else
        var unprocessedFiles = mediaItem.Files
            .Where(f => f is { Type: FileType.File, TagsProcessed: false })
            .ToDictionary(f => f.Id, f => f.Title);
        if (unprocessedFiles.Count > 0)
        {
            await bus.SendEventAsync(SocketRequests.ParseTags, unprocessedFiles);
        }
            
        var currentFiles = mediaItem.Files.GetFilesForDirectory(mediaItem.FolderDirectory)
            .OrderByDescending(f => f.Type)
            .ThenBy(f => f.Title)
            .ToList();
        await bus.SendEventAsync(SocketRequests.MediaItemFileList, currentFiles);

        var historyItems = await itemHistoryService.GetItemHistoryAsync(mediaItem.Id, mediaItem.History);
        await bus.SendEventAsync(SocketRequests.MediaItemHistory, historyItems);

        mediaItem.Files = null!;
        mediaItem.History = null!;
        await bus.SendEventAsync(SocketRequests.MediaItem, mediaItem);
    }

    [WebSocketEvent(SocketRequests.MediaItemFileList)]
    public async Task SendMediaItemFileList(MediaItemFileListRequest item)
    {
        // TODO load directories
        // TODO Unused
        await mediaItemRepository.GetMediaItem(item.Id);
    }
    
    [WebSocketBinaryEvent(SocketRequests.CreateMediaItem)]
    public async Task CreateMediaItem(string[] metadata, ArraySegment<byte> fileBytes)
    {
        var newItem = new MediaItem
        {
            FolderDirectory = metadata[0],
            Title = metadata[1],
            Files = new ()
        };
            
        var mediaScanResult = await MediaLoader.ScanDirectorySingle(newItem.FolderDirectory);
        if (mediaScanResult == null) return;

        var newItemId = await mediaItemService.CreateMediaItemFromScan(newItem, mediaScanResult);
        var mediaFiles = await mediaFileRepository.GetFilesForItemId(newItemId);
            
        // This is wonky way to process tags, since library is JS only. Would need to port it to c# or use somthing else
        var unprocessedFiles = mediaFiles
            .Where(f => f is { Type: FileType.File, TagsProcessed: false })
            .ToDictionary(f => f.Id, f => f.Title);
        if (unprocessedFiles.Count > 0)
        {
            await bus.SendEventAsync(SocketRequests.ParseTags, unprocessedFiles);
        }
    }

    [WebSocketEvent(SocketRequests.RemoveMediaItem)]
    public async Task DeleteMediaItem(Guid mediaItemId)
    {
        await mediaItemRepository.DeleteMediaItem(mediaItemId);
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