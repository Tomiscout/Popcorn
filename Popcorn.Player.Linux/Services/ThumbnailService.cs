using FFMpegCore;
using Popcorn.Player.Linux.Core;
using Popcorn.Player.Services;
using Popcorn.Shared.Utils;
using PopcornPlayer.Shared;
using Serilog;

namespace Popcorn.Player.Linux.Services;
public class ThumbnailService
{

    private static readonly string OutputFolder = Path.Combine(DirUtils.DataFolder, "thumb");

    public static void Initialize() {
        if(!Directory.Exists(OutputFolder))
        {
            Directory.CreateDirectory(OutputFolder); 
        }
    }

    public static CancellationTokenSource FfmpegCts;
    public static bool OnThumbnailRequest(long time)
    {
        FfmpegCts?.Cancel();
        FfmpegCts = new CancellationTokenSource();

        Task.Run(() =>
        {
            try
            {
                if(FfmpegCts is null)
                {
                    FfmpegCts = new CancellationTokenSource(); //Race condition dirty fix
                }
                var bytes = GetThumbnailBytes(PlayerCore.CurrentMediaFile.Path, PlayerCore.CurrentMediaProbe, PlayerCore.CurrentMediaFile.Id.ToString(), time, FfmpegCts.Token);
                if (bytes is not null)
                {
                    PlayerCore.PostStreamMessage($"{{\"name\":\"thumb\",\"time\":{time}}}", bytes);
                }
            }
            catch (OperationCanceledException)
            {
                // Nothing
            }
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
        });
        return true;
    }

    // TODO argument caching, since for file it would be reused, except of output file path and timestamp
    // But need to benchmark if it's necessary at all, since caching may be too complex.
    // Although it would be beneficial, because if some method fails to generate, fallback would be faster and multiple ffmpeg instances / file checks wouldnt be called
    public static byte[] GetThumbnailBytes(string inputFilePath, IMediaAnalysis metadata, string id, long timeInSeconds, CancellationToken ct)
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
