using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Popcorn.Persistence;
using Popcorn.Player.Core;
using Popcorn.Player.Core.Core;
using Popcorn.Player.Core.Player;
using Popcorn.Player.Core.WebSocket;
using Popcorn.Player.Windows.Native;
using Popcorn.Shared.Ffmpeg;
using Popcorn.Shared.Logging;
using Popcorn.Shared.Utils;
using Serilog;

namespace Popcorn.Player.Windows;

/// <summary>
/// Windows player. The window, video output and WebView2 overlay are native (popcorn_player.dll,
/// Popcorn.Player.Windows.Native); this process hosts the player logic: DB, file navigation, mpv control, the overlay's
/// WebSocket bridge and the link to Popcorn.Server (PlayerLink). Works standalone when the server isn't running.
/// Same structure as Popcorn.Player.Linux.
/// </summary>
public class Program
{
    public static IServiceProvider Services { get; private set; } = null!;
    private static readonly TimeSpan OverlayTimeout = TimeSpan.FromSeconds(10);

    // WebView2 runs in the window's thread, which has to be a single-threaded apartment.
    [STAThread]
    public static int Main(string[] args)
    {
        // Logging first: a WinExe has no console to show anything that fails from here on.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.DailyFile(DirectoryUtils.LogsFolder, "Popcorn.Player",
                restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information)
            .CreateLogger();
        AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionTrapper;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "An unobserved task exception occurred");
            e.SetObserved();
        };
        Log.Information("Popcorn player starting from {dir}, args: {args}", AppContext.BaseDirectory, args);

        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Player failed");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static int Run(string[] args)
    {
        // Bundled FFmpeg is next to the server, one folder up (Player\); in a dev build it may be in our own folder.
        Log.Information("FFmpeg: {Source}", FfmpegBinaries.Configure(AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "..")) ?? "from PATH");
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
            // playerHost: what the overlay offers on top of Linux (dropping files, the host's subtitle file picker).
            InjectScript = $"window.wsAddress = {JsonSerializer.Serialize(wsAddress)}; window.playerHost = 'windows';",
            UserDataDir = Path.Combine(DirectoryUtils.DataFolder, "webview2-player"),
            DevToolsPort = int.TryParse(Environment.GetEnvironmentVariable("POPCORN_PLAYER_DEVTOOLS_PORT"), out var port) ? port : 0,
        });

        webEventHandler.OnFullscreen += (_, fullscreen) => PlayerNative.SetFullscreen(fullscreen);
        webEventHandler.OnCloseRequested += (_, _) => PlayerNative.Quit();
        // Also F11 and the window's own changes, so the overlay's fullscreen button stays right.
        PlayerNative.FullscreenChanged += fullscreen => webEventHandler.PostEvent("fullscreen", fullscreen);
        PlayerNative.CloseRequested += () => Log.Information("Player window closing");
        PlayerNative.FilesDropped += paths => _ = OpenDroppedFilesAsync(paths);

        Services.GetRequiredService<AudioSubtitlesService>().SubtitleFileSelect = () => FileDialog.OpenAsync(
            PlayerNative.WindowHandle, "Add subtitles",
            ("Subtitle files", "*.srt;*.sub;*.ssa;*.ass;*.vtt;*.idx;*.rt;*.smil;*.smi"))!;

        var player = Services.GetRequiredService<IPlayer>();
        player.Initialize();

        // Nothing plays before the overlay page is up (the window shows the logo until then), and the server link
        // comes after it too, so the server can't send a file before the player is usable. If the page never connects
        // (WebView2 broken), play anyway after a while.
        var started = 0;
        void Start(string reason)
        {
            if (Interlocked.Exchange(ref started, 1) != 0) return;
            Log.Information("Starting playback ({reason})", reason);
            StartPlayer(args);
            Services.GetRequiredService<ServerLink>().Start();
        }
        webEventHandler.OverlayConnected += () => Start("overlay connected");
        PlayerNative.Ready += () => Log.Information("Overlay WebView2 created");
        using var fallback = new Timer(_ =>
        {
            if (Volatile.Read(ref started) != 0) return;
            Log.Warning("The overlay did not connect within {seconds} s", OverlayTimeout.TotalSeconds);
            Start("overlay timeout");
        }, null, OverlayTimeout, Timeout.InfiniteTimeSpan);

        Log.Information("Showing player window");
        var exitCode = PlayerNative.Run();
        Log.Information("Player window closed ({exitCode})", exitCode);

        TryDispose("server link", () => Services.GetService<ServerLink>()?.Dispose());
        TryDispose("player core", () => Services.GetService<PlayerCore>()?.Dispose());
        TryDispose("player", player.Dispose);
        PlayerNative.Destroy();
        return exitCode;
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
                return;
            }

            // Relative paths too (started from a shell)
            paths = paths.Select(Path.GetFullPath).ToList();
            foreach (var missing in paths.Where(p => !File.Exists(p))) Log.Warning("File not found: {path}", missing);
            paths = paths.Where(File.Exists).ToList();
            if (paths.Count >= 1)
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
            // No file or a failed load: the overlay shows that over the black window. A file mpv is opening
            // takes the logo away with its first frame (the native layer watches for it).
            if (Services.GetRequiredService<PlayerCore>().CurrentMediaFile is null) PlayerNative.HideSplash();
        });
    }

    private static async Task OpenDroppedFilesAsync(string[] paths)
    {
        try
        {
            var player = Services.GetRequiredService<PlayerCore>();
            foreach (var path in paths) await player.HandleFileDrop(path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not open dropped files");
        }
    }

    private static void TryDispose(string what, Action dispose)
    {
        try { dispose(); }
        catch (Exception e) { Log.Warning(e, "Failed to dispose {what}", what); }
    }

    private static void UnhandledExceptionTrapper(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Fatal((Exception)e.ExceptionObject, "An unhandled exception occurred.");
        Log.CloseAndFlush();
        Environment.Exit(1);
    }
}
