using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Popcorn.Persistence;
using Popcorn.Player.Core;
using Popcorn.Player.Core.Core;
using Popcorn.Player.Core.Player;
using Popcorn.Player.Core.WebSocket;
using Popcorn.Player.Linux.Native;
using Popcorn.Shared.Ffmpeg;
using Popcorn.Shared.Logging;
using Popcorn.Shared.Utils;
using Serilog;

namespace Popcorn.Player.Linux;

/// <summary>
/// Linux player (Wayland only). The window, video output and CEF overlay are native (libpopcorn_player.so,
/// Popcorn.Player.Native); this process hosts the player logic: DB, file navigation, mpv control, the overlay's
/// WebSocket bridge and the link to Popcorn.Server. Works standalone when the server isn't running.
/// </summary>
public class Program
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static int Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.Debug()
            .WriteTo.DailyFile(DirectoryUtils.LogsFolder, "Popcorn.Player",
                restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information)
            .CreateLogger();
        AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionTrapper;

        var cacheDir = CreateCefCacheDir();
        try
        {
            return Run(args, cacheDir);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Player failed");
            return 1;
        }
        finally
        {
            TryDeleteDirectory(cacheDir);
            Log.CloseAndFlush();
        }
    }

    private static int Run(string[] args, string cacheDir)
    {
        Log.Information("FFmpeg: {Source}", FfmpegBinaries.Configure() ?? "from PATH");
        var services = new ServiceCollection();
        services.AddPopcornDatabase();
        services.AddPopcornPlayer();
        // The mpv core is created by the native layer (it owns the video output); we only attach a client.
        services.AddSingleton<IMpvClient>(sp => new LibMpvClient(PlayerNative.MpvCore, sp.GetRequiredService<ILogger<LibMpvClient>>()));
        Services = services.BuildServiceProvider();

        var webEventHandler = Services.GetRequiredService<WebEventHandler>();
        var wsAddress = webEventHandler.StartServer();

        var baseDir = AppContext.BaseDirectory;
        PlayerNative.Init(new PlayerNativeOptions
        {
            OverlayDir = Path.Combine(baseDir, "web-player"),
            OverlayPage = "index.html",
            InjectScript = $"window.wsAddress = {JsonSerializer.Serialize(wsAddress)};",
            CacheDir = cacheDir,
            CefLogFile = Path.Combine(DirectoryUtils.DataFolder, "logs", "cef-player.log"),
            SubprocessPath = Path.Combine(baseDir, "popcorn-player-cef"),
            DevToolsPort = int.TryParse(Environment.GetEnvironmentVariable("POPCORN_PLAYER_DEVTOOLS_PORT"), out var port) ? port : 0,
        });

        webEventHandler.OnFullscreen += (_, fullscreen) => PlayerNative.SetFullscreen(fullscreen);
        webEventHandler.OnCloseRequested += (_, _) => PlayerNative.Quit();
        // Also F11 and compositor-initiated changes, so the overlay's fullscreen button stays right.
        PlayerNative.FullscreenChanged += fullscreen => webEventHandler.PostEvent("fullscreen", fullscreen);
        PlayerNative.CloseRequested += () => Log.Information("Player window closing");

        var player = Services.GetRequiredService<IPlayer>();
        player.Initialize();

        // Close the window the normal way (overlay, mpv and CEF shut down in order) on kill/Ctrl+C.
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnQuitSignal);
        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnQuitSignal);

        // Only after CEF start-up: loading runs ffprobe/ffmpeg, see PlayerNative.Ready. The server link comes after it
        // too, so the server can't send a file before the player is usable.
        PlayerNative.Ready += () =>
        {
            Log.Information("Player ready");
            StartPlayer(args);
            Services.GetRequiredService<ServerLink>().Start();
        };

        Log.Information("Showing player window");
        var exitCode = PlayerNative.Run();
        Log.Information("Player window closed ({exitCode})", exitCode);

        Services.GetService<ServerLink>()?.Dispose();
        Services.GetService<PlayerCore>()?.Dispose();
        player.Dispose();
        PlayerNative.Destroy();
        return exitCode;
    }

    private static void OnQuitSignal(PosixSignalContext context)
    {
        context.Cancel = true;
        Log.Information("Received {signal}, closing", context.Signal);
        PlayerNative.Quit();
    }

    private static void StartPlayer(string[] args)
    {
        Task.Run(async () =>
        {
            var player = Services.GetRequiredService<PlayerCore>();

            var paths = args.Where(arg => !arg.StartsWith('-')).ToList();
            if (paths.Count >= 1 && Guid.TryParse(paths[0], out var fileId))
            {
                Log.Information("Loading from file id: {fileId}", fileId);
                await player.LoadFileId(fileId);
            }
            else if (paths.Count >= 1)
            {
                Log.Information("Loading from file path: {paths}", string.Join(",", paths));
                await player.LoadFilesFromInput(paths);
            }
            else
            {
                Log.Information("No file to load, waiting for one");
                player.ShowNoFile();
            }
        }).ContinueWith(t =>
        {
            if (t.IsFaulted) Log.Error(t.Exception, "Unhandled exception occured while loading player");
        });
    }

    /// <summary>
    /// A CEF profile can only be used by one process at a time and the overlay keeps no state, so every player
    /// process gets a throwaway one.
    /// </summary>
    private static string CreateCefCacheDir()
    {
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var root = string.IsNullOrEmpty(runtimeDir) ? Path.GetTempPath() : runtimeDir;
        var dir = Path.Combine(root, $"popcorn-player-cef-{Environment.ProcessId}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not delete {dir}", dir);
        }
    }

    private static void UnhandledExceptionTrapper(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Fatal((Exception)e.ExceptionObject, "An unhandled exception occurred.");
        Log.CloseAndFlush();
        Environment.Exit(1);
    }
}
