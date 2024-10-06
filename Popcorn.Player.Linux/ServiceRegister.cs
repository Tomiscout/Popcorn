using Fleck;
using Microsoft.Extensions.DependencyInjection;
using Popcorn.Player.Linux.Core;
using Popcorn.Player.Linux.Player;
using Popcorn.Player.Linux.Services;
using Popcorn.Player.Linux.WebSocket;

namespace Popcorn.Player.Linux;

public static class ServiceRegister
{
    public static void AddPopcornPlayer(this IServiceCollection services)
    {
        services.AddSingleton<PlayerCore>();
        services.AddSingleton<MediaItemManager>();
        services.AddSingleton<AudioSubtitlesService>();
        services.AddSingleton<ThumbnailService>();
        services.AddSingleton<PlayerWebSocket>();
        services.AddSingleton<IPlayer, MpvPlayer>();
        
        // Since the app was all static classes at start, sadly we have to deal with circular dependencies
        services.AddTransient<Lazy<PlayerWebSocket>>(provider => new Lazy<PlayerWebSocket>(() => provider.GetRequiredService<PlayerWebSocket>()));
        services.AddTransient<Lazy<PlayerCore>>(provider => new Lazy<PlayerCore>(() => provider.GetRequiredService<PlayerCore>()));
    }
}