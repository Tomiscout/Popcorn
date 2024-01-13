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
using LibVLCSharp.Shared.Structures;
using VideoPlayer.Utils;
using System.Threading.Tasks;
using VideoPlayer.Extensions;
using Serilog;
using VideoPlayer.Services;

namespace VideoPlayer;
internal static class PlayerCore
{
    private static LibVLC _libVLC;
    private static MediaPlayer _mediaPlayer;

    public static Action<string> PostWebMessage { get; set; }
    public static Action<string, byte[]> PostStreamMessage { get; set; }
    public static Func<string> SubtitleFileSelect { get; set; }
    public static Func<bool> PlayerInitialized { get; set; }
    public static Func<bool, bool> ChangeFullscreen { get; set; }

    private static List<MediaFile> _inputFilePaths { get; set; }
    private static MediaItem _currentMediaItem = null;
    private static MediaFile _currentMediaFile = null;
    private static bool _isLoadingTrack = false;

    public static void Initialize()
    {
        Core.Initialize();

        _libVLC = new LibVLC();
        _libVLC.Log += (object sender, LogEventArgs e) => Log.Debug($"[LibVLC] {e.FormattedLog}");
        _mediaPlayer = new MediaPlayer(_libVLC);
        _mediaPlayer.TimeChanged += RecalculateSeekValue;
        _mediaPlayer.LengthChanged += SendLengthEvent;
        _mediaPlayer.EndReached += PlaybackEndReached;
        _mediaPlayer.Playing += PlayingStarted;
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
        if(_currentMediaItem != null)
        {
            LoadMedia(_currentMediaItem.Files.First(f => f.Path == _inputFilePaths.First().Path));
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
        _currentMediaFile = file;
        Log.Warning($"Set of current media file {file.Path}");
        if(_currentMediaFile.Id == null || _currentMediaFile.Id == Guid.Empty) //TODO HACK
        {
            _currentMediaFile.Id = Guid.NewGuid();
        }
        _currentMediaFile.Played = true;
        var media = new Media(_libVLC, file.Path);

        // When playing eventually happends - PlayingStarted event is called
        ThreadPool.QueueUserWorkItem(_ => _mediaPlayer.Play(media));

        //Update history in main app
        if (_currentMediaItem is not null)
        {
            PipeManager.SendTrackPlayedEvent(new TrackPlayedEvent
            {
                ItemId = _currentMediaItem.Id,
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
        var mediaFile = _currentMediaItem?.Files.FirstOrDefault(f => f.Path == path) ?? _inputFilePaths?.FirstOrDefault(f => f.Path == path);
        if(mediaFile is null)
        {
            if(_inputFilePaths is null)
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
            FileName = _currentMediaFile.Path,
            Title = _currentMediaFile.Title,
            ShortTitle = _currentMediaFile.GetDisplayTitle(),
        });
        OpenFolder(Path.GetDirectoryName(_currentMediaFile.Path));
        AutoSelectAudioAndSubtitles();
        SendAudioAndSubtitles();

        // On initial run from media player due to fast startup dummy media item is created, and will have no data for segments and other extra options
        SendAudioSegments(); 
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

    private static void SendAudioSegments()
    {
        Log.Information($"Sending segments of file: {JsonSerializer.Serialize(_currentMediaFile)}");
        if (_currentMediaFile is null)
        {
            Log.Warning("Trying to send audio segments of null media file");
            return;
        }

        var @event = new AudioSegmentsEvent()
        {
            Intro = _currentMediaFile.IntroTimes,
            Outro = _currentMediaFile.OutroTimes
        };
        PostEvent("segments_update", @event);
    }

    private static void AutoSelectAudioAndSubtitles()
    {
        // TODO default japaneese preference for anime
        if(_mediaPlayer.AudioTrackCount > 1)
        {
            var englishTracks = _mediaPlayer.AudioTrackDescription.Where(EnglishSelectPredicate).ToList();
            if(englishTracks.Any())
            {
                ThreadPool.QueueUserWorkItem(_ => _mediaPlayer.SetAudioTrack(englishTracks.First().Id));
            }
        }

        if (_mediaPlayer.SpuCount > 1)
        {
            var englishTracks = _mediaPlayer.SpuDescription.Where(EnglishSelectPredicate).ToList();
            if (englishTracks.Any())
            {
                ThreadPool.QueueUserWorkItem(_ => _mediaPlayer.SetSpu(englishTracks.First().Id));
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

    private static bool PlayNextTitle()
    {
        var nextItem = _currentMediaItem?.GetNextMediaFile();
        if (nextItem is null) return false;

        LoadMedia(nextItem);

        return true;
    }

    private static bool SubtitleFileAdd()
    {
        var filePath = SubtitleFileSelect();
        if (string.IsNullOrWhiteSpace(filePath)) return true;

        var currentCount = _mediaPlayer.SpuCount;
        ThreadPool.QueueUserWorkItem(_ => _mediaPlayer.AddSlave(MediaSlaveType.Subtitle, $"file:///{filePath}", true));

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
            ThreadPool.QueueUserWorkItem(_ => {
                _mediaPlayer.Time += time * 1000;
                PostEvent("current_time", _mediaPlayer.Time);
            });
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to playback skip");
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
            Log.Error(e, "Failed to set volume");
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
                ThreadPool.QueueUserWorkItem(_ => _mediaPlayer.Play());
            }
            else
            {
                ThreadPool.QueueUserWorkItem(_ => _mediaPlayer.Pause());
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed media player play/pause");
            return false;
        }
        return true;
    }

    private static bool SetPlaybackTime(object data)
    {
        try
        {
            var time = data.GetJsonInt64();
            ThreadPool.QueueUserWorkItem(_ => _mediaPlayer.Time = time);
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to set playback time");
            return false;
        }
        return true;
    }

    private static bool AudioTrackSelected(object data)
    {
        var trackId = ((JsonElement)data).GetInt32();
        return _mediaPlayer.SetAudioTrack(trackId); //TODO threadpool
    }

    private static bool SubtitleTrackSelected(object data)
    {
        var trackId = ((JsonElement)data).GetInt32();
        return _mediaPlayer.SetSpu(trackId); //TODO threadpool
    }

    private static bool OpenFolder(string path)
    {
        if(_currentMediaItem is null)
        {
            PostEvent("folder_loaded", new FolderLoadedEvent()
            {
                Files = _inputFilePaths.GetFilesForDirectory(path)
            });
            return true;
        }

        SendCurrentMediaFolderLoaded(path);
        return true;
    }

    private static void SendCurrentMediaFolderLoaded(string path)
    {
        var @event = new FolderLoadedEvent
        {
            Navigation = _currentMediaItem.BuildNavigationPath(path),
            Files = _currentMediaItem.Files.GetFilesForDirectory(path),
        };
        _currentMediaItem.RecalculatePlayedFiles(@event.Files);
        PostEvent("folder_loaded", @event);
    }

    #region Thumbnail handling

    private static CancellationTokenSource _ffmpegCts;
    public static bool OnThumbnailRequest(long time)
    {
        _ffmpegCts?.Cancel();
        _ffmpegCts = new CancellationTokenSource();

        Task.Run(async () => {
            try
            {
                var bytes = await GenerateThumbnailAsync(time, _ffmpegCts.Token);
                if(bytes is not null)
                {
                    PostStreamMessage($"{{\"name\":\"thumb\",\"time\":{time}}}", bytes);
                }
            }
            catch (OperationCanceledException)
            {
                // Nothing
            }
            catch (Exception ex)
            {
                // Log exception if needed
                Log.Error(ex, "Failed to generate thumbnail {time}", time);
            }
            finally
            {
                _ffmpegCts?.Dispose();
                _ffmpegCts = null;
            }
        });
        return true;
    }

    public static async Task<byte[]> GenerateThumbnailAsync(long time, CancellationToken ct)
    {
        var codec = _mediaPlayer.Media.Tracks.FirstOrDefault(t => t.TrackType == TrackType.Video).Codec;
        return await ThumbnailService.GetThumbnailBytes(time, codec, _currentMediaFile.Path, _currentMediaFile.Id.ToString(), ct);
    }
    #endregion

    public static void WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = e.WebMessageAsJson;
        var webEvent = JsonSerializer.Deserialize<WebEvent>(message);

        Log.Information(webEvent.Name);
        var _ = webEvent.Name switch
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
            "file_open" => LoadMedia(webEvent.Data.GetJsonString()),
            "folder_open" => OpenFolder(webEvent.Data.GetJsonString()),
            "folder_open_root" => OpenFolder(_currentMediaItem?.FolderDirectory ?? Path.GetDirectoryName(_currentMediaFile.Path)),
            "thumb_requested" => OnThumbnailRequest(webEvent.Data.GetJsonInt64()),
            _ => throw new NotImplementedException($"Event from player not supported: {webEvent.Name}")
        };
    }

    #region Events from main program

    public static void SetMediaInfo(MediaItem item)
    {
        _currentMediaItem = item;
        _currentMediaItem.RecalculatePlayedFiles(_currentMediaItem.Files);

        // Update current item if it already playing
        if(_currentMediaFile != null)
        {
            _currentMediaFile = _currentMediaItem.Files.First(f => f.Path == _currentMediaFile.Path);
            SendAudioSegments();
        }
    }

    public static void UpdateItemHistory(List<ItemHistory> history) //From main client
    {
        _currentMediaItem.History = history;
        var lastPlayed = _currentMediaItem.History.LastOrDefault();
        _currentMediaItem.RecalculatePlayedFiles(_currentMediaItem.Files.Where(f => f.Path == lastPlayed.Filename).ToList());
    }

    internal static void SavePlayerHistory(TrackPlayedEvent e) //Backup save when main client has exited
    {
        Log.Information("Main app is missing, saving progress from player");
        SendCurrentMediaFolderLoaded(e.HistoryItem.Filename);
        //TODO save info. Need to reference persistence and pass item folder.
    }
    #endregion

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
