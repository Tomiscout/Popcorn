using LibVLCSharp.Shared;
using System;
using System.Text.Json.Serialization;
using System.Text.Json;
using VideoPlayer.Contracts;
using VideoPlayer.Domain.Extensions;
using VideoBlazor.Domain.Models;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using VideoPlayer.Pipes;
using VideoPlayer.Shared.PipeEvents.Server;
using System.Threading;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using System.Linq;
using LibVLCSharp.Shared.Structures;
using VideoPlayer.Utils;
using System.Threading.Tasks;

namespace VideoPlayer;
internal static class PlayerCore
{
    private static LibVLC _libVLC;
    private static MediaPlayer _mediaPlayer;

    public static Action<string> PostWebMessage { get; set; }
    public static Func<string> SubtitleFileSelect { get; set; }
    public static Func<bool> PlayerInitialized { get; set; }
    public static Func<bool, bool> ChangeFullscreen { get; set; }

    private static MediaItem _currentMediaItem = null;
    private static MediaFile _currentMediaFile = null;
    private static bool _isLoadingTrack = false;

    public static void Initialize()
    {
        Core.Initialize();

        _libVLC = new LibVLC();
        _libVLC.Log += (object sender, LogEventArgs e) => Debug.WriteLine($"[LibVLC] {e.FormattedLog}");
        _mediaPlayer = new MediaPlayer(_libVLC);
        _mediaPlayer.TimeChanged += RecalculateSeekValue;
        _mediaPlayer.LengthChanged += SendLengthEvent;
        _mediaPlayer.EndReached += PlaybackEndReached;
        _mediaPlayer.Playing += PlayingStarted;
    }

    //TODO dublicate code
    public static void LoadMedia(MediaFile file)
    {
        _isLoadingTrack = true;
        if (!File.Exists(file.Path))
        {
            //TODO popover file not found
            return;
        }
        _currentMediaFile = file;
        var media = new Media(_libVLC, file.Path);
        _mediaPlayer.Play(media);

        //Activate(); For some reason it crashes
    }
    public static void LoadMedia(string path)
    {
        _isLoadingTrack = false;
        var media = new MediaFile //TODO this could be prettier
        {
            Path = path,
            FormatTags = new List<string>() { Path.GetExtension(path) }
        };
        media.ParseTitle();
        LoadMedia(media);
    }

    private static void PlayingStarted(object sender, EventArgs e)
    {
        if (!_isLoadingTrack) return;
        PostEvent("track_loaded", new TrackLoadedEvent() //TODO episodes, seasons and such
        {
            FileName = _currentMediaFile.Path,
            Title = _currentMediaFile.Title,
            ShortTitle = _currentMediaFile.GetDisplayTitle(),
        });
        AutoSelectAudioAndSubtitles();
        SendAudioAndSubtitles();

        _isLoadingTrack = false;
    }

    private static void SendAudioAndSubtitles()
    {
        var @event = new AudioAndSubtitlesLoadedEvent()
        {
            AudioTracks = _mediaPlayer.AudioTrackDescription
            .Select(track => MapToVlcTrack(track, track.Id == _mediaPlayer.AudioTrack))
            .ToList(),
            SubtitleTracks = _mediaPlayer.SpuDescription
            .Select(track => MapToVlcTrack(track, track.Id == _mediaPlayer.Spu))
            .ToList(),
        };
        PostEvent("audio_subtitles_loaded", @event);
    }

    private static void AutoSelectAudioAndSubtitles()
    {
        if(_mediaPlayer.AudioTrackCount > 1)
        {
            var englishTracks = _mediaPlayer.AudioTrackDescription.Where(EnglishSelectPredicate).ToList();
            if(englishTracks.Any())
            {
                _mediaPlayer.SetAudioTrack(englishTracks.First().Id);
            }
        }

        if (_mediaPlayer.SpuCount > 1)
        {
            var englishTracks = _mediaPlayer.SpuDescription.Where(EnglishSelectPredicate).ToList();
            if (englishTracks.Any())
            {
                _mediaPlayer.SetSpu(englishTracks.First().Id);
            }
        }
    }

    private static Func<TrackDescription, bool> EnglishSelectPredicate = (track) =>
    {
        var (lang, description) = TrackDescriptionParser.Parse(track.Name);
        if (lang?.ToLower() == "english") return true;

        return false;
    };

    //This is invoked 3-4 times per second
    private static void RecalculateSeekValue(object sender, MediaPlayerTimeChangedEventArgs e)
    {
        PostEvent("current_time", e.Time);
    }

    private static void SendLengthEvent(object sender, MediaPlayerLengthChangedEventArgs e)
    {
        PostEvent("duration_update", e.Length);
    }

    private static void PlaybackEndReached(object sender, EventArgs e)
    {
        PostEvent("playback_ended", null);
    }

    public static void SetMediaInfo(MediaItem item)
    {
        _currentMediaItem = item;
        //TODO set media info
    }

    private static bool PlayNextTitle()
    {
        var nextItem = _currentMediaItem?.GetNextMediaFile();
        if (nextItem is null) return false;

        //Update history
        PipeManager.SendTrackPlayedEvent(new TrackPlayedEvent
        {
            ItemId = _currentMediaItem.Id,
            HistoryItem = new PlaybackHistory
            {
                Filename = nextItem.Path,
                Title = nextItem.Title,
                PlaybackTime = DateTime.Now
            }
        }).ConfigureAwait(false);

        LoadMedia(nextItem);

        return true;
    }

