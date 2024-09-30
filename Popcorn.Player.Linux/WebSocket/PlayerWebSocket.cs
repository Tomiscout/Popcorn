using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Popcorn.Player.Linux.Contracts;
using Popcorn.Player.Linux.Core;
using Popcorn.Player.Linux.Extensions;
using Popcorn.Player.Linux.Services;
using Serilog;

namespace Popcorn.Player.Linux.WebSocket;

public class PlayerWebSocket
{
    public static void OnMessage (WebEvent message)
    {
        var player = Program.GetPlayer();
        Log.Information(message.Name);
        switch (message.Name)
        {
            case "play_status_changed":
                player.Play(message.Data!.GetJsonBoolean()); break;
            case "fullscreen":
                Program.SetFullscreen(message.Data!.GetJsonBoolean()); break;
            case "set_playback_time":
                player.SetPlaybackTime(message.Data!.GetJsonInt64()); break;
            case "skip_seconds":
                player.SkipTime(message.Data!.GetJsonInt64()); break;
            case "volume":
                player.SetVolume(message.Data!.GetJsonDouble()); break;
            case "play_next_title":
                PlayerCore.PlayNextTitle(); break;
            case "initialized":
                WebSocketContainer.SendPendingMessages(); break;
            case "audio_track_selected":
                player.AudioTrackSelected(message.Data!.GetJsonInt32()); break;
            case "subtitle_track_selected":
                player.SubtitleTrackSelected(message.Data!.GetJsonInt32()); break;
            case "subtitle_file_select":
                AudioAndSubtitles.SubtitleFileAdd(); break;
            case "subtitle_delay_set":
                player.SubtitleSetDelay(message.Data!.GetJsonDouble()); break;
            case "file_open":
                PlayerCore.LoadMedia(message.Data!.GetJsonString()); break;
            case "folder_open":
                PlayerCore.OpenFolder(message.Data!.GetJsonString()); break;
            case "folder_open_root":
                PlayerCore.OpenFolder(MediaItemManager.CurrentMediaItem?.FolderDirectory ?? Path.GetDirectoryName(PlayerCore.CurrentMediaFile.Path)); break;
            case "thumb_requested":
                ThumbnailService.OnThumbnailRequest(message.Data!.GetJsonInt64()); break;
            default:
                throw new NotImplementedException($"Event from player not supported: {message.Name}");
        }
    }

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    
    
    public static void PostEvent(string name, object? data)
    {
        var @event = new WebEvent
        {
            Name = name,
            Data = data,
        };
        WebSocketContainer.SendMessage(JsonSerializer.Serialize(@event, _serializerOptions));
    }

    /// <summary>
    /// Post binary data to websocket. Only used for thumbnails
    /// </summary>
    /// <param name="webEvent"></param>
    /// <param name="data"></param>
    public static void PostBinaryEvent(byte[] data)
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
        
        WebSocketContainer.SendBytesMessage(data);
    }
}