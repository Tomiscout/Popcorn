using VideoBlazor.Core;
using MudBlazor.Services;
using VideoBlazor.SearchProviders.MovieDb;
using VideoBlazor.SearchProviders.Utils;
using Serilog;

namespace VideoBlazor;

public static class MauiProgram
{
    static MauiProgram() {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.Debug()
            .WriteTo.File("logs\\VideoBlazor.txt", rollingInterval: RollingInterval.Day)
            .CreateLogger();
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

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

		ServiceRegister.RegisterServices(builder.Services);

		TorrentUtils.EnsureTorrentsFolder();
        MovieDbProvider.Initialize();

        return builder.Build();
	}
}
