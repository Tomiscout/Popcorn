using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Popcorn.Player.Linux.Contracts;
using Popcorn.Player.Linux.Core;
using Popcorn.Player.Linux.Extensions;
using Popcorn.Player.Linux.Player;
using Popcorn.Player.Linux.Services;
using Serilog;

namespace Popcorn.Player.Linux.WebSocket;

public class PlayerWebSocket
{
    private readonly IPlayer _player;
    private readonly PlayerCore _playerCore;
    private readonly MediaItemManager _mediaItemManager;
    private readonly AudioSubtitlesService _audioSubtitlesService;
    private readonly ThumbnailService _thumbnailService;
    private readonly WebSocketContainer _webSocketContainer;

    public PlayerWebSocket(IPlayer player, PlayerCore playerCore, MediaItemManager mediaItemManager, AudioSubtitlesService audioSubtitlesService, ThumbnailService thumbnailService)
    {
        _player = player;
        _playerCore = playerCore;
        _mediaItemManager = mediaItemManager;
        _audioSubtitlesService = audioSubtitlesService;
        _thumbnailService = thumbnailService;
        _webSocketContainer = new WebSocketContainer(OnMessage);
    }
    
    private void OnMessage (WebEvent message)
    {
        Log.Information(message.Name);
        switch (message.Name)
        {
            case "play_status_changed":
                _player.Play(message.Data!.GetJsonBoolean()); break;
            case "fullscreen":
                Program.SetFullscreen(message.Data!.GetJsonBoolean()); break;
            case "set_playback_time":
                _player.SetPlaybackTime(message.Data!.GetJsonInt64()); break;
            case "skip_seconds":
                _player.SkipTime(message.Data!.GetJsonInt64()); break;
            case "volume":
                _player.SetVolume(message.Data!.GetJsonDouble()); break;
            case "play_next_title":
                _playerCore.PlayNextTitle(); break;
            case "initialized":
                _webSocketContainer.SendPendingMessages(); break;
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
        return _webSocketContainer.StartServer();
    }
    
    public void PostEvent(string name, object? data)
    {
        var @event = new WebEvent
        {
            Name = name,
            Data = data,
        };
        _webSocketContainer.SendMessage(JsonSerializer.Serialize(@event, _serializerOptions));
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
        
        _webSocketContainer.SendBytesMessage(data);
    }
}