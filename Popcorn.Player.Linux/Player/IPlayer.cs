using Popcorn.Player.Linux.Contracts;

namespace Popcorn.Player.Linux.Player;

public interface IPlayer : IDisposable
{
    void LoadMedia(string path);
    void Play(bool play = true);
    void Stop();
    void SetPlaybackTime(long time);
    void SkipTime(long time);
    void SetVolume(double volume);
    event EventHandler MediaEnded;
    event EventHandler<long> TimeChanged;
    event EventHandler<long> LengthChanged;
    event EventHandler MediaPlaying;
    MediaTrack[] GetMediaTracks();
    void AudioTrackSelected(int track);
    void SubtitleTrackSelected(int track);
    void SubtitleSetDelay(double delay);
}
