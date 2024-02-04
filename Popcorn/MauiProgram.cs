using Popcorn.Core;
using MudBlazor.Services;
using Popcorn.SearchProviders.MovieDb;
using Popcorn.SearchProviders.Utils;
using Serilog;
using Popcorn.Shared.Utils;
using Serilog.Events;
using PopcornPlayer.Shared;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Storage;

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
            .WriteTo.File(Path.Combine(DirUtils.DataFolder, "logs\\Popcorn.txt"), rollingInterval: RollingInterval.Day)
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
        builder.Services.AddSingleton<IFolderPicker>(FolderPicker.Default);

        //#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
//#endif

        builder.Logging.AddSerilog(dispose: true);

        ServiceRegister.RegisterServices(builder.Services);

		TorrentUtils.EnsureTorrentsFolder();
        MovieDbProvider.Initialize();
        FFmpegWrapper.ExePath = "ffmpeg"; //TODO proper linking

        return builder.Build();
	}
}
