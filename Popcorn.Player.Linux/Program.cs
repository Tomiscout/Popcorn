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
            //.WriteTo.File(Path.Combine(DirUtils.DataFolder, $"logs{Path.DirectorySeparatorChar}PopcornPlayer.txt"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddPopcornDatabase();
        services.AddPopcornPlayer();
        Services = services.BuildServiceProvider();
        
        _ = Task.Run(ConnectToSocketAsync);
        WebKit.Module.Initialize();

        var application = global::Gtk.Application.New("tomiscout.popcorn.player", Gio.ApplicationFlags.FlagsNone);

        application.OnActivate += OnActivate;
        application.OnShutdown += OnShutdown;

        return application.RunWithSynchronizationContext(null);
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
        
        var player = Services.GetService<PlayerCore>()!;
        var envPaths = Environment.GetCommandLineArgs().Skip(1).ToList();
        if (envPaths.Count >= 1)
        {
            if (Guid.TryParse(envPaths[0], out var guidOutput))
            {
                Log.Logger.Information("Loading from file id: {fileId}", guidOutput);
                player.LoadFileId(guidOutput);
            }
            else
            {
                var paths = envPaths.Where(arg => !arg.StartsWith("-")).ToList();
                Log.Logger.Information($"Loading from file path: {string.Join(",", paths)}");
                player.LoadFilesFromInput(paths);
            }
        }
        else
        {
            Log.Logger.Information("Loading default file");
            player.LoadMedia(@"/home/tom/Downloads/Torrent/Bobs Burgers S01-S14 Complete + Movie [1080p WEB-DL x265 HEVC]-CAPT/Series/Season 1/S01E01.mkv");
            //player.LoadMedia(@"/mnt/data/Downloads/Sex.Education.S04.COMPLETE.1080p.NF.WEB-DL.DDP5.1.H.264-Archie[TGx]/Sex.Education.S04E01.Episode.1.1080p.NF.WEB-DL.DDP5.1.H.264-Archie.mkv");
            //player.LoadMedia(@"/home/tom/Videos/4K60p.mov");
        }
    }

    public static void SetTitle(string title) => _window.Title = title;

    private static bool _isFullscreen = false;
    public static void SetFullscreen(bool fullscreen)
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
        PopcornClient.MessageReceived += (sender, @event) =>
        {
            // Need to marshal this on GTK thread, otherwise opengl surface is blue
            GLib.Functions.IdleAdd(0, () =>
            {
                try
                {
                    Services.GetService<PopcornPipeEventHandler>().Handle(@event);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error handling message from Popcorn client");
                }
                return false;
            });
        };
        PopcornClient.Start();
    }
}
