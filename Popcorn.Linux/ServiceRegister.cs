using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Popcorn.Core.Services;
using Popcorn.Core.Utils;
using Popcorn.Domain;
using Popcorn.Domain.Services;
using Popcorn.Persistence;
using Popcorn.PlayerServer;
using Popcorn.PlayerServer.Windows;
using Popcorn.SearchProviders.MovieDb;
using Popcorn.SearchProviders.Utils;
using Popcorn.SegmentFinder;
using Popcorn.Torrents;

namespace Popcorn.Core;

public static class ServiceRegister
{
    public static void AddPopcornServices(this IServiceCollection services)
    {
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
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            services.AddSingleton<IPlayerService, WindowsPlayerService>();
        }
        else
        {
            services.AddSingleton<IPlayerService, LinuxPlayerService>();
        }

        MovieDbProvider.Initialize();
    }
}
