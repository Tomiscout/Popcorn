namespace Popcorn.Player.Linux.Player;

public interface IPlayer : IDisposable
{
    void Initialize();
    void LoadMedia(string path);
    void Play();
    void Pause();
    void Stop();
    void SetPlaybackTime(long time);
    long GetPlaybackTime();
    void SetVolume(double volume);
    event EventHandler MediaEnded;
    event EventHandler<long> TimeChanged;
    event EventHandler<long> LengthChanged;
    event EventHandler MediaPlaying;
}
