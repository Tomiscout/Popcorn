using FFMpegCore;
using Microsoft.Extensions.DependencyInjection;
using Popcorn.Player.Core.Core;
using Popcorn.Player.Core.WebSocket;
using Popcorn.Shared.Utils;
using PopcornPlayer.Shared;
using Serilog;

namespace Popcorn.Player.Core.Services;
public class ThumbnailService
{
    private readonly Lazy<PlayerCore> _playerCore;
    private readonly Lazy<WebEventHandler> _webSocket;

    private readonly string OutputFolder = Path.Combine(DirUtils.DataFolder, "thumb");

    public ThumbnailService(Lazy<PlayerCore> playerCore, Lazy<WebEventHandler> webSocket)
    {
        _playerCore = playerCore;
        _webSocket = webSocket;
    }

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
        FfmpegCts?.Cancel();
        FfmpegCts = new CancellationTokenSource();

        Task.Run(() =>
        {
            try
            {
                FfmpegCts ??= new CancellationTokenSource();

                var currentFile = _playerCore.Value.CurrentMediaFile;
                if (currentFile == null) return Task.CompletedTask;

                var bytes = GetThumbnailBytes(currentFile.Path, _playerCore.Value.CurrentMediaProbe, currentFile.Id.ToString(), time, FfmpegCts.Token);
                if (!bytes.Any()) return Task.CompletedTask;

                _webSocket.Value.PostBinaryEvent(bytes);
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

    // TODO argument caching, since for file it would be reused, except of output file path and timestamp
    // But need to benchmark if it's necessary at all, since caching may be too complex.
    // Although it would be beneficial, because if some method fails to generate, fallback would be faster and multiple ffmpeg instances / file checks wouldnt be called
    public byte[] GetThumbnailBytes(string inputFilePath, IMediaAnalysis metadata, string id, long timeInSeconds, CancellationToken ct)
    {
        var outputFilePath = $"{OutputFolder + Path.DirectorySeparatorChar + id}-{timeInSeconds}.jpg"; // HDR generates png output, but jpg is kept for now
        var thumbFile = new FileInfo(outputFilePath);
        if (thumbFile.Exists && thumbFile.Length > 0)
        {
            return File.ReadAllBytes(outputFilePath);
        }

        string command = VideoDecoders.BuildDecoderCommand(timeInSeconds, metadata, inputFilePath, outputFilePath);

        FFmpegWrapper.RunCommand(command, ct);
        thumbFile.Refresh();
        if (thumbFile.Exists && thumbFile.Length > 0)
        {
            return File.ReadAllBytes(outputFilePath);
        }

        if (ct.IsCancellationRequested) return null; // If ct canceled - dont attempt cpu command

        FFmpegWrapper.RunCommand(VideoDecoders.GetCpuCommand(inputFilePath, outputFilePath, metadata, timeInSeconds), ct);
        thumbFile.Refresh();

        return thumbFile.Exists && thumbFile.Length > 0 ? File.ReadAllBytes(thumbFile.FullName) : null;
    }
}
