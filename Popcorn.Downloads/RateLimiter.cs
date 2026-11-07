namespace Popcorn.Downloads;

/// <summary>Token bucket (the limit shared by all direct downloads, or one download's priority cap). 0 bytes/s = unlimited.</summary>
public sealed class RateLimiter
{
    private readonly object _lock = new();
    private long _bytesPerSecond;
    private double _tokens;
    private long _lastRefill = Environment.TickCount64;

    public long BytesPerSecond
    {
        get => Interlocked.Read(ref _bytesPerSecond);
        set => Interlocked.Exchange(ref _bytesPerSecond, Math.Max(0, value));
    }

    /// <summary>Waits until <paramref name="bytes"/> may be transferred.</summary>
    public async ValueTask WaitAsync(int bytes, CancellationToken cancellationToken)
    {
        while (true)
        {
            var limit = BytesPerSecond;
            if (limit <= 0) return;

            TimeSpan wait;
            lock (_lock)
            {
                var now = Environment.TickCount64;
                // At most one second of burst.
                _tokens = Math.Min(limit, _tokens + (now - _lastRefill) * limit / 1000.0);
                _lastRefill = now;
                if (_tokens >= bytes || _tokens >= limit)
                {
                    _tokens -= bytes;
                    return;
                }
                wait = TimeSpan.FromMilliseconds(Math.Clamp((bytes - _tokens) * 1000.0 / limit, 5, 1000));
            }
            await Task.Delay(wait, cancellationToken);
        }
    }
}

/// <summary>Bytes per second over the last few seconds.</summary>
public sealed class SpeedMeter
{
    private const int Window = 3;
    private readonly long[] _buckets = new long[Window];
    private long _current;
    private int _index;

    public void Add(long bytes) => Interlocked.Add(ref _current, bytes);

    public long Rate { get; private set; }

    /// <summary>Called once a second.</summary>
    public void Tick()
    {
        _buckets[_index] = Interlocked.Exchange(ref _current, 0);
        _index = (_index + 1) % Window;
        Rate = _buckets.Sum() / Window;
    }
}
