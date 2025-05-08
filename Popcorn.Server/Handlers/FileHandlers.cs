using System.Diagnostics;
using Popcorn.Application.Exceptions;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Persistence.Repositories;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;
using Serilog;

namespace Popcorn.Server.Handlers;

public static class FileHandlers
{
    public static void Register(WebSocketBus bus, IServiceScopeFactory scopeFactory)
    {

        bus.RegisterHandler<Guid>(SocketRequests.FileOpen, async fileId =>
        {
            using var scope = scopeFactory.CreateScope();
            var mediaItemRepository = scope.ServiceProvider.GetRequiredService<IMediaItemRepository>();
            var mediaFileRepository = scope.ServiceProvider.GetRequiredService<IMediaFileRepository>();
            var playerService = scope.ServiceProvider.GetRequiredService<IPlayerService>();
            var fileService = scope.ServiceProvider.GetRequiredService<IFileService>();

            var mediaFile = await mediaFileRepository.GetFileById(fileId.ToString());
            if (mediaFile == null) return;
            
            var useInternalPlayer = true;
            if (!useInternalPlayer)
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
                Log.Warning($"File \"{Path.GetFileName(mediaFile.Path)}\" not found");
                //Snackbar.Add($"File \"{Path.GetFileName(file.Path)}\" not found", Severity.Error);
                return;
            }

            if(useInternalPlayer)
            {
                await playerService.OpenFile(mediaFile.Id!.Value);
            } else
            {
                try
                {
                    fileService.LaunchMediaFileDefault(mediaFile.Path);
                }
                catch (UnknownAssociatedProgramException e)
                {
                    Log.Warning($"Program not found for extension: {e.Message}");
                    //Snackbar.Add($"Program not found for extension: {e.Message}", Severity.Error);
                }
                catch (Exception e)
                {
                    Log.Warning($"Unable to open file: {e.Message}");
                    //Snackbar.Add($"Unable to open file: {e.Message}", Severity.Error);
                }
            }
        });
        
        bus.RegisterHandler<FilesRequest>(SocketRequests.ImageFile, async request =>
        {
            using var scope = scopeFactory.CreateScope();
            var imageRepository = scope.ServiceProvider.GetRequiredService<ImageRepository>();

            var files = new Dictionary<string, byte[]>();
            
            foreach (var filePath in request.Files.Where(filePath => !string.IsNullOrWhiteSpace(filePath)))
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

            await bus.SendEventAsync(SocketRequests.ImageFilesBatchSent, "");
        });
        
        bus.RegisterHandler<Dictionary<string, ParsedTags>>(SocketRequests.ParseTags, async request =>
        {
            using var scope = scopeFactory.CreateScope();
            var mediaFileRepository = scope.ServiceProvider.GetRequiredService<IMediaFileRepository>();

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
        });
    }
}