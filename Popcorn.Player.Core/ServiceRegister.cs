using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Popcorn.Application.Services;
using Popcorn.Domain.Interfaces;
using Popcorn.Persistence;
using Popcorn.Persistence.Repositories;
using Popcorn.Player.Core.Core;
using Popcorn.Player.Core.Player;
using Popcorn.Player.Core.Services;
using Popcorn.Player.Core.WebSocket;
using Popcorn.Shared.Interfaces;
using Popcorn.Platform.Linux;
using Popcorn.Platform.Windows;

namespace Popcorn.Player.Core;

public static class ServiceRegister
{
    public static void AddPopcornPlayer(this IServiceCollection services)
    {
        services.AddSingleton<PlayerCore>();
        services.AddSingleton<MediaItemManager>();
        services.AddSingleton<AudioSubtitlesService>();
        services.AddSingleton<ThumbnailService>();
        services.AddSingleton<SubtitleSyncService>();
        services.AddSingleton<DownloadPlaybackClient>();
        services.AddSingleton<WebEventHandler>();
        services.AddSingleton<IPlayer, MpvPlayer>();
        services.AddSingleton<ImageRepository>();
        services.AddSingleton<ServerLink>();
        services.AddSingleton<FFmpegService>();
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<SegmentSkipService>();
        services.AddSingleton<MediaItemService>();
        services.AddSingleton<AppWindowLauncher>();
        services.AddSingleton<MediaSessionService>();

        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            services.AddSingleton<INativeProcessRunner, WindowsProcessRunner>();
            services.AddSingleton<ISleepInhibitor, WindowsSleepInhibitor>();
            services.AddSingleton<ISystemMediaControls, NoSystemMediaControls>();
        } else
        {
            services.AddSingleton<INativeProcessRunner, LinuxProcessRunner>();
            services.AddSingleton<ISleepInhibitor, LinuxSleepInhibitor>();
            services.AddSingleton<ISystemMediaControls, LinuxMediaControls>();
        }

        // Since the app was all static classes at start, sadly we have to deal with circular dependencies
        services.AddTransient(provider => new Lazy<AudioSubtitlesService>(() => provider.GetRequiredService<AudioSubtitlesService>()));
        services.AddTransient(provider => new Lazy<WebEventHandler>(() => provider.GetRequiredService<WebEventHandler>()));
        services.AddTransient(provider => new Lazy<PlayerCore>(() => provider.GetRequiredService<PlayerCore>()));
    }
}