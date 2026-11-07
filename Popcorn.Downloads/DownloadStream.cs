namespace Popcorn.Downloads;

/// <summary>One file of a download as something that can be read while it downloads.</summary>
public interface IStreamSource
{
    long Length { get; }
    /// <summary>All bytes of <c>[offset, offset + count)</c> are on disk.</summary>
    bool Has(long offset, long count);
    /// <summary>A reader jumped to <paramref name="position"/>: download from there next.</summary>
    void Seek(long position);
    /// <summary>A reader got to <paramref name="position"/>: keep downloading ahead of it.</summary>
    void Read(long position);
    /// <summary>Starts the download if it is paused / finished but this part is missing. Throws when it failed.</summary>
    Task EnsureRunningAsync();
    /// <summary>Reads bytes that <see cref="Has"/> confirmed.</summary>
    ValueTask<int> ReadAsync(long position, Memory<byte> buffer, CancellationToken cancellationToken);
}

/// <summary>
/// Seekable read-only stream over a file that is still downloading. A read of a missing part tells the download to
/// fetch it first and waits for it, so HTTP range requests (players seeking) block until the data arrives.
/// </summary>
public sealed class DownloadStream(IStreamSource source) : Stream
{
    /// <summary>Waiting for one read is capped so a request for a large range returns data as it comes.</summary>
    private const int MaxRead = 256 * 1024;

    private long _position;
    private bool _disposed;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => source.Length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        target = Math.Clamp(target, 0, Length);
        if (target != _position)
        {
            _position = target;
            source.Seek(target);
        }
        return _position;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var count = (int)Math.Min(Math.Min(buffer.Length, MaxRead), Length - _position);
        if (count <= 0) return 0;

        if (!source.Has(_position, count))
        {
            source.Seek(_position);
            await source.EnsureRunningAsync();
            while (!source.Has(_position, count))
            {
                await Task.Delay(100, cancellationToken);
                ObjectDisposedException.ThrowIf(_disposed, this);
                // Keeps the reader's position first while it waits (the downloads forget idle readers).
                source.Read(_position);
                await source.EnsureRunningAsync();
            }
        }

        var read = await source.ReadAsync(_position, buffer[..count], cancellationToken);
        _position += read;
        source.Read(_position);
        return read;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }
}

internal static class Ranges
{
    /// <summary>Runs of true values of a piece map as [start, end] fractions of the file.</summary>
    public static List<double[]> FromPieces(int firstPiece, int lastPiece, Func<int, bool> has)
    {
        var result = new List<double[]>();
        var count = lastPiece - firstPiece + 1;
        if (count <= 0) return result;

        var runStart = -1;
        for (var i = 0; i <= count; i++)
        {
            var have = i < count && has(firstPiece + i);
            if (have && runStart < 0) runStart = i;
            else if (!have && runStart >= 0)
            {
                result.Add([(double)runStart / count, (double)i / count]);
                runStart = -1;
            }
        }
        return result;
    }
}
