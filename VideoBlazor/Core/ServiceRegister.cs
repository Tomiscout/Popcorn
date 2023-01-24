using Blazored.Modal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoBlazor.Core.Services;
using VideoBlazor.Data;

namespace VideoBlazor.Core
{
    public static class ServiceRegister
    {
        public static void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton<WeatherForecastService>();
            services.AddSingleton<FolderService>();
            services.AddSingleton<MediaItemService>();
            services.AddBlazoredModal();
        }
    }
}
