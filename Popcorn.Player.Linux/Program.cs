using Gtk;
using Popcorn.Player.Linux.Core;
using Popcorn.Player.Linux.Gtk;
using Popcorn.Player.Linux.Player;
using Popcorn.Shared.Utils;
using Serilog;

namespace Popcorn.Player.Linux;

public class Program
{
    private static ApplicationWindow _window;
    private static MpvPlayer Mpv;
    
    public static int Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.Debug()
            //.WriteTo.File(Path.Combine(DirUtils.DataFolder, $"logs{Path.DirectorySeparatorChar}PopcornPlayer.txt"), rollingInterval: RollingInterval.Day)
            .CreateLogger();
        
        WebKit.Module.Initialize();

        var application = global::Gtk.Application.New("tomiscout.popcorn.player", Gio.ApplicationFlags.FlagsNone);

        application.OnActivate += OnActivate;
        application.OnShutdown += OnShutdown;

        return application.RunWithSynchronizationContext(null);
    }

    private static void OnActivate(Gio.Application app, EventArgs args)
    {
        _window = global::Gtk.ApplicationWindow.New((global::Gtk.Application)app);
        _window.Title = "Popcorn player";
        _window.SetDefaultSize(1280, 720);

        var overlay = global::Gtk.Overlay.New();
        _window.SetChild(overlay);
        
        Mpv = new MpvPlayer();
        overlay.SetChild(MpvGtk.CreateGlArea());
  
        var webView = WebviewGtk.CreateWebView();
        overlay.AddOverlay(webView);
        
        _window.Show();
        
        PlayerCore.Initialize();
        var envPaths = Environment.GetCommandLineArgs().Where(Path.IsPathFullyQualified).ToList();
        if (envPaths.Count > 1) {
            envPaths.RemoveAt(0); //DLL path
            PlayerCore.LoadFilesFromInput(envPaths.Where(arg => !arg.StartsWith("-")).ToList());
        }
        else
        {
            //PlayerCore.LoadMedia(@"/mnt/data/Downloads/JoJo no Kimyou na Bouken Part 5 Ougon no Kaze [Dual]/[Cleo]JoJo_no_Kimyou_na_Bouken_Part_5_Ougon_no_Kaze_-_37_(Dual Audio_10bit_BD1080p_x265).mkv");
            PlayerCore.LoadMedia(@"/mnt/data/Downloads/Sex.Education.S04.COMPLETE.1080p.NF.WEB-DL.DDP5.1.H.264-Archie[TGx]/Sex.Education.S04E01.Episode.1.1080p.NF.WEB-DL.DDP5.1.H.264-Archie.mkv");
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

    public static IPlayer GetPlayer() => Mpv; //Or vlc

    private static void OnShutdown(Gio.Application app, EventArgs args)
    {
        PlayerCore.Dispose();
    }
}