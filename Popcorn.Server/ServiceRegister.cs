using System.Runtime.InteropServices;
using Popcorn.Linux.Tray;
using Popcorn.Linux.Tray.Interfaces;
using Popcorn.Application.Services;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Services;
using Popcorn.Persistence;
using Popcorn.Persistence.Repositories;
using Popcorn.PlayerServer;
using Popcorn.PlayerServer.Linux;
using Popcorn.PlayerServer.Windows;
using Popcorn.SearchProviders;
using Popcorn.SearchProviders.MovieDb;
using Popcorn.Server.Services;
using Popcorn.Server.Services.Disk;
using Popcorn.Shared.Interfaces;
using Popcorn.Shared.Linux;

namespace Popcorn.Server;

public static class ServiceRegister
{
    public static void AddPopcornServices(this IServiceCollection services)
    {
        services.AddSingleton<ScratchFilesCleanupService>();
        services.AddSingleton<FolderService>();
        services.AddSingleton<MediaItemService>();
        services.AddSingleton<ItemHistoryService>();
        // services.AddSingleton<TorrentCore>();
        // services.AddSingleton<TorrentUtils>();
        services.AddSingleton<ImageRepository>();
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<JsonRepository>();
        services.AddSingleton<MediaItemRefreshService>();
        // services.AddSingleton<SegmentFinderService>();
        services.AddSingleton<IFileService, FileService>();
        services.AddScoped<FileStreamService>();
        
        services.AddScoped<VideoService>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            services.AddSingleton<INativeFileAssosiation, WindowsFileAssociation>();
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            services.AddSingleton<IPlayerService, LinuxPlayerService>();
            services.AddSingleton<INativeFolderService, LinuxFolderService>();
            services.AddSingleton<INativeFileAssosiation, LinuxFileAssociation>();
            services.AddSingleton<INativeProcessRunner, LinuxProcessRunner>();
            services.AddSingleton<ITrayManager, TrayManager>();
            services.AddHostedService<ServerTrayHostedService>();
        }

        services.AddSearchProviders();
        
        services.AddMemoryCache();
    }
}
