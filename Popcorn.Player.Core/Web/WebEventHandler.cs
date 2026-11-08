using System.Text.Json;
using System.Text.Json.Serialization;
using Popcorn.Player.Core.Core;
using Popcorn.Player.Core.Extensions;
using Popcorn.Player.Core.Player;
using Popcorn.Player.Core.Contracts;
using Serilog;
using Popcorn.Player.Core.Services;
using Popcorn.Player.Core.Web;
using Popcorn.Application.Services;
using Popcorn.Domain.Enum;

namespace Popcorn.Player.Core.WebSocket;

public class WebEventHandler
{
    private readonly IPlayer _player;
    private readonly PlayerCore _playerCore;
    private readonly MediaItemManager _mediaItemManager;
    private readonly AudioSubtitlesService _audioSubtitlesService;
    private readonly ThumbnailService _thumbnailService;
    private readonly SegmentSkipService _segmentSkipService;
    private readonly AppWindowLauncher _appWindowLauncher;
    private readonly SubtitleSyncService _subtitleSyncService;
    private readonly IWebBridge _webBridge;

    private bool _playerInitialized = false;
    private List<string>? _pendingMessages = new();

    public event EventHandler<bool> OnFullscreen;
    /// <summary>The overlay's close button.</summary>
    public event EventHandler? OnCloseRequested;
    /// <summary>The overlay page connected for the first time (not on reconnects). Raised on the socket's thread.</summary>
    public event Action? OverlayConnected;

    public WebEventHandler(IPlayer player, PlayerCore playerCore, MediaItemManager mediaItemManager, AudioSubtitlesService audioSubtitlesService, ThumbnailService thumbnailService, SegmentSkipService segmentSkipService, AppWindowLauncher appWindowLauncher, SubtitleSyncService subtitleSyncService)
    {
        _subtitleSyncService = subtitleSyncService;
        _appWindowLauncher = appWindowLauncher;
        _segmentSkipService = segmentSkipService;
        _player = player;
        _playerCore = playerCore;
        _mediaItemManager = mediaItemManager;
        _audioSubtitlesService = audioSubtitlesService;
        _thumbnailService = thumbnailService;

        // Both players' overlays (CEF on Linux, WebView2 on Windows) connect to this WebSocket.
        _webBridge = new SocketWebBridge(OnMessage);
    }

    public void OnMessage(WebEvent message)
    {
        Log.Information(message.Name);
        switch (message.Name)
        {
            case "play_status_changed":
                _player.Play(message.Data!.GetJsonBoolean()); break;
            case "request_fullscreen":
                OnFullscreen?.Invoke(this, message.Data!.GetJsonBoolean()); break;
            case "set_playback_time":
                _player.SetPlaybackTime(message.Data!.GetJsonInt64()); break;
            case "skip_seconds":
                _player.SkipTime(message.Data!.GetJsonInt64()); break;
            case "volume":
                _player.SetVolume(message.Data!.GetJsonDouble()); break;
            case "play_next_title":
                _playerCore.PlayNextTitle(); break;
            case "next_file_refresh":
                Run(Task.Run(_playerCore.RefreshFiles)); break;
            case "open_app_path":
                _appWindowLauncher.Open(message.Data!.GetJsonString()); break;
            case "initialized":
                var reconnected = _playerInitialized;
                _playerInitialized = true;
                SendPendingMessages();
                if (reconnected) _playerCore.ResendState();
                else OverlayConnected?.Invoke();
                break;
            case "audio_track_selected":
                _player.AudioTrackSelected(message.Data!.GetJsonInt32()); break;
            case "subtitle_track_selected":
                _player.SubtitleTrackSelected(message.Data!.GetJsonInt32()); break;
            case "subtitle_file_select":
                _audioSubtitlesService.SubtitleFileAdd(); break;
            case "subtitle_delay_set":
                _player.SubtitleSetDelay(message.Data!.GetJsonDouble()); break;
            case "subtitle_text_requested":
                _subtitleSyncService.OnSubtitleTextRequest(message.Data!.GetJsonInt32()); break;
            case "waveform_requested":
                RequestWaveform((JsonElement)message.Data!); break;
            case "file_open":
                _playerCore.LoadMedia(Guid.Parse(message.Data!.GetJsonString())); break;
            case "folder_open":
                _playerCore.OpenFolder(message.Data!.GetJsonString()); break;
            case "folder_open_root":
                _playerCore.OpenFolder(_mediaItemManager.CurrentMediaItem?.FolderDirectory ?? Path.GetDirectoryName(_playerCore.CurrentMediaFile.Path)); break;
            case "thumb_requested":
                _thumbnailService.OnThumbnailRequest(message.Data!.GetJsonInt64()); break;
            case "player_close":
                OnCloseRequested?.Invoke(this, EventArgs.Empty); break;
            case "segment_seen":
                Run(_segmentSkipService.MarkSeenAsync(Guid.Parse(message.Data!.GetJsonString()))); break;
            case "segment_auto_skip_set":
                SetSegmentAutoSkip(message.Data); break;
            default:
                throw new NotImplementedException($"Event from _player not supported: {message.Name}");
        }
    }

