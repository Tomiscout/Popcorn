using Popcorn.Application.Extensions;
using Popcorn.Application.Services;
using Popcorn.Domain;
using Popcorn.Domain.Enum;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Persistence;
using Popcorn.Persistence.Repositories;
using Popcorn.Server.Attributes;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;
using Popcorn.Server.Models.Responses;

namespace Popcorn.Server.Services;

[WebSocketService]
public class FileWebService(
    WebSocketBus bus,
    IMediaFileRepository mediaFileRepository,
    ImageRepository imageRepository,
    IMediaItemRepository mediaItemRepository,
    PlayerLaunchService playerLaunchService,
    MediaItemService mediaItemService,
    ItemHistoryService itemHistoryService,
    ILogger<FileWebService> logger
    )
{

    [WebSocketEvent(SocketRequests.FileOpen)]
    public Task OpenFile(Guid fileId) => OpenFileWithPlayer(fileId, null);

    [WebSocketEvent(SocketRequests.FileOpenWith)]
    public async Task OpenFileWith(FileOpenWithRequest request)
    {
        var fileId = request.FileId;
        if (fileId == null && request.MediaId != null)
        {
            var item = await mediaItemRepository.GetMediaItemWithFilesAndHistory(request.MediaId.Value);
            fileId = item?.GetContinueFile()?.Id;
        }
        if (fileId == null)
        {
            logger.LogError("FileOpenWith: no file to open (file {fileId}, media {mediaId})", request.FileId, request.MediaId);
            return;
        }

        await OpenFileWithPlayer(fileId.Value, request.PlayerId);
    }

    private async Task OpenFileWithPlayer(Guid fileId, string? playerId)
    {
        var mediaFile = await mediaFileRepository.GetFileById(fileId);
        if (mediaFile == null)
        {
            logger.LogError($"File with id: {fileId} could not be found");
            return;
        }

        if(!File.Exists(mediaFile.Path))
        {
            //TODO notificaiton
            logger.LogError($"File \"{Path.GetFileName(mediaFile.Path)}\" not found");
            return;
        }

        var player = await playerLaunchService.OpenAsync(mediaFile, playerId);
        if (player == null) return; //TODO notification

        // The Popcorn player reports its own history (with playback position); external players can't.
        if (player.Type != PlayerOptionType.Popcorn)
        {
            await mediaItemRepository.AddHistoryItem(mediaFile.ItemId, new ItemHistory
            {
                FileId = fileId,
                PlaybackTime = DateTime.Now
            });
        }

        await mediaFileRepository.SetFileAsPlayed(mediaFile.Id);
        var historyItems = await itemHistoryService.GetItemHistoryAsync(mediaFile.ItemId);
        await bus.SendEventAsync(SocketRequests.MediaItemHistory, historyItems);
    }

    [WebSocketEvent(SocketRequests.FolderOpen)]
    public async Task OpenFolder(Guid fileOrMediaId)
    {
        var response = await GetPlayerPlaylistResponseForDirectory(fileOrMediaId);
        if (response == null) return;
            
        await bus.SendEventAsync(SocketRequests.MediaItemFileList, response.Files);
    }
    
    [WebSocketEvent(SocketRequests.FolderOpenPlayer)]
    public async Task OpenFolderPlayer(Guid fileOrMediaId)
    {
        var response = await GetPlayerPlaylistResponseForDirectory(fileOrMediaId);
        if (response == null) return;
        
        await bus.SendEventAsync(SocketRequests.FolderOpenPlayer, response);
    }
    
    [WebSocketEvent(SocketRequests.NextFile)]
    public async Task PlayNextFile(Guid mediaId)
    {
        var item = await mediaItemRepository.GetMediaItemWithFilesAndHistory(mediaId);
        if (item == null) return;
        
        var nextFile = item.GetContinueFile();
        if (nextFile == null) return;

        await bus.SendEventAsync(SocketRequests.NextFile, nextFile.Id);
    }

    /// <summary>Web player: where it is in the file, every few seconds and when it stops (home page "continue watching").</summary>
    [WebSocketEvent(SocketRequests.PlaybackProgress)]
    public async Task PlaybackProgress(PlaybackProgressRequest request)
    {
        if (!double.IsFinite(request.Position) || !double.IsFinite(request.Duration) ||
            request.Position < 0 || request.Duration <= 0) return;

        await mediaFileRepository.SetPlaybackProgress(request.FileId, Math.Min(request.Position, request.Duration), request.Duration);
    }

    /// <summary>Web player, at the end of a file or on its next button: what to play, or which episode is missing.</summary>
    [WebSocketEvent(SocketRequests.PlayerNextFile)]
    public async Task PlayerNextFile(Guid fileId) => await SendNextFilePlan(fileId);

    /// <summary>"Refresh files" of the missing episode warning: rescan the item, then answer as <see cref="PlayerNextFile"/>.</summary>
    [WebSocketEvent(SocketRequests.PlayerRefreshFiles)]
    public async Task PlayerRefreshFiles(Guid fileId)
    {
        var file = await mediaFileRepository.GetFileById(fileId);
        if (file is not null && file.ItemId != Guid.Empty) await mediaItemService.RescanItem(file.ItemId);
        await SendNextFilePlan(fileId);
    }

    private async Task SendNextFilePlan(Guid fileId)
    {
        var file = await mediaFileRepository.GetFileById(fileId);
        var item = file is null ? null : await mediaItemRepository.GetMediaItemWithFilesAndHistory(file.ItemId);
        var plan = item?.GetNextFilePlan(fileId) ?? new NextFilePlan { CurrentFileId = fileId };
        await bus.SendEventAsync(SocketRequests.PlayerNextFile, plan);
    }

    private async Task<PlayerPlaylistResponse?> GetPlayerPlaylistResponseForDirectory(Guid fileOrMediaId)
    {
        Guid mediaItemId;
        string rootPath;
        MediaItem? mediaItem = null;
        
        var mediaFile = await mediaFileRepository.GetFileById(fileOrMediaId);
        if (mediaFile == null)
        {
            mediaItemId = fileOrMediaId; //Media item id comes when clicking on root. I'm lazy
            mediaItem = await mediaItemRepository.GetMediaItem(mediaItemId);
            if (mediaItem == null) return null;
                
            rootPath = mediaItem.FolderDirectory;
        }
        else
        {
            mediaItemId = mediaFile.ItemId;
            rootPath = mediaFile.Type == FileType.File ? Path.GetDirectoryName(mediaFile.Path) ?? mediaFile.Path : mediaFile.Path;
        }
            
        var allFiles = await mediaFileRepository.GetFilesForItemId(mediaItemId);
        mediaItem ??= await mediaItemRepository.GetMediaItem(mediaItemId);
        if (mediaItem == null) return null;

        return new PlayerPlaylistResponse
        {
            Navigation = mediaItem.BuildNavigationPath(rootPath),
            Files = allFiles.GetFilesForDirectory(rootPath)
        };
    }

    [WebSocketEvent(SocketRequests.ImageFile)]
    public async Task SendImageFiles(List<string> filesList)
    {
        var files = new Dictionary<string, byte[]>();
            
        //TODO remove duplciates from list and send items immediately
        foreach (var filePath in filesList.Where(filePath => !string.IsNullOrWhiteSpace(filePath)))
        {
            var imageData = await imageRepository.LoadImageBlob(filePath);
            if (imageData != null)
            {
                files.Add(filePath, imageData);
            }
        }
            
        foreach (var (path, data) in files)
        {
            await bus.SendBinaryAsync(SocketRequests.ImageFile, data, path);
        }

        await bus.SendEventAsync(SocketRequests.ImageFilesBatchSent, string.Empty);
    }

}