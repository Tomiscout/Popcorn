using FFMpegCore;
using Popcorn.Application.Extensions;
using Popcorn.Domain.Models;
using Popcorn.Player.Core.Player;
using Popcorn.Player.Core.Services;
using Popcorn.Player.Core.WebSocket;
using Popcorn.Player.Core.Contracts;
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
    private readonly ServerClient _downloads;
    private readonly MediaItemService _mediaItemService;

    public PlayerCore(IPlayer player, MediaItemManager mediaItemManager, AudioSubtitlesService audioSubtitlesService, ThumbnailService thumbnailService, ServerClient downloads, MediaItemService mediaItemService, Lazy<WebEventHandler> webSocket)
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
    private CancellationTokenSource? _downloadWatch;

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
    
    /// <summary>
    /// One load at a time (file_open runs on the overlay socket, the media controls' previous on D-Bus): two
    /// interleaved loads stop each other's file in mpv. Of requests waiting meanwhile only the newest runs.
    /// </summary>
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private int _loadRequest;

    /// <summary>The end of the current file asked for a next file that is not loading yet.</summary>
    private volatile bool _nextFileRequested;

    public async Task LoadMedia(MediaFile file)
    {
        var request = Interlocked.Increment(ref _loadRequest);
        await _loadGate.WaitAsync();
        try
        {
            if (request != Volatile.Read(ref _loadRequest)) return;
            await LoadMediaCore(file);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private async Task LoadMediaCore(MediaFile file)
    {
        await SaveProgress();
        _isLoadingTrack = true;
        _nextFileRequested = false;
        StopDownloadWatch();

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
            ? ExternalSubtitles.Find(file.Path, _isLoadedFromArgs ? null : _mediaItemManager.CurrentMediaItem?.RootFolderOf(file))
            : [];
        if (_externalSubtitles.Count > 0)
        {
            Log.Information("Subtitle files for {Path}: {Subtitles}", file.Path, _externalSubtitles.Select(s => s.Path));
        }
        SetDownloadRanges(download is { Complete: false } ? download.Ranges : null);
        if (download is { Complete: false }) StartDownloadWatch(file.Path);

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

        OpenFolder(FolderOf(CurrentMediaFile));
    }

    /// <summary>Next button and end of file: the overlay decides from the plan (play it, or warn about a missing episode).</summary>
    public NextFilePlan? PlayNextTitle()
    {
        var file = CurrentMediaFile;
        if (file is null) return null;

        // A file outside the library has nothing after it.
        var plan = _mediaItemManager.CurrentMediaItem?.GetNextFilePlan(file.Id) ?? new NextFilePlan { CurrentFileId = file.Id };
        _webSocket.Value.PostEvent("next_file_plan", plan);
        return plan;
    }

    /// <summary>"Refresh files" of the missing episode warning: rescan the item and answer with a new plan.</summary>
    public async Task RefreshFiles()
    {
        var file = CurrentMediaFile;
        if (file is null || file.ItemId == Guid.Empty) return;

        await _mediaItemService.RescanItem(file.ItemId);
        _mediaItemManager.LoadMediaInfo(file.Id);
        OpenFolder(FolderOf(file));
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
        OpenFolder(FolderOf(CurrentMediaFile));
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
        OpenFolder(FolderOf(file));
    }

    private void SetDownloadRanges(double[][]? ranges)
    {
        _downloadRanges = ranges;
        _webSocket.Value.PostEvent("download_ranges", ranges);
    }

    /// <summary>Sends the downloaded parts of the playing file to the seek bar as they arrive, until it is complete.</summary>
    private void StartDownloadWatch(string path)
    {
        var cts = _downloadWatch = new CancellationTokenSource();
        // Stays on the stream when it completes (mpv keeps its position); the bar is just full.
        _ = Task.Run(() => _downloads.WatchAsync(path, download =>
        {
            if (!cts.IsCancellationRequested)
                SetDownloadRanges(download is null ? null : download.Complete ? [[0, 1]] : download.Ranges);
        }, cts.Token));
    }

    private void StopDownloadWatch()
    {
        _downloadWatch?.Cancel();
        _downloadWatch = null;
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
        // eof-reached can flip true -> false -> true at one end: a seek to the very end (skipped outro tail) makes
        // mpv seek back to the last frame. A second end must not ask for the next file again (two loads of it), nor
        // mark the next file, whose track_loaded may already be out, as ended.
        if (_nextFileRequested || _isLoadingTrack) return;
        _ = SaveProgress(_player.Duration);
        _webSocket.Value.PostEvent("playback_ended", null);
        // The overlay plays it (next-file.ts); with an episode missing it shows the warning instead.
        if (PlayNextTitle() is { NextFileId: not null, MissingLabel: null }) _nextFileRequested = true;
    }

    /// <summary>
    /// Folder of the file in its item's tree (a virtual folder when the item has several sources, <c>MediaTree</c>),
    /// else on disk.
    /// </summary>
    private string? FolderOf(MediaFile file)
    {
        var inItem = _mediaItemManager.CurrentMediaItem?.Files?.FirstOrDefault(f => f.Id == file.Id) ?? file;
        return Path.GetDirectoryName(inItem.Tree());
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
