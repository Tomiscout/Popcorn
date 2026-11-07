using Microsoft.Extensions.Logging;
using MonoTorrent;
using MonoTorrent.Client;

namespace Popcorn.Downloads;

/// <summary>A torrent in the MonoTorrent engine, downloaded with <see cref="SequentialPieceRequester"/>.</summary>
public sealed class TorrentDownload : Download
{
    private readonly TorrentManager _manager;
    private readonly SequentialPieceRequester _requester;
    private readonly ILogger _logger;
    private bool _metadataApplied;
    private long _lastSent;
    private readonly object _engineLock = new();
    /// <summary>Queued engine starts / stops (<see cref="ApplyRunState"/>).</summary>
    private Task _engineWork = Task.CompletedTask;
    private long _throttle;

    private TorrentDownload(DownloadRecord record, TorrentManager manager, SequentialPieceRequester requester, ILogger logger) : base(record)
    {
        _manager = manager;
        _requester = requester;
        _logger = logger;
    }

    /// <summary>Info hash of a magnet link or .torrent file, to find duplicates before adding.</summary>
    public static async Task<InfoHashes> ReadInfoHashesAsync(string source) =>
        source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)
            ? MagnetLink.Parse(source).InfoHashes
            : (await Torrent.LoadAsync(source)).InfoHashes;

    public static async Task<TorrentDownload> AddAsync(ClientEngine engine, DownloadRecord record, ILogger logger)
    {
        var settings = new TorrentSettingsBuilder
        {
            // SavePath is already a folder of its own.
            CreateContainingDirectory = false,
        }.ToSettings();

        var manager = record.Source.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)
            ? await engine.AddAsync(MagnetLink.Parse(record.Source), record.SavePath, settings)
            : await engine.AddAsync(await Torrent.LoadAsync(record.Source), record.SavePath, settings);

        var requester = new SequentialPieceRequester();
        await manager.ChangePickerAsync(requester);
        return new TorrentDownload(record, manager, requester, logger);
    }

    public InfoHashes InfoHashes => _manager.InfoHashes;

    public override DownloadState State => _manager.State switch
    {
        TorrentState.Error => DownloadState.Error,
        // Stopping takes a few seconds; it is paused as far as the user is concerned.
        _ when Record.Paused => DownloadState.Paused,
        _ when Record.Finished => DownloadState.Completed,
        TorrentState.Stopped or TorrentState.Stopping or TorrentState.Paused or TorrentState.HashingPaused =>
            Record.Finished ? DownloadState.Completed : DownloadState.Paused,
        TorrentState.Metadata or TorrentState.FetchingHashes => DownloadState.Metadata,
        TorrentState.Hashing => DownloadState.Hashing,
        TorrentState.Seeding => DownloadState.Seeding,
        _ => IsComplete ? DownloadState.Seeding : DownloadState.Downloading,
    };

    public string? Error => _manager.Error?.Exception?.Message ?? (_manager.Error is { } e ? e.Reason.ToString() : null);

    public override long Size => _manager.HasMetadata
        ? WantedFiles.Sum(f => f.Length)
        : _manager.MagnetLink?.Size ?? -1;

    public override long Downloaded => _manager.HasMetadata ? WantedFiles.Sum(f => f.BytesDownloaded()) : 0;
    public override long DownloadRate => _manager.Monitor.DownloadRate;
    public override long UploadRate => _manager.Monitor.UploadRate;
    public override long Uploaded => Record.UploadedBytes;
    public override int Peers => _manager.OpenConnections;
    public override int Seeds => _manager.Peers.Seeds;
    public override bool IsComplete => _manager.HasMetadata && WantedFiles.All(f => f.BitField.AllTrue);

    private IEnumerable<ITorrentManagerFile> WantedFiles => _manager.Files.Where(f => f.Priority != Priority.DoNotDownload);

    public override IReadOnlyList<DownloadFile> Files => !_manager.HasMetadata
        ? []
        : _manager.Files.Select((f, i) => new DownloadFile(
                i, f.FullPath, f.Path, f.Length,
                f.Length > 0 ? (double)f.BytesDownloaded() / f.Length : 1,
                FromEngine(f.Priority)))
            .ToList();

    private static Priority ToEngine(FilePriority priority) => priority switch
    {
        FilePriority.Skip => Priority.DoNotDownload,
        FilePriority.Low => Priority.Low,
        FilePriority.High => Priority.High,
        _ => Priority.Normal,
    };

    private static FilePriority FromEngine(Priority priority) => priority switch
    {
        Priority.DoNotDownload => FilePriority.Skip,
        < Priority.Normal => FilePriority.Low,
        Priority.Normal => FilePriority.Normal,
        _ => FilePriority.High,
    };

    public override int? StreamingFile =>
        _manager.HasMetadata && _requester.ReaderFile is { } file && _manager.Files.IndexOf(file) is var i and >= 0 ? i : null;

    public override async Task SetThrottleAsync(long bytesPerSecond)
    {
        // Small changes are not worth a settings update every second.
        if (bytesPerSecond == _throttle || (bytesPerSecond > 0 && _throttle > 0 && Math.Abs(bytesPerSecond - _throttle) < _throttle / 10)) return;
        _throttle = bytesPerSecond;
        await _manager.UpdateSettingsAsync(new TorrentSettingsBuilder(_manager.Settings)
        {
            MaximumDownloadRate = (int)Math.Min(int.MaxValue, bytesPerSecond),
        }.ToSettings());
    }

    public override Task StartAsync()
    {
        if (Record.Finished)
        {
            Record.Finished = false;
            Record.IgnoreSeedLimit = true;
        }
        Record.Paused = false;
        ApplyRunState();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Record.Paused = true;
        ApplyRunState();
        return Task.CompletedTask;
    }

    /// <summary>Seed limit reached.</summary>
    public Task FinishAsync()
    {
        Record.Finished = true;
        ApplyRunState();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Brings the engine to the state the record asks for (running unless paused / finished) in the background:
    /// stopping takes seconds (tracker announces, flushing) and the caller holds the manager's lock, so pausing and
    /// resuming answer at once. Runs one after another; each run reads the record again, so the last request wins.
    /// </summary>
    private void ApplyRunState()
    {
        lock (_engineLock) _engineWork = ApplyRunStateAfter(_engineWork);
    }

    private async Task ApplyRunStateAfter(Task previous)
    {
        await previous;
        try
        {
            if (Record.Paused || Record.Finished)
            {
                if (_manager.State is not (TorrentState.Stopped or TorrentState.Stopping))
                    await _manager.StopAsync(TimeSpan.FromSeconds(5));
                return;
            }
            if (_manager.State == TorrentState.Error) await _manager.StopAsync();
            if (_manager.State is TorrentState.Stopped or TorrentState.Paused or TorrentState.Error)
                await _manager.StartAsync();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Starting / stopping {Name} failed", Record.Name);
        }
    }

    /// <summary>Completes when queued starts / stops are done.</summary>
    public Task Settled
    {
        get { lock (_engineLock) return _engineWork; }
    }

    public override async Task RemoveAsync()
    {
        await Settled;
        if (_manager.State is not (TorrentState.Stopped or TorrentState.Stopping))
            await _manager.StopAsync(TimeSpan.FromSeconds(5));
        await _manager.Engine!.RemoveAsync(_manager, RemoveMode.CacheDataOnly);
    }

    public override async Task SetFilePriorityAsync(int index, FilePriority priority)
    {
        if (!_manager.HasMetadata || index < 0 || index >= _manager.Files.Count) return;
        await _manager.SetFilePriorityAsync(_manager.Files[index], ToEngine(priority));
        if (priority == FilePriority.Normal) Record.FilePriorities.Remove(index);
        else Record.FilePriorities[index] = priority;
        // Wanting more of a finished torrent makes it download again.
        if (priority != FilePriority.Skip && !IsComplete && Record.Finished) await StartAsync();
    }

    public override async Task TickAsync(TimeSpan elapsed)
    {
        if (!_metadataApplied && _manager.HasMetadata)
        {
            _metadataApplied = true;
            Record.Name = _manager.Torrent!.Name;
            foreach (var (index, priority) in Record.FilePriorities.Where(p => p.Key >= 0 && p.Key < _manager.Files.Count))
                await _manager.SetFilePriorityAsync(_manager.Files[index], ToEngine(priority));
        }

        var sent = _manager.Monitor.DataBytesSent;
        var delta = sent - _lastSent;
        if (delta < 0) delta = sent; // counters restart with every start
        Record.UploadedBytes += delta;
        _lastSent = sent;

        if (State == DownloadState.Seeding)
        {
            Record.SeedingSeconds += elapsed.TotalSeconds;
            Record.CompletedAt ??= DateTime.UtcNow;
        }
        Record.Error = State == DownloadState.Error ? Error : null;
    }

    public override IStreamSource? OpenFile(int index) =>
        _manager.HasMetadata && index >= 0 && index < _manager.Files.Count ? new FileSource(this, _manager.Files[index]) : null;

    public override IReadOnlyList<double[]> FileRanges(int index)
    {
        if (!_manager.HasMetadata || index < 0 || index >= _manager.Files.Count) return [];
        var file = _manager.Files[index];
        var bitfield = _manager.Bitfield;
        return Ranges.FromPieces(file.StartPieceIndex, file.EndPieceIndex, i => bitfield[i]);
    }

    private sealed class FileSource(TorrentDownload owner, ITorrentManagerFile file) : IStreamSource
    {
        private TorrentManager Manager => owner._manager;

        public long Length => file.Length;

        public bool Has(long offset, long count)
        {
            if (count <= 0) return true;
            var (first, last) = Pieces(offset, count);
            var bitfield = Manager.Bitfield;
            for (var i = first; i <= last; i++)
                if (!bitfield[i]) return false;
            return true;
        }

        private (int First, int Last) Pieces(long offset, long count)
        {
            var torrent = Manager.Torrent!;
            var first = torrent.ByteOffsetToPieceIndex(file.OffsetInTorrent + offset);
            var last = torrent.ByteOffsetToPieceIndex(file.OffsetInTorrent + Math.Min(offset + count, file.Length) - 1);
            return (first, last);
        }

        public void Seek(long position) => owner._requester.SeekToPosition(file, position);
        public void Read(long position) => owner._requester.ReadToPosition(file, position);

        public async Task EnsureRunningAsync()
        {
            if (Manager.State == TorrentState.Error)
                throw new IOException($"Torrent failed: {owner.Error}");
            if (file.Priority == Priority.DoNotDownload)
                await owner.SetFilePriorityAsync(owner._manager.Files.IndexOf(file), FilePriority.Normal);
            if (Manager.State is TorrentState.Stopped or TorrentState.Paused)
            {
                owner._logger.LogInformation("Resuming {Name}: a player reads a missing part", owner.Record.Name);
                await owner.StartAsync();
            }
        }

        public async ValueTask<int> ReadAsync(long position, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var (first, last) = Pieces(position, buffer.Length);
            // Hashed pieces can still sit in MonoTorrent's write cache.
            await Manager.Engine!.DiskManager.FlushAsync(Manager, first, last);
            using var handle = File.OpenHandle(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return await RandomAccess.ReadAsync(handle, buffer, position, cancellationToken);
        }
    }
}
