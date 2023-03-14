using VideoBlazor.Core;
using MudBlazor.Services;
using VideoBlazor.SearchProviders.MovieDb;
using VideoBlazor.SearchProviders.Utils;
using VideoBlazor.Torrent;
using VideoBlazor.Torrents;

namespace VideoBlazor;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
        builder.Services.AddMudServices();

#if WINDOWS
        builder.Services.AddTransient<Platforms.Windows.IFolderPicker, Platforms.Windows.FolderPicker>();
#endif

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

		ServiceRegister.RegisterServices(builder.Services);
		JsonUtils.EnsureAppFolder();
		ImageUtils.EnsureImageFolder();
		TorrentUtils.EnsureTorrentsFolder();
        MovieDbProvider.Initialize();
        TorrentCore.Initialize();

        return builder.Build();
	}
}
