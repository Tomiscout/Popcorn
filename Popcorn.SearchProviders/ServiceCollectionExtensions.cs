using Microsoft.Extensions.DependencyInjection;
// using Popcorn.SearchProviders.Imdb;
using Popcorn.SearchProviders.MovieDb;

namespace Popcorn.SearchProviders;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSearchProviders(this IServiceCollection services)
    {
        // services.AddSingleton<ImdbScoreService>();
        services.AddSingleton<MovieDbProvider>();
        return services;
    }
}