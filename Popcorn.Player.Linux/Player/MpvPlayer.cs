using Nickvision.MPVSharp;
using Popcorn.Player.Linux.WebSocket;

namespace Popcorn.Player.Linux.Player;

public class MpvPlayer : IPlayer
{
    public Client _player { get; }
    private string _currentTimePos = string.Empty;

    public MpvPlayer()
    {
        _player = new Client();
        _player.SetProperty("vo", "libmpv");
        _player.SetProperty("hwdec", "auto");
        //_player.SetProperty("log-file", "mpv_log.log");

        //_player.RequestLogMessages("debug");
        _player.LogMessageReceived += (sender, e) =>
        {
            Console.Write($"[{e.Prefix}] {e.Text}"); // Log messages (e.Text) end with newline char
        };
        _player.PropertyChanged += (sender, e) =>
        {
            switch (e.Name)
            {
                //case "time-pos": _currentTimePos = e.Node.Value.ToString(); return;
                case "media-title": Program.SetTitle((string)e.Node!); return;
            }
        };
        _player.ObserveProperty("time-pos");
        _player.ObserveProperty("media-title");
    }
    
    public Client Client => _player;

    public void Initialize()
    {
        _player.Initialize();
    }

    public void LoadMedia(string path)
    {
        _player.Command($"loadfile {path} append-play");
    }

    public void Play()
    {
        _player.SetProperty("pause", "false");
    }

    public void Pause()
    {
        _player.SetProperty("pause", "true");
    }

    public void Stop()
    {
        _player.Command("stop");
    }

    public void SetPlaybackTime(long time)
    {
        _player.Command($"seek {time} absolute");
    }

    public long GetPlaybackTime()
    {
        return 0;
        //return _currentTimePos;
    }

    public void SetVolume(double volume)
    {
        throw new NotImplementedException();
    }

    public event EventHandler? MediaEnded;
    public event EventHandler<long>? TimeChanged;
    public event EventHandler<long>? LengthChanged;
    public event EventHandler? MediaPlaying;

    public void Dispose()
    {
        WebSocketContainer.Dispose();
        _player.Dispose();
    }
}