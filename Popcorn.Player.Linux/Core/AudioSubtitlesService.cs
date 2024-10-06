using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Popcorn.Domain.Models;
using Popcorn.Player.Linux.Contracts;
using Popcorn.Player.Linux.Player;
using Popcorn.Player.Linux.WebSocket;
using Serilog;

namespace Popcorn.Player.Linux.Core;
public class AudioSubtitlesService
{
    private readonly IPlayer _player;
    private readonly Lazy<PlayerWebSocket>  _webSocket;
    public Func<string> SubtitleFileSelect { get; set; }

    public AudioSubtitlesService(IPlayer player, Lazy<PlayerWebSocket> webSocket)
    {
        _player = player;
        _webSocket = webSocket;
    }
    
    public void SendAudioAndSubtitles()
    {
        var mediaTracks = _player.GetMediaTracks();
        
        var @event = new AudioAndSubtitlesLoadedEvent
        {
            AudioTracks = mediaTracks.Where(track => track.Type == MediaTrackType.Audio).ToList(),
            SubtitleTracks =  mediaTracks.Where(track => track.Type == MediaTrackType.Subtitle).ToList(),
        };
        _webSocket.Value.PostEvent("audio_subtitles_loaded", @event);
    }

    public void SendAudioSegments(MediaFile mediaFile)
    {
        if (mediaFile is null)
        {
            Log.Warning("Trying to send audio segments of null media file");
            return;
        }

        var @event = new AudioSegmentsEvent()
        {
            Intro = mediaFile.SegmentLocations.Where(s => s.StartSeconds >= 0).ToList(),
            Outro = mediaFile.SegmentLocations.Where(s => s.StartSeconds < 0).ToList(),
        };
        _webSocket.Value.PostEvent("segments_update", @event);
    }

    public bool SubtitleFileAdd()
    {
        /*var filePath = SubtitleFileSelect();
        if (string.IsNullOrWhiteSpace(filePath)) return true;

        var currentCount = PlayerCore.MediaPlayer.SpuCount;
        ThreadPool.QueueUserWorkItem(_ => PlayerCore.MediaPlayer.AddSlave(MediaSlaveType.Subtitle, $"file:///{filePath}", true));

        //This could be avoided if 'media_subitem_added' event support was added.
        bool isSubtitleLoaded = false;
        Task.Run(() =>
        {
            for (int i = 0; i < 10; i++)
            {
                Task.Delay(50).Wait();

                if (PlayerCore.MediaPlayer.SpuCount > currentCount)
                {
                    isSubtitleLoaded = true;
                    break;
                }
            }
        }).Wait();
        if (isSubtitleLoaded)
        {
            SendAudioAndSubtitles();
        }
*/
        return true;
    }

    public void AutoSelectAudioAndSubtitles()
    {
        var mediaTracks = _player.GetMediaTracks();
        var audioTracks = mediaTracks.Where(track => track.Type == MediaTrackType.Audio).ToList();
        var subtitleTracks = mediaTracks.Where(track => track.Type == MediaTrackType.Subtitle).ToList();
        
        //Prefers japaneese audio, for anime. TODO needs to be a setting
        var jpAudioTracks = audioTracks.Where(track => track.Language == "ja").ToList();

        if (audioTracks.Count > 1)
        {
            int? id = null;
            
            if (jpAudioTracks.Any())
            {
                id = audioTracks.FirstOrDefault(track => track.Language == "ja")?.Id;
            }
            else
            {
                var englishTracks = audioTracks.Where(t => t.Language == "en").ToList();
                if (englishTracks.Any())
                {
                    id = englishTracks.FirstOrDefault()?.Id;
                }
            }
            
            id ??= audioTracks.First().Id;
            _player.AudioTrackSelected(id.Value);
        }

        if (subtitleTracks.Count > 1)
        {
            var englishTracks = subtitleTracks.Where(t => t.Language == "en").ToList();
            if (englishTracks.Any())
            {
                _player.SubtitleTrackSelected(englishTracks.First().Id);
            }
        }
    }
    
    public bool SetVolume(object data)
    {
        // try
        // {
        //     var volume = ((JsonElement)data).GetDouble();
        //     ThreadPool.QueueUserWorkItem(_ => PlayerCore.MediaPlayer.Volume = (int)Math.Floor(volume * 100));
        // }
        // catch (Exception e)
        // {
        //     Log.Error(e, "Failed to set volume");
        //     return false;
        // }
        return true;
    }

    public bool AudioTrackSelected(object data)
    {
        // var trackId = ((JsonElement)data).GetInt32();
        // return PlayerCore.MediaPlayer.SetAudioTrack(trackId); //TODO threadpool
        return true;
    }

    public bool SubtitleTrackSelected(object data)
    {
        // var trackId = ((JsonElement)data).GetInt32();
        // return PlayerCore.MediaPlayer.SetSpu(trackId); //TODO threadpool
        return true;
    }

    // private VlcTrack MapToVlcTrack(TrackDescription track, bool isSelected)
    // {
    //     var (lang, description) = TrackDescriptionParser.Parse(track.Name);
    //     return new VlcTrack
    //     {
    //         Id = track.Id,
    //         Language = lang,
    //         Description = description,
    //         Selected = isSelected
    //     };
    // }
    //
    // private Func<TrackDescription, bool> EnglishSelectPredicate = (track) =>
    // {
    //     var (lang, description) = TrackDescriptionParser.Parse(track.Name);
    //     if (lang?.ToLower() == "english") return true;
    //
    //     return false;
    // };
    
}
