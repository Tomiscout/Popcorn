using Blazored.Modal;
using Microsoft.Extensions.DependencyInjection;
using Popcorn.Application.Services;
using Popcorn.Core.Services;
using Popcorn.Core.Utils;
using Popcorn.Domain;
using Popcorn.Domain.Services;
using Popcorn.Persistence;
using Popcorn.Persistence.Repositories;
using Popcorn.SearchProviders.MovieDb;
using Popcorn.SearchProviders.Utils;
using Popcorn.SegmentFinder;
using Popcorn.Torrents;

namespace Popcorn.WebView;

public static class ServiceRegister
{
    public static void AddPopcornServices(this IServiceCollection services)
    {
        services.AddBlazoredModal();
        
        services.AddSingleton<FolderService>();
        services.AddSingleton<MediaItemService>();
        services.AddSingleton<TorrentCore>();
        services.AddSingleton<TorrentUtils>();
        services.AddSingleton<ImageRepository>();
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<JsonRepository>();
        services.AddSingleton<MediaItemRefreshService>();
        services.AddSingleton<SegmentFinderService>();
        services.AddSingleton<IFileService, FileService>();

        MovieDbProvider.Initialize();
    }
}
