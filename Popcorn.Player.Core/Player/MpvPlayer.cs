using System.Globalization;
using System.Text.Json;
using FFMpegCore;
using Microsoft.Extensions.Logging;
using Popcorn.Application.Utils;
using Popcorn.Player.Core.Contracts;
using Popcorn.Player.Core.Core;
using Popcorn.Player.Core.Services;
using Popcorn.Player.Core.WebSocket;

namespace Popcorn.Player.Core.Player;

public class MpvPlayer : IPlayer
{
    private readonly Lazy<WebEventHandler> _webSocket;
    private readonly Lazy<AudioSubtitlesService> _audioSubtitlesService;
    private readonly FFmpegService _ffmpegService;
    private readonly ILogger<MpvPlayer> _logger;
    private readonly IMpvClient _player;
    private IMediaAnalysis? _currentMediaAnalysis;
    private string? _currentFilePath;
    private List<MediaTrack>? _mediaTracks;

    public double Duration { get; private set; } = -1;
    public double? Position => _player.GetDouble("time-pos");
    public bool IsPaused => _player.GetString("pause") == "yes";
    public bool IsPlaying { get; private set; }

    public MpvPlayer(IMpvClient player, Lazy<WebEventHandler> webSocket, ILogger<MpvPlayer> logger, FFmpegService ffmpegService, Lazy<AudioSubtitlesService> audioSubtitlesService)
    {
        _player = player;
        _webSocket = webSocket;
        _audioSubtitlesService = audioSubtitlesService;
        _ffmpegService = ffmpegService;
        _logger = logger;

        _player.LogMessage += (_, text) => logger.LogInformation("{mpvLog}", text);
        _player.DoublePropertyChanged += (_, e) =>
        {
            if (e.Name == "time-pos") _webSocket.Value.PostEvent("current_time", e.Value);
        };
        _player.FlagPropertyChanged += (_, e) =>
        {
            if (e.Name == "pause")
            {
                _paused = e.Value;
                UpdatePlaying();
                return;
            }
            if (e.Name != "eof-reached" || e.Value == _eofReached) return;
            _eofReached = e.Value;
            UpdatePlaying();
            (e.Value ? MediaEnded : MediaResumed)?.Invoke(this, EventArgs.Empty);
        };
        _player.FileLoaded += (_, _) => PlayerFileLoaded();
        _player.PlaybackRestarted += (_, _) => Seeked?.Invoke(this, EventArgs.Empty);
    }

    private bool _eofReached;
    private bool _paused;
    /// <summary>Between mpv's file-loaded and the next <see cref="Stop"/>.</summary>
    private bool _fileLoaded;
    private readonly object _playingLock = new();

    private void UpdatePlaying()
    {
        bool playing;
        lock (_playingLock)
        {
            playing = _fileLoaded && !_paused && !_eofReached;
            if (playing == IsPlaying) return;
            IsPlaying = playing;
        }
        PlayingChanged?.Invoke(this, playing);
    }

    /// <summary>Settings shared by every platform. The video output itself is chosen by whoever created the core.</summary>
    public void Initialize()
    {
        _player.SetProperty("hwdec", "auto");
        _player.SetProperty("osd-level", "0");
        _player.SetProperty("sub-border-size", "1");
        // Subtitle files next to the video are found by ExternalSubtitles (item folder, Subs/ folders), not by mpv.
        _player.SetProperty("sub-auto", "no");
        // Files still downloading are read over HTTP from the server, which holds a request until the download has
        // the data: 0 = FFmpeg's default, no read timeout (mpv's own default gives up after 60 s).
        _player.SetProperty("network-timeout", "0");
        // Keep the file loaded (paused on its last frame) at its end instead of unloading it, so it can still be
        // seeked back and played again; eof-reached tells when the end is reached and left again.
        _player.SetProperty("keep-open", "yes");
        _player.ObserveDouble("time-pos");
        _player.ObserveFlag("eof-reached");
        _player.ObserveFlag("pause");
        // _player.SetProperty("osd-level", "3");
        // _player.Command("script-binding", "stats/display-stats-toggle");
    }

    public Task<bool> LoadMedia(string path, IMediaAnalysis mediaAnalysis, double? startSeconds = null)
    {
        try
        {
            Stop();
            // An option, so it applies to every later loadfile too: always set it.
            // sub-delay is an option too: a delay set for one file must not carry over to the next.
            _player.SetProperty("sub-delay", "0");
            _player.SetProperty("start", startSeconds is { } start ? start.ToString(CultureInfo.InvariantCulture) : "none");
            _player.Command("loadfile", path);
        }
        catch (Exception ex)
        {
            var message = "Failed to open file: " + path;
            _logger.LogError(ex, message);
            _webSocket.Value.PostError(message, ex);
            return Task.FromResult(false);
        }

        _currentMediaAnalysis = mediaAnalysis;
        _currentFilePath = path;
        return Task.FromResult(true);
    }

    private void PlayerFileLoaded()
    {
        LoadFileDuration();
        LoadMediaTracks();
        _fileLoaded = true;
        UpdatePlaying();

        _currentMediaAnalysis = null; //Cleanup as its no longer needed.
        MediaPlaying?.Invoke(this, EventArgs.Empty); //TODO this feels kinda nasty, should review the event system
    }

