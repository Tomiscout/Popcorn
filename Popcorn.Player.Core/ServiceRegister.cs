using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Popcorn.Persistence;
using Popcorn.Player.Core.Core;
using Popcorn.Player.Core.Player;
using Popcorn.Player.Core.Services;
using Popcorn.Player.Core.WebSocket;
using Popcorn.Shared.Interfaces;
using Popcorn.Shared.Linux;
using Popcorn.Shared.Windows;

namespace Popcorn.Player.Core;

public static class ServiceRegister
{
    public static void AddPopcornPlayer(this IServiceCollection services)
    {
        services.AddSingleton<PlayerCore>();
        services.AddSingleton<MediaItemManager>();
        services.AddSingleton<AudioSubtitlesService>();
        services.AddSingleton<ThumbnailService>();
        services.AddSingleton<WebEventHandler>();
        services.AddSingleton<IPlayer, MpvPlayer>();
        services.AddSingleton<ImageRepository>();
        services.AddSingleton<PopcornPipeEventHandler>();
        services.AddSingleton<FFmpegService>();

        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            services.AddSingleton<INativeProcessRunner, WindowsProcessRunner>();
        } else
        {
            services.AddSingleton<INativeProcessRunner, LinuxProcessRunner>();
        }

        // Since the app was all static classes at start, sadly we have to deal with circular dependencies
        services.AddTransient(provider => new Lazy<AudioSubtitlesService>(() => provider.GetRequiredService<AudioSubtitlesService>()));
        services.AddTransient(provider => new Lazy<WebEventHandler>(() => provider.GetRequiredService<WebEventHandler>()));
        services.AddTransient(provider => new Lazy<PlayerCore>(() => provider.GetRequiredService<PlayerCore>()));
    }
}