using System.Runtime.InteropServices;
using Popcorn.Application.Services;
using Popcorn.Core.Services;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Services;
using Popcorn.Persistence;
using Popcorn.Persistence.Repositories;
using Popcorn.PlayerServer;
using Popcorn.PlayerServer.Linux;
using Popcorn.Shared.Interfaces;
using Popcorn.Shared.Linux;

namespace Popcorn.Server;

public static class ServiceRegister
{
    public static void AddPopcornServices(this IServiceCollection services)
    {
        // services.AddSingleton<FolderService>();
        services.AddSingleton<MediaItemService>();
        // services.AddSingleton<TorrentCore>();
        // services.AddSingleton<TorrentUtils>();
        services.AddSingleton<ImageRepository>();
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<JsonRepository>();
        services.AddSingleton<MediaItemRefreshService>();
        // services.AddSingleton<SegmentFinderService>();
        services.AddSingleton<IFileService, FileService>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            services.AddSingleton<IPlayerService, LinuxPlayerService>();
            services.AddSingleton<INativeFolderService, LinuxFolderService>();
            services.AddSingleton<INativeFileAssosiation, LinuxFileAssociation>();
            services.AddSingleton<INativeProcessRunner, LinuxProcessRunner>();
        }

        //MovieDbProvider.Initialize();
    }
}
