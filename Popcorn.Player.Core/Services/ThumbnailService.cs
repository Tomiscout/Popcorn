using FFMpegCore;
using Microsoft.Extensions.DependencyInjection;
using Popcorn.Player.Core.Core;
using Popcorn.Player.Core.WebSocket;
using Popcorn.Shared.Ffmpeg;
using Popcorn.Shared.Utils;
using Popcorn.Application.Utils;
using Serilog;

namespace Popcorn.Player.Core.Services;
public class ThumbnailService(Lazy<PlayerCore> playerCore, Lazy<WebEventHandler> webSocket)
{
    private readonly string OutputFolder = Path.Combine(DirectoryUtils.DataFolder, "thumb");

    public void Initialize()
    {
        if (!Directory.Exists(OutputFolder))
        {
            Directory.CreateDirectory(OutputFolder);
        }
    }

    public CancellationTokenSource FfmpegCts;
    public bool OnThumbnailRequest(long time)
    {
        if(time < 0) {
            return false;
        }
        
        FfmpegCts?.Cancel();
        FfmpegCts = new CancellationTokenSource();

        Task.Run(() =>
        {
            try
            {
                FfmpegCts ??= new CancellationTokenSource();

                var currentFile = playerCore.Value.CurrentMediaFile;
                // Undownloaded parts would decode as garbage, and reading the stream would steer the download.
                if (currentFile == null || playerCore.Value.IsStreamingDownload) return Task.CompletedTask;

                var bytes = GetThumbnailBytes(currentFile.Path, playerCore.Value.CurrentMediaProbe, currentFile.Id.ToString(), time, FfmpegCts.Token);
                if (bytes is null || bytes.Length == 0) return Task.CompletedTask;

                webSocket.Value.PostBinaryEvent(bytes);
            }
            catch (OperationCanceledException) { } // It was canceled by another thumb request
            catch (Exception ex)
            {
                // Log exception if needed
                Log.Error(ex, "Failed to generate thumbnail {time}", time);
            }
            finally
            {
                FfmpegCts?.Dispose();
                FfmpegCts = null;
            }

            return Task.CompletedTask;
        });
        return true;
    }

    public byte[]? GetThumbnailBytes(string inputFilePath, IMediaAnalysis metadata, string id, long timeInSeconds, CancellationToken ct)
    {
        var outputFilePath = $"{OutputFolder + Path.DirectorySeparatorChar + id}-{timeInSeconds}.jpg"; // HDR generates png output, but jpg is kept for now
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
