using System.Runtime.InteropServices;
using Popcorn.Application.Services;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Services;
using Popcorn.Downloads;
using Popcorn.Persistence;
using Popcorn.Persistence.Repositories;
using Popcorn.Platform.Linux;
using Popcorn.Platform.Windows;
using Popcorn.SegmentFinder;
using Popcorn.Server.PluginHost;
using Popcorn.Server.Push;
using Popcorn.Server.Services;
using Popcorn.Server.Services.Disk;
using Popcorn.Shared.Interfaces;
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
        services.AddSingleton<SocketSubscriptions>();
        services.AddHostedService<LivePushService>();
        

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            services.AddSingleton<IPlayerService, WindowsPlayerService>();
            services.AddSingleton<INativeFolderService, WindowsFolderService>();
            services.AddSingleton<INativeFileAssosiation, WindowsFileAssociation>();
            services.AddSingleton<INativeProcessRunner, WindowsProcessRunner>();
            services.AddSingleton<INativeAutostart, WindowsAutostart>();
            services.AddSingleton<ITrayManager, WindowsTrayManager>();
            services.AddHostedService<ServerTrayHostedService>();
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            services.AddSingleton<IPlayerService, LinuxPlayerService>();
            services.AddSingleton<INativeFolderService, LinuxFolderService>();
            services.AddSingleton<INativeFileAssosiation, LinuxFileAssociation>();
            services.AddSingleton<INativeProcessRunner, LinuxProcessRunner>();
            services.AddSingleton<INativeAutostart, LinuxAutostart>();
            services.AddSingleton<ITrayManager, LinuxTrayManager>();
            services.AddHostedService<ServerTrayHostedService>();
        }

        services.TryAddSingleton<INativeAutostart, UnsupportedAutostart>();
        services.AddSingleton<PluginVariableStore>();
        services.AddSingleton<PluginEventHub>();
        services.AddSingleton<PluginManager>();
        services.AddHostedService<PluginShutdownService>();
        services.AddSingleton<IMediaDescriptionProvider, PluginMediaDescriptions>();
        
        services.AddMemoryCache();
    }
}
