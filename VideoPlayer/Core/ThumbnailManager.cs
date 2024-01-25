using LibVLCSharp.Shared;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VideoPlayer.Services;

namespace VideoPlayer.Core;
public static class ThumbnailManager
{
    private static CancellationTokenSource _ffmpegCts;
    public static bool OnThumbnailRequest(long time)
    {
        _ffmpegCts?.Cancel();
        _ffmpegCts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            try
            {
                var bytes = await GenerateThumbnailAsync(time, _ffmpegCts.Token);
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
                _ffmpegCts?.Dispose();
                _ffmpegCts = null;
            }
        });
        return true;
    }

    public static async Task<byte[]> GenerateThumbnailAsync(long time, CancellationToken ct)
    {
        var codec = PlayerCore.MediaPlayer.Media.Tracks.FirstOrDefault(t => t.TrackType == TrackType.Video).Codec;
        return await ThumbnailService.GetThumbnailBytes(time, codec, PlayerCore.CurrentMediaFile.Path, PlayerCore.CurrentMediaFile.Id.ToString(), ct);
    }
}
