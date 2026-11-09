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
            .AddLogging(lb => lb.AddFluentMigratorConsole());
    }

    /// <summary>
    /// Brings the database schema up to date (server start-up; the players don't migrate). FluentMigrator's runner
    /// is a scoped service: resolving it from the root provider fails under the scope validation Development turns on.
    /// </summary>
    public static void MigrateDatabase(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}