using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Popcorn.Player.Contracts;
using Popcorn.Player.Utils;

namespace Popcorn.Player.Core;
public static class AudioAndSubtitles
{
    public static Func<string> SubtitleFileSelect { get; set; }

    public static void SendAudioAndSubtitles()
    {
        var @event = new AudioAndSubtitlesLoadedEvent()
        {
            AudioTracks = PlayerCore.MediaPlayer.AudioTrackDescription
            .Select(track => MapToVlcTrack(track, track.Id == PlayerCore.MediaPlayer.AudioTrack))
            .ToList(),
            SubtitleTracks = PlayerCore.MediaPlayer.SpuDescription
            .Select(track => MapToVlcTrack(track, track.Id == PlayerCore.MediaPlayer.Spu))
            .ToList(),
        };
        PlayerCore.PostEvent("audio_subtitles_loaded", @event);
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
        PlayerCore.PostEvent("segments_update", @event);
    }

    public static bool SubtitleFileAdd()
    {
        var filePath = SubtitleFileSelect();
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

        return true;
    }

    public static void AutoSelectAudioAndSubtitles()
    {
        // TODO default japaneese preference for anime
        if (PlayerCore.MediaPlayer.AudioTrackCount > 1)
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
        }
    }

    public static bool SubtitleDelaySet(object data)
    {
        var delay = ((JsonElement)data).GetDouble();
        return ThreadPool.QueueUserWorkItem(_ => PlayerCore.MediaPlayer.SetSpuDelay(Convert.ToInt64(delay * 1000000)));
    }

    public static bool SetVolume(object data)
    {
        try
        {
            var volume = ((JsonElement)data).GetDouble();
            ThreadPool.QueueUserWorkItem(_ => PlayerCore.MediaPlayer.Volume = (int)Math.Floor(volume * 100));
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to set volume");
            return false;
        }
        return true;
    }

    public static bool AudioTrackSelected(object data)
    {
        var trackId = ((JsonElement)data).GetInt32();
        return PlayerCore.MediaPlayer.SetAudioTrack(trackId); //TODO threadpool
    }

    public static bool SubtitleTrackSelected(object data)
    {
        var trackId = ((JsonElement)data).GetInt32();
        return PlayerCore.MediaPlayer.SetSpu(trackId); //TODO threadpool
    }

    private static VlcTrack MapToVlcTrack(TrackDescription track, bool isSelected)
    {
        var (lang, description) = TrackDescriptionParser.Parse(track.Name);
        return new VlcTrack
        {
            Id = track.Id,
            Language = lang,
            Description = description,
            Selected = isSelected
        };
    }

    private static Func<TrackDescription, bool> EnglishSelectPredicate = (track) =>
    {
        var (lang, description) = TrackDescriptionParser.Parse(track.Name);
        if (lang?.ToLower() == "english") return true;

        return false;
    };
}
