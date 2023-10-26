using VideoBlazor.Core;
using MudBlazor.Services;
using VideoBlazor.SearchProviders.MovieDb;
using VideoBlazor.SearchProviders.Utils;
using Serilog;
using VideoBlazor.Shared.Utils;
using Serilog.Events;

namespace VideoBlazor;

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
            .WriteTo.File(Path.Combine(DirUtils.DataFolder, "logs\\VideoBlazor.txt"), rollingInterval: RollingInterval.Day)
            .CreateLogger();
        Serilog.Debugging.SelfLog.Enable(m => Console.Error.WriteLine(m));
    }

    public static MauiApp CreateMauiApp()
	{
        var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

        builder.Services.AddLogging(builder => builder.AddSerilog());
        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddMudServices();

#if WINDOWS
        builder.Services.AddTransient<Platforms.Windows.IFolderPicker, Platforms.Windows.FolderPicker>();
#endif

//#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
//#endif

        builder.Logging.AddSerilog(dispose: true);

        ServiceRegister.RegisterServices(builder.Services);

		TorrentUtils.EnsureTorrentsFolder();
        MovieDbProvider.Initialize();

        return builder.Build();
	}

    private static void SetupSerilog()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.Debug()
            .WriteTo.File(Path.Combine(DirUtils.DataFolder, "logs\\VideoBlazor.txt"), rollingInterval: RollingInterval.Day)
            .CreateLogger();
    }
}
