using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace Popcorn.Downloads;

/// <summary>
/// HTTP(S) download of one file. When the server supports ranges it is fetched in <see cref="ChunkSize"/> chunks by a
/// few workers, in the same order as torrents (<see cref="SequentialPieceRequester"/>): the chunk a player reads, else
/// head, tail, then sequentially; so it can be watched while it downloads and resumes after a restart. Without range
/// support it is one sequential stream that restarts from zero.
/// </summary>
public sealed class DirectDownload(DownloadRecord record, HttpClient http, RateLimiter limiter, ILogger logger) : Download(record)
{
    public const int ChunkSize = 4 * 1024 * 1024;
    private const int Workers = 3;
    private const int MaxAttempts = 5;
    private static readonly TimeSpan ReaderTimeout = TimeSpan.FromSeconds(20);

    private readonly object _lock = new();
    private readonly SpeedMeter _meter = new();
    /// <summary>Priority cap of this download, on top of the shared <c>limiter</c>.</summary>
    private readonly RateLimiter _throttle = new();
    /// <summary>Chunks being downloaded → bytes written from the chunk start (readers may use them already).</summary>
    private readonly Dictionary<int, long> _inFlight = [];
    private BitArray _chunks = new(0);
    private CancellationTokenSource? _cts;
    private Task? _run;
    private bool _probing;
    private string? _error;
    private long _inFlightBytes;
    /// <summary>Sequential mode: bytes written from the start.</summary>
    private long _written;
    private int _readerChunk = -1;
    private DateTime _readerAt;

    private bool Running => _run is { IsCompleted: false };
    private string? FullPath => Record.FileName is null ? null : Path.Combine(Record.SavePath, Record.FileName);
    private int ChunkCount => Record.Length <= 0 ? 0 : (int)((Record.Length + ChunkSize - 1) / ChunkSize);

    public override DownloadState State =>
        _error is not null ? DownloadState.Error
        : Record.Finished ? DownloadState.Completed
        : Running ? _probing ? DownloadState.Metadata : DownloadState.Downloading
        : DownloadState.Paused;

    public string? Error => _error;
    public override long Size => Record.Length;
    public override long DownloadRate => IsActive ? _meter.Rate : 0; // the meter averages the last seconds
    public override bool IsComplete => Record.Finished;

    public override long Downloaded
    {
        get
        {
            if (Record.Finished) return Math.Max(Record.Length, _written);
            if (!Record.SupportsRanges) return _written;
            lock (_lock) return CompletedBytes() + Interlocked.Read(ref _inFlightBytes);
        }
    }

    public override IReadOnlyList<DownloadFile> Files => FullPath is null
        ? []
        : [new DownloadFile(0, FullPath, Record.FileName!, Record.Length, Progress, FilePriority.Normal)];

    public override int? StreamingFile => _readerChunk >= 0 && DateTime.UtcNow - _readerAt < ReaderTimeout ? 0 : null;

    public override Task SetThrottleAsync(long bytesPerSecond)
    {
        _throttle.BytesPerSecond = bytesPerSecond;
        return Task.CompletedTask;
    }

    public override Task TickAsync(TimeSpan elapsed)
    {
        _meter.Tick();
        Record.Error = _error;
        return Task.CompletedTask;
    }

    public override Task StartAsync()
    {
        Record.Paused = false;
        if (Record.Finished || Running) return Task.CompletedTask;
        _error = null;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _run = Task.Run(() => RunAsync(token), token);
        return Task.CompletedTask;
    }

    public override async Task StopAsync()
    {
        Record.Paused = true;
        await CancelAsync();
    }

    public override Task RemoveAsync() => CancelAsync();

    private async Task CancelAsync()
    {
        _cts?.Cancel();
        if (_run is not null)
        {
            try { await _run; }
            catch (OperationCanceledException) { }
        }
        // The partial file of a non-resumable download is useless.
        if (!Record.SupportsRanges && !Record.Finished) _written = 0;
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            if (Record.FileName is null || !Record.SupportsRanges)
            {
                _probing = true;
                var response = await http.GetAsync(Record.Source, HttpCompletionOption.ResponseHeadersRead, token);
                _probing = false;
                using (response)
                {
                    response.EnsureSuccessStatusCode();
                    ReadFileInfo(response);
                    if (!Record.SupportsRanges)
                    {
                        await DownloadSequentialAsync(response, token);
                        return;
                    }
                }
            }
            await DownloadChunksAsync(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Direct download {Name} failed", Record.Name);
            _error = e.Message;
        }
        finally
        {
            _probing = false;
        }
    }

