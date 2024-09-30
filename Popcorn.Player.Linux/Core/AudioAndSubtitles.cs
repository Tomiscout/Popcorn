using System.Text.Json;
using Popcorn.Player.Linux.Contracts;
using Popcorn.Player.Linux.WebSocket;
using Serilog;

namespace Popcorn.Player.Linux.Core;
public static class AudioAndSubtitles
{
    public static Func<string> SubtitleFileSelect { get; set; }

    public static void SendAudioAndSubtitles()
    {
        var mediaTracks = Program.GetPlayer().GetMediaTracks();
        
        var @event = new AudioAndSubtitlesLoadedEvent
        {
            AudioTracks = mediaTracks.Where(track => track.Type == MediaTrackType.Audio).ToList(),
            SubtitleTracks =  mediaTracks.Where(track => track.Type == MediaTrackType.Subtitle).ToList(),
        };
        PlayerWebSocket.PostEvent("audio_subtitles_loaded", @event);
    }

    public static void SendAudioSegments()
    {
        if (PlayerCore.CurrentMediaFile is null)
        {
            Log.Warning("Trying to send audio segments of null media file");
            return;
        }

        var @event = new AudioSegmentsEvent()
        {
            Intro = PlayerCore.CurrentMediaFile.SegmentLocations.Where(s => s.StartSeconds >= 0).ToList(),
            Outro = PlayerCore.CurrentMediaFile.SegmentLocations.Where(s => s.StartSeconds < 0).ToList(),
        };
        PlayerWebSocket.PostEvent("segments_update", @event);
    }

    public static bool SubtitleFileAdd()
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

    public static void AutoSelectAudioAndSubtitles()
    {
        // TODO default japaneese preference for anime
        /*if (PlayerCore.MediaPlayer.AudioTrackCount > 1)
        {
            var englishTracks = PlayerCore.MediaPlayer.AudioTrackDescription.Where(EnglishSelectPredicate).ToList();
            if (englishTracks.Any())
            {
                ThreadPool.QueueUserWorkItem(_ => PlayerCore.MediaPlayer.SetAudioTrack(englishTracks.First().Id));
            }
        }

        if (PlayerCore.MediaPlayer.SpuCount > 1)
        {
            var englishTracks = PlayerCore.MediaPlayer.SpuDescription.Where(EnglishSelectPredicate).ToList();
            if (englishTracks.Any())
            {
                ThreadPool.QueueUserWorkItem(_ => PlayerCore.MediaPlayer.SetSpu(englishTracks.First().Id));
            }
        }*/
    }
    
    public static bool SetVolume(object data)
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

    public static bool AudioTrackSelected(object data)
    {
        // var trackId = ((JsonElement)data).GetInt32();
        // return PlayerCore.MediaPlayer.SetAudioTrack(trackId); //TODO threadpool
        return true;
    }

    public static bool SubtitleTrackSelected(object data)
    {
        // var trackId = ((JsonElement)data).GetInt32();
        // return PlayerCore.MediaPlayer.SetSpu(trackId); //TODO threadpool
        return true;
    }

    // private static VlcTrack MapToVlcTrack(TrackDescription track, bool isSelected)
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
    // private static Func<TrackDescription, bool> EnglishSelectPredicate = (track) =>
    // {
    //     var (lang, description) = TrackDescriptionParser.Parse(track.Name);
    //     if (lang?.ToLower() == "english") return true;
    //
    //     return false;
    // };
}
