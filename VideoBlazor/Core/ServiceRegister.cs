using Blazored.Modal;
using VideoBlazor.Core.Services;
using VideoBlazor.Persistence;
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
            services.AddSingleton<JsonRepository>();
            services.AddBlazoredModal();
        }
    }
}
