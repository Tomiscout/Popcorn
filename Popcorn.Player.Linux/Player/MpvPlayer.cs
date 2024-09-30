using Nickvision.MPVSharp;
using Nickvision.MPVSharp.Internal;
using Popcorn.Player.Linux.Contracts;
using Popcorn.Player.Linux.WebSocket;

namespace Popcorn.Player.Linux.Player;

public class MpvPlayer : IPlayer
{
    public Client _player { get; }

    public MpvPlayer()
    {
        _player = new Client();
        _player.SetProperty("vo", "libmpv");
        _player.SetProperty("hwdec", "auto");
        _player.SetProperty("osd-level", "0");
        _player.SetOption("sub-border-size", "1");
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
        _player.ObserveProperty("duration");
        
        _player.PropertyChanged += (sender, e) =>
        {
            if(e.Node == null) return;
            
            switch (e.Name)
            {
                case "time-pos":
                    PlayerWebSocket.PostEvent("current_time", (double)e.Node.Value!);
                    return;
                case "duration":
                    PlayerWebSocket.PostEvent("duration_update", (double)e.Node.Value!);
                    return;
            }
            
            Console.WriteLine($"{e.Name}: {e.Node.ToString()}");
        };
    }
    
    public Client Client => _player;

    public void Initialize()
    {
        _player.Initialize();
    }

    public void LoadMedia(string path)
    {
        _player.Command($"loadfile '{path}' append-play");
    }

    public void Play(bool play = true)
    {
        _player.SetProperty("pause", YesNo(!play));
    }

    public void Stop()
    {
        _player.Command("stop");
    }

    public void SetPlaybackTime(long time)
    {
        _player.Command($"seek {time / 1000} absolute");
    }

    public void SkipTime(long time)
    {
        _player.Command($"seek {time}");
    }

    public void SetVolume(double volume)
    {
        _player.SetProperty("ao-volume", (int)Math.Floor(volume * 100));
    }

    public event EventHandler? MediaEnded;
    public event EventHandler<long>? TimeChanged;
    public event EventHandler<long>? LengthChanged;
    public event EventHandler? MediaPlaying;
    public MediaTrack[] GetMediaTracks()
    {
        _player.GetProperty("track-list", out MPVNode trackListNode);

        if (trackListNode.Format != MPVFormat.NodeArray) throw new Exception("track-list is not an array.");
        
        var trackNodes = (MPVNode[])trackListNode;
        var mediaTracks = new List<MediaTrack>();

        foreach (var trackNode in trackNodes)
        {
            if (trackNode.Format != MPVFormat.NodeMap) continue;
            
            var trackNodeList = (MPVNodeList)trackNode;

            var trackDict = MPVNodeList.ToDictionary(trackNodeList);
            if (trackDict == null) continue;
                
            var type = trackDict.ContainsKey("type") ? (string)trackDict["type"] : "";
            if (type != "audio" && type != "video" && type != "sub") continue;
            
            var mediaTrack = new MediaTrack
            {
                Id = trackDict.ContainsKey("id") ? (int)trackDict["id"] : 0,
                Type = trackDict.ContainsKey("type") ? (string?)trackDict["type"] : string.Empty,
                Language = trackDict.ContainsKey("lang") ? (string)trackDict["lang"] : string.Empty,
                Description = trackDict.ContainsKey("title") ? (string)trackDict["title"] : string.Empty,
                Selected = trackDict.ContainsKey("selected") && (bool)trackDict["selected"]
            };

            mediaTracks.Add(mediaTrack);
        }

        return mediaTracks.ToArray();
    }

    public void AudioTrackSelected(int track)
    {
        _player.SetProperty("aid", track);
    }

    public void SubtitleTrackSelected(int track)
    {
        _player.SetProperty("sid", track);
    }

    public void SubtitleSetDelay(double delay)
    {
        _player.SetProperty("sub-delay", delay);
    }

    public MediaTrack[] GetSubtitleTracks()
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        WebSocketContainer.Dispose();
        _player.Dispose();
    }

    private static string YesNo(bool val) => val ? "yes" : "no";
}