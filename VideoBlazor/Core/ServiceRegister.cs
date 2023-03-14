using Blazored.Modal;
using VideoBlazor.Core.Services;

namespace VideoBlazor.Core
{
    public static class ServiceRegister
    {
        public static void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton<FolderService>();
            services.AddSingleton<MediaItemService>();
            services.AddBlazoredModal();
        }
    }
}
