using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Serilog;
using Popcorn.Shared.Utils;
using PopcornPlayer.Shared;
using Microsoft.Extensions.DependencyInjection;
using Popcorn.Player.Core;
using Popcorn.Persistence;
using Popcorn.Player.Core.Player;
using Popcorn.Player.Core.Core;
using Popcorn.Player.Core.WebSocket;
using Popcorn.Player.Windows.Pipe;

namespace Popcorn.Player.Windows;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static bool _isShuttingDown = false;
    private static object _shutdownLock = new object();
    public static IServiceProvider Services { get; private set; }
    public static IConfiguration Config { get; private set; }

    public App()
    {
        Config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        var services = new ServiceCollection();
        services.AddPopcornDatabase();
        services.AddPopcornPlayer();
        services.AddSingleton<ClientPipeManager>();
        
        Services = services.BuildServiceProvider();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.Debug()
            .WriteTo.File(Path.Combine(DirUtils.DataFolder, $"logs{Path.DirectorySeparatorChar}PopcornPlayer.txt"), rollingInterval: RollingInterval.Day)
            .CreateLogger();
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var exception = e.ExceptionObject as Exception;
            Log.Fatal(exception, "An unhandled exception occurred.");
            Log.CloseAndFlush();
        };
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Log.Error(e.Exception, "An unobserved task exception occurred.");
            e.SetObserved(); // This prevents the exception from terminating the application.
        };
        DispatcherUnhandledException += (s, e) =>
        {
            Log.Error(e.Exception, "An unhandled exception occurred within the WPF dispatcher.");
            GracefulShutdown();
        };

        FFmpegWrapper.ExePath = "ffmpeg"; //TODO proper linking
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        GracefulShutdown();
        base.OnExit(e);
    }
 
    public static void GracefulShutdown()
    {
        lock (_shutdownLock) // This could be executed from multiple locations, but ran only once
        {
            if (_isShuttingDown) return;
            _isShuttingDown = true;
        }

        Services.GetService<ClientPipeManager>().Dispose();
        Services.GetService<PlayerCore>().Dispose();
        Log.CloseAndFlush();
    }

    internal static void InitializeMpv(nint playerHostPtr)
    {
        Services.GetService<IPlayer>().InitializeWindows(playerHostPtr.ToString());
    }

    internal static void SetupEvents()
    {
        Services.GetService<WebEventHandler>().OnFullscreen += (object sender, bool isFullscreen) =>
        {
            ((MainWindow)Application.Current.MainWindow).ChangeFullscreen(isFullscreen);
        };
    }
}
