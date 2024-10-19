using FFMpegCore;
using Popcorn.Domain.Models;
using Popcorn.Player.Core.Player;
using Popcorn.Player.Core.Services;
using Popcorn.Player.Core.WebSocket;
using Popcorn.Player.Core.Contracts;
using PopcornPlayer.Domain.Extensions;
using PopcornPlayer.Shared.PipeEvents.Server;
using Serilog;

namespace Popcorn.Player.Core.Core;

public class PlayerCore
{
    private readonly IPlayer _player;
    private readonly MediaItemManager _mediaItemManager;
    private readonly Lazy<WebEventHandler> _webSocket;
    private readonly AudioSubtitlesService _audioSubtitlesService;
    private readonly ThumbnailService _thumbnailService;

    public PlayerCore(IPlayer player, MediaItemManager mediaItemManager, AudioSubtitlesService audioSubtitlesService, ThumbnailService thumbnailService, Lazy<WebEventHandler> webSocket)
    {
        _player = player;
        _mediaItemManager = mediaItemManager;
        _webSocket = webSocket;
        _audioSubtitlesService = audioSubtitlesService;
        _thumbnailService = thumbnailService;

        //player.TimeChanged += RecalculateSeekValue;
        //player.LengthChanged += SendLengthEvent;
        _player.MediaEnded += PlaybackEndReached;
        _player.MediaPlaying += PlayingStarted;

        _thumbnailService.Initialize();
    }

    public MediaFile CurrentMediaFile = null;
    public IMediaAnalysis CurrentMediaProbe = null;

    private List<MediaFile> _inputFilePaths { get; set; }
    private bool _isLoadedFromArgs = false;

    private bool _isLoadingTrack = false;

    public void LoadFileId(Guid fileId)
    {
        _mediaItemManager.LoadMediaInfo(fileId);
        var file = _mediaItemManager.CurrentMediaItem.Files.First(f => f.Id == fileId);

        LoadMedia(file);
    }

    public void LoadFilesFromInput(List<string> paths)
    {
        _isLoadedFromArgs = true;
        _inputFilePaths = paths.Select(p => new MediaFile
        {
            Path = p,
            FormatTags = new List<string>() { Path.GetExtension(p) }
        }).ToList();

        foreach (var file in _inputFilePaths)
        {
            file.ParseTitle();
        }
        var media = _inputFilePaths.FirstOrDefault();
        LoadMedia(media);
    }

    public void LoadMedia(MediaFile file)
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
        if (CurrentMediaProbe is null)
        {
            return;
        }

        file.Played = true;
        CurrentMediaFile = file;

        _player.LoadMedia(file.Path);

        //Sending media info to frontend
        _webSocket.Value.PostEvent("track_loaded", new TrackLoadedEvent() //TODO episodes, seasons and such
        {
            FileName = CurrentMediaFile.Path,
            Title = CurrentMediaFile.Title,
            ShortTitle = CurrentMediaFile.GetDisplayTitle(),
        });
        OpenFolder(Path.GetDirectoryName(CurrentMediaFile.Path));

        // On initial run from media player due to fast startup dummy media item is created, and will have no data for segments and other extra options

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
    public bool LoadMedia(string path)
    {
        var mediaFile = _mediaItemManager.CurrentMediaItem?.Files.FirstOrDefault(f => f.Path == path) ?? _inputFilePaths?.FirstOrDefault(f => f.Path == path);
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

    public bool PlayNextTitle()
    {
        var nextItem = _mediaItemManager.CurrentMediaItem?.GetNextMediaFile();
        if (nextItem is null) return false;

        LoadMedia(nextItem);

        return true;
    }

    private IMediaAnalysis ProbeMetadata(string path)
    {
        try
        {
            return FFProbe.Analyse(path);
        }
        catch (Exception e)
        {
            Log.Error(e, "Couldn't parse media file: {path}", path);
            return null;
        }
    }


    private void PlayingStarted(object sender, EventArgs e)
    {
        if (!_isLoadingTrack) return;
        _webSocket.Value.PostEvent("track_loaded", new TrackLoadedEvent() //TODO episodes, seasons and such
        {
            FileName = CurrentMediaFile.Path,
            Title = CurrentMediaFile.Title,
            ShortTitle = CurrentMediaFile.GetDisplayTitle(),
        });
        OpenFolder(Path.GetDirectoryName(CurrentMediaFile.Path));
        _audioSubtitlesService.AutoSelectAudioAndSubtitles();
        _audioSubtitlesService.SendAudioAndSubtitles();

        // On initial run from media player due to fast startup dummy media item is created, and will have no data for segments and other extra options
        _audioSubtitlesService.SendAudioSegments(CurrentMediaFile);
        _isLoadingTrack = false;
    }

    private void PlaybackEndReached(object sender, EventArgs e)
    {
        _webSocket.Value.PostEvent("playback_ended", null);
    }

    public bool OpenFolder(string path)
    {
        if (_mediaItemManager.CurrentMediaItem is null)
        {
            _webSocket.Value.PostEvent("folder_loaded", new FolderLoadedEvent()
            {
                Files = _isLoadedFromArgs ? _inputFilePaths : _inputFilePaths.GetFilesForDirectory(path)
            });
            return true;
        }

        _mediaItemManager.SendCurrentMediaFolderLoaded(path);
        return true;
    }

    #region Events from main program

    internal void SavePlayerHistory(TrackPlayedEvent e) //Backup save when main client has exited
    {
        Log.Information("Main app is missing, saving progress from player");
        _mediaItemManager.SendCurrentMediaFolderLoaded(e.HistoryItem.Filename);
        //TODO save info. Need to reference persistence and pass item folder.
    }
    #endregion

    #region Utility
    public void Dispose()
    {
        if (_thumbnailService.FfmpegCts?.IsCancellationRequested == false)
        {
            // Not tested :)
            _thumbnailService.FfmpegCts?.Cancel();
            Thread.Sleep(200);
        }
        _thumbnailService.FfmpegCts?.Dispose();

        _player.Dispose();
    }

    #endregion
}
