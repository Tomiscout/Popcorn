using FFMpegCore;
using FFMpegCore.Pipes;
using Popcorn.Domain.Models;
using Popcorn.Server.Controllers.Models;
using Popcorn.Shared.Ffmpeg;
using Popcorn.Shared.Utils;

namespace Popcorn.Server.Services;

/// <summary>
/// Audio tracks the web player can't play from the video file. A track is converted in full into the stream cache
/// (AAC in MP4: seekable, reused by later plays); until that is done the player plays a live stream from its position,
/// which only hides the start-up time.
/// </summary>
public sealed class AudioTrackService(ILogger<AudioTrackService> logger)
{
    /// <summary>A conversion nobody asked about (<see cref="Prepare"/>) for this long is cancelled: the player left.</summary>
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(20);

    private sealed class Conversion
    {
        /// <summary>Never disposed: the watchdog may still fire as the conversion ends; a CTS without a timer holds nothing.</summary>
        public readonly CancellationTokenSource Cancel = new();
        public long LastAsked = Environment.TickCount64;
    }

    /// <summary>Running conversions by cache path. Also the lock between them and their finished files.</summary>
    private readonly Dictionary<string, Conversion> conversions = new();

    /// <summary>The converted track, null while it isn't (completely) there.</summary>
    public static string? ConvertedPath(Guid fileId, int trackIndex)
    {
        var path = CachePath(fileId, trackIndex);
        return File.Exists(path) ? path : null;
    }

    private static string CachePath(Guid fileId, int trackIndex) =>
        Path.Combine(DirectoryUtils.StreamCache, $"{fileId}-{trackIndex}.m4a");

    /// <summary>
    /// Whether the converted track is there; if not, starts its conversion or keeps the running one alive. The player
    /// asks again every few seconds while it streams, so leaving it stops the conversion after <see cref="Lease"/>.
    /// </summary>
    public AudioTrackState Prepare(MediaFile mediaFile, AudioTrackDto track)
    {
        var path = CachePath(mediaFile.Id, track.Index);
        lock (conversions)
        {
            if (File.Exists(path)) return new AudioTrackState(Ready: true, Converting: false);
            if (conversions.TryGetValue(path, out var running))
                Interlocked.Exchange(ref running.LastAsked, Environment.TickCount64);
            else
            {
                var conversion = new Conversion();
                conversions[path] = conversion;
                _ = Task.Run(() => ConvertAsync(mediaFile, track, path, conversion));
            }
        }
        return new AudioTrackState(Ready: false, Converting: true);
    }

    private async Task ConvertAsync(MediaFile mediaFile, AudioTrackDto track, string path, Conversion conversion)
    {
        var part = path + ".part";
        await using var watchdog = new Timer(_ =>
        {
            if (Environment.TickCount64 - Interlocked.Read(ref conversion.LastAsked) > Lease.TotalMilliseconds)
                conversion.Cancel.Cancel();
        }, null, Lease, TimeSpan.FromSeconds(5));

        try
        {
            logger.LogInformation("Converting audio track {Track} of {Path}", track.Index, mediaFile.Path);
            await FFMpegArguments.FromFileInput(mediaFile.Path)
                .OutputToFile(part, overwrite: true, opts => Encode(opts, track)
                    .ForceFormat("mp4")
                    .WithCustomArgument("-movflags +faststart"))
                .CancellableThrough(conversion.Cancel.Token)
                .ProcessAsynchronously();
            lock (conversions) File.Move(part, path, overwrite: true);
            logger.LogInformation("Converted audio track {Track} of {Path}", track.Index, mediaFile.Path);
        }
        catch (Exception ex)
        {
            TryDelete(part);
            if (conversion.Cancel.IsCancellationRequested)
                logger.LogInformation("Audio track conversion {Path} cancelled: player left", path);
            else
                logger.LogError(ex, "Audio track conversion {Path} failed", path);
        }
        finally
        {
            lock (conversions) conversions.Remove(path);
        }
    }

    /// <summary>
    /// Streams the track from <paramref name="start"/> on as ADTS AAC while ffmpeg reads it, at the pace the client
    /// reads. Cancelling (the client went away) kills ffmpeg.
    /// </summary>
    /// <param name="inputUrl">Read this URL instead of the file (a download that is not complete yet).</param>
    public Task StreamAsync(MediaFile mediaFile, AudioTrackDto track, TimeSpan start, string? inputUrl,
        Stream output, CancellationToken cancellationToken)
    {
        void Input(FFMpegArgumentOptions opts) => opts.Seek(start);
        var input = inputUrl is null
            ? FFMpegArguments.FromFileInput(mediaFile.Path, true, Input)
            : FFMpegArguments.FromUrlInput(new Uri(inputUrl), Input);

        return input
            .OutputToPipe(new StreamPipeSink(output), opts => Encode(opts, track).ForceFormat("adts"))
            .CancellableThrough(cancellationToken)
            .ProcessAsynchronously();
    }

    /// <summary>AAC is copied, anything else transcoded: browsers play AAC everywhere.</summary>
    private static FFMpegArgumentOptions Encode(FFMpegArgumentOptions opts, AudioTrackDto track) => opts
        .WithCustomArgument($"-map 0:{track.Index} -vn -sn -dn")
        .WithCustomArgument(track.Codec == nameof(AudioCodec.AAC) ? "-c:a copy" : "-c:a aac -b:a 192k");

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete {Path}", path);
        }
    }
}

/// <param name="Ready">The converted track is there (<c>api/video/{id}/audio/{track}</c>).</param>
/// <param name="Converting">It is being converted: ask again. Neither = only the live stream (a file still downloading).</param>
public record AudioTrackState(bool Ready, bool Converting);
