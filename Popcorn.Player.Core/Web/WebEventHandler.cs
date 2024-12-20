using System.Text.Json;
using System.Text.Json.Serialization;
using Popcorn.Player.Core.Core;
using Popcorn.Player.Core.Extensions;
using Popcorn.Player.Core.Player;
using Popcorn.Player.Core.Contracts;
using Serilog;
using Popcorn.Player.Core.Services;
using Popcorn.Player.Core.Web;
using System.Runtime.InteropServices;

namespace Popcorn.Player.Core.WebSocket;

public class WebEventHandler
{
    private readonly IPlayer _player;
    private readonly PlayerCore _playerCore;
    private readonly MediaItemManager _mediaItemManager;
    private readonly AudioSubtitlesService _audioSubtitlesService;
    private readonly ThumbnailService _thumbnailService;
    private readonly IWebBridge _webBridge;

    private bool _playerInitialized = false;
    private List<string>? _pendingMessages = new();


    public event EventHandler<bool> OnFullscreen;

    public WebEventHandler(IPlayer player, PlayerCore playerCore, MediaItemManager mediaItemManager, AudioSubtitlesService audioSubtitlesService, ThumbnailService thumbnailService)
    {
        _player = player;
        _playerCore = playerCore;
        _mediaItemManager = mediaItemManager;
        _audioSubtitlesService = audioSubtitlesService;
        _thumbnailService = thumbnailService;

        _webBridge = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? new ChromiumWebBridge() : new SocketWebBridge(OnMessage);
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
            case "initialized":
                _playerInitialized = true;
                SendPendingMessages();
                break;
            case "audio_track_selected":
                _player.AudioTrackSelected(message.Data!.GetJsonInt32()); break;
            case "subtitle_track_selected":
                _player.SubtitleTrackSelected(message.Data!.GetJsonInt32()); break;
            case "subtitle_file_select":
                _audioSubtitlesService.SubtitleFileAdd(); break;
            case "subtitle_delay_set":
                _player.SubtitleSetDelay(message.Data!.GetJsonDouble()); break;
            case "file_open":
                _playerCore.LoadMedia(message.Data!.GetJsonString()); break;
            case "folder_open":
                _playerCore.OpenFolder(message.Data!.GetJsonString()); break;
            case "folder_open_root":
                _playerCore.OpenFolder(_mediaItemManager.CurrentMediaItem?.FolderDirectory ?? Path.GetDirectoryName(_playerCore.CurrentMediaFile.Path)); break;
            case "thumb_requested":
                _thumbnailService.OnThumbnailRequest(message.Data!.GetJsonInt64()); break;
            default:
                throw new NotImplementedException($"Event from _player not supported: {message.Name}");
        }
    }

    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string StartServer()
    {
        if (_webBridge is SocketWebBridge socketWebBridge)
        {
            return socketWebBridge.StartServer();
        }
        return string.Empty;
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

    public void RegisterWindowsMethods(Action<string> postMessage, Action<byte[]> postMessageBytes)
    {
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (_webBridge is ChromiumWebBridge chromeWebBridge)
            {
                chromeWebBridge.PostMessage = postMessage;
                chromeWebBridge.PostMessageBytes = postMessageBytes;
            }
            else
            {
                throw new InvalidOperationException("Web bridge not initialized for hooking post message");
            }
        } else
        {
            throw new InvalidOperationException("Only available in windows");
        }
    }

    //Hacky way to fix webview race condition, but whatever, it works
    private void SendPendingMessages()
    {
        if (_pendingMessages?.Count < 0) return;

        foreach (var msg in _pendingMessages!)
        {
            _webBridge.SendMessage(msg);
        }
        _pendingMessages = null;
    }
}