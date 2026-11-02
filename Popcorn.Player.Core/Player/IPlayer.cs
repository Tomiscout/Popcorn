using FFMpegCore;
using Popcorn.Player.Core.Contracts;

namespace Popcorn.Player.Core.Player;

public interface IPlayer : IDisposable
{
    Task<bool> LoadMedia(string path, IMediaAnalysis mediaAnalysis);
    void Play(bool play = true);
    void Stop();
    void SetPlaybackTime(long time);
    void SkipTime(long time);
    void Initialize();
    void InitializeWindows(string wid);
    void SetVolume(double volume);
    event EventHandler MediaEnded;
    event EventHandler<long> TimeChanged;
    event EventHandler<long> LengthChanged;
    event EventHandler MediaPlaying;
    List<MediaTrack> GetAudioTracks();
    List<MediaTrack> GetSubtitleTracks();
    void AudioTrackSelected(int track);
    void SubtitleTrackSelected(int track);
    void SubtitleSetDelay(double delay);
    void AddSubtitleTrack(string path);

    /// <summary>Duration in seconds as last reported to the overlay, -1 if unknown.</summary>
    double Duration { get; }
    /// <summary>Playback position in seconds, null without a file.</summary>
    double? Position { get; }
    bool IsPaused { get; }
}
