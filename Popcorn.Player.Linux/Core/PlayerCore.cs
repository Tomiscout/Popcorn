using System.Text.Json;
using System.Text.Json.Serialization;
using FFMpegCore;
using Popcorn.Domain.Models;
using Popcorn.Player.Linux.Contracts;
using Popcorn.Player.Linux.Player;
using Popcorn.Player.Linux.Services;
using Popcorn.Player.Linux.WebSocket;
using PopcornPlayer.Domain.Extensions;
using PopcornPlayer.Shared.PipeEvents.Server;
using Serilog;

namespace Popcorn.Player.Linux.Core;

//using PopcornPlayer.Pipes;

internal static class PlayerCore
{
    public static MediaFile CurrentMediaFile = null;
    public static IMediaAnalysis CurrentMediaProbe = null;

    public static IPlayer Player;

    public static Action<string, byte[]> PostStreamMessage { get; set; }
    public static Func<bool> PlayerInitialized { get; set; }
    private static List<MediaFile> _inputFilePaths { get; set; }
    private static bool _isLoadedFromArgs = false;

    private static bool _isLoadingTrack = false;

    public static void Initialize()
    {
        Player = Program.GetPlayer();

        //player.TimeChanged += RecalculateSeekValue;
        //player.LengthChanged += SendLengthEvent;
        Player.MediaEnded += PlaybackEndReached;
        Player.MediaPlaying += PlayingStarted;

        ThumbnailService.Initialize();
        Log.Information("Player core initialized");
    }

    public static void LoadFilesFromInput(List<string> paths)
    {
        _isLoadedFromArgs = true;
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

        CurrentMediaProbe = ProbeMetadata(file.Path);
        if(CurrentMediaProbe is null)
        {
            return;
        }

        file.Played = true;
        CurrentMediaFile = file;
        
        Player.LoadMedia(file.Path);
        
        //Sending media info to frontend
        PlayerWebSocket.PostEvent("track_loaded", new TrackLoadedEvent() //TODO episodes, seasons and such
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
        
        //Update history in main app
        // if (MediaItemManager.CurrentMediaItem is not null)
        // {
        //     PipeManager.SendTrackPlayedEvent(new TrackPlayedEvent
        //     {
        //         ItemId = MediaItemManager.CurrentMediaItem.Id,
        //         HistoryItem = new ItemHistory
        //         {
        //             ItemId = MediaItemManager.CurrentMediaItem.Id,
        //             Filename = file.Path,
        //             FileId = file.Id.Value,
        //             Title = file.Title,
        //             PlaybackTime = DateTime.Now
        //         }
        //     }).ConfigureAwait(false);
        // }
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
    
    public static bool PlayNextTitle()
    {
        var nextItem = MediaItemManager.CurrentMediaItem?.GetNextMediaFile();
        if (nextItem is null) return false;

        PlayerCore.LoadMedia(nextItem);

        return true;
    }

    private static IMediaAnalysis ProbeMetadata(string path)
    {
        try
        {
            return FFProbe.Analyse(path);
        } catch (Exception e)
        {
            Log.Error(e, "Couldn't parse media file: {path}", path);
            return null;
        }
    }


    private static void PlayingStarted(object sender, EventArgs e)
    {
        if (!_isLoadingTrack) return;
        PlayerWebSocket.PostEvent("track_loaded", new TrackLoadedEvent() //TODO episodes, seasons and such
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

    private static void PlaybackEndReached(object sender, EventArgs e)
    {
        PlayerWebSocket.PostEvent("playback_ended", null);
    }

    public static bool OpenFolder(string path)
    {
        if (MediaItemManager.CurrentMediaItem is null)
        {
            PlayerWebSocket.PostEvent("folder_loaded", new FolderLoadedEvent()
            {
                Files = _isLoadedFromArgs ? _inputFilePaths : _inputFilePaths.GetFilesForDirectory(path)
            });
            return true;
        }

        MediaItemManager.SendCurrentMediaFolderLoaded(path);
        return true;
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
        if(ThumbnailService.FfmpegCts?.IsCancellationRequested == false)
        {
            // Not tested :)
            ThumbnailService.FfmpegCts?.Cancel();
            Thread.Sleep(200);
        }
        ThumbnailService.FfmpegCts?.Dispose();

        Program.GetPlayer().Dispose();
    }

    #endregion
}
