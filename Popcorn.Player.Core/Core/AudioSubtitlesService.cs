using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Popcorn.Domain.Models;
using Popcorn.Player.Core.Contracts;
using Popcorn.Player.Core.Player;
using Popcorn.Player.Core.WebSocket;
using Serilog;

namespace Popcorn.Player.Core.Core;
public class AudioSubtitlesService
{
    private readonly IPlayer _player;
    private readonly Lazy<WebEventHandler> _webSocket;
    public Func<Task<string>> SubtitleFileSelect { get; set; }

    public AudioSubtitlesService(IPlayer player, Lazy<WebEventHandler> webSocket)
    {
        _player = player;
        _webSocket = webSocket;
    }

    public void SendAudioAndSubtitles()
    {
        var @event = new AudioAndSubtitlesLoadedEvent
        {
            AudioTracks = _player.GetAudioTracks(),
            SubtitleTracks = _player.GetSubtitleTracks(),
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
        var filePath = SubtitleFileSelect().ContinueWith(result =>
        {
            var filePath = result.Result;
            if (string.IsNullOrWhiteSpace(filePath)) return;

            _player.AddSubtitleTrack(filePath);
        });
        return true;

       /* var currentCount = PlayerCore.MediaPlayer.SpuCount;
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
        }*/
    }

    public void AutoSelectAudioAndSubtitles()
    {
        var audioTracks = _player.GetAudioTracks();
        var subtitleTracks = _player.GetSubtitleTracks();

        //Prefers japaneese audio, for anime. TODO needs to be a setting
        var jpAudioTracks = audioTracks.Where(track => track.Language == "jpn").ToList();

        if (audioTracks.Count > 1)
        {
            int? id = null;

            if (jpAudioTracks.Any())
            {
                id = audioTracks.FirstOrDefault(track => track.Language == "jpn")?.Id;
            }
            else
            {
                var englishTracks = audioTracks.Where(t => t.Language == "eng").ToList();
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
            var englishTracks = subtitleTracks.Where(t => t.Language == "eng").ToList();
            if (englishTracks.Any())
            {
                _player.SubtitleTrackSelected(englishTracks.First().Id);
            }
        }
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
