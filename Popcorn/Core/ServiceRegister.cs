using Blazored.Modal;
using Popcorn.Core.Services;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Services;
using Popcorn.Persistence;
using Popcorn.Persistence.Repositories;
using Popcorn.PlayerServer;
using Popcorn.PlayerServer.Windows;
using Popcorn.SearchProviders.Utils;
using Popcorn.SegmentFinder;
using Popcorn.Torrents;

namespace Popcorn.Core
{
    public static class ServiceRegister
    {
        public static void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton<FolderService>();
            services.AddSingleton<MediaItemService>();
            services.AddSingleton<TorrentCore>();
            services.AddSingleton<TorrentUtils>();
            services.AddSingleton<ImageRepository>();
            services.AddSingleton<SettingsRepository>();
            services.AddSingleton<JsonRepository>();
            services.AddSingleton<IPlayerService, WindowsPlayerService>();
            services.AddSingleton<IMediaItemRepository, MediaItemRepository>();
            services.AddSingleton<MediaItemRefreshService>();
            services.AddSingleton<SegmentFinderService>();
            services.AddBlazoredModal();
        }
    }
}
