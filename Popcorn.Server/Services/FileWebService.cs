using Popcorn.Application.Exceptions;
using Popcorn.Application.Services;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Persistence;
using Popcorn.Persistence.Repositories;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;
using PopcornPlayer.Domain.Extensions;

namespace Popcorn.Server.Services;

public class FileWebService(
    WebSocketBus bus,
    IMediaFileRepository mediaFileRepository,
    ImageRepository imageRepository,
    IMediaItemRepository mediaItemRepository,
    IPlayerService playerService,
    IFileService fileService,
    SettingsRepository settingsRepository,
    ItemHistoryService itemHistoryService,
    ILogger<FileWebService> logger
    )
{

    public async Task OpenFile(Guid fileId)
    {
        var mediaFile = await mediaFileRepository.GetFileById(fileId);
        if (mediaFile == null)
        {
            logger.LogError($"File with id: {fileId} could not be found");
            return;
        }

        var settings = await settingsRepository.GetSettings();
        if (!settings.UseInternalPlayer)
        {
            await mediaItemRepository.AddHistoryItem(mediaFile.ItemId, new ItemHistory
            {
                FileId = fileId,
                PlaybackTime = DateTime.Now
            });
        }
        
        if(!File.Exists(mediaFile.Path))
        {
            //TODO notificaiton
            logger.LogError($"File \"{Path.GetFileName(mediaFile.Path)}\" not found");
            //Snackbar.Add($"File \"{Path.GetFileName(file.Path)}\" not found", Severity.Error);
            return;
        }

        if(settings.UseInternalPlayer)
        {
            await playerService.OpenFile(mediaFile.Id);
        } else
        {
            try
            {
                fileService.LaunchMediaFileDefault(mediaFile.Path);
            }
            catch (UnknownAssociatedProgramException e)
            {
                logger.LogWarning($"Program not found for extension: {e.Message}");
                //Snackbar.Add($"Program not found for extension: {e.Message}", Severity.Error);
            }
            catch (Exception e)
            {
                logger.LogWarning($"Unable to open file: {e.Message}");
                //Snackbar.Add($"Unable to open file: {e.Message}", Severity.Error);
            }
        }
        
        await mediaFileRepository.SetFileAsPlayed(mediaFile.Id);
        var historyItems = await itemHistoryService.GetItemHistoryAsync(mediaFile.ItemId);
        await bus.SendEventAsync(SocketRequests.MediaItemHistory, historyItems);
    }

    public async Task OpenFolder(Guid fileId)
    {
        Guid mediaItemId;
        string? rootPath = null;
            
        var mediaFile = await mediaFileRepository.GetFileById(fileId);
        if (mediaFile == null)
        {
            mediaItemId = fileId; //Media item id comes when clicking on root. I'm lazy
            var mediaItem = await mediaItemRepository.GetMediaItem(mediaItemId);
            if (mediaItem == null) return;
                
            rootPath = mediaItem.FolderDirectory;
        }
        else
        {
            mediaItemId = mediaFile.ItemId;
            rootPath = mediaFile.Path;
        }
            
        var allFiles = await mediaFileRepository.GetFilesForItemId(mediaItemId);
        var directoryFiles = allFiles.GetFilesForDirectory(rootPath);
            
        await bus.SendEventAsync(SocketRequests.MediaItemFileList, directoryFiles);
    }

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

    public async Task HandleParsedTags(Dictionary<string, ParsedTags> request)
    {
        var mediaFiles = await mediaFileRepository.GetFilesByIds(request.Keys.ToArray());
            
        foreach (var (fileId, tags) in request)
        {
            var mediaFile = mediaFiles.FirstOrDefault(m => m.Id.ToString() == fileId);
            if(mediaFile is null) continue;
                
            mediaFile.Seasons = tags.Seasons?.ToList();
            mediaFile.Episodes = tags.Episodes?.ToList();
                
            mediaFile.FormatTags = new List<string>();
            if (!string.IsNullOrWhiteSpace(tags.Resolution))
            {
                mediaFile.FormatTags.Add(tags.Resolution);
            }
            if(!string.IsNullOrWhiteSpace(tags.VideoCodec))
            {
                mediaFile.FormatTags.Add(tags.VideoCodec);
            }
            mediaFile.FormatTags.Add(Path.GetExtension(mediaFile.Path));

            mediaFile.OtherTags = new List<string>();
            if (!string.IsNullOrWhiteSpace(tags.ReleaseGroup))
            {
                mediaFile.OtherTags.Add(tags.ReleaseGroup);
            }

            mediaFile.ParseTitle();
            mediaFile.TagsProcessed = true;
            await mediaFileRepository.UpdateMediaFile(mediaFile);
        }
    }
}