    private static bool SubtitleFileAdd()
    {
        var filePath = SubtitleFileSelect();
        if (string.IsNullOrWhiteSpace(filePath)) return true;

        var currentCount = _mediaPlayer.SpuCount;
        _mediaPlayer.AddSlave(MediaSlaveType.Subtitle, $"file:///{filePath}", true);

        //This could be avoided if 'media_subitem_added' event support was added.
        bool isSubtitleLoaded = false;
        Task.Run(() =>
        {
            for (int i = 0; i < 10; i++)
            {
                Task.Delay(50).Wait();

                if (_mediaPlayer.SpuCount > currentCount)
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

    private static bool SubtitleDelaySet(object data)
    {
        var delay = ((JsonElement)data).GetDouble();
        return ThreadPool.QueueUserWorkItem(_ => _mediaPlayer.SetSpuDelay(Convert.ToInt64(delay * 1000000)));
    }

    private static bool SetPlaybackTimeSkip(object data)
    {
        try
        {
            var time = ((JsonElement)data).GetInt64();
            _mediaPlayer.Time += time * 1000;
            PostEvent("current_time", _mediaPlayer.Time);
        }
        catch (Exception e)
        {
            Debug.WriteLine("Failed to playback skip", e);
            return false;
        }
        return true;
    }

    private static bool SetVolume(object data)
    {
        try
        {
            var volume = ((JsonElement)data).GetDouble();
            ThreadPool.QueueUserWorkItem(_ => _mediaPlayer.Volume = (int)Math.Floor(volume * 100));
        }
        catch (Exception e)
        {
            Debug.WriteLine("Failed to set volume", e);
            return false;
        }
        return true;
    }

    private static bool SetFullscreen(object data)
    {
        var fullscreen = ((JsonElement)data).GetBoolean();
        return ChangeFullscreen(fullscreen);
    }

    private static bool PlayPause(object data)
    {
        try
        {
            var play = ((JsonElement)data).GetBoolean();
            if (play)
            {
                _mediaPlayer.Play();
            }
            else
            {
                _mediaPlayer.Pause();
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine("Failed media player play/pause", e);
            return false;
        }
        return true;
    }

    private static bool SetPlaybackTime(object data)
    {
        try
        {
            var time = (long)Math.Round(double.Parse(((JsonElement)data).GetString()));
            ThreadPool.QueueUserWorkItem(_ => _mediaPlayer.Time = time);
        }
        catch (Exception e)
        {
            Debug.WriteLine("Failed to set playback time", e);
            return false;
        }
        return true;
    }

    private static bool AudioTrackSelected(object data)
    {
        var trackId = ((JsonElement)data).GetInt32();
        return _mediaPlayer.SetAudioTrack(trackId);
    }

    private static bool SubtitleTrackSelected(object data)
    {
        var trackId = ((JsonElement)data).GetInt32();
        return _mediaPlayer.SetSpu(trackId);
    }

    public static void WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = e.WebMessageAsJson;
        var webEvent = JsonSerializer.Deserialize<WebEvent>(message);

        Debug.WriteLine(webEvent.Name);
        var success = webEvent.Name switch
        {
            "play_status_changed" => PlayPause(webEvent.Data),
            "fullscreen" => SetFullscreen(webEvent.Data),
            "set_playback_time" => SetPlaybackTime(webEvent.Data),
            "skip_seconds" => SetPlaybackTimeSkip(webEvent.Data),
            "volume" => SetVolume(webEvent.Data),
            "play_next_title" => PlayNextTitle(),
            "initialized" => PlayerInitialized(),
            "audio_track_selected" => AudioTrackSelected(webEvent.Data),
            "subtitle_track_selected" => SubtitleTrackSelected(webEvent.Data),
            "subtitle_file_select" => SubtitleFileAdd(),
            "subtitle_delay_set" => SubtitleDelaySet(webEvent.Data),
            _ => throw new NotImplementedException()
        };
    }

    #region Mappings
    private static VlcTrack MapToVlcTrack(TrackDescription track, bool isSelected) {
        var (lang, description) = TrackDescriptionParser.Parse(track.Name);
        return new VlcTrack
        {
            Id = track.Id,
            Language = lang,
            Description = description,
            Selected = isSelected
        };
    }
    #endregion

    #region Utility
    public static void Dispose()
    {
        // Unsubscribe from events
        _mediaPlayer.TimeChanged -= RecalculateSeekValue;
        _mediaPlayer.LengthChanged -= SendLengthEvent;
        _mediaPlayer.EndReached -= PlaybackEndReached;

        // Dispose of unmanaged resources
        _mediaPlayer.Dispose();
        _libVLC.Dispose();

        // Set large fields to null
        _mediaPlayer = null;
        _libVLC = null;
    }

    public static MediaPlayer GetMediaPlayer() => _mediaPlayer;

    private static void PostEvent(string eventName, object data)
    {
        var json = JsonSerializer.Serialize(new WebEvent
        {
            Name = eventName,
            Data = data
        }, new JsonSerializerOptions()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        PostWebMessage(json); //TODO fix task was canceled exception. Happends probably on current time update, since it's called a lot
    }
    #endregion
}
