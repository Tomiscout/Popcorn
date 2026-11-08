using FFMpegCore;
using Popcorn.Application.Utils;
using Popcorn.Player.Core.Contracts;

namespace Popcorn.Player.Core.Player;

public interface IPlayer : IDisposable
{
    /// <param name="startSeconds">Where playback starts, null = the beginning.</param>
    Task<bool> LoadMedia(string path, IMediaAnalysis mediaAnalysis, double? startSeconds = null);
    void Play(bool play = true);
    void Stop();
    void SetPlaybackTime(long time);
    void SkipTime(long time);
    void Initialize();
    void SetVolume(double volume);
    /// <summary>Playback reached the end of the file; it stays loaded on its last frame (paused).</summary>
    event EventHandler MediaEnded;
    /// <summary>Playback left the end again after <see cref="MediaEnded"/> (seek back, replay).</summary>
    event EventHandler MediaResumed;
    event EventHandler<long> TimeChanged;
    event EventHandler<long> LengthChanged;
    event EventHandler MediaPlaying;
    List<MediaTrack> GetAudioTracks();
    List<MediaTrack> GetSubtitleTracks();
    void AudioTrackSelected(int track);
    void SubtitleTrackSelected(int track);
    /// <summary>Seconds the subtitles are shown later (negative = earlier). Reset to 0 for every file.</summary>
    void SubtitleSetDelay(double delay);
    /// <summary>Path or URL (download stream) of the loaded file.</summary>
    string? CurrentFilePath { get; }
    /// <summary>A track of the loaded file by mpv id.</summary>
    MediaTrack? GetTrack(MediaTrackType type, int id);
    void AddSubtitleTrack(string path);
    /// <summary>Adds subtitle files found for the loaded file as tracks (not selected) and rereads the track list.</summary>
    void AddExternalSubtitles(IReadOnlyList<ExternalSubtitle> subtitles);

    /// <summary>Duration in seconds as last reported to the overlay, -1 if unknown.</summary>
    double Duration { get; }
    /// <summary>Playback position in seconds, null without a file.</summary>
    double? Position { get; }
    bool IsPaused { get; }
}
