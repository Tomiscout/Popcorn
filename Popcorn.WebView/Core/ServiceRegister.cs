using System.Runtime.InteropServices;
using Blazored.Modal;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Popcorn.Core.Services;
using Popcorn.Core.Utils;
using Popcorn.Domain;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Services;
using Popcorn.Persistence;
using Popcorn.Persistence.Repositories;
using Popcorn.PlayerServer;
using Popcorn.PlayerServer.Windows;
using Popcorn.SearchProviders.MovieDb;
using Popcorn.SearchProviders.Utils;
using Popcorn.SegmentFinder;
using Popcorn.Torrents;

namespace Popcorn.Core;

public static class ServiceRegister
{
    public static void AddPopcornServices(this IServiceCollection services)
    {
        services.AddMudServices();

        services.AddSingleton<FolderService>();
        services.AddSingleton<MediaItemService>();
        services.AddSingleton<TorrentCore>();
        services.AddSingleton<TorrentUtils>();
        services.AddSingleton<ImageRepository>();
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<JsonRepository>();
        services.AddSingleton<IMediaItemRepository, MediaItemRepository>();
        services.AddSingleton<MediaItemRefreshService>();
        services.AddSingleton<SegmentFinderService>();
        services.AddSingleton<IFileService, FileService>();
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            services.AddSingleton<IPlayerService, WindowsPlayerService>();
        }
        else
        {
            services.AddSingleton<IPlayerService, LinuxPlayerService>();
        }

        services.AddBlazoredModal();

        services
       .AddFluentMigratorCore()
       .ConfigureRunner(rb => rb
           .AddSQLite()
           .WithGlobalConnectionString(DapperContext.ConnectionString)
           .ScanIn(typeof(DapperContext).Assembly).For.Migrations());
           
        services.AddSingleton<DapperContext>();
        DapperContext.RegisterMappings();

        MovieDbProvider.Initialize();
    }
}
