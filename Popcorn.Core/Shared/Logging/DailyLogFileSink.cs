using System.Text;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Parsing;

namespace Popcorn.Shared.Logging;

/// <summary>
/// Serilog file sink writing <see cref="LogFileName"/> names (Serilog's rolling sink can only put the date after the
/// name). Rolls over at local midnight; when another process of the same app holds the day's file, the next free
/// <c>_001</c>, <c>_002</c>... name is used. Flushed after every event so the Settings → Logs viewer sees it live.
/// A file growing past <see cref="MaxFileBytes"/> (a log loop) is emptied and starts again with the last
/// <see cref="KeptEventsOnTruncate"/> events and a warning saying so.
/// Also linked into Popcorn.Windows, keep its dependencies to Serilog.
/// </summary>
public sealed class DailyLogFileSink(string folder, string app, ITextFormatter formatter, long maxFileBytes = DailyLogFileSink.MaxFileBytes)
    : ILogEventSink, IDisposable
{
    public const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    public const long MaxFileBytes = 100L * 1024 * 1024;
    public const int KeptEventsOnTruncate = 10;

    /// <summary>The last events as written, for the restart of a truncated file.</summary>
    private readonly Queue<string> _recent = new(KeptEventsOnTruncate);

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
                var text = Format(logEvent);
                _writer.Write(text);
                _writer.Flush();
                if (_recent.Count == KeptEventsOnTruncate) _recent.Dequeue();
                _recent.Enqueue(text);
                if (_writer.BaseStream.Length > maxFileBytes) Truncate();
            }
            catch (IOException)
            {
                // disk full or file gone: drop the event rather than the app
            }
        }
    }

    private string Format(LogEvent logEvent)
    {
        using var text = new StringWriter();
        formatter.Format(logEvent, text);
        return text.ToString();
    }

    /// <summary>Empties the file and writes the last events back, followed by a warning about it.</summary>
    private void Truncate()
    {
        var now = DateTimeOffset.Now;
        var notice = new LogEvent(now, LogEventLevel.Warning, null,
            new MessageTemplateParser().Parse(
                $"This log file was truncated due to its size exceeding {FormatSize(maxFileBytes)} at {now:yyyy-MM-dd HH:mm:ss}"),
            []);

        var stream = _writer!.BaseStream;
        stream.SetLength(0);
        stream.Position = 0;
        foreach (var text in _recent) _writer.Write(text);
        _writer.Write(Format(notice));
        _writer.Flush();
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / (1024 * 1024)}mb" : $"{bytes} bytes";

    private void Open(DateOnly day)
    {
        _writer?.Dispose();
        _writer = null;
        _recent.Clear();
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
                // Not FileMode.Append: it can't be truncated (Truncate). Only this process writes it (lock below), so
                // writing from the end it had on opening is the same.
                var stream = new FileStream(Path.Combine(folder, LogFileName.Format(app, day, i)), FileMode.OpenOrCreate,
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

                stream.Seek(0, SeekOrigin.End);
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