    private void LoadFileDuration()
    {
        double duration = -1;
        if (_currentFilePath == null)
        {
            Duration = duration;
            _webSocket.Value.PostEvent("duration_update", duration);
            return;
        }

        var mpvDuration = _player.GetDouble("duration");
        if (mpvDuration is null)
        {
            _logger.LogWarning("No duration field in the file {path}", _currentFilePath);
        }
        duration = mpvDuration ?? -1;

        if (duration < 10) //When Mpv can't parse duration - it's always the current time
        {
            // The video stream's duration, else the container's (WebM/MKV have none per stream). Only without
            // either decode the whole file: slow, and it runs on mpv's event thread (delays every other event).
            var probed = _currentMediaAnalysis?.PrimaryVideoStream?.Duration.TotalSeconds is > 0 and var streamDuration
                ? streamDuration
                : _currentMediaAnalysis?.Duration.TotalSeconds ?? 0;
            if (probed > 0)
            {
                duration = probed;
            }
            else
            {
                var fileSize = new FileInfo(_currentFilePath).Length;
                if (fileSize <= 1024 * 1024 * 100) //Clamp to small file sizes
                {
                    duration = _ffmpegService.GetFileDurationFullDecode(_currentFilePath) ?? -1;
                }
            }
        }

        Duration = duration;
        _webSocket.Value.PostEvent("duration_update", duration);
    }

    public void Play(bool play = true)
    {
        // Play at the end of the file = from the start again.
        if (play && _eofReached) _player.Command("seek", "0", "absolute");
        _player.SetProperty("pause", YesNo(!play));
    }

    public void Stop()
    {
        _currentMediaAnalysis = null;
        _currentFilePath = null;
        _mediaTracks = null;
        Duration = -1;
        _eofReached = false;
        _fileLoaded = false;
        UpdatePlaying();
        _player.Command("stop");
    }

    public void SetPlaybackTime(long time)
    {
        _player.Command("seek", (time / 1000.0).ToString("0.###", CultureInfo.InvariantCulture), "absolute");
    }

    public void SkipTime(long time)
    {
        _player.Command("seek", time.ToString(CultureInfo.InvariantCulture));
    }

    public void SetVolume(double volume)
    {
        _player.SetProperty("ao-volume", ((int)Math.Floor(volume * 100)).ToString(CultureInfo.InvariantCulture));
    }

    public event EventHandler? MediaEnded;
    public event EventHandler? MediaResumed;
    public event EventHandler<long>? TimeChanged;
    public event EventHandler<long>? LengthChanged;
    public event EventHandler? MediaPlaying;
    public event EventHandler<bool>? PlayingChanged;
    public event EventHandler? Seeked;

    /// <summary>mpv returns node properties as JSON when read as a string.</summary>
    private void LoadMediaTracks()
    {
        var json = _player.GetString("track-list") ?? throw new Exception("track-list is not available.");
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new Exception("track-list is not an array.");

        var mediaTracks = new List<MediaTrack>();
        foreach (var track in doc.RootElement.EnumerateArray())
        {
            if (track.ValueKind != JsonValueKind.Object) continue;

            var type = track.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String
                ? MediaTrack.ParseTrackType(typeProp.GetString()!)
                : MediaTrackType.Unknown;
            if (type == MediaTrackType.Unknown)
            {
                _logger.LogWarning("Unknown media type for file {path}", _currentFilePath);
            }

            mediaTracks.Add(new MediaTrack
            {
                Id = track.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt32() : 0,
                Type = type,
                Language = StringOrEmpty(track, "lang"),
                Description = StringOrEmpty(track, "title"),
                Selected = track.TryGetProperty("selected", out var selected) && selected.ValueKind == JsonValueKind.True,
                Codec = StringOrEmpty(track, "codec"),
                StreamIndex = track.TryGetProperty("ff-index", out var ffIndex) && ffIndex.ValueKind == JsonValueKind.Number ? ffIndex.GetInt32() : null,
                ExternalFilename = StringOrEmpty(track, "external-filename") is { Length: > 0 } external ? external : null,
            });
        }

        _mediaTracks = mediaTracks;
    }

    private static string StringOrEmpty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

    public void AudioTrackSelected(int track)
    {
        _player.SetProperty("aid", track.ToString(CultureInfo.InvariantCulture));
        MarkSelected(MediaTrackType.Audio, track);
    }

    /// <summary>A negative id turns subtitles off.</summary>
    public void SubtitleTrackSelected(int track)
    {
        _player.SetProperty("sid", track < 0 ? "no" : track.ToString(CultureInfo.InvariantCulture));
        MarkSelected(MediaTrackType.Subtitle, track);
    }

    /// <summary>track-list is read once per file; keep its selection current for the overlay's track lists.</summary>
    private void MarkSelected(MediaTrackType type, int id)
    {
        if (_mediaTracks is null) return;
        foreach (var track in _mediaTracks.Where(t => t.Type == type))
        {
            track.Selected = track.Id == id;
        }
    }

    public string? CurrentFilePath => _currentFilePath;

    public MediaTrack? GetTrack(MediaTrackType type, int id) => _mediaTracks?.FirstOrDefault(t => t.Type == type && t.Id == id);

    public void SubtitleSetDelay(double delay)
    {
        _player.SetProperty("sub-delay", delay.ToString(CultureInfo.InvariantCulture));
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
        _player.Command("sub-add", path);

        LoadMediaTracks();
        _audioSubtitlesService.Value.SendAudioAndSubtitles();
    }

    public void AddExternalSubtitles(IReadOnlyList<ExternalSubtitle> subtitles)
    {
        if (subtitles.Count == 0) return;
        foreach (var subtitle in subtitles)
        {
            try
            {
                if (subtitle.Language is { } language) _player.Command("sub-add", subtitle.Path, "auto", subtitle.Title, language);
                else _player.Command("sub-add", subtitle.Path, "auto", subtitle.Title);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Could not add the subtitle file {Path}", subtitle.Path);
            }
        }
        LoadMediaTracks();
    }

    public void Dispose()
    {
        _player.Dispose();
    }

    private static string YesNo(bool val) => val ? "yes" : "no";
}
