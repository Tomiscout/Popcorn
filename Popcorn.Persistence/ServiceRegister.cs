using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Popcorn.Domain.Interfaces;
using Popcorn.Persistence.Repositories;

namespace Popcorn.Persistence;

public static class ServiceRegister
{
    public static void AddPopcornDatabase(this IServiceCollection services)
    {
        services.AddSingleton<IMediaItemRepository, MediaItemRepository>();
        services.AddSingleton<IMediaFileRepository, MediaFileRepository>();
        services.AddSingleton<DapperContext>();
        DapperContext.RegisterMappings();
        
        services
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSQLite()
                .WithGlobalConnectionString(DapperContext.ConnectionString)
                .ScanIn(typeof(DapperContext).Assembly).For.Migrations())
            .AddLogging(lb => lb.AddFluentMigratorConsole())
            .BuildServiceProvider(false);
    }
}