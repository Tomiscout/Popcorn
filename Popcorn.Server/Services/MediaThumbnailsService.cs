using FFMpegCore;
using Microsoft.Extensions.Caching.Memory;
using Popcorn.Domain.Interfaces;
using Popcorn.Server.Attributes;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;
using Popcorn.Shared.Ffmpeg;
using Popcorn.Shared.Utils;
using Popcorn.Application.Utils;
using Serilog;

namespace Popcorn.Server.Services;

[WebSocketService]
public class MediaThumbnailsService(WebSocketBus bus,
    IMediaFileRepository mediaFileRepository, IMemoryCache memoryCache, ILogger<MediaThumbnailsService> logger)
{
    public CancellationTokenSource? FfmpegCts;
    
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
        memoryCache.TryGetValue<IMediaAnalysis>($"{mediaFile.Id}-metadata-probe", out var probe);
        if(probe == null)
        {
            logger.LogError("Metadata probe not found not found when requesting thumbnail {fileId}, {time}", request.FileId, request.TimeSeconds);
            return;
        }

        FfmpegCts?.Cancel();
        var cts = new CancellationTokenSource();
        FfmpegCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                var bytes = GetThumbnailBytes(mediaFile.Path, probe, mediaFile.Id.ToString(), request.TimeSeconds, cts.Token);
                if (bytes == null || bytes.Length == 0) return;

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
                // Only clear our own source; a newer request may have replaced it already
                Interlocked.CompareExchange(ref FfmpegCts, null, cts);
                cts.Dispose();
            }
        });
    }

    private byte[]? GetThumbnailBytes(string inputFilePath, IMediaAnalysis metadata, string id, long timeInSeconds, CancellationToken ct)
    {
        var outputFilePath = $"{DirectoryUtils.ThumbnailsFolder + Path.DirectorySeparatorChar + id}-{timeInSeconds}.jpg"; // HDR generates png output, but jpg is kept for now
        var thumbFile = new FileInfo(outputFilePath);
        if (thumbFile.Exists && thumbFile.Length > 0)
        {
            return File.ReadAllBytes(outputFilePath);
        }

        // Hardware decoding where it is faster, CPU otherwise and when it fails
        return ThumbnailRenderer.Render(inputFilePath, metadata, outputFilePath, timeInSeconds, ct)
            ? File.ReadAllBytes(outputFilePath)
            : null;
    }
}