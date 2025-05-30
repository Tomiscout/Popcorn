using GdkPixbuf.Internal;
using Gtk;
using Microsoft.Extensions.DependencyInjection;
using Popcorn.Persistence;
using Popcorn.Player.Core;
using Popcorn.Player.Core.Core;
using Popcorn.Player.Linux.Gtk;
using Popcorn.Player.Core.Player;
using Serilog;
using Popcorn.Player.Core.WebSocket;
using Popcorn.Shared.Utils;
using WebKit;

namespace Popcorn.Player.Linux;

public class Program
{
    private static ApplicationWindow _window;
    private static WebView _webView;
    public static IServiceProvider Services { get; private set; }
    
    public static int Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.Debug()
            .WriteTo.File(Path.Combine(DirUtils.DataFolder, $"logs{Path.DirectorySeparatorChar}Popcorn.Player-.txt"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionTrapper;
        GLib.UnhandledException.SetHandler(GtkUnhandledException);
        var services = new ServiceCollection();
        services.AddPopcornDatabase();
        services.AddPopcornPlayer();
        Services = services.BuildServiceProvider();
        
        _ = Task.Run(ConnectToSocketAsync);
        WebKit.Module.Initialize();

        var application = global::Gtk.Application.New("tomiscout.popcorn.player", Gio.ApplicationFlags.FlagsNone);

        application.OnActivate += OnActivate;
        application.OnShutdown += OnShutdown;

        try
        {
            return application.RunWithSynchronizationContext(null);
        }
        catch (Exception ex)
        {
            GtkUnhandledException(ex);
            return -1;
        }
    }

    private static void OnActivate(Gio.Application app, EventArgs args)
    {
        Log.Information("Building player window");
    
        _window = global::Gtk.ApplicationWindow.New((global::Gtk.Application)app);
        _window.Title = "Popcorn player";
        _window.SetDefaultSize(1280, 720);

        var overlay = global::Gtk.Overlay.New();
        _window.SetChild(overlay);
    
        overlay.SetChild(MpvGtk.CreateGlArea());

        _webView = WebviewGtk.CreateWebView();
        overlay.AddOverlay(_webView);
    
        Log.Information("Showing player window");
        _window.Show();

        StartPlayer();
    }

    private static void StartPlayer()
    {
        Task.Run(async () =>
        {
            var webEventHandler = Services.GetRequiredService<WebEventHandler>();
            webEventHandler.OnFullscreen += SetFullscreen;
            
            var player = Services.GetRequiredService<PlayerCore>();
            
            var envPaths = Environment.GetCommandLineArgs().Skip(1).ToList();
            if (envPaths.Count >= 1)
            {
                if (Guid.TryParse(envPaths[0], out var guidOutput))
                {
                    Log.Information("Loading from file id: {fileId}", guidOutput);
                    await player.LoadFileId(guidOutput);
                }
                else
                {
                    var paths = envPaths.Where(arg => !arg.StartsWith("-")).ToList();
                    Log.Information($"Loading from file path: {string.Join(",", paths)}");
                    await player.LoadFilesFromInput(paths);
                }
            }
            else
            {
                Log.Information("Loading default file");
                await player.LoadFilesFromInput([
                    @"/home/tom/Downloads/Torrent/Bobs Burgers S01-S14 Complete + Movie [1080p WEB-DL x265 HEVC]-CAPT/Series/Season 1/S01E01.mkv"
                ]);
                //player.LoadMedia(@"/mnt/data/Downloads/Sex.Education.S04.COMPLETE.1080p.NF.WEB-DL.DDP5.1.H.264-Archie[TGx]/Sex.Education.S04E01.Episode.1.1080p.NF.WEB-DL.DDP5.1.H.264-Archie.mkv");
                //player.LoadMedia(@"/home/tom/Videos/4K60p.mov");
            }
        }).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                Log.Error(t.Exception, "Unhandled exception occured while loading player");
            }
            else if (t.IsCanceled)
            {
                Log.Warning("StartPlayer was canceled.");
            }
        });
    }
    
    private static void UnhandledExceptionTrapper(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Fatal((Exception)e.ExceptionObject, "An unhandled exception occurred.");
        Log.CloseAndFlush();
        Environment.Exit(1);
    }
    
    private static void GtkUnhandledException(Exception ex)
    {
        Log.Fatal(ex, "An unhandled exception occurred on the GTK thread.");
        Log.CloseAndFlush();
        Environment.Exit(1);
    }

    public static void SetTitle(string title) => _window.Title = title;

    private static bool _isFullscreen = false;
    public static void SetFullscreen(object? sender, bool fullscreen)
    {
        switch (fullscreen)
        {
            case true when !_isFullscreen:
                _window.Fullscreen();
                _isFullscreen = true;
                break;
            case false when _isFullscreen:
                _window.Unfullscreen();
                _isFullscreen = false;
                break;
        }
    }

    public static int GetScaleFactor() => _window.GetScaleFactor();

    private static void OnShutdown(Gio.Application app, EventArgs args)
    {
        _webView.Dispose();
        Services.GetService<PlayerCore>()?.Dispose();
    }

    private const string _socketPath = "/tmp/popcorn_bridge.sock";
    public static PopcornSocketClient PopcornClient { get; private set; }
    private static async Task ConnectToSocketAsync()
    {
        if (!File.Exists(_socketPath))
        {
            Log.Information("Could not find popcorn socket file, Popcorn is not started.");
            return;
        }

        PopcornClient = new PopcornSocketClient(_socketPath);
        
        // Important! If main client sends an event on player startup - it messes up mpv opengl context, and it becomes blue
        // We can fix that with GLib.Functions.IdleAdd, but since the lambda then runs as a native function - we can't handle exceptions, and it crashes without any logs.
        // Setting GLib.UnhandledException should handle this case - but it doesn't :) so let's just not send events on startup
        PopcornClient.MessageReceived += (sender, @event) =>
        {
            _ = Task.Run(() => Services.GetRequiredService<PopcornPipeEventHandler>().Handle(@event)); 
        };
        
        try
        {
            PopcornClient.Start();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Popcorn socket client error");
        }
    }
}
