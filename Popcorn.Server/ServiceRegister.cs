using System.Runtime.InteropServices;
using Popcorn.Application.Services;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Services;
using Popcorn.Downloads;
using Popcorn.Persistence;
using Popcorn.Persistence.Repositories;
using Popcorn.PlayerServer;
using Popcorn.PlayerServer.Linux;
using Popcorn.PlayerServer.Windows;
using Popcorn.SegmentFinder;
using Popcorn.Server.PluginHost;
using Popcorn.Server.Services;
using Popcorn.Server.Services.Disk;
using Popcorn.Shared.Interfaces;
using Popcorn.Shared.Linux;
using Popcorn.Shared.Windows;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Popcorn.Server;

public static class ServiceRegister
{
    public static void AddPopcornServices(this IServiceCollection services)
    {
        services.AddSingleton<ScratchFilesCleanupService>();
        services.AddSingleton<FolderService>();
        services.AddSingleton<MediaItemService>();
        services.AddSingleton<MediaImageService>();
        services.AddSingleton<ItemHistoryService>();
        services.AddSingleton<SegmentSkipService>();
        services.AddSingleton<DownloadManager>();
        services.AddHostedService(sp => sp.GetRequiredService<DownloadManager>());
        services.AddSingleton<ImageRepository>();
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<JsonRepository>();
        services.AddSingleton<MediaItemRefreshService>();
        services.AddSingleton<SegmentFinderService>();
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<PlayerLaunchService>();
        services.AddSingleton<UpdateService>();
        services.AddScoped<FileStreamService>();
        services.AddSingleton<AudioTrackService>();
        

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            services.AddSingleton<IPlayerService, WindowsPlayerService>();
            services.AddSingleton<INativeFolderService, WindowsFolderService>();
            services.AddSingleton<INativeFileAssosiation, WindowsFileAssociation>();
            services.AddSingleton<INativeProcessRunner, WindowsProcessRunner>();
            services.AddSingleton<INativeAutostart, WindowsAutostart>();
            services.AddSingleton<ITrayManager, Popcorn.Windows.Tray.TrayManager>();
            services.AddHostedService<ServerTrayHostedService>();
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            services.AddSingleton<IPlayerService, LinuxPlayerService>();
            services.AddSingleton<INativeFolderService, LinuxFolderService>();
            services.AddSingleton<INativeFileAssosiation, LinuxFileAssociation>();
            services.AddSingleton<INativeProcessRunner, LinuxProcessRunner>();
            services.AddSingleton<INativeAutostart, LinuxAutostart>();
            services.AddSingleton<ITrayManager, Popcorn.Linux.Tray.TrayManager>();
            services.AddHostedService<ServerTrayHostedService>();
        }

        services.TryAddSingleton<INativeAutostart, UnsupportedAutostart>();
        services.AddSingleton<PluginVariableStore>();
        services.AddSingleton<PluginManager>();
        services.AddSingleton<IMediaDescriptionProvider, PluginMediaDescriptions>();
        
        services.AddMemoryCache();
    }
}
