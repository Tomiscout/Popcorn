using Gtk;
using Microsoft.Extensions.DependencyInjection;
using Popcorn.Persistence;
using Popcorn.Player.Linux.Core;
using Popcorn.Player.Linux.Gtk;
using Popcorn.Player.Linux.Player;
using Serilog;

namespace Popcorn.Player.Linux;

public class Program
{
    private static ApplicationWindow _window;
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
        application.OnShutdown += OnShutdown; ;

        return application.RunWithSynchronizationContext(null);
    }

    private static void OnActivate(Gio.Application app, EventArgs args)
    {
        _window = global::Gtk.ApplicationWindow.New((global::Gtk.Application)app);
        _window.Title = "Popcorn player";
        _window.SetDefaultSize(1280, 720);

        var overlay = global::Gtk.Overlay.New();
        _window.SetChild(overlay);
        
        overlay.SetChild(MpvGtk.CreateGlArea());
  
        var webView = WebviewGtk.CreateWebView();
        overlay.AddOverlay(webView);
        
        _window.Show();
        
        var player = Services.GetService<PlayerCore>()!;
        var envPaths = Environment.GetCommandLineArgs().Skip(1).ToList();
        if (envPaths.Count > 1)
        {
            if (Guid.TryParse(envPaths[0], out var guidOutput))
            {
                player.LoadFileId(guidOutput);
            }
            else
            {
                player.LoadFilesFromInput(envPaths.Where(arg => !arg.StartsWith("-")).ToList());
            }
        }
        else
        {
            //PlayerCore.LoadMedia(@"/mnt/data/Downloads/JoJo no Kimyou na Bouken Part 5 Ougon no Kaze [Dual]/[Cleo]JoJo_no_Kimyou_na_Bouken_Part_5_Ougon_no_Kaze_-_37_(Dual Audio_10bit_BD1080p_x265).mkv");
            player.LoadMedia(@"/mnt/data/Downloads/Sex.Education.S04.COMPLETE.1080p.NF.WEB-DL.DDP5.1.H.264-Archie[TGx]/Sex.Education.S04E01.Episode.1.1080p.NF.WEB-DL.DDP5.1.H.264-Archie.mkv");
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
        Services.GetService<PlayerCore>()?.Dispose();
    }

    private const string _socketPath = "/tmp/popcorn_bridge.sock";
    public static PopcornSocketClient PopcornClient { get; private set; }
    private static async Task ConnectToSocketAsync()
    {
        if (!File.Exists(_socketPath))
        {
            Log.Warning("Could not find popcorn socket file, Popcorn is not started.");
            return;
        }
        
        PopcornClient = new PopcornSocketClient(_socketPath);
        PopcornClient.Start();
    }
}
