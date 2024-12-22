using System.Diagnostics;
using System.IO;
using FFMpegCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nickvision.MPVSharp;
using Nickvision.MPVSharp.Internal;
using Popcorn.Player.Core.Contracts;
using Popcorn.Player.Core.Core;
using Popcorn.Player.Core.Services;
using Popcorn.Player.Core.WebSocket;
using Popcorn.Shared.Interfaces;

namespace Popcorn.Player.Core.Player;

public class MpvPlayer : IPlayer
{
    private readonly Lazy<WebEventHandler> _webSocket;
    private readonly Lazy<AudioSubtitlesService> _audioSubtitlesService;
    private readonly FFmpegService _ffmpegService;
    private readonly ILogger<MpvPlayer> _logger;
    private IMediaAnalysis? _currentMediaAnalysis;
    private string? _currentFilePath;
    private List<MediaTrack>? _mediaTracks;

    public Client _player { get; }

    public MpvPlayer(Lazy<WebEventHandler> webSocket, ILogger<MpvPlayer> logger, FFmpegService ffmpegService, Lazy<AudioSubtitlesService> audioSubtitlesService)
    {
        _webSocket = webSocket;
        _audioSubtitlesService = audioSubtitlesService;
        _ffmpegService = ffmpegService;
        _player = new Client();
        _player.SetProperty("vo", "libmpv");
        _player.SetProperty("hwdec", "auto");
        _player.SetProperty("osd-level", "0");
        //_player.SetOption("deinterlace", "auto"); // Should find a way to enable this by updating MPV to 0.38. Nickvision.MPVSharp is archived.
        _player.SetOption("sub-border-size", "1");
        _logger = logger;
        //_player.SetProperty("log-file", "mpv_log.log");

        //_player.RequestLogMessages("debug");
        _player.LogMessageReceived += (sender, e) =>
        {
            logger.LogInformation($"[{e.Prefix}] {e.Text}"); // Log messages (e.Text) end with newline char
        };

        _player.PropertyChanged += (sender, e) =>
        {
            logger.LogInformation($"PropertyChanged {e.Name}: {e.Node?.ToString()}");

            if (e.Node == null) return;

            switch (e.Name)
            {
                case "time-pos":
                    _webSocket.Value.PostEvent("current_time", (double)e.Node.Value!);
                    return;
                //case "media-title":
            }
        };
        _player.FileLoaded += PlayerFileLoaded;
    }

    public Client Client => _player;

    public void Initialize()
    {
        _player.Initialize();
    }

    public void InitializeWindows(string wid)
    {
        _player.SetProperty("vo", "gpu");
        _player.SetOption("wid", wid);
        Initialize();
    }

    public async Task<bool> LoadMedia(string path, IMediaAnalysis mediaAnalysis)
    {
        try
        {
            Stop();
            _player.Command(new string[] { "loadfile", path });
        } catch (Exception ex)
        {
            var message = "Failed to open file: " + path;
            _logger.LogError(ex, message);
            _webSocket.Value.PostError(message, ex);
            return false;
        }
        _player.ObserveProperty("time-pos");
        _player.ObserveProperty("duration");

        _currentMediaAnalysis = mediaAnalysis;
        _currentFilePath = path;
        return true;
    }

    private void PlayerFileLoaded()
    {
        LoadFileDuration();
        LoadMediaTracks();

        _currentMediaAnalysis = null; //Cleanup as its no longer needed.
        MediaPlaying?.Invoke(this, EventArgs.Empty); //TODO this feels kinda nasty, should review the event system
    }

    private void LoadFileDuration()
    {
        double duration = -1;
        if (_currentFilePath == null)
        {
            _webSocket.Value.PostEvent("duration_update", duration);
            return;
        }

        try
        {
            _player.GetProperty("duration", out duration);
        } catch(Exception ex)
        {
            _logger.LogWarning(ex, "No duration field in the file {path}", _currentFilePath);
        }
        if (duration < 10) //When Mpv can't parse duration - it's always the current time
        {
            if (_currentMediaAnalysis != null && _currentMediaAnalysis.PrimaryVideoStream?.Duration.TotalSeconds > 0)
            {
                duration = _currentMediaAnalysis.PrimaryVideoStream?.Duration.TotalSeconds ?? _currentMediaAnalysis.Duration.TotalSeconds;
            } else
            {
                var fileSize = new FileInfo(_currentFilePath).Length;
                if(fileSize <= 1024 * 1024 * 100) //Clamp to small file sizes
                {
                    duration = _ffmpegService.GetFileDurationFullDecode(_currentFilePath) ?? -1;
                }
            }
        }

        _webSocket.Value.PostEvent("duration_update", duration);
    }

    public void Play(bool play = true)
    {
        _player.SetProperty("pause", YesNo(!play));
    }

    public void Stop()
    {
        _currentMediaAnalysis = null;
        _currentFilePath = null;
        _mediaTracks = null;
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
    private void LoadMediaTracks()
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

            var type = trackDict.ContainsKey("type") ? MediaTrack.ParseTrackType((string)trackDict["type"]) : MediaTrackType.Unknown;
            if ( type == MediaTrackType.Unknown)
            {
                _logger.LogWarning("Unknown media type for file {path}", _currentFilePath);
            }

            var mediaTrack = new MediaTrack
            {
                Id = trackDict.ContainsKey("id") ? (int)trackDict["id"] : 0,
                Type = type,
                Language = trackDict.ContainsKey("lang") ? (string)trackDict["lang"] : string.Empty,
                Description = trackDict.ContainsKey("title") ? (string)trackDict["title"] : string.Empty,
                Selected = trackDict.ContainsKey("selected") && (bool)trackDict["selected"]
            };

            mediaTracks.Add(mediaTrack);
        }

        _mediaTracks = mediaTracks;
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

    public List<MediaTrack> GetSubtitleTracks()
    {
        return _mediaTracks?.Where(t => t.Type == MediaTrackType.Subtitle).ToList() ?? new List<MediaTrack>();
    }
    public List<MediaTrack> GetAudioTracks()
    {
        return _mediaTracks?.Where(t => t.Type == MediaTrackType.Audio).ToList() ?? new List<MediaTrack>();
    }

    public void AddSubtitleTrack(string path)
    {
        _player.Command(new string[] { "sub-add", path });

        LoadMediaTracks();
        _audioSubtitlesService.Value.SendAudioAndSubtitles();
    }

    public void Dispose()
    {
        _player.Dispose();
    }

    private static string YesNo(bool val) => val ? "yes" : "no";
}