using LibVLCSharp.Shared;
using System;
using System.Text.Json.Serialization;
using System.Text.Json;
using VideoPlayer.Contracts;
using VideoPlayer.Domain.Extensions;
using VideoBlazor.Domain.Models;
using System.IO;
using System.Collections.Generic;
using Microsoft.Web.WebView2.Core;
using VideoPlayer.Pipes;
using VideoPlayer.Shared.PipeEvents.Server;
using System.Threading;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using System.Linq;
using VideoPlayer.Extensions;
using Serilog;

namespace VideoPlayer.Core;
internal static class PlayerCore
{
    public static MediaFile CurrentMediaFile = null;
    public static Media CurrentMediaInstance = null;

    public static LibVLC LibVLC;
    public static MediaPlayer MediaPlayer;

    public static Action<string> PostWebMessage { get; set; }
    public static Action<string, byte[]> PostStreamMessage { get; set; }
    public static Func<bool> PlayerInitialized { get; set; }
    public static Func<bool, bool> ChangeFullscreen { get; set; }
    private static List<MediaFile> _inputFilePaths { get; set; }

    private static bool _isLoadingTrack = false;

    public static void Initialize()
    {
        LibVLCSharp.Shared.Core.Initialize();

        LibVLC = new LibVLC();
        LibVLC.Log += (sender, e) => Log.Debug($"[LibVLC] {e.FormattedLog}");
        MediaPlayer = new MediaPlayer(LibVLC);
        MediaPlayer.TimeChanged += RecalculateSeekValue;
        MediaPlayer.LengthChanged += SendLengthEvent;
        MediaPlayer.EndReached += PlaybackEndReached;
        MediaPlayer.Playing += PlayingStarted;
        Log.Information("Player core initialized");
    }

    public static void LoadFilesFromInput(List<string> paths)
    {
        _inputFilePaths = paths.Select(p => new MediaFile
        {
            Path = p,
            FormatTags = new List<string>() { Path.GetExtension(p) }
        }).ToList();

        //On initial load initial pipe event will have already happened, so we can load the file from main app
        if (MediaItemManager.CurrentMediaItem != null)
        {
            LoadMedia(MediaItemManager.CurrentMediaItem.Files.First(f => f.Path == _inputFilePaths.First().Path));
            return;
        }

        foreach (var file in _inputFilePaths)
        {
            file.ParseTitle();
        }
        var media = _inputFilePaths.FirstOrDefault();
        LoadMedia(media);
    }

    public static void LoadMedia(MediaFile file)
    {
        _isLoadingTrack = true;
        if (!File.Exists(file.Path))
        {
            _isLoadingTrack = false;
            Log.Error($"File not found. {file.Path}");
            return;
        }
        Log.Warning($"Set of current media file {file.Path}");
        if (file.Id == null || file.Id == Guid.Empty) //TODO HACK
        {
            file.Id = Guid.NewGuid();
        }
        file.Played = true;
        CurrentMediaFile = file;
        CurrentMediaInstance = new Media(LibVLC, file.Path);

        // When playing eventually happends - PlayingStarted event is called
        ThreadPool.QueueUserWorkItem(_ => MediaPlayer.Play(CurrentMediaInstance));

        //Update history in main app
        if (MediaItemManager.CurrentMediaItem is not null)
        {
            PipeManager.SendTrackPlayedEvent(new TrackPlayedEvent
            {
                ItemId = MediaItemManager.CurrentMediaItem.Id,
                HistoryItem = new ItemHistory
                {
                    Filename = file.Path,
                    Title = file.Title,
                    PlaybackTime = DateTime.Now
                }
            }).ConfigureAwait(false);
        }
        //Activate(); For some reason it crashes
    }
    public static bool LoadMedia(string path)
    {
        var mediaFile = MediaItemManager.CurrentMediaItem?.Files.FirstOrDefault(f => f.Path == path) ?? _inputFilePaths?.FirstOrDefault(f => f.Path == path);
        if (mediaFile is null)
        {
            if (_inputFilePaths is null)
            {
                _inputFilePaths = new List<MediaFile>();
                Log.Warning($"Recieved LoadMedia for unloaded file: {path}");
            }
            mediaFile = new MediaFile
            {
                Path = path,
                FormatTags = new List<string>() { Path.GetExtension(path) }
            };
            mediaFile.ParseTitle();
        }

        LoadMedia(mediaFile);
        return true;
    }


