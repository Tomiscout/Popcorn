using Popcorn.Core.Services;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Services;
using Popcorn.Persistence;
using Popcorn.Persistence.Repositories;

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
        services.AddSingleton<IMediaItemRepository, MediaItemRepository>();
        // services.AddSingleton<SegmentFinderService>();
        //services.AddSingleton<IFileService, FileService>();

        //MovieDbProvider.Initialize();
    }
}
