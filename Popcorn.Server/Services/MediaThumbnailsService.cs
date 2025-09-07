using FFMpegCore;
using Microsoft.Extensions.Caching.Memory;
using Popcorn.Domain.Interfaces;
using Popcorn.Server.Attributes;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;
using Popcorn.Shared.Ffmpeg;
using Popcorn.Shared.Utils;
using PopcornPlayer.Shared;
using Serilog;

namespace Popcorn.Server.Services;

[WebSocketService]
public class MediaThumbnailsService(WebSocketBus bus,
    IMediaFileRepository mediaFileRepository, IMemoryCache memoryCache, ILogger<MediaThumbnailsService> logger)
{
    public CancellationTokenSource FfmpegCts;
    
    [WebSocketEvent(SocketRequests.ThumbnailFile)]
    public async Task OnThumbnailRequest(ThumbnailRequest request)
    {
        if(request.TimeSeconds < 0 || request.FileId == Guid.Empty) return;
        var mediaFile = await mediaFileRepository.GetFileById(request.FileId);
        if(mediaFile == null)
        {
            logger.LogError("File {fileId} not found when requesting thumbnail {time}", request.FileId, request.TimeSeconds);
            return;
        }
        memoryCache.TryGetValue<IMediaAnalysis>($"{mediaFile?.Id}-metadata-probe", out var probe);
        if(probe == null)
        {
            logger.LogError("Metadata probe not found not found when requesting thumbnail {fileId}, {time}", request.FileId, request.TimeSeconds);
            return;
        }

        FfmpegCts?.Cancel();
        FfmpegCts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            try
            {
                var bytes = GetThumbnailBytes(mediaFile.Path, probe, mediaFile.Id.ToString(), request.TimeSeconds, FfmpegCts.Token);
                if (!bytes.Any()) return Task.CompletedTask;

                await bus.SendBinaryAsync(SocketRequests.ThumbnailFile, bytes, $"thumbnail_{request.FileId}_{request.TimeSeconds}");
            }
            catch (OperationCanceledException) { } // It was canceled by another thumb request
            catch (Exception ex)
            {
                // Log exception if needed
                Log.Error(ex, "Failed to generate thumbnail {time}", request.TimeSeconds);
            }
            finally
            {
                FfmpegCts?.Dispose();
                FfmpegCts = null;
            }

            return Task.CompletedTask;
        });
    }

    // TODO argument caching, since for file it would be reused, except of output file path and timestamp
    // But need to benchmark if it's necessary at all, since caching may be too complex.
    // Although it would be beneficial, because if some method fails to generate, fallback would be faster and multiple ffmpeg instances / file checks wouldnt be called
    private byte[] GetThumbnailBytes(string inputFilePath, IMediaAnalysis metadata, string id, long timeInSeconds, CancellationToken ct)
    {
        var outputFilePath = $"{DirectoryUtils.ThumbnailsFolder + Path.DirectorySeparatorChar + id}-{timeInSeconds}.jpg"; // HDR generates png output, but jpg is kept for now
        var thumbFile = new FileInfo(outputFilePath);
        if (thumbFile.Exists && thumbFile.Length > 0)
        {
            return File.ReadAllBytes(outputFilePath);
        }

        string command = ThumbnailUtils.BuildDecoderCommand(timeInSeconds, metadata, inputFilePath, outputFilePath);

        FFmpegWrapper.RunCommand(command, ct);
        thumbFile.Refresh();
        if (thumbFile.Exists && thumbFile.Length > 0)
        {
            return File.ReadAllBytes(outputFilePath);
        }

        if (ct.IsCancellationRequested) return null; // If ct canceled - dont attempt cpu command

        FFmpegWrapper.RunCommand(ThumbnailUtils.GetCpuCommand(inputFilePath, outputFilePath, metadata, timeInSeconds), ct);
        thumbFile.Refresh();

        return thumbFile.Exists && thumbFile.Length > 0 ? File.ReadAllBytes(thumbFile.FullName) : null;
    }
}