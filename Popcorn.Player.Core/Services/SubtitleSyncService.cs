using System.Collections.Concurrent;
using Popcorn.Player.Core.Contracts;
using Popcorn.Player.Core.Player;
using Popcorn.Player.Core.WebSocket;
using Popcorn.Shared.Ffmpeg;
using Serilog;

namespace Popcorn.Player.Core.Services;

/// <summary>
/// Data for the overlay's subtitle syncer: the text of a subtitle track (every line with its times, parsed by the
/// overlay) and waveform windows of the audio track around the playback position. Answers are events
/// (<c>subtitle_text</c>, <c>waveform</c>) and dropped when another file was loaded meanwhile.
/// </summary>
public class SubtitleSyncService(IPlayer player, Lazy<WebEventHandler> webSocket)
{
    /// <summary>Extracted subtitle text per file + track (extraction reads the whole file).</summary>
    private readonly ConcurrentDictionary<(string Path, int Track), Task<string>> _texts = new();

    /// <summary>The syncer asks for a few windows at once while scrolling; ffmpeg runs for at most this many.</summary>
    private readonly SemaphoreSlim _waveformSlots = new(2);

    public void OnSubtitleTextRequest(int trackId) => _ = Task.Run(async () =>
    {
        var path = player.CurrentFilePath;
        try
        {
            var track = player.GetTrack(MediaTrackType.Subtitle, trackId);
            if (path is null || track is null) throw new InvalidOperationException("Subtitle track not found.");
            if (SubtitleText.ImageCodecs.Contains(track.Codec))
                throw new InvalidOperationException("Image based subtitles can't be synced here.");

            var text = await _texts.GetOrAdd((path, trackId), _ => ReadTextAsync(path, track));
            if (player.CurrentFilePath == path) webSocket.Value.PostEvent("subtitle_text", new { track = trackId, text });
        }
        catch (Exception e)
        {
            _texts.TryRemove((path ?? string.Empty, trackId), out _);
            Log.Warning(e, "Subtitle text of track {Track} failed", trackId);
            if (player.CurrentFilePath == path) webSocket.Value.PostEvent("subtitle_text", new { track = trackId, error = e.Message });
        }
    });

    private static Task<string> ReadTextAsync(string path, MediaTrack track)
    {
        if (track.ExternalFilename is { } external) return SubtitleText.ReadFileAsync(external);
        if (IsUrl(path))
            throw new InvalidOperationException("The file is still downloading; its own subtitles can be read once it is complete.");
        if (track.StreamIndex is not { } index) throw new InvalidOperationException("Subtitle stream not found.");
        return SubtitleText.ExtractAsSrtAsync(path, index);
    }

    /// <param name="trackId">mpv id of the audio track, negative = the file's first one.</param>
    /// <param name="key">Echoed back with the peaks.</param>
    public void OnWaveformRequest(int trackId, double start, double duration, string key) => _ = Task.Run(async () =>
    {
        var path = player.CurrentFilePath;
        if (path is null) return;
        await _waveformSlots.WaitAsync();
        try
        {
            if (player.CurrentFilePath != path) return;
            var track = trackId < 0 ? null : player.GetTrack(MediaTrackType.Audio, trackId);
            var (input, map) = track switch
            {
                { ExternalFilename: { } external } => (external, "a:0"),
                { StreamIndex: { } index } => (path, index.ToString()),
                _ => (path, "a:0"),
            };
            var peaks = await AudioWaveform.ReadPeaksAsync(input, map, start, duration);
            if (player.CurrentFilePath == path) webSocket.Value.PostEvent("waveform", new { key, peaks });
        }
        catch (Exception e)
        {
            Log.Warning(e, "Waveform at {Start} s failed", start);
            if (player.CurrentFilePath == path) webSocket.Value.PostEvent("waveform", new { key, error = e.Message });
        }
        finally
        {
            _waveformSlots.Release();
        }
    });

    private static bool IsUrl(string path) =>
        Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