    private void ReadFileInfo(HttpResponseMessage response)
    {
        var length = response.Content.Headers.ContentLength ?? -1;
        var ranges = length > 0 && response.Headers.AcceptRanges.Contains("bytes");
        var name = FileNameOf(response);

        // A resumed download keeps its file name; another length means another file.
        if (Record.FileName is not null && Record.Length != length) Record.Chunks = null;
        Record.FileName ??= name;
        Record.Length = length;
        Record.SupportsRanges = ranges;
        if (Record.Name.Length == 0 || Record.Name == Record.Source) Record.Name = Record.FileName;
    }

    private string FileNameOf(HttpResponseMessage response)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        var name = disposition?.FileNameStar ?? disposition?.FileName;
        if (string.IsNullOrWhiteSpace(name))
        {
            var uri = response.RequestMessage?.RequestUri ?? new Uri(Record.Source);
            name = Uri.UnescapeDataString(uri.Segments.LastOrDefault() ?? string.Empty);
        }
        name = DownloadPaths.SanitizeFileName(name.Trim('"', ' ', '/'));
        return name.Length == 0 ? "download" : name;
    }

    private SafeFileHandle OpenFile()
    {
        Directory.CreateDirectory(Record.SavePath);
        return File.OpenHandle(FullPath!, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, FileOptions.Asynchronous);
    }

    private async Task DownloadSequentialAsync(HttpResponseMessage response, CancellationToken token)
    {
        using var handle = OpenFile();
        RandomAccess.SetLength(handle, 0);
        _written = 0;
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            await limiter.WaitAsync(read, token);
            await _throttle.WaitAsync(read, token);
            await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, read), _written, token);
            Interlocked.Add(ref _written, read);
            _meter.Add(read);
        }
        if (Record.Length >= 0 && _written != Record.Length)
            throw new IOException($"Connection closed after {_written} of {Record.Length} bytes");
        Record.Length = _written;
        Complete();
    }

    private async Task DownloadChunksAsync(CancellationToken token)
    {
        lock (_lock)
        {
            _chunks = Record.Chunks is { } saved && saved.Length * 8 >= ChunkCount
                ? new BitArray(saved) { Length = ChunkCount }
                : new BitArray(ChunkCount);
            _inFlight.Clear();
        }

        using var handle = OpenFile();
        if (RandomAccess.GetLength(handle) != Record.Length) RandomAccess.SetLength(handle, Record.Length);

        using var failed = CancellationTokenSource.CreateLinkedTokenSource(token);
        var workers = Enumerable.Range(0, Workers).Select(_ => Task.Run(() => WorkerAsync(handle, failed), failed.Token));
        try
        {
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && _error is not null)
        {
            throw new IOException(_error);
        }

        lock (_lock)
        {
            if (_chunks.Cast<bool>().All(c => c)) Complete();
        }
    }

    private async Task WorkerAsync(SafeFileHandle handle, CancellationTokenSource failed)
    {
        var token = failed.Token;
        while (NextChunk() is var chunk && chunk >= 0)
        {
            for (var attempt = 1; ; attempt++)
            {
                long received = 0;
                try
                {
                    received = await DownloadChunkAsync(handle, chunk, token);
                    lock (_lock)
                    {
                        _chunks[chunk] = true;
                        _inFlight.Remove(chunk);
                        Record.Chunks = ToBytes(_chunks);
                    }
                    Interlocked.Add(ref _inFlightBytes, -received);
                    break;
                }
                catch (Exception e) when (e is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    Interlocked.Add(ref _inFlightBytes, -(e is ChunkException ce ? ce.Received : received));
                    if (attempt >= MaxAttempts)
                    {
                        _error = $"Chunk {chunk}: {e.Message}";
                        lock (_lock) _inFlight.Remove(chunk);
                        await failed.CancelAsync();
                        return;
                    }
                    logger.LogWarning("Direct download {Name}: chunk {Chunk} attempt {Attempt} failed: {Error}", Record.Name, chunk, attempt, e.Message);
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), token);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Add(ref _inFlightBytes, -received);
                    lock (_lock) _inFlight.Remove(chunk);
                    throw;
                }
            }
        }
    }

    private sealed class ChunkException(string message, long received, Exception? inner = null) : IOException(message, inner)
    {
        public long Received { get; } = received;
    }

    /// <summary>Downloads one chunk; returns its size. Partial progress is counted in <see cref="_inFlightBytes"/>.</summary>
    private async Task<long> DownloadChunkAsync(SafeFileHandle handle, int chunk, CancellationToken token)
    {
        var start = (long)chunk * ChunkSize;
        var end = Math.Min(start + ChunkSize, Record.Length) - 1;
        long received = 0;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Record.Source);
            request.Headers.Range = new RangeHeaderValue(start, end);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode != HttpStatusCode.PartialContent)
                throw new IOException($"Server answered {(int)response.StatusCode} to a range request");

            await using var stream = await response.Content.ReadAsStreamAsync(token);
            var buffer = new byte[64 * 1024];
            var expected = end - start + 1;
            while (received < expected)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, expected - received)), token);
                if (read == 0) throw new IOException($"Connection closed after {received} of {expected} bytes");
                await limiter.WaitAsync(read, token);
                await _throttle.WaitAsync(read, token);
                await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, read), start + received, token);
                received += read;
                lock (_lock) _inFlight[chunk] = received;
                Interlocked.Add(ref _inFlightBytes, read);
                _meter.Add(read);
            }
            return received;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new ChunkException(e.Message, received, e);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Add(ref _inFlightBytes, -received);
            throw new OperationCanceledException(token);
        }
    }

    /// <summary>Claims the next chunk to download, -1 when none is left.</summary>
    private int NextChunk()
    {
        lock (_lock)
        {
            var count = _chunks.Length;
            bool Free(int i) => !_chunks[i] && !_inFlight.ContainsKey(i);
            int FirstFree(int from, int to)
            {
                for (var i = Math.Max(0, from); i <= to && i < count; i++)
                    if (Free(i)) return i;
                return -1;
            }

            var pick = -1;
            if (_readerChunk >= 0 && DateTime.UtcNow - _readerAt < ReaderTimeout)
                pick = FirstFree(_readerChunk, count - 1);
            if (pick < 0) pick = FirstFree(0, 0);
            if (pick < 0) pick = FirstFree(count - (int)(SequentialPieceRequester.TailBytes / ChunkSize), count - 1);
            if (pick < 0) pick = FirstFree(0, count - 1);
            if (pick >= 0) _inFlight[pick] = 0;
            return pick;
        }
    }

    private void Complete()
    {
        Record.Finished = true;
        Record.CompletedAt = DateTime.UtcNow;
        Record.Chunks = null;
        logger.LogInformation("Direct download {Name} completed", Record.Name);
    }

    private long CompletedBytes()
    {
        long total = 0;
        for (var i = 0; i < _chunks.Length; i++)
            if (_chunks[i]) total += Math.Min(ChunkSize, Record.Length - (long)i * ChunkSize);
        return total;
    }

    private static byte[] ToBytes(BitArray bits)
    {
        var bytes = new byte[(bits.Length + 7) / 8];
        bits.CopyTo(bytes, 0);
        return bytes;
    }

    private bool Has(long offset, long count)
    {
        if (Record.Finished) return true;
        if (!Record.SupportsRanges) return offset + count <= Interlocked.Read(ref _written);
        lock (_lock)
        {
            if (_chunks.Length == 0) return false;
            var end = offset + Math.Max(count, 1);
            var first = (int)(offset / ChunkSize);
            var last = (int)((end - 1) / ChunkSize);
            for (var i = first; i <= last && i < _chunks.Length; i++)
            {
                if (_chunks[i]) continue;
                // Chunks are written from their start, so the end of the range may lie in the written part.
                if (i == last && _inFlight.TryGetValue(i, out var written) && end <= (long)i * ChunkSize + written) continue;
                return false;
            }
            return true;
        }
    }

    public override IStreamSource? OpenFile(int index) =>
        index == 0 && FullPath is not null && Record.Length > 0 ? new FileSource(this) : null;

    public override IReadOnlyList<double[]> FileRanges(int index)
    {
        if (index != 0 || Record.Length <= 0) return [];
        if (Record.Finished) return [[0, 1]];
        if (!Record.SupportsRanges) return [[0, Math.Min(1, (double)Interlocked.Read(ref _written) / Record.Length)]];
        lock (_lock)
        {
            // Completed chunks + the written start of chunks in flight, merged.
            var result = new List<double[]>();
            double length = Record.Length;
            for (var i = 0; i < _chunks.Length; i++)
            {
                var start = (long)i * ChunkSize;
                var end = _chunks[i] ? Math.Min(start + ChunkSize, Record.Length)
                    : _inFlight.TryGetValue(i, out var written) ? start + written : start;
                if (end <= start) continue;
                if (result.Count > 0 && result[^1][1] == start / length) result[^1][1] = end / length;
                else result.Add([start / length, end / length]);
            }
            return result;
        }
    }

    private sealed class FileSource(DirectDownload owner) : IStreamSource
    {
        public long Length => owner.Record.Length;
        public bool Has(long offset, long count) => owner.Has(offset, count);

        public void Seek(long position)
        {
            owner._readerChunk = (int)(position / ChunkSize);
            owner._readerAt = DateTime.UtcNow;
        }

        public void Read(long position) => Seek(position);

        public async Task EnsureRunningAsync()
        {
            if (owner._error is not null) throw new IOException($"Download failed: {owner._error}");
            if (!owner.Running && !owner.Record.Finished) await owner.StartAsync();
        }

        public async ValueTask<int> ReadAsync(long position, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            using var handle = File.OpenHandle(owner.FullPath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return await RandomAccess.ReadAsync(handle, buffer, position, cancellationToken);
        }
    }
}
