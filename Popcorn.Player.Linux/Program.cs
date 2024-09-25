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
        overlay.SetChild(new MpvGtk().GlArea);
  
        var webView = WebviewGtk.CreateWebView();
        overlay.AddOverlay(webView);
        
        _window.Show();
        
        PlayerCore.Initialize();
        PlayerCore.LoadMedia("/home/tom/Downloads/test.h264");
    }

    public static void SetTitle(string title) => _window.Title = title;
    public static int GetScaleFactor() => _window.GetScaleFactor();

    public static IPlayer GetPlayer() => Mpv; //Or vlc

    private static void OnShutdown(Gio.Application app, EventArgs args)
    {
        PlayerCore.Dispose();
    }
}