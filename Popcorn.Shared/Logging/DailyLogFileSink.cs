using System.Text;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace Popcorn.Shared.Logging;

/// <summary>
/// Serilog file sink writing <see cref="LogFileName"/> names (Serilog's rolling sink can only put the date after the
/// name). Rolls over at local midnight; when another process of the same app holds the day's file, the next free
/// <c>_001</c>, <c>_002</c>... name is used. Flushed after every event so the Settings → Logs viewer sees it live.
/// Also linked into Popcorn.Windows, keep its dependencies to Serilog.
/// </summary>
public sealed class DailyLogFileSink(string folder, string app, ITextFormatter formatter) : ILogEventSink, IDisposable
{
    public const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    private readonly Lock _lock = new();
    private StreamWriter? _writer;
    private DateOnly _day;
    private bool _disposed;

    public void Emit(LogEvent logEvent)
    {
        lock (_lock)
        {
            if (_disposed) return;
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (_writer is null || today != _day) Open(today);
            if (_writer is null) return;
            try
            {
                formatter.Format(logEvent, _writer);
                _writer.Flush();
            }
            catch (IOException)
            {
                // disk full or file gone: drop the event rather than the app
            }
        }
    }

    private void Open(DateOnly day)
    {
        _writer?.Dispose();
        _writer = null;
        _day = day;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (IOException)
        {
            return;
        }

        for (var i = 0; i < 100; i++)
        {
            try
            {
                var stream = new FileStream(Path.Combine(folder, LogFileName.Format(app, day, i)), FileMode.Append,
                    FileAccess.Write, FileShare.Read);
                if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
                {
                    // FileShare.Read is only a shared flock on Unix (a second writer opens fine and, without O_APPEND,
                    // overwrites this one's lines). A POSIX record lock excludes other writers but no readers. On
                    // Windows FileShare.Read already does, and a lock there would block the log viewer's reads.
                    try
                    {
                        stream.Lock(0, long.MaxValue);
                    }
                    catch (IOException)
                    {
                        stream.Dispose();
                        continue;
                    }
                    catch (PlatformNotSupportedException)
                    {
                    }
                }

                _writer = new StreamWriter(stream, new UTF8Encoding(false));
                return;
            }
            catch (IOException)
            {
                // held by another process of this app: next index
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}

public static class DailyLogFileSinkExtensions
{
    /// <summary>Daily <c>&lt;folder&gt;/YYYY-MM-DD-&lt;app&gt;.txt</c> file, see <see cref="DailyLogFileSink"/>.</summary>
    public static LoggerConfiguration DailyFile(this LoggerSinkConfiguration sinkConfiguration, string folder, string app,
        LogEventLevel restrictedToMinimumLevel = LevelAlias.Minimum) =>
        sinkConfiguration.Sink(
            new DailyLogFileSink(folder, app, new MessageTemplateTextFormatter(DailyLogFileSink.OutputTemplate)),
            restrictedToMinimumLevel);
}
