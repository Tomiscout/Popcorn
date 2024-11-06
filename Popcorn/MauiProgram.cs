using MudBlazor.Services;
using Popcorn.SearchProviders.MovieDb;
using Popcorn.SearchProviders.Utils;
using Serilog;
using Popcorn.Shared.Utils;
using PopcornPlayer.Shared;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Storage;
using Popcorn.Persistence;
using FluentMigrator.Runner;
using MathNet.Numerics;
using Popcorn.Domain.Interfaces;
using Popcorn.PlayerServer.Windows;
using Popcorn.PlayerServer;
using Popcorn.Shared;
using Popcorn.Services;

namespace Popcorn;

public static class MauiProgram
{
    static MauiProgram() {
        if(!Directory.Exists(DirUtils.DataFolder))
        {
            Directory.CreateDirectory(DirUtils.DataFolder);
        }

        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.Debug()
            .WriteTo.File(Path.Combine(DirUtils.DataFolder, $"logs{Path.DirectorySeparatorChar}Popcorn.txt"), rollingInterval: RollingInterval.Day)
            .CreateLogger();
        Serilog.Debugging.SelfLog.Enable(m => Console.Error.WriteLine(m));
    }

    public static MauiApp CreateMauiApp()
	{
        var builder = MauiApp.CreateBuilder();
        builder
			.UseMauiApp<App>()
            .UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

        builder.Services.AddLogging(builder => builder.AddSerilog());
        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddMudServices();

       
        // Register the FolderPicker as a singleton

        //#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
//#endif

        builder.Logging.AddSerilog(dispose: true);

        WebView.ServiceRegister.AddPopcornServices(builder.Services);
        ServiceRegister.AddPopcornDatabase(builder.Services);


        builder.Services.AddSingleton(FolderPicker.Default);
        builder.Services.AddSingleton<INativeFolderService, WindowsFolderService>();

        builder.Services.AddSingleton<IPlayerService, WindowsPlayerService>();
        builder.Services.AddSingleton<INativeFileAssosiation, WindowsFileAssosiation>();
        builder.Services.AddSingleton<INativeProcessRunner, WindowsProcessRunner>();

        TorrentUtils.EnsureTorrentsFolder();
        MovieDbProvider.Initialize();
        FFmpegWrapper.ExePath = "ffmpeg"; //TODO proper linking

        var app = builder.Build();

        var runner = app.Services.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();

        return app;
	}
}
