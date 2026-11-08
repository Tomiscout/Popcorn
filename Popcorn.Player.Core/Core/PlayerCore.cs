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
using Popcorn.Application.Services;
using Popcorn.Shared.Ffmpeg;
using Popcorn.Application.Utils;

namespace Popcorn.Player.Core.Core;

public class PlayerCore
{
    private readonly IPlayer _player;
    private readonly MediaItemManager _mediaItemManager;
    private readonly Lazy<WebEventHandler> _webSocket;
    private readonly AudioSubtitlesService _audioSubtitlesService;
    private readonly ThumbnailService _thumbnailService;
    private readonly DownloadPlaybackClient _downloads;
    private readonly MediaItemService _mediaItemService;

    public PlayerCore(IPlayer player, MediaItemManager mediaItemManager, AudioSubtitlesService audioSubtitlesService, ThumbnailService thumbnailService, DownloadPlaybackClient downloads, MediaItemService mediaItemService, Lazy<WebEventHandler> webSocket)
    {
        _mediaItemService = mediaItemService;
        _downloads = downloads;
        _player = player;
        _mediaItemManager = mediaItemManager;
        _webSocket = webSocket;
        _audioSubtitlesService = audioSubtitlesService;
        _thumbnailService = thumbnailService;

        //player.TimeChanged += RecalculateSeekValue;
        //player.LengthChanged += SendLengthEvent;
        _player.MediaEnded += PlaybackEndReached;
        _player.MediaResumed += (_, _) => _webSocket.Value.PostEvent("playback_resumed", null);
        _player.MediaPlaying += PlayingStarted;

        _thumbnailService.Initialize();
        _ = Task.Run(SaveProgressPeriodically);
    }

    public MediaFile? CurrentMediaFile = null;
    public IMediaAnalysis? CurrentMediaProbe = null;

    private List<MediaFile> _inputFilePaths { get; set; }
    private bool _isLoadedFromArgs = false;

    private bool _isLoadingTrack = false;
    /// <summary>Subtitle files found for the loading file, added to mpv once it has loaded it.</summary>
    private List<ExternalSubtitle> _externalSubtitles = [];

    /// <summary>The current file is still downloading and is played from the server's download stream.</summary>
    public bool IsStreamingDownload => _downloadRanges is not null;
    /// <summary>Downloaded parts of the current file, null when it is not a download in progress.</summary>
    private double[][]? _downloadRanges;
    private CancellationTokenSource? _downloadPolling;

    public async Task LoadFileId(Guid fileId)
    {
        _mediaItemManager.LoadMediaInfo(fileId);
        await LoadMedia(fileId);
    }

    /// <summary>Started without a file: the overlay shows how to open one instead of an empty window.</summary>
    public void ShowNoFile() => _webSocket.Value.PostEvent("no_file", null);

