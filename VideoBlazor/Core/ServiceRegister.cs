using Blazored.Modal;
using VideoBlazor.Core.Services;
using VideoBlazor.Domain.Interfaces;
using VideoBlazor.Domain.Services;
using VideoBlazor.Persistence;
using VideoBlazor.Persistence.Repositories;
using VideoBlazor.PlayerServer;
using VideoBlazor.SearchProviders.Utils;
using VideoBlazor.Torrents;

namespace VideoBlazor.Core
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
            services.AddSingleton<PlayerService>();
            services.AddSingleton<IMediaItemRepository, MediaItemRepository>();
            services.AddSingleton<MediaItemRefreshService>();
            services.AddBlazoredModal();
        }
    }
}
