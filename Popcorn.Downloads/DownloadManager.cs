using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MonoTorrent;
using MonoTorrent.Client;
using Popcorn.Application.Services;
using Popcorn.Domain.Enum;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Persistence;
using Popcorn.Shared.Utils;

namespace Popcorn.Downloads;

/// <summary>
/// The built-in torrent client + direct downloader: owns the MonoTorrent engine and the download list, applies
/// Settings → Downloads (folder, speed limits, seed limits), adds finished metadata to the library and serves files
/// that are still downloading to the players (<see cref="OpenStream"/>).
/// </summary>
public sealed class DownloadManager(
    ILogger<DownloadManager> logger,
    SettingsRepository settingsRepository,
    MediaItemService mediaItemService,
    MediaImageService mediaImageService,
    IMediaItemRepository mediaItemRepository,
    IMediaFileRepository mediaFileRepository) : IHostedService
{
    private const long MaxTorrentFileBytes = 16 * 1024 * 1024;
    /// <summary>A download one priority below the highest downloading one gets this share of their rate (squared for two below, ...).</summary>
    private const double PriorityShare = 0.25;
    /// <summary>Lower priority downloads are never throttled below this (bytes/s).</summary>
    private const long MinThrottle = 64 * 1024;

    private readonly DownloadStore _store = new();
    private readonly List<Download> _downloads = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<(Guid Id, int Index), MediaTimeline?> _timelines = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly RateLimiter _directLimiter = new();
    private readonly HashSet<Guid> _linkAttempted = [];
    private readonly Dictionary<Guid, (DateTime At, string? Title, Dictionary<string, Guid> Files)> _items = [];
    private readonly HttpClient _http = CreateHttpClient();
    private readonly CancellationTokenSource _stopping = new();
    private ClientEngine? _engine;
    private DownloadSettings _settings = new();
    /// <summary>Torrents and direct downloads are both downloading: they share the download limit.</summary>
    private bool _shareLimit;
    private Task? _loop;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Popcorn/1.0");
        return client;
    }

    private ClientEngine Engine => _engine ?? throw new InvalidOperationException("Downloads are not started");

    /// <summary>
    /// Something may have changed: raised after every tick (rates, progress) and after each add / pause / resume /
    /// priority change / removal. Listeners (the server's pushes) compare what they send themselves.
    /// </summary>
    public event Action? Changed;

    private void OnChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Download change listener failed");
        }
    }

    #region Lifetime

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Restoring torrents (hash checks, metadata) must not hold up the server start.
        _ = Task.Run(InitializeAsync, CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task InitializeAsync()
    {
        try
        {
            _settings = (await settingsRepository.GetSettingsAsync()).Downloads ?? new DownloadSettings();
            _engine = new ClientEngine(BuildEngineSettings());
            _directLimiter.BytesPerSecond = SplitDownloadLimit().Direct;
            foreach (var record in _store.Load())
            {
                try
                {
                    var download = await CreateAsync(record);
                    _downloads.Add(download);
                    if (!record.Paused && !record.Finished) await download.StartAsync();
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Could not restore download {Name} ({Source})", record.Name, record.Source);
                }
            }
            logger.LogInformation("Downloads ready: {Count} restored", _downloads.Count);
            _ready.SetResult();
            _loop = Task.Run(() => LoopAsync(_stopping.Token));
        }
        catch (Exception e)
        {
            logger.LogError(e, "Download client failed to start");
            _ready.TrySetException(e);
        }
    }

    private bool _stopped;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        if (!_ready.Task.IsCompletedSuccessfully) return;
        if (_loop is not null) await _loop.ContinueWith(_ => { });

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // The host calls this again when its start failed after this service started.
            if (_stopped) return;
            _stopped = true;
            foreach (var direct in _downloads.OfType<DirectDownload>())
            {
                var paused = direct.Record.Paused;
                await direct.RemoveAsync(); // stops without marking it paused
                direct.Record.Paused = paused;
            }
            await Task.WhenAll(_downloads.OfType<TorrentDownload>().Select(t => t.Settled));
            // Writes fast resume data, so nothing is hashed again on the next start.
            await Engine.StopAllAsync(TimeSpan.FromSeconds(5));
            foreach (var download in _downloads) await download.TickAsync(TimeSpan.Zero);
            await SaveAsync();
            Engine.Dispose();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var lastTick = DateTime.UtcNow;
        var ticks = 0;
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            var now = DateTime.UtcNow;
            var elapsed = now - lastTick;
            lastTick = now;
            try
            {
                await TickAsync(elapsed);
                if (++ticks % 10 == 0) await SaveAsync();
                OnChanged();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Download tick failed");
            }
        }
    }

    private async Task TickAsync(TimeSpan elapsed)
    {
        List<Download> link = [];
        await _gate.WaitAsync();
        try
        {
            foreach (var download in _downloads)
            {
                await download.TickAsync(elapsed);

                if (download is TorrentDownload torrent && SeedLimitReached(torrent))
                {
                    logger.LogInformation("Seed limit reached for {Name}, stopping it", torrent.Record.Name);
                    await torrent.FinishAsync();
                    await SaveAsync();
                }

                if (download.Record is { AddToLibrary: true, MediaItemId: null } && download.Files.Count > 0 && _linkAttempted.Add(download.Id))
                    link.Add(download);
            }

            await ApplyPrioritiesAsync();

            var shareLimit = _downloads.OfType<DirectDownload>().Any(d => d.IsActive)
                && _downloads.OfType<TorrentDownload>().Any(t => t.State is DownloadState.Metadata or DownloadState.Hashing or DownloadState.Downloading);
            if (shareLimit != _shareLimit)
            {
                _shareLimit = shareLimit;
                await ApplySpeedLimitsAsync();
            }
        }
        finally
        {
            _gate.Release();
        }

        foreach (var download in link) await LinkToLibraryAsync(download);
    }

    /// <summary>
    /// Download priorities: while downloads of different priorities download, the highest ones run free and the
    /// others are capped to a share of their rate, so they only take what the high ones leave. Streaming ranks above
    /// <see cref="DownloadPriority.High"/>.
    /// </summary>
    private async Task ApplyPrioritiesAsync()
    {
        static int Rank(Download d) => d.StreamingFile is not null ? (int)DownloadPriority.High + 1 : (int)d.Record.Priority;
        var downloading = _downloads.Where(d => d.State == DownloadState.Downloading).ToList();
        var top = downloading.Count == 0 ? 0 : downloading.Max(Rank);
        var topRate = downloading.Where(d => Rank(d) == top).Sum(d => d.DownloadRate);
        foreach (var download in _downloads)
        {
            var below = top - Rank(download);
            var cap = below > 0 && downloading.Contains(download)
                ? Math.Max(MinThrottle, (long)(topRate * Math.Pow(PriorityShare, below)))
                : 0;
            await download.SetThrottleAsync(cap);
        }
    }

    private bool SeedLimitReached(TorrentDownload torrent)
    {
        if (torrent.State != DownloadState.Seeding || torrent.Record.IgnoreSeedLimit) return false;
        var ratioLimit = _settings.SeedRatioLimit;
        var minutes = _settings.SeedTimeLimitMinutes;
        if (ratioLimit <= 0 && minutes <= 0) return true;
        var ratio = torrent.Size > 0 ? (double)torrent.Uploaded / torrent.Size : 0;
        return (ratioLimit > 0 && ratio >= ratioLimit) || (minutes > 0 && torrent.Record.SeedingSeconds >= minutes * 60.0);
    }

    private Task SaveAsync() => _store.SaveAsync(_downloads.Select(d => d.Record));

    #endregion

    #region Settings

    private EngineSettings BuildEngineSettings()
    {
        var port = Math.Clamp(_settings.ListenPort, 0, 65535);
        var (down, _) = SplitDownloadLimit();
        return new EngineSettingsBuilder
        {
            CacheDirectory = DownloadPaths.EngineCache,
            AutoSaveLoadFastResume = true,
            AutoSaveLoadMagnetLinkMetadata = true,
            AutoSaveLoadDhtCache = true,
            // Files keep their final name while downloading, so library items and players can point at them.
            UsePartialFiles = false,
            AllowPortForwarding = true,
            MaximumConnections = Math.Max(10, _settings.MaxConnections),
            MaximumDownloadRate = down,
            MaximumUploadRate = KiB(_settings.MaxUploadKiBps),
            ListenEndPoints = new Dictionary<string, IPEndPoint>
            {
                ["ipv4"] = new(IPAddress.Any, port),
                ["ipv6"] = new(IPAddress.IPv6Any, port),
            },
            DhtEndPoint = new IPEndPoint(IPAddress.Any, port),
        }.ToSettings();
    }

    private static int KiB(int kib) => (int)Math.Min(int.MaxValue, Math.Max(0, (long)kib * 1024));

    /// <summary>The download limit is shared: half each for torrents and direct downloads while both run.</summary>
    private (int Torrents, int Direct) SplitDownloadLimit()
    {
        var limit = KiB(_settings.MaxDownloadKiBps);
        return limit > 0 && _shareLimit ? (limit / 2, limit - limit / 2) : (limit, limit);
    }

    private async Task ApplySpeedLimitsAsync()
    {
        var (_, direct) = SplitDownloadLimit();
        _directLimiter.BytesPerSecond = direct;
        await Engine.UpdateSettingsAsync(BuildEngineSettings());
    }

    /// <summary>Settings were saved: apply limits / port. The download folder only affects new downloads.</summary>
    public async Task ApplySettingsAsync(DownloadSettings settings)
    {
        await _ready.Task;
        await _gate.WaitAsync();
        try
        {
            _settings = settings;
            await ApplySpeedLimitsAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    #endregion

    #region Adding / managing

    private async Task<Download> CreateAsync(DownloadRecord record) => record.Kind switch
    {
        DownloadKind.Torrent => await TorrentDownload.AddAsync(Engine, record, logger),
        _ => new DirectDownload(record, _http, _directLimiter, logger),
    };

    /// <summary>
    /// Adds a torrent from a magnet link, a .torrent URL or the content of a .torrent file. Adding one that is already
    /// in the list returns the existing download.
    /// </summary>
    public async Task<Download> AddTorrentAsync(string? link, byte[]? torrentFile, AddDownloadOptions options)
    {
        await _ready.Task;
        var root = await AttachFolderAsync(options);

        string source;
        string name;
        InfoHashes infoHashes;
        if (torrentFile is null && link is not null && link.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            if (!MagnetLink.TryParse(link, out var magnet)) throw new ArgumentException("Invalid magnet link");
            source = link;
            infoHashes = magnet.InfoHashes;
            name = magnet.Name ?? infoHashes.V1OrV2.ToHex();
        }
        else
        {
            torrentFile ??= await DownloadTorrentFileAsync(link ?? throw new ArgumentException("No torrent given"));
            if (!Torrent.TryLoad(torrentFile, out var torrent)) throw new ArgumentException("Not a valid .torrent file");
            infoHashes = torrent.InfoHashes;
            name = torrent.Name;
            source = Path.Combine(DownloadPaths.Torrents, infoHashes.V1OrV2.ToHex() + ".torrent");
            await File.WriteAllBytesAsync(source, torrentFile);
        }

        await _gate.WaitAsync();
        try
        {
            var existing = _downloads.OfType<TorrentDownload>().FirstOrDefault(t => t.InfoHashes == infoHashes);
            if (existing is not null)
            {
                Adopt(existing.Record, options);
                return existing;
            }

            var record = NewRecord(DownloadKind.Torrent, source, name, options, root);
            var download = await TorrentDownload.AddAsync(Engine, record, logger);
            _downloads.Add(download);
            await download.StartAsync();
            await SaveAsync();
            logger.LogInformation("Added torrent {Name} to {Path}", name, record.SavePath);
            return download;
        }
        finally
        {
            _gate.Release();
            OnChanged();
        }
    }

    private async Task<byte[]> DownloadTorrentFileAsync(string url)
    {
        if (TorrentDataUrl.IsTorrentDataUrl(url))
        {
            if (!TorrentDataUrl.TryDecode(url, out var data)) throw new ArgumentException("Invalid .torrent data: link");
            return data.Length > MaxTorrentFileBytes ? throw new ArgumentException("The .torrent file is too large") : data;
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("A torrent link must be a magnet:, http(s) or .torrent data: URL");
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxTorrentFileBytes) throw new ArgumentException("The .torrent file is too large");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return bytes.Length > MaxTorrentFileBytes ? throw new ArgumentException("The .torrent file is too large") : bytes;
    }

    public async Task<Download> AddDirectAsync(string url, AddDownloadOptions options)
    {
        await _ready.Task;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("A direct download must be an http(s) URL");

        var root = await AttachFolderAsync(options);
        await _gate.WaitAsync();
        try
        {
            var existing = _downloads.FirstOrDefault(d => d.Record.Kind == DownloadKind.Direct && d.Record.Source == url);
            if (existing is not null)
            {
                Adopt(existing.Record, options);
                return existing;
            }

            var name = Uri.UnescapeDataString(uri.Segments.LastOrDefault()?.Trim('/') ?? string.Empty);
            if (name.Length == 0) name = uri.Host;
            var record = NewRecord(DownloadKind.Direct, url, name, options, root, options.MediaTitle ?? Path.GetFileNameWithoutExtension(name));
            var download = new DirectDownload(record, _http, _directLimiter, logger);
            _downloads.Add(download);
            await download.StartAsync();
            await SaveAsync();
            logger.LogInformation("Added direct download {Url} to {Path}", url, record.SavePath);
            return download;
        }
        finally
        {
            _gate.Release();
            OnChanged();
        }
    }

    /// <summary>Folder of the item a download is attached to (its download folder goes inside), null = the download folder.</summary>
    private async Task<string?> AttachFolderAsync(AddDownloadOptions options)
    {
        if (options.MediaItemId is not { } id) return null;
        var item = await mediaItemRepository.GetMediaItem(id) ?? throw new ArgumentException("The library item no longer exists");
        if (string.IsNullOrWhiteSpace(item.FolderDirectory)) throw new ArgumentException("The library item has no folder");
        return Path.TrimEndingDirectorySeparator(item.FolderDirectory);
    }

    /// <summary>
    /// A new, empty library item in its own folder of the download folder, for a batch of downloads attached to it
    /// (e.g. the episodes of a show picked on a search page).
    /// </summary>
    public async Task<Guid> CreateLibraryItemAsync(string title, string? posterUrl, string? backdropUrl = null, MediaSourceRef? source = null)
    {
        await _ready.Task;
        var root = _settings.ResolveDownloadFolder();
        string folder;
        await _gate.WaitAsync();
        try
        {
            folder = DownloadPaths.NewDownloadFolder(root, title, _downloads.Select(d => d.Record.SavePath));
            Directory.CreateDirectory(folder);
        }
        finally
        {
            _gate.Release();
        }

        var item = new MediaItem { FolderDirectory = folder, Title = title, Files = [], Source = source };
        var id = await mediaItemService.CreateMediaItemFromScan(item, new MediaScanResult { FolderPath = folder, Title = title, Files = [], Folders = [] });
        await mediaImageService.TrySetImageAsync(item, MediaImageKind.Poster, posterUrl);
        await mediaImageService.TrySetImageAsync(item, MediaImageKind.Backdrop, backdropUrl);
        return id;
    }

    private DownloadRecord NewRecord(DownloadKind kind, string source, string name, AddDownloadOptions options, string? root, string? folderName = null)
    {
        root ??= _settings.ResolveDownloadFolder();
        Directory.CreateDirectory(root);
        return new DownloadRecord
        {
            Kind = kind,
            Source = source,
            Name = name,
            SavePath = DownloadPaths.NewDownloadFolder(root, folderName ?? name, _downloads.Select(d => d.Record.SavePath)),
            AddToLibrary = options.AddToLibrary || options.MediaItemId is not null,
            MediaTitle = options.MediaTitle,
            PosterUrl = options.PosterUrl,
            BackdropUrl = options.BackdropUrl,
            MediaSource = MediaSourceRef.Clean(options.MediaSource),
            AttachToItemId = options.MediaItemId,
        };
    }

    /// <summary>The same thing was added again, e.g. from a media page: link it to the library if not yet.</summary>
    private void Adopt(DownloadRecord record, AddDownloadOptions options)
    {
        // Its files are already elsewhere: they cannot move into another item's folder.
        if (options.MediaItemId is not null)
        {
            logger.LogInformation("{Name} is already downloading, not attaching it to item {Item}", record.Name, options.MediaItemId);
            return;
        }
        if (!options.AddToLibrary || record.MediaItemId is not null) return;
        record.AddToLibrary = true;
        record.MediaTitle ??= options.MediaTitle;
        record.PosterUrl ??= options.PosterUrl;
        record.BackdropUrl ??= options.BackdropUrl;
        record.MediaSource ??= MediaSourceRef.Clean(options.MediaSource);
        _linkAttempted.Remove(record.Id);
    }

    private async Task<T> WithDownloadAsync<T>(Guid id, Func<Download, Task<T>> action)
    {
        await _ready.Task;
        await _gate.WaitAsync();
        try
        {
            var download = _downloads.FirstOrDefault(d => d.Id == id) ?? throw new KeyNotFoundException($"Download {id} not found");
            var result = await action(download);
            await SaveAsync();
            return result;
        }
        finally
        {
            _gate.Release();
            OnChanged();
        }
    }

    public Task PauseAsync(Guid id) => WithDownloadAsync(id, async d => { await d.StopAsync(); return true; });
    public Task ResumeAsync(Guid id) => WithDownloadAsync(id, async d => { await d.StartAsync(); return true; });

    public Task SetFilePriorityAsync(Guid id, int index, FilePriority priority) =>
        WithDownloadAsync(id, async d => { await d.SetFilePriorityAsync(index, priority); return true; });

    public Task SetPriorityAsync(Guid id, DownloadPriority priority) =>
        WithDownloadAsync(id, d => { d.Record.Priority = priority; return Task.FromResult(true); });

    /// <summary>Removes a download; returns its record (the caller drops the library item when files are deleted).</summary>
    public Task<DownloadRecord> RemoveAsync(Guid id, bool deleteFiles) => WithDownloadAsync(id, async d =>
    {
        await d.RemoveAsync();
        _downloads.Remove(d);
        _linkAttempted.Remove(d.Id);

        if (d.Record.Kind == DownloadKind.Torrent && d.Record.Source.StartsWith(DownloadPaths.Torrents, StringComparison.Ordinal))
            TryDelete(() => File.Delete(d.Record.Source));
        if (deleteFiles && IsDownloadFolder(d.Record.SavePath) && Directory.Exists(d.Record.SavePath))
            TryDelete(() => Directory.Delete(d.Record.SavePath, recursive: true));
        logger.LogInformation("Removed download {Name} (files deleted: {Deleted})", d.Record.Name, deleteFiles);
        return d.Record;
    });

    /// <summary>Only folders made for a download are deleted, never the download root or anything above it.</summary>
    private bool IsDownloadFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var parent = Path.GetDirectoryName(full);
        return parent is not null && Path.GetFileName(full).Length > 0 && full != Path.TrimEndingDirectorySeparator(Path.GetFullPath(_settings.ResolveDownloadFolder()))
               && parent != Path.GetPathRoot(full);
    }

    private void TryDelete(Action delete)
    {
        try { delete(); }
        catch (Exception e) { logger.LogWarning(e, "Could not delete download files"); }
    }

    #endregion

    #region Queries

    public async Task<DownloadStatsDto> GetStatsAsync()
    {
        await _ready.Task;
        await _gate.WaitAsync();
        try
        {
            var direct = _downloads.OfType<DirectDownload>().Sum(d => d.DownloadRate);
            return new DownloadStatsDto(Engine.TotalDownloadRate + direct, Engine.TotalUploadRate, _downloads.Count(d => d.IsActive));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<DownloadDto>> GetListAsync()
    {
        await _ready.Task;
        List<(Download Download, DownloadState State, IReadOnlyList<DownloadFile> Files, int? Streaming)> snapshot;
        await _gate.WaitAsync();
        try
        {
            snapshot = _downloads.Select(d => (d, d.State, d.Files, d.StreamingFile)).ToList();
        }
        finally
        {
            _gate.Release();
        }

        var result = new List<DownloadDto>(snapshot.Count);
        foreach (var (d, state, files, streaming) in snapshot)
        {
            var itemId = d.Record.MediaItemId ?? d.Record.AttachToItemId;
            var item = await ItemInfoAsync(itemId);
            var remaining = d.Size - d.Downloaded;
            result.Add(new DownloadDto
            {
                Id = d.Id,
                Kind = d.Record.Kind,
                Name = d.Record.Name,
                DisplayName = ReleaseName.Display(d.Record.Name),
                State = state,
                Error = d.Record.Error,
                SavePath = d.Record.SavePath,
                Size = d.Size,
                Downloaded = d.Downloaded,
                Progress = d.Progress,
                DownloadRate = d.DownloadRate,
                UploadRate = d.UploadRate,
                Uploaded = d.Uploaded,
                Ratio = d.Size > 0 ? Math.Round((double)d.Uploaded / d.Size, 2) : 0,
                Eta = state == DownloadState.Downloading && d.DownloadRate > 0 && remaining > 0 ? remaining / d.DownloadRate : null,
                Peers = d.Peers,
                Seeds = d.Seeds,
                AddedAt = d.Record.AddedAt,
                MediaItemId = item.Exists ? itemId : null,
                MediaItemTitle = item.Title,
                AttachedToItem = d.Record.AttachToItemId is not null,
                Priority = d.Record.Priority,
                Streaming = streaming is not null,
                Files = files.Select(f => new DownloadFileDto(f.Index, f.RelativePath, f.Length, f.Progress, f.Priority, f.Index == streaming,
                    f.IsVideo, item.Files.TryGetValue(f.FullPath, out var fileId) ? fileId : null)).ToList(),
            });
        }
        return result.OrderByDescending(d => d.AddedAt).ToList();
    }

    /// <summary>
    /// Title (null = the item is gone) and library file ids by path of a download's item, cached for a few seconds (the
    /// list is rebuilt every second while someone watches it).
    /// </summary>
    private async Task<(bool Exists, string? Title, Dictionary<string, Guid> Files)> ItemInfoAsync(Guid? itemId)
    {
        if (itemId is not { } id) return (false, null, []);
        lock (_items)
        {
            if (_items.TryGetValue(id, out var cached) && DateTime.UtcNow - cached.At < TimeSpan.FromSeconds(10))
                return (cached.Title is not null, cached.Title, cached.Files);
        }
        var item = await mediaItemRepository.GetMediaItem(id);
        var files = item is null
            ? []
            : (await mediaFileRepository.GetFilesForItemId(id))
                .Where(f => f.Path is not null)
                .GroupBy(f => f.Path)
                .ToDictionary(g => g.Key, g => g.First().Id);
        var title = item is null ? null : item.Title ?? string.Empty;
        lock (_items) _items[id] = (DateTime.UtcNow, title, files);
        return (title is not null, title, files);
    }

    /// <summary>The download a file on disk belongs to, if it is one of ours; null for any other file.</summary>
    public async Task<DownloadPlaybackDto?> GetPlaybackAsync(string path)
    {
        if (!_ready.Task.IsCompletedSuccessfully || string.IsNullOrWhiteSpace(path)) return null;
        var full = Path.GetFullPath(path);
        Download? download;
        DownloadFile? file = null;
        IReadOnlyList<double[]> ranges;
        await _gate.WaitAsync();
        try
        {
            download = _downloads.FirstOrDefault(d =>
                (file = d.Files.FirstOrDefault(f => string.Equals(Path.GetFullPath(f.FullPath), full, StringComparison.Ordinal))) is not null);
            if (download is null || file is null) return null;
            ranges = download.FileRanges(file.Index);
        }
        finally
        {
            _gate.Release();
        }

        var complete = file.Progress >= 1;
        if (!complete && await GetTimelineAsync(download, file.Index) is { } timeline)
            ranges = timeline.ToTimeRanges(ranges, file.Length);
        return new DownloadPlaybackDto(download.Id, file.Index, complete, $"api/downloads/{download.Id}/files/{file.Index}", file.Length, ranges);
    }

    /// <summary>The playback time map of a file, cached once its container index is downloaded and read.</summary>
    private async Task<MediaTimeline?> GetTimelineAsync(Download download, int index)
    {
        var key = (download.Id, index);
        if (_timelines.TryGetValue(key, out var cached)) return cached;
        if (download.OpenFile(index) is not { } source) return null;
        try
        {
            var (final, timeline) = await MediaTimeline.ReadAsync(source);
            if (final) _timelines[key] = timeline;
            return timeline;
        }
        catch (Exception e)
        {
            // Only the seek bar's accuracy depends on it: fall back to byte fractions, try again on the next poll.
            logger.LogDebug(e, "Could not read the media index of {Name}", download.Record.Name);
            return null;
        }
    }

    /// <summary>A stream over a file of a download that waits for missing parts; null if there is no such file.</summary>
    public async Task<(Stream Stream, string FileName)?> OpenStream(Guid id, int index)
    {
        await _ready.Task;
        await _gate.WaitAsync();
        try
        {
            var download = _downloads.FirstOrDefault(d => d.Id == id);
            var file = download?.Files.FirstOrDefault(f => f.Index == index);
            var source = download?.OpenFile(index);
            return file is null || source is null ? null : (new DownloadStream(source), Path.GetFileName(file.FullPath));
        }
        finally
        {
            _gate.Release();
        }
    }

    #endregion

    #region Library

    private async Task LinkToLibraryAsync(Download download)
    {
        var record = download.Record;
        var videos = download.Files.Where(f => f.IsVideo && f.Wanted).Select(f => f.FullPath).ToList();
        if (videos.Count == 0) return;

        if (record.AttachToItemId is { } targetId && await AttachToItemAsync(record, targetId, videos)) return;

        try
        {
            var existing = await mediaItemRepository.GetMediaItemFolders();
            if (existing.Contains(record.SavePath)) return;

            Directory.CreateDirectory(record.SavePath);
            var item = new MediaItem
            {
                FolderDirectory = record.SavePath,
                Title = record.MediaTitle ?? record.Name,
                Files = [],
                Source = record.MediaSource,
            };
            var scan = new MediaScanResult
            {
                FolderPath = record.SavePath,
                Title = item.Title,
                Files = videos,
                Folders = FoldersBetween(record.SavePath, videos),
            };
            var itemId = await mediaItemService.CreateMediaItemFromScan(item, scan);
            await mediaImageService.TrySetImageAsync(item, MediaImageKind.Poster, record.PosterUrl);
            await mediaImageService.TrySetImageAsync(item, MediaImageKind.Backdrop, record.BackdropUrl);
            await _gate.WaitAsync();
            try { record.MediaItemId = itemId; await SaveAsync(); }
            finally { _gate.Release(); }
            logger.LogInformation("Added download {Name} to the library ({Count} videos)", record.Name, videos.Count);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not add download {Name} to the library", record.Name);
        }
    }

    /// <summary>Adds the videos of an attached download to its item; false if the item is gone (then it gets its own).</summary>
    private async Task<bool> AttachToItemAsync(DownloadRecord record, Guid itemId, List<string> videos)
    {
        try
        {
            var item = await mediaItemRepository.GetMediaItem(itemId);
            if (item is null)
            {
                logger.LogWarning("Item {Item} of download {Name} was removed, adding the download as its own item", itemId, record.Name);
                record.AttachToItemId = null;
                return false;
            }

            var root = Path.TrimEndingDirectorySeparator(item.FolderDirectory);
            await mediaItemService.AddFilesFromScan(itemId, new MediaScanResult
            {
                FolderPath = root,
                Title = item.Title,
                Files = videos,
                Folders = FoldersBetween(root, videos),
            });
            await _gate.WaitAsync();
            try { record.MediaItemId = itemId; await SaveAsync(); }
            finally { _gate.Release(); }
            lock (_items) _items.Remove(itemId);
            logger.LogInformation("Added download {Name} to item {Title} ({Count} videos)", record.Name, item.Title, videos.Count);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Could not add download {Name} to item {Item}", record.Name, itemId);
        }
        return true;
    }

    /// <summary>Folders below <paramref name="root"/> that contain the files, at any depth (the item's folder list).</summary>
    private static List<string> FoldersBetween(string root, IEnumerable<string> files)
    {
        var prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        HashSet<string> folders = [];
        foreach (var file in files)
            for (var dir = Path.GetDirectoryName(file); dir is not null && dir.StartsWith(prefix, StringComparison.Ordinal); dir = Path.GetDirectoryName(dir))
                folders.Add(dir);
        return folders.ToList();
    }

    #endregion
}