    public async Task LoadFilesFromInput(List<string> paths)
    {
        _isLoadedFromArgs = true;
        _inputFilePaths = paths.Select(p => new MediaFile
        {
            Id = Guid.NewGuid(),
            Path = p
        }).ToList();

        // The first file's tags are parsed when it loads (PostTrackLoaded), the others' after it.
        await LoadMedia(_inputFilePaths.First());
        foreach (var file in _inputFilePaths.Where(f => !f.TagsProcessed))
        {
            file.ParseTags();
        }
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
        await SaveProgress();
        _isLoadingTrack = true;
        StopDownloadPolling();

        // A file the built-in downloader is still fetching (may not even exist yet) plays from the server's stream,
        // which fetches the parts mpv reads first and waits for them.
        var download = await _downloads.GetAsync(file.Path);
        var source = file.Path;
        if (download is { Complete: false })
        {
            source = _downloads.StreamUrl(download);
            Log.Information("{Path} is still downloading, playing {Url}", file.Path, source);
        }
        else if (!File.Exists(file.Path))
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

        CurrentMediaProbe = ProbeMetadata(source);
        if (CurrentMediaProbe is null)
        {
            _isLoadingTrack = false;
            return;
        }

        CurrentMediaFile = file;
        // Only for files without subtitles of their own.
        _externalSubtitles = CurrentMediaProbe.SubtitleStreams.Count == 0
            ? ExternalSubtitles.Find(file.Path, _isLoadedFromArgs ? null : _mediaItemManager.CurrentMediaItem?.FolderDirectory)
            : [];
        if (_externalSubtitles.Count > 0)
        {
            Log.Information("Subtitle files for {Path}: {Subtitles}", file.Path, _externalSubtitles.Select(s => s.Path));
        }
        SetDownloadRanges(download is { Complete: false } ? download.Ranges : null);
        if (download is { Complete: false }) StartDownloadPolling(file.Path);

        // Before mpv gets the file (its track lists must come after this, which resets them in the overlay), not
        // only once it plays.
        PostTrackLoaded(file);
        var hasLoaded = await _player.LoadMedia(source, CurrentMediaProbe, _isLoadedFromArgs ? null : file.GetResumePosition());
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

    /// <summary>Next button and end of file: the overlay decides from the plan (play it, or warn about a missing episode).</summary>
    public void PlayNextTitle()
    {
        var file = CurrentMediaFile;
        if (file is null) return;

        // A file outside the library has nothing after it.
        var plan = _mediaItemManager.CurrentMediaItem?.GetNextFilePlan(file.Id) ?? new NextFilePlan { CurrentFileId = file.Id };
        _webSocket.Value.PostEvent("next_file_plan", plan);
    }

    /// <summary>"Refresh files" of the missing episode warning: rescan the item and answer with a new plan.</summary>
    public async Task RefreshFiles()
    {
        var file = CurrentMediaFile;
        if (file is null || file.ItemId == Guid.Empty) return;

        await _mediaItemService.RescanItem(file.ItemId);
        _mediaItemManager.LoadMediaInfo(file.Id);
        OpenFolder(Path.GetDirectoryName(file.Path));
        PlayNextTitle();
    }

    private IMediaAnalysis? ProbeMetadata(string path)
    {
        try
        {
            return Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                ? FFProbe.Analyse(uri)
                : FFProbe.Analyse(path);
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
        OpenFolder(Path.GetDirectoryName(CurrentMediaFile.Path));
        _player.AddExternalSubtitles(_externalSubtitles);
        _audioSubtitlesService.AutoSelectAudioAndSubtitles(selectSubtitle: _externalSubtitles.Count > 0);
        _audioSubtitlesService.SendAudioAndSubtitles();
        _audioSubtitlesService.SendAudioSegments(CurrentMediaFile);

        _isLoadingTrack = false;
    }

    private void PostTrackLoaded(MediaFile file)
    {
        // Files are tagged when scanned; this catches rows stored before that.
        if (!file.TagsProcessed) file.ParseTags();

        _webSocket.Value.PostEvent("track_loaded", new TrackLoadedEvent()
        {
            FileName = file.Path,
            Title = file.Title,
            ShortTitle = file.GetPlayerTitle(_mediaItemManager.CurrentMediaItem),
            Season = file.SeasonNumber,
            Episode = file.EpisodeNumber,
            Chapters = CurrentMediaProbe is { } probe ? MediaInfoBuilder.Chapters(probe) : [],
            MediaInfo = CurrentMediaProbe is { } analysis ? MediaInfoBuilder.Build(analysis, file.Path) : null,
            NextFile = _mediaItemManager.CurrentMediaItem?.GetNextFilePlan(file.Id),
        });
    }

    /// <summary>
    /// The overlay connected again after it got the state of the current file (page reload, DevTools, the Vite dev
    /// server attached to a running player): send that state again.
    /// </summary>
    public void ResendState()
    {
        var file = CurrentMediaFile;
        if (_isLoadingTrack) return;
        if (file is null)
        {
            ShowNoFile();
            return;
        }

        var socket = _webSocket.Value;
        PostTrackLoaded(file);
        socket.PostEvent("duration_update", _player.Duration);
        if (_player.Position is { } position) socket.PostEvent("current_time", position);
        socket.PostEvent("play_state", !_player.IsPaused);
        _audioSubtitlesService.SendAudioAndSubtitles();
        _audioSubtitlesService.SendAudioSegments(file);
        socket.PostEvent("download_ranges", _downloadRanges);
        OpenFolder(Path.GetDirectoryName(file.Path));
    }

    private void SetDownloadRanges(double[][]? ranges)
    {
        _downloadRanges = ranges;
        _webSocket.Value.PostEvent("download_ranges", ranges);
    }

    /// <summary>Sends the downloaded parts of the playing file to the seek bar until it is complete.</summary>
    private void StartDownloadPolling(string path)
    {
        var cts = _downloadPolling = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(cts.Token).ConfigureAwait(false))
            {
                var download = await _downloads.GetAsync(path);
                if (cts.IsCancellationRequested) return;
                // Stays on the stream when it completes (mpv keeps its position); the bar is just full.
                SetDownloadRanges(download is null ? null : download.Complete ? [[0, 1]] : download.Ranges);
                if (download is null or { Complete: true }) return;
            }
        }, cts.Token);
    }

    private void StopDownloadPolling()
    {
        _downloadPolling?.Cancel();
        _downloadPolling = null;
        if (_downloadRanges is not null) SetDownloadRanges(null);
    }

    /// <summary>Where the current library file is (home page "continue watching"), saved every 10 s while it plays.</summary>
    private async Task SaveProgressPeriodically()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        double? lastSaved = null;
        while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
        {
            var position = _player.Position;
            if (position == lastSaved) continue; // paused
            lastSaved = position;
            await SaveProgress(position);
        }
    }

    private async Task SaveProgress(double? position = null)
    {
        var file = CurrentMediaFile;
        position ??= _player.Position;
        var duration = _player.Duration;
        if (_isLoadingTrack || _isLoadedFromArgs || file is null || file.ItemId == Guid.Empty ||
            position is not { } seconds || duration <= 0) return;

        try
        {
            await _mediaItemManager.SavePlaybackProgress(file, seconds, duration);
            // Opening it again in this player starts from here (the item's files were read when it was loaded).
            file.PlaybackPosition = Math.Min(seconds, duration);
            file.Duration = duration;
        }
        catch (Exception e)
        {
            Log.Error(e, "Could not save the playback position of {Path}", file.Path);
        }
    }

    private void PlaybackEndReached(object sender, EventArgs e)
    {
        _ = SaveProgress(_player.Duration);
        _webSocket.Value.PostEvent("playback_ended", null);
        PlayNextTitle();
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
        SaveProgress().GetAwaiter().GetResult();
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
