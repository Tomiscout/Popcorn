using Popcorn.Shared.Ffmpeg;
using Popcorn.Shared.Utils;
using Serilog;

namespace Popcorn.Server;

public abstract class Program
{
    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
            .WriteTo.Console()
            .WriteTo.Debug()
            .WriteTo.File(Path.Combine(DirectoryUtils.DataFolder, $"logs{Path.DirectorySeparatorChar}Popcorn.Server-.txt"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        var ffmpeg = FfmpegBinaries.Configure();
        Log.Information("FFmpeg: {Source}", ffmpeg ?? "from PATH");

        // Windows has no service manager keeping one server per user. It is started at login, by the desktop app
        // (Popcorn.Windows) and by hand; a second one would only fail on the port, so it exits right away instead.
        using var instanceLock = OperatingSystem.IsWindows() ? new Mutex(false, @"Local\Popcorn.Server") : null;
        if (instanceLock is not null && !TryAcquire(instanceLock))
        {
            Log.Information("Popcorn.Server is already running, exiting.");
            await Log.CloseAndFlushAsync();
            return;
        }

        // Popcorn.Installer sets this event to stop the server cleanly (database, downloads state) before replacing its
        // files; it kills the process only when that takes too long. Auto-reset, so a signal never outlives this server.
        using var shutdown = new CancellationTokenSource();
        using var shutdownEvent = OperatingSystem.IsWindows()
            ? new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Popcorn.Server.Shutdown")
            : null;
        var shutdownWait = shutdownEvent is null
            ? null
            : ThreadPool.RegisterWaitForSingleObject(shutdownEvent, (_, _) =>
            {
                Log.Information("Shutdown requested (installer).");
                shutdown.Cancel();
            }, null, Timeout.Infinite, executeOnlyOnce: true);

        try
        {
            await using var serverHost = new PopcornServerHost(args);
            await serverHost.RunAsync(Log.Logger, shutdown.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            Log.Fatal(ex, "Popcorn.Server terminated unexpectedly.");
        }
        finally
        {
            shutdownWait?.Unregister(null);
            Log.CloseAndFlush();
        }
    }

    private static bool TryAcquire(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            return true; // the previous server died holding it; ours now
        }
    }
}