    /// <summary>Subtitle syncer: <c>{ track, start, duration, key }</c> (seconds; track = mpv id, -1 = first).</summary>
    private void RequestWaveform(JsonElement data) => _subtitleSyncService.OnWaveformRequest(
        data.GetProperty("track").GetInt32(),
        data.GetProperty("start").GetDouble(),
        data.GetProperty("duration").GetDouble(),
        data.GetProperty("key").GetString() ?? string.Empty);

    /// <summary>The item's auto-skip override from the overlay's settings menu (null = global setting).</summary>
    private void SetSegmentAutoSkip(object? data)
    {
        var itemId = _playerCore.CurrentMediaFile?.ItemId ?? Guid.Empty;
        if (itemId == Guid.Empty) return;
        var value = data?.GetJsonString();
        SegmentAutoSkip? mode = string.IsNullOrEmpty(value) ? null : Enum.Parse<SegmentAutoSkip>(value);
        Run(_segmentSkipService.SetItemModeAsync(itemId, mode));
    }

    private static async void Run(Task task)
    {
        try { await task; }
        catch (Exception e) { Log.Error(e, "Overlay request failed"); }
    }

    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string StartServer()
    {
        return ((SocketWebBridge)_webBridge).StartServer();
    }

    public void PostEvent(string name, object? data)
    {
        var @event = new WebEvent
        {
            Name = name,
            Data = data,
        };
        var message = JsonSerializer.Serialize(@event, _serializerOptions);
        
        if(_playerInitialized)
        {
            _webBridge.SendMessage(message);
        } else
        {
            _pendingMessages?.Add(message);
        }
    }

    public void PostError(string messageString, Exception ex)
    {
        PostEvent("error", new ErrorEvent() {
            Message = messageString,
            ExceptionMessage = ex.Message,
            Stacktrace = ex.StackTrace ?? string.Empty,
            InnerMessage = ex.InnerException != null ? ex.InnerException.Message : null,
            InnerStacktrace = ex.InnerException != null ? ex.InnerException.StackTrace : null,
        });
    }

    /// <summary>
    /// Post binary data to websocket. Only used for thumbnails
    /// </summary>
    /// <param name="webEvent"></param>
    /// <param name="data"></param>
    public void PostBinaryEvent(byte[] data)
    {
        //Posts binary data to web socket with json header. JsonLength (4 bytes) + Json string bytes(JsonLength bytes) + payload
        /*var eventJson = JsonSerializer.Serialize(webEvent, _serializerOptions);
        using var ms = new MemoryStream();
        
        var jsonBytes = Encoding.UTF8.GetBytes(eventJson);
        var jsonLength = jsonBytes.Length;

        ms.Write(BitConverter.GetBytes(jsonLength), 0, sizeof(int));
        ms.Write(jsonBytes, 0, jsonLength);
        ms.Write(data, 0, data.Length);

        WebSocketContainer.SendBytesMessage(ms.ToArray());*/

        _webBridge.SendBytesMessage(data);
    }

    //Hacky way to fix webview race condition, but whatever, it works
    private void SendPendingMessages()
    {
        if (_pendingMessages is null) return; // Overlay reloaded: already flushed

        foreach (var msg in _pendingMessages)
        {
            _webBridge.SendMessage(msg);
        }
        _pendingMessages = null;
    }
}