    private static void PlayingStarted(object sender, EventArgs e)
    {
        if (!_isLoadingTrack) return;
        PostEvent("track_loaded", new TrackLoadedEvent() //TODO episodes, seasons and such
        {
            FileName = CurrentMediaFile.Path,
            Title = CurrentMediaFile.Title,
            ShortTitle = CurrentMediaFile.GetDisplayTitle(),
        });
        OpenFolder(Path.GetDirectoryName(CurrentMediaFile.Path));
        AudioAndSubtitles.AutoSelectAudioAndSubtitles();
        AudioAndSubtitles.SendAudioAndSubtitles();

        // On initial run from media player due to fast startup dummy media item is created, and will have no data for segments and other extra options
        AudioAndSubtitles.SendAudioSegments();
        _isLoadingTrack = false;
    }

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

    private static bool SetFullscreen(object data)
    {
        var fullscreen = ((JsonElement)data).GetBoolean();
        return ChangeFullscreen(fullscreen);
    }

    private static bool OpenFolder(string path)
    {
        if (MediaItemManager.CurrentMediaItem is null)
        {
            PostEvent("folder_loaded", new FolderLoadedEvent()
            {
                Files = _inputFilePaths.GetFilesForDirectory(path)
            });
            return true;
        }

        MediaItemManager.SendCurrentMediaFolderLoaded(path);
        return true;
    }

    public static void WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = e.WebMessageAsJson;
        var webEvent = JsonSerializer.Deserialize<WebEvent>(message);

        Log.Information(webEvent.Name);
        var _ = webEvent.Name switch
        {
            "play_status_changed" => MediaPlayback.PlayPause(webEvent.Data),
            "fullscreen" => SetFullscreen(webEvent.Data),
            "set_playback_time" => MediaPlayback.SetPlaybackTime(webEvent.Data),
            "skip_seconds" => MediaPlayback.SetPlaybackTimeSkip(webEvent.Data),
            "volume" => AudioAndSubtitles.SetVolume(webEvent.Data),
            "play_next_title" => MediaPlayback.PlayNextTitle(),
            "initialized" => PlayerInitialized(),
            "audio_track_selected" => AudioAndSubtitles.AudioTrackSelected(webEvent.Data),
            "subtitle_track_selected" => AudioAndSubtitles.SubtitleTrackSelected(webEvent.Data),
            "subtitle_file_select" => AudioAndSubtitles.SubtitleFileAdd(),
            "subtitle_delay_set" => AudioAndSubtitles.SubtitleDelaySet(webEvent.Data),
            "file_open" => LoadMedia(webEvent.Data.GetJsonString()),
            "folder_open" => OpenFolder(webEvent.Data.GetJsonString()),
            "folder_open_root" => OpenFolder(MediaItemManager.CurrentMediaItem?.FolderDirectory ?? Path.GetDirectoryName(CurrentMediaFile.Path)),
            "thumb_requested" => ThumbnailManager.OnThumbnailRequest(webEvent.Data.GetJsonInt64()),
            _ => throw new NotImplementedException($"Event from player not supported: {webEvent.Name}")
        };
    }

    #region Events from main program

    internal static void SavePlayerHistory(TrackPlayedEvent e) //Backup save when main client has exited
    {
        Log.Information("Main app is missing, saving progress from player");
        MediaItemManager.SendCurrentMediaFolderLoaded(e.HistoryItem.Filename);
        //TODO save info. Need to reference persistence and pass item folder.
    }
    #endregion

    #region Utility
    public static void Dispose()
    {
        // Unsubscribe from events
        MediaPlayer.TimeChanged -= RecalculateSeekValue;
        MediaPlayer.LengthChanged -= SendLengthEvent;
        MediaPlayer.EndReached -= PlaybackEndReached;

        // Dispose of unmanaged resources
        MediaPlayer.Dispose();
        LibVLC.Dispose();

        // Set large fields to null
        MediaPlayer = null;
        LibVLC = null;
    }

    public static void PostEvent(string eventName, object data)
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
