using FFMpegCore;
using Popcorn.Application.Extensions;
using Popcorn.Domain.Models;
using Popcorn.Player.Core.Player;
using Popcorn.Player.Core.Services;
using Popcorn.Player.Core.WebSocket;
using Popcorn.Player.Core.Contracts;
using PopcornPlayer.Shared.PipeEvents.Server;
using Serilog;
using Popcorn.Domain;

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

    public MediaFile? CurrentMediaFile = null;
    public IMediaAnalysis? CurrentMediaProbe = null;

    private List<MediaFile> _inputFilePaths { get; set; }
    private bool _isLoadedFromArgs = false;

    private bool _isLoadingTrack = false;

    public async Task LoadFileId(Guid fileId)
    {
        _mediaItemManager.LoadMediaInfo(fileId);
        await LoadMedia(fileId);
    }

    public async Task LoadFilesFromInput(List<string> paths)
    {
        _isLoadedFromArgs = true;
        _inputFilePaths = paths.Select(p => new MediaFile
        {
            Id = Guid.NewGuid(),
            Path = p,
            FormatTags = new List<string>() { Path.GetExtension(p) }
        }).ToList();

        foreach (var file in _inputFilePaths)
        {
            file.ParseTitle();
        }
        var media = _inputFilePaths.FirstOrDefault();
        await LoadMedia(media);
    }

    public Task LoadMedia(Guid fileId)
    {
        var mediaFile = _mediaItemManager.CurrentMediaItem?.Files.FirstOrDefault(f => f.Id == fileId) ?? _inputFilePaths?.FirstOrDefault(f => f.Id == fileId);
        if (mediaFile != null) return LoadMedia(mediaFile);
        
        Log.Warning("File {FileId} not found", fileId);
        return Task.CompletedTask;
    }
    
    public async Task LoadMedia(MediaFile file)
    {
        _isLoadingTrack = true;
        if (!File.Exists(file.Path))
        {
            _isLoadingTrack = false;
            Log.Error($"File not found. {file.Path}");
            return;
        }
        
        file.Played = true;

        Log.Warning($"Set of current media file {file.Path}");
        if (!_isLoadedFromArgs)
        {
            await _mediaItemManager.UpdatePlayedFile(file);
        }

        CurrentMediaProbe = ProbeMetadata(file.Path);
        if (CurrentMediaProbe is null)
        {
            return;
        }

        CurrentMediaFile = file;

        var hasLoaded = await _player.LoadMedia(file.Path, CurrentMediaProbe);
        if(!hasLoaded)
        {
            CurrentMediaProbe = null;
            CurrentMediaFile = null;
            _player.Stop();
            _webSocket.Value.PostEvent("playback_reset", null);
            return;
        }

        OpenFolder(Path.GetDirectoryName(CurrentMediaFile.Path));
    }

    public bool PlayNextTitle()
    {
        var nextItem = _mediaItemManager.CurrentMediaItem?.GetNextMediaFile();
        if (nextItem is null) return false;

        LoadMedia(nextItem);

        return true;
    }

    private IMediaAnalysis? ProbeMetadata(string path)
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
        _audioSubtitlesService.SendAudioSegments(CurrentMediaFile);

        _isLoadingTrack = false;
    }

    private void PlaybackEndReached(object sender, EventArgs e)
    {
        _webSocket.Value.PostEvent("playback_ended", null);
    }

    public bool OpenFolder(string? path)
    {
        if(string.IsNullOrWhiteSpace(path)) return false;

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
    
    public async Task HandleFileDrop(string localPath)
    {
        if(string.IsNullOrWhiteSpace(localPath)) return;

        foreach(var format in MediaLoader.VideoFormats)
        {
            if (localPath.EndsWith(format))
            { 
                await LoadFilesFromInput([ localPath ]);
                return;
            }
        }
        
        foreach(var format in MediaLoader.SubtitleFormats)
        {
            if (localPath.EndsWith(format))
            {
                _player.AddSubtitleTrack(localPath);
                return;
            }
        }
    }

